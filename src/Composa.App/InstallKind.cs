using System.Diagnostics;
using System.Reflection;

namespace Composa.App;

/// <summary>
/// How this copy of Composa was installed, which decides the file an update downloads. It has to be
/// found at run time: the .deb, .rpm and AppImage are packed from one staged build and the Windows zip
/// and installer from one published tree, so no build property can tell them apart.
/// </summary>
public enum InstallKind
{
    /// <summary>Nothing on the releases page replaces it: a <c>dotnet run</c> build, or a platform with no download yet.</summary>
    Developer,
    Tarball,
    Deb,
    Rpm,
    AppImage,
    WindowsInstaller,
    WindowsZip,
}

/// <summary>
/// What <see cref="Install.Detect"/> asks of the machine, passed in so every kind can be tested on any
/// platform without being installed that way.
/// </summary>
/// <param name="HasRuntimeIdentifier">Whether the build was published for one platform, which every download is and <c>dotnet run</c> is not.</param>
/// <param name="BaseDirectory">The folder the running application was started from.</param>
/// <param name="Variable">Reads an environment variable.</param>
/// <param name="ReadFile">A file's text, or null when it cannot be read.</param>
/// <param name="Run">Runs a program and returns what it printed, or null when it could not run or failed.</param>
/// <param name="InstallerLocations">Where the Windows installer says it installed Composa, for the current user and for every user.</param>
public sealed record InstallProbes(
    AppPaths.Platform Platform,
    bool HasRuntimeIdentifier,
    string BaseDirectory,
    Func<string, string?> Variable,
    Func<string, string?> ReadFile,
    Func<string, IReadOnlyList<string>, string?> Run,
    Func<IEnumerable<string>> InstallerLocations);

public static class Install
{
    /// <summary>Where the .deb and .rpm put the application, as <c>scripts/package/stage.sh</c> lays it out.</summary>
    public const string PackageFolder = "/usr/lib/composa";

    /// <summary>
    /// The uninstall key Inno Setup writes for <c>packaging/windows/composa.iss</c>: its fixed AppId
    /// followed by <c>_is1</c>, in HKCU for a per-user install and HKLM for one for every user.
    /// </summary>
    public const string InnoUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{3D9BED7E-2C94-450E-A609-E183B1E42ACA}_is1";

    private static readonly Lazy<InstallKind> current = new(() => Detect(Probes()));

    /// <summary>How the running copy was installed, found once.</summary>
    public static InstallKind Current => UpdateCheck.Channel == UpdateChannel.Local ? InstallKind.Developer : current.Value;

    public static InstallKind Detect(InstallProbes probe)
    {
        if (!probe.HasRuntimeIdentifier) return InstallKind.Developer;
        switch (probe.Platform)
        {
            case AppPaths.Platform.Windows:
                return probe.InstallerLocations().Any(location => SameFolder(location, probe.BaseDirectory, ignoreCase: true))
                    ? InstallKind.WindowsInstaller
                    : InstallKind.WindowsZip;

            case AppPaths.Platform.Linux:
                // The AppImage runtime sets both. APPIMAGE alone is not enough: every program started
                // from an AppImage inherits it, so the application must also be running from inside
                // the mounted image.
                if (probe.Variable("APPIMAGE") is { Length: > 0 } && probe.Variable("APPDIR") is { Length: > 0 } mount &&
                    IsInside(probe.BaseDirectory, mount))
                    return InstallKind.AppImage;
                if (SameFolder(probe.BaseDirectory, PackageFolder, ignoreCase: false))
                {
                    // dpkg keeps a list of every file a package installed. Its name carries the
                    // architecture only for a Multi-Arch: same package, which Composa is not, but both
                    // spellings are cheap to try.
                    if (new[] { "composa.list", "composa:amd64.list", "composa:arm64.list" }
                        .Any(list => probe.ReadFile("/var/lib/dpkg/info/" + list)?.Contains(PackageFolder, StringComparison.Ordinal) == true))
                        return InstallKind.Deb;
                    if (probe.Run("rpm", ["-qf", "--queryformat", "%{NAME}", PackageFolder])?.Trim() == "composa")
                        return InstallKind.Rpm;
                }
                // Unpacked somewhere by hand, or a package format this does not know, which the
                // tarball replaces as well as anything.
                return InstallKind.Tarball;

            default:
                // No macOS download exists yet, so there is nothing to offer but the release notes.
                return InstallKind.Developer;
        }
    }

    // Separators are trimmed by hand rather than by Path, which only knows the running platform's, so
    // a Windows path compares the same way when the tests run on Linux.
    private static string Trim(string folder) => folder.TrimEnd('/', '\\');

    private static bool SameFolder(string a, string b, bool ignoreCase) =>
        string.Equals(Trim(a), Trim(b), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsInside(string path, string folder) =>
        (Trim(path) + "/").StartsWith(Trim(folder) + "/", StringComparison.Ordinal);

    /// <summary>The real machine's answers.</summary>
    private static InstallProbes Probes() => new(
        OperatingSystem.IsWindows() ? AppPaths.Platform.Windows : OperatingSystem.IsMacOS() ? AppPaths.Platform.MacOS : AppPaths.Platform.Linux,
        typeof(Install).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a.Key == "RuntimeIdentifier" && !string.IsNullOrEmpty(a.Value)),
        AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable,
        path =>
        {
            try { return File.ReadAllText(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        },
        RunQuietly,
        InnoLocations);

    private static string? RunQuietly(string program, IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process == null) return null;
            // Both streams are drained while waiting, so a program that prints more than a pipe holds
            // cannot stall, and one that hangs is given up on.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { process.Kill(); return null; }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> InnoLocations()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            string? location;
            try
            {
                using var key = hive.OpenSubKey(InnoUninstallKey);
                location = key?.GetValue("InstallLocation") as string;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(location)) yield return location;
        }
    }
}
