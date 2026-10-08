using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Composa.App.Mcp;

/// <summary>
/// <c>composa --mcp</c>: the process an MCP client launches. Every client speaks stdio to a process it starts, but the
/// editor is already running with a window, so this process carries the client's messages to the running
/// application's pipe and the answers back. It outlives the application: the client's handshake is answered here and
/// replayed to the application whenever it is reached, so Composa can be started, quit and rebuilt while the client
/// keeps one server, and the client is told its tools changed each time. While the application is away, the tool
/// list is empty and calls fail with a message saying so. With <c>--launch</c> the bridge starts the application
/// once when nothing answers at first; a later quit is the person's and is left alone.
/// </summary>
public sealed class McpBridge
{
    /// <summary>The id of the initialize request the bridge sends to the application, never one a client would use.</summary>
    private const string InitializeId = "composa-bridge-initialize";

    private readonly string pipeName;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly Action? launch;
    private readonly SemaphoreSlim outputLock = new(1, 1);
    private readonly SemaphoreSlim pipeLock = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly HashSet<string> inFlight = [];      // Ids of client requests the application still owes an answer to.
    private readonly List<string> waiting = [];          // Client messages held while the handshake with the application runs.
    private StreamWriter? pipeWriter;
    private JsonNode? initializeParams;                    // The client's, replayed to the application on every connection.
    private bool clientInitialized;                        // The client sent notifications/initialized.
    private bool appReady;                                 // The application answered the bridge's initialize.
    private volatile bool firstAttemptDone;                // Requests arriving before the first connection attempt has failed are held, not refused.

    private bool launched;                                 // The application was started by this bridge; it is started at most once.

    public McpBridge(string pipeName, TextReader input, TextWriter output, Action? launch = null)
    {
        this.pipeName = pipeName;
        this.input = input;
        this.output = output;
        this.launch = launch;
    }

    public static Task<int> RunAsync(string pipeName, bool launch)
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        return new McpBridge(pipeName, input, output, launch ? LaunchApplication : null).RunAsync();
    }

    /// <summary>
    /// Starts the application this bridge belongs to: the same executable without the bridge flags, or the same
    /// assembly under the dotnet host during development. The bridge's stdin and stdout are the protocol channel and
    /// a child must never share them: it gets a closed stdin and its stdout is drained and dropped. stderr is
    /// inherited, where the client logs it.
    /// </summary>
    private static void LaunchApplication()
    {
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            var process = Process.Start(start) ?? throw new InvalidOperationException("The application could not be started.");
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            Console.Error.WriteLine($"Starting {AppInfo.Name}.");
        }
        catch (Exception error) { Console.Error.WriteLine($"Couldn't start {AppInfo.Name}: {error.Message}"); }
    }

    /// <summary>Runs until the client closes its end.</summary>
    public async Task<int> RunAsync()
    {
        _ = ConnectLoopAsync();
        while (await input.ReadLineAsync() is { } line)
        {
            if (line.Length == 0) continue;
            try { await FromClientAsync(line); }
            catch (Exception error) { await Console.Error.WriteLineAsync(error.ToString()); }
        }
        stop.Cancel();
        return 0;
    }

    // ---- The application's side --------------------------------------------------------------------------------------

    private async Task ConnectLoopAsync()
    {
        var said = false;
        while (!stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(1000, stop.Token); }
            catch (OperationCanceledException) { pipe.Dispose(); return; }
            catch (Exception)
            {
                pipe.Dispose();
                firstAttemptDone = true;
                await RefuseHeldAsync();
                if (launch != null && !launched) { launched = true; launch(); }
                if (!said) { await Console.Error.WriteLineAsync($"{AppInfo.Name} is not running, or Help > Allow AI Control is off; waiting for it."); said = true; }
                try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { return; }
                continue;
            }
            said = false;
            await ServeConnectionAsync(pipe);
        }
    }

    /// <summary>Reads the application's messages until it closes the pipe, then answers everything it left hanging.</summary>
    private async Task ServeConnectionAsync(NamedPipeClientStream pipe)
    {
        using (pipe)
        {
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            await pipeLock.WaitAsync();
            try { pipeWriter = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true }; }
            finally { pipeLock.Release(); }
            if (initializeParams != null) await SendInitializeToAppAsync();
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                    if (line.Length > 0) await FromAppAsync(line);
            }
            catch (Exception) { /* The pipe broke: the same as the application closing it. */ }
        }
        await pipeLock.WaitAsync();
        try { pipeWriter = null; }
        finally { pipeLock.Release(); }
        var wasReady = appReady;
        appReady = false;
        firstAttemptDone = true;
        List<string> owed;
        lock (gate) { owed = [.. inFlight]; inFlight.Clear(); }
        foreach (var id in owed) await ToClientAsync(Error(JsonNode.Parse(id), $"{AppInfo.Name} closed while this was running."));
        await RefuseHeldAsync();
        if (wasReady && clientInitialized) await AnnounceListsChangedAsync();
    }

    /// <summary>The application came or went: what the client can list has changed.</summary>
    private async Task AnnounceListsChangedAsync()
    {
        await ToClientAsync(Notification("notifications/tools/list_changed"));
        await ToClientAsync(Notification("notifications/resources/list_changed"));
    }

    /// <summary>Answers the messages held for a handshake that did not come: the application went away, or was never there.</summary>
    private async Task RefuseHeldAsync()
    {
        List<string> held;
        lock (gate) { held = [.. waiting]; waiting.Clear(); }
        foreach (var line in held) await AnswerAwayAsync(line);
    }

    private Task SendInitializeToAppAsync() =>
        ToAppAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = InitializeId, ["method"] = "initialize", ["params"] = initializeParams!.DeepClone() }.ToJsonString());

    private async Task FromAppAsync(string line)
    {
        var message = TryParse(line);
        var id = message?["id"];
        if (id is JsonValue value && value.TryGetValue<string>(out var text) && text == InitializeId)
        {
            // The handshake is complete: the application is ready for the client's requests, held ones first.
            await ToAppAsync(Notification("notifications/initialized"));
            appReady = true;
            List<string> held;
            lock (gate) { held = [.. waiting]; waiting.Clear(); }
            foreach (var line2 in held) await ForwardAsync(line2);
            if (clientInitialized) await AnnounceListsChangedAsync();
            return;
        }
        if (id != null && message?["method"] == null) lock (gate) inFlight.Remove(id.ToJsonString());
        await ToClientAsync(line);
    }

    private async Task ToAppAsync(string line)
    {
        await pipeLock.WaitAsync();
        try { if (pipeWriter != null) await pipeWriter.WriteAsync(line + "\n"); }
        catch (Exception) { /* The reader notices the broken pipe and answers what was in flight. */ }
        finally { pipeLock.Release(); }
    }

    // ---- The client's side -------------------------------------------------------------------------------------------

    private async Task FromClientAsync(string line)
    {
        var message = TryParse(line);
        var method = message?["method"]?.GetValue<string>();
        var id = message?["id"];
        if (method == "initialize")
        {
            initializeParams = message!["params"]?.DeepClone() ?? new JsonObject();
            await ToClientAsync(InitializeResult(id, message["params"]?["protocolVersion"]?.GetValue<string>()));
            if (pipeWriter != null && !appReady) await SendInitializeToAppAsync();
            return;
        }
        if (method == "notifications/initialized")
        {
            clientInitialized = true;
            if (appReady) await AnnounceListsChangedAsync();
            return;
        }
        if (appReady) { await ForwardAsync(line); return; }
        if (pipeWriter != null || !firstAttemptDone)
        {
            // The application is there (or may be, a moment after launch) but the handshake is still running.
            lock (gate) waiting.Add(line);
            return;
        }
        await AnswerAwayAsync(line);
    }

    /// <summary>The application is away. Notifications and responses have nowhere to go; requests get an honest answer.</summary>
    private async Task AnswerAwayAsync(string line)
    {
        var message = TryParse(line);
        var method = message?["method"]?.GetValue<string>();
        var id = message?["id"];
        if (id == null || method == null) return;
        if (method == "tools/list") await ToClientAsync(Result(id, new JsonObject { ["tools"] = new JsonArray() }));
        else if (method == "resources/list") await ToClientAsync(Result(id, new JsonObject { ["resources"] = new JsonArray() }));
        else if (method == "resources/templates/list") await ToClientAsync(Result(id, new JsonObject { ["resourceTemplates"] = new JsonArray() }));
        else if (method == "ping") await ToClientAsync(Result(id, new JsonObject()));
        else await ToClientAsync(Error(id, $"{AppInfo.Name} is not running, or Help > Allow AI Control is off. Start it and the tools appear by themselves."));
    }

    private async Task ForwardAsync(string line)
    {
        var message = TryParse(line);
        if (message?["id"] is { } id && message["method"] != null) lock (gate) inFlight.Add(id.ToJsonString());
        await ToAppAsync(line);
    }

    private async Task ToClientAsync(string line)
    {
        await outputLock.WaitAsync();
        try { await output.WriteAsync(line + "\n"); }
        finally { outputLock.Release(); }
    }

    // ---- Messages ----------------------------------------------------------------------------------------------------

    private static string InitializeResult(JsonNode? id, string? protocolVersion) => Result(id, new JsonObject
    {
        ["protocolVersion"] = protocolVersion ?? "2025-06-18",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true }, ["resources"] = new JsonObject { ["listChanged"] = true } },
        ["serverInfo"] = new JsonObject { ["name"] = AppInfo.Id, ["title"] = AppInfo.Name, ["version"] = AppInfo.Version },
        ["instructions"] = AppInfo.Name + " is a layer-based image editor. The tools act on the documents open in its window; " +
                           "every change is an undoable step the person can see and undo. Coordinates are canvas pixels " +
                           "with the origin at the top left. Call render to see the result of your changes. " +
                           $"When the tool list is empty, {AppInfo.Name} is not running or Help > Allow AI Control is off; the tools appear once it is."
    });

    private static string Result(JsonNode? id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = -32000, ["message"] = message } }.ToJsonString();

    private static string Notification(string method) => new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }.ToJsonString();

    private static JsonNode? TryParse(string line)
    {
        try { return JsonNode.Parse(line); }
        catch (JsonException) { return null; }
    }
}
