using System.Runtime.InteropServices;
using System.Text;

namespace Composa.App;

/// <summary>
/// Where Dieying keeps its own files, in each platform's own convention: XDG on Linux, the roaming
/// and local application data folders on Windows, and Application Support and Caches on macOS.
/// Settings and crash-recovery copies both ask here, so a platform is added in one place. The
/// person's Downloads folder, where an update is saved, is found here too.
/// </summary>
public static partial class AppPaths
{
    public enum Platform { Linux, Windows, MacOS }

    /// <summary>The config directory (preferences) and the cache directory (recovery copies) for one platform.</summary>
    public readonly record struct Folders(string Config, string Cache);

    public static Platform CurrentPlatform { get; } =
        OperatingSystem.IsWindows() ? Platform.Windows : OperatingSystem.IsMacOS() ? Platform.MacOS : Platform.Linux;

    private static readonly Folders current = For(CurrentPlatform, Environment.GetEnvironmentVariable, Environment.GetFolderPath);

    /// <summary>Preferences: small, and worth carrying to another machine, so Windows keeps them in the roaming profile.</summary>
    public static string Config => current.Config;

    /// <summary>Things that can be lost without harm, which Windows keeps out of the roaming profile.</summary>
    public static string Cache => current.Cache;

    /// <summary>Read-only preferences fallback from the earlier development name; recovery is never migrated.</summary>
    public static string? LegacyConfig { get; } = LegacyConfigFor(CurrentPlatform, Environment.GetEnvironmentVariable, Environment.GetFolderPath);

    /// <summary>The folders for a platform, given how to read its environment. Separate from the running OS so every platform can be tested anywhere.</summary>
    public static Folders For(Platform platform, Func<string, string?> variable, Func<Environment.SpecialFolder, string> folder)
    {
        // Keep a local development build's preferences and recovery files separate from an
        // installed copy. Requiring an absolute path makes the result independent of the
        // working directory used by a shortcut or launcher.
        var overrideVariable = variable(AppInfo.DataDirectoryVariable) is { Length: > 0 }
            ? AppInfo.DataDirectoryVariable : AppInfo.LegacyDataDirectoryVariable;
        if (variable(overrideVariable) is { Length: > 0 } dataDirectory)
        {
            if (!Path.IsPathFullyQualified(dataDirectory))
                throw new ArgumentException($"{overrideVariable} must be an absolute path.");
            var root = Path.GetFullPath(dataDirectory);
            return new(Path.Combine(root, "config"), Path.Combine(root, "cache"));
        }
        return DefaultFolders(platform, variable, folder, AppInfo.Id);
    }

    /// <summary>An explicit data profile stays isolated; only a default profile can inherit old preferences.</summary>
    public static string? LegacyConfigFor(Platform platform, Func<string, string?> variable, Func<Environment.SpecialFolder, string> folder)
        => variable(AppInfo.DataDirectoryVariable) is { Length: > 0 } || variable(AppInfo.LegacyDataDirectoryVariable) is { Length: > 0 }
            ? null : DefaultFolders(platform, variable, folder, AppInfo.LegacyDevelopmentId).Config;

    private static Folders DefaultFolders(Platform platform, Func<string, string?> variable,
        Func<Environment.SpecialFolder, string> folder, string productId)
    {
        var home = folder(Environment.SpecialFolder.UserProfile);
        switch (platform)
        {
            case Platform.Windows:
                return new(Path.Combine(folder(Environment.SpecialFolder.ApplicationData), productId),
                           Path.Combine(folder(Environment.SpecialFolder.LocalApplicationData), productId));
            case Platform.MacOS:
                return new(Path.Combine(home, "Library", "Application Support", productId),
                           Path.Combine(home, "Library", "Caches", productId));
            default:
                // Lowercase, as every other program in ~/.config is.
                var config = variable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config)) config = Path.Combine(home, ".config");
                var cache = variable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cache)) cache = Path.Combine(home, ".cache");
                return new(Path.Combine(config, productId), Path.Combine(cache, productId));
        }
    }

    /// <summary>
    /// The person's Downloads folder, read afresh each time, since someone may move it while Composa
    /// runs. Avalonia's own answer is not used: on Linux it reads only an environment variable that
    /// nothing sets, and so misses a folder the desktop has localised, such as Téléchargements.
    /// </summary>
    public static string Downloads => DownloadsFor(CurrentPlatform, Environment.GetEnvironmentVariable, Environment.GetFolderPath, ReadText, WindowsDownloads);

    /// <summary>The Downloads folder for a platform, given how to read its environment, its files and, on Windows, its known folders.</summary>
    public static string DownloadsFor(Platform platform, Func<string, string?> variable, Func<Environment.SpecialFolder, string> folder,
        Func<string, string?> readFile, Func<string?> windowsKnownFolder)
    {
        var home = folder(Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(home, "Downloads");
        switch (platform)
        {
            case Platform.Windows:
                // Someone can move Downloads to another drive, and only the known folder follows it.
                return windowsKnownFolder() is { Length: > 0 } known ? known : fallback;
            case Platform.MacOS:
                return fallback;
            default:
                // xdg-user-dirs writes the folder's localised name here when the desktop is set up.
                var config = variable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config)) config = Path.Combine(home, ".config");
                if (readFile(Path.Combine(config, "user-dirs.dirs")) is { } dirs && XdgUserDir(dirs, "XDG_DOWNLOAD_DIR", home) is { } listed)
                    return listed;
                return variable("XDG_DOWNLOAD_DIR") is { Length: > 0 } set && Path.IsPathRooted(set) ? set : fallback;
        }
    }

    /// <summary>
    /// One folder from <c>user-dirs.dirs</c>, which xdg-user-dirs writes as shell assignments of the form
    /// <c>XDG_DOWNLOAD_DIR="$HOME/Downloads"</c> or an absolute path, with shell escapes inside the quotes.
    /// Anything else in the file is ignored, as xdg-user-dirs itself ignores it.
    /// </summary>
    public static string? XdgUserDir(string text, string name, string home)
    {
        string? found = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || !line.StartsWith(name + "=", StringComparison.Ordinal)) continue;
            var value = line[(name.Length + 1)..];
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            var unescaped = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
                unescaped.Append(value[i] == '\\' && i + 1 < value.Length ? value[++i] : value[i]);
            value = unescaped.ToString();
            foreach (var spelling in new[] { "$HOME", "${HOME}" })
                if (value == spelling || value.StartsWith(spelling + "/", StringComparison.Ordinal))
                    value = home + value[spelling.Length..];
            // A later line wins, as it would in the shell that the file is written for.
            if (value.StartsWith('/')) found = value;
        }
        return found;
    }

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads

    private static string? WindowsDownloads()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var path) != 0) return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folder, uint flags, IntPtr token, out IntPtr path);
}
