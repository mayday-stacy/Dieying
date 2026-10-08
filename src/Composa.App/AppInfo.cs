using System.Reflection;

namespace Composa.App;

/// <summary>
/// What this build calls itself. The version comes from MinVer by way of the informational version
/// attribute, so it is derived from the git tag and never hand-edited; the About dialog and the
/// update check both read it here so there is one answer rather than two.
/// </summary>
public static class AppInfo
{
    /// <summary>The product identity, kept independent of upstream Composa.</summary>
    public const string Id = "dieying";
    public const string Name = "Dieying";
    public const string DataDirectoryVariable = "DIEYING_DATA_DIR";
    public const string LegacyDevelopmentId = "image-editor-dev";
    public const string LegacyDataDirectoryVariable = "IMAGE_EDITOR_DEV_DATA_DIR";
    public const string RepositoryUrl = "https://github.com/mayday-stacy/Dieying";
    public const string UpstreamUrl = "https://github.com/dvdstelt/Composa";
    public const string Attribution = "An independent development fork of Composa by Dennis van der Stelt (MIT license). " +
        "Composa reimplements the macOS image editor Compositor by Robbie Tilton. This is not an official Composa release.";

    public static string DisplayName => L10n.Text(Name);
    public static Uri ResourceUri(string path = "") => new($"avares://{Id}/{path}");

    /// <summary>The release URL a user is sent to when a newer version exists.</summary>
    public const string ReleasesUrl = "https://github.com/dvdstelt/Composa/releases";

    /// <summary>
    /// The version without build metadata: MinVer writes <c>0.2.0+&lt;sha&gt;</c>, and the commit hash is
    /// noise in a dialog and would break a comparison against a release tag.
    /// </summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return "0.0.0";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
