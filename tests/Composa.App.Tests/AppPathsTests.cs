using static Composa.App.AppPaths;

namespace Composa.App.Tests;

public class AppPathsTests
{
    private static string Folder(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.UserProfile => "/home/ada",
        Environment.SpecialFolder.ApplicationData => @"C:\Users\ada\AppData\Roaming",
        Environment.SpecialFolder.LocalApplicationData => @"C:\Users\ada\AppData\Local",
        _ => throw new ArgumentException($"{folder} is not a folder Composa should use"),
    };

    private static Func<string, string?> Variables(params (string Name, string Value)[] set) => name => set.FirstOrDefault(v => v.Name == name).Value;

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void A_local_data_directory_isolates_preferences_and_recovery(Platform platform)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Composa development", "中文"));
        var result = For(platform, Variables((AppInfo.DataDirectoryVariable, root)),
            _ => throw new InvalidOperationException("The installed profile must not be consulted."));
        Assert.Equal(new Folders(Path.Combine(root, "config"), Path.Combine(root, "cache")), result);
    }

    [Theory]
    [InlineData("local-data")]
    [InlineData("../local-data")]
    public void A_relative_data_override_cannot_silently_use_a_different_profile(string root)
    {
        Assert.Throws<ArgumentException>(() => For(Platform.Windows,
            Variables((AppInfo.DataDirectoryVariable, root)), Folder));
    }

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOS)]
    public void The_new_data_override_wins_and_the_old_development_override_remains_supported(Platform platform)
    {
        var oldRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "old-development-profile"));
        var newRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dieying-profile"));
        var legacy = Variables((AppInfo.LegacyDataDirectoryVariable, oldRoot));
        var both = Variables((AppInfo.LegacyDataDirectoryVariable, oldRoot), (AppInfo.DataDirectoryVariable, newRoot));
        Assert.Equal(new Folders(Path.Combine(oldRoot, "config"), Path.Combine(oldRoot, "cache")), For(platform, legacy, Folder));
        Assert.Equal(new Folders(Path.Combine(newRoot, "config"), Path.Combine(newRoot, "cache")), For(platform, both, Folder));
        Assert.Null(LegacyConfigFor(platform, legacy, Folder));
        Assert.Null(LegacyConfigFor(platform, both, Folder));
        Assert.Equal("image-editor-dev", Path.GetFileName(LegacyConfigFor(platform, Variables(), Folder)));
    }

    [Fact]
    public void An_invalid_new_override_does_not_fall_back_to_the_old_one()
    {
        var oldRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "old-development-profile"));
        Assert.Throws<ArgumentException>(() => For(Platform.Windows,
            Variables((AppInfo.DataDirectoryVariable, "relative"), (AppInfo.LegacyDataDirectoryVariable, oldRoot)), Folder));
        Assert.Throws<ArgumentException>(() => For(Platform.Windows,
            Variables((AppInfo.LegacyDataDirectoryVariable, "relative")), Folder));
    }

    [Fact]
    public void A_local_profile_does_not_redirect_the_users_downloads()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Composa development"));
        Assert.Equal(@"D:\Downloads", Downloads(Platform.Windows,
            Variables((AppInfo.DataDirectoryVariable, root)), known: @"D:\Downloads"));
    }

    /// <summary>The fork respects XDG without sharing upstream Composa's profile.</summary>
    [Fact]
    public void Linux_follows_xdg_and_falls_back_to_the_home_directory()
    {
        Assert.Equal(new Folders(Path.Combine("/home/ada", ".config", "dieying"), Path.Combine("/home/ada", ".cache", "dieying")),
            For(Platform.Linux, Variables(), Folder));
        Assert.Equal(new Folders(Path.Combine("/xdg/config", "dieying"), Path.Combine("/xdg/cache", "dieying")),
            For(Platform.Linux, Variables(("XDG_CONFIG_HOME", "/xdg/config"), ("XDG_CACHE_HOME", "/xdg/cache")), Folder));
    }

    /// <summary>Preferences roam with the profile; recovery copies, which can be large and are worthless elsewhere, do not.</summary>
    [Fact]
    public void Windows_keeps_preferences_roaming_and_the_cache_local()
    {
        var folders = For(Platform.Windows, Variables(("XDG_CONFIG_HOME", "/ignored")), Folder);
        Assert.Equal(Path.Combine(@"C:\Users\ada\AppData\Roaming", "dieying"), folders.Config);
        Assert.Equal(Path.Combine(@"C:\Users\ada\AppData\Local", "dieying"), folders.Cache);
    }

    [Fact]
    public void MacOS_uses_application_support_and_caches()
    {
        var folders = For(Platform.MacOS, Variables(("XDG_CONFIG_HOME", "/ignored")), Folder);
        Assert.Equal(Path.Combine("/home/ada", "Library", "Application Support", "dieying"), folders.Config);
        Assert.Equal(Path.Combine("/home/ada", "Library", "Caches", "dieying"), folders.Cache);
    }

    private static Func<string, string?> Files(params (string Path, string Text)[] files) => path => files.FirstOrDefault(f => f.Path == path).Text;

    private static string Downloads(Platform platform, Func<string, string?>? variables = null, Func<string, string?>? files = null, string? known = null)
        => DownloadsFor(platform, variables ?? Variables(), Folder, files ?? Files(), () => known);

    /// <summary>A desktop set up in another language names the folder in it, and only user-dirs.dirs knows the name.</summary>
    [Fact]
    public void Linux_finds_a_localised_downloads_folder_in_user_dirs()
    {
        var dirs = "# This file is written by xdg-user-dirs-update\nXDG_DESKTOP_DIR=\"$HOME/Bureau\"\nXDG_DOWNLOAD_DIR=\"$HOME/Téléchargements\"\n";
        Assert.Equal("/home/ada/Téléchargements", Downloads(Platform.Linux, files: Files((Path.Combine("/home/ada", ".config", "user-dirs.dirs"), dirs))));
        // XDG_CONFIG_HOME moves the file along with every other setting.
        Assert.Equal("/home/ada/Téléchargements", Downloads(Platform.Linux, Variables(("XDG_CONFIG_HOME", "/xdg/config")),
            Files((Path.Combine("/xdg/config", "user-dirs.dirs"), dirs))));
    }

    [Fact]
    public void Linux_falls_back_to_the_home_folders_downloads()
    {
        Assert.Equal(Path.Combine("/home/ada", "Downloads"), Downloads(Platform.Linux));
        // A file that lists other folders only.
        Assert.Equal(Path.Combine("/home/ada", "Downloads"), Downloads(Platform.Linux,
            files: Files((Path.Combine("/home/ada", ".config", "user-dirs.dirs"), "XDG_MUSIC_DIR=\"$HOME/Music\"\n"))));
        // Without the file, a variable someone set still counts.
        Assert.Equal("/data/incoming", Downloads(Platform.Linux, Variables(("XDG_DOWNLOAD_DIR", "/data/incoming"))));
    }

    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/Downloads\"", "/home/ada/Downloads")]
    [InlineData("XDG_DOWNLOAD_DIR=\"${HOME}/Downloads\"", "/home/ada/Downloads")]
    [InlineData("XDG_DOWNLOAD_DIR=\"/mnt/data/Downloads\"", "/mnt/data/Downloads")]
    [InlineData("XDG_DOWNLOAD_DIR=$HOME/Downloads", "/home/ada/Downloads")] // Unquoted, which a person editing it might write.
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/My \\\"Files\\\"\"", "/home/ada/My \"Files\"")] // Shell escapes inside the quotes.
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME\"", "/home/ada")] // Set to the home folder, which is how the folder is switched off.
    [InlineData("  XDG_DOWNLOAD_DIR=\"$HOME/Spaced\"  ", "/home/ada/Spaced")]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/First\"\r\nXDG_DOWNLOAD_DIR=\"$HOME/Second\"\r\n", "/home/ada/Second")] // The last one wins, as in the shell.
    public void The_user_dirs_parser_reads_what_xdg_user_dirs_writes(string text, string expected)
        => Assert.Equal(expected, XdgUserDir(text, "XDG_DOWNLOAD_DIR", "/home/ada"));

    [Theory]
    [InlineData("")]
    [InlineData("# XDG_DOWNLOAD_DIR=\"$HOME/Commented\"")]
    [InlineData("XDG_DOWNLOAD_DIR=\"Downloads\"")] // Neither absolute nor under $HOME, which xdg-user-dirs never writes.
    [InlineData("XDG_DOWNLOAD_DIRECTORY=\"$HOME/Other\"")]
    [InlineData("OTHER_XDG_DOWNLOAD_DIR=\"$HOME/Other\"")]
    public void The_user_dirs_parser_ignores_what_is_not_the_folder(string text)
        => Assert.Null(XdgUserDir(text, "XDG_DOWNLOAD_DIR", "/home/ada"));

    /// <summary>Downloads can be moved to another drive on Windows, and only the known folder says where.</summary>
    [Fact]
    public void Windows_asks_for_the_known_folder()
    {
        Assert.Equal(@"D:\Downloads", Downloads(Platform.Windows, known: @"D:\Downloads"));
        Assert.Equal(Path.Combine("/home/ada", "Downloads"), Downloads(Platform.Windows, known: null));
    }

    [Fact]
    public void MacOS_uses_the_home_folders_downloads()
        => Assert.Equal(Path.Combine("/home/ada", "Downloads"), Downloads(Platform.MacOS, files: Files((Path.Combine("/home/ada", ".config", "user-dirs.dirs"), "XDG_DOWNLOAD_DIR=\"/ignored\""))));

    [Fact]
    public void The_running_machine_has_a_downloads_folder() => Assert.True(Path.IsPathRooted(AppPaths.Downloads));
}
