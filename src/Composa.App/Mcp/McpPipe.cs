namespace Composa.App.Mcp;

/// <summary>
/// Where an MCP client finds the running application. Both ends speak over a named pipe, which .NET backs with a Unix
/// domain socket on Linux and macOS: a name with a directory separator is used as that socket's path, so it lives in
/// Composa's own cache folder rather than in the shared /tmp. On Windows the name is a real named pipe, opened for the
/// current user only. <c>DIEYING_MCP_PIPE</c> overrides the name, which the tests use to keep their server apart.
/// </summary>
public static class McpPipe
{
    public const string Variable = "DIEYING_MCP_PIPE";

    public static string Name => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } name ? name : Default;

    public static string Default => For(AppPaths.CurrentPlatform, AppPaths.Cache);

    public static string For(AppPaths.Platform platform, string cache) => platform == AppPaths.Platform.Windows
        ? AppInfo.Id + "-mcp" : Path.Combine(cache, "mcp.sock");
}
