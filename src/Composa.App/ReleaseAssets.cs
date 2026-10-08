using System.Runtime.InteropServices;

namespace Composa.App;

/// <summary>
/// Which of a release's files replaces the running copy. The names are the ones the packaging scripts
/// in <c>scripts/package</c> write, so a package renamed there must be renamed here as well; the tests
/// hold a real release's file list to catch the day they drift apart.
/// </summary>
public static class ReleaseAssets
{
    /// <summary>
    /// The file for this kind of install on this processor, or null when the release has none or the
    /// install has no download at all.
    /// </summary>
    /// <remarks>
    /// Files are recognised by how their names begin and end, the format and the architecture, and not
    /// by the version between them. A release holds one version only, and the version is spelled three
    /// ways across the formats (0.2.1~alpha for dpkg, 0.2.1-0.alpha for rpm), which GitHub may rewrite
    /// again when the file is uploaded.
    /// </remarks>
    public static ReleaseAsset? For(InstallKind kind, Architecture architecture, ReleaseInfo release)
    {
        if (Pattern(kind, architecture) is not (var prefix, var suffix)) return null;
        return release.Assets.FirstOrDefault(asset =>
            asset.Name.Length > prefix.Length + suffix.Length &&
            asset.Name.StartsWith(prefix, StringComparison.Ordinal) &&
            asset.Name.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The file the update strip offers to download. Managed builds leave updates to their package
    /// manager, and local builds are rebuilt from source: neither offers a release download.
    /// </summary>
    public static ReleaseAsset? Offered(UpdateChannel channel, InstallKind kind, Architecture architecture, ReleaseInfo release) =>
        channel == UpdateChannel.GitHub ? For(kind, architecture, release) : null;

    /// <summary>How the file for an install begins and ends, spelling the architecture as each format does (see <c>scripts/package/common.sh</c>).</summary>
    private static (string Prefix, string Suffix)? Pattern(InstallKind kind, Architecture architecture)
    {
        var (dotnet, deb, rpm) = architecture switch
        {
            Architecture.X64 => ("x64", "amd64", "x86_64"),
            Architecture.Arm64 => ("arm64", "arm64", "aarch64"),
            _ => (null, null, null),
        };
        if (dotnet == null) return null;
        return kind switch
        {
            InstallKind.Deb => ("composa_", $"_{deb}.deb"),                       // deb.sh: composa_1.2.0_amd64.deb
            InstallKind.Rpm => ("composa-", $".{rpm}.rpm"),                       // rpm.sh: composa-1.2.0-1.x86_64.rpm
            InstallKind.AppImage => ("Composa-", $"-{rpm}.AppImage"),             // appimage.sh: Composa-1.2.0-x86_64.AppImage
            InstallKind.Tarball => ("composa-", $"-linux-{dotnet}.tar.gz"),       // tarball.sh: composa-1.2.0-linux-x64.tar.gz
            InstallKind.WindowsInstaller => ("composa-", $"-win-{dotnet}-setup.exe"), // windows.sh: composa-1.2.0-win-x64-setup.exe
            InstallKind.WindowsZip => ("composa-", $"-win-{dotnet}.zip"),         // windows.sh: composa-1.2.0-win-x64.zip
            _ => null,
        };
    }
}
