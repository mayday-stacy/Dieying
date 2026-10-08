using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Mcp;
using Composa.Editing;
using SkiaSharp;

namespace Composa.App.Tests;

public class AppIdentityTests
{
    [Theory]
    [InlineData(AppPaths.Platform.Windows)]
    [InlineData(AppPaths.Platform.Linux)]
    [InlineData(AppPaths.Platform.MacOS)]
    public void Upstream_environment_does_not_redirect_the_forks_settings_or_recovery(AppPaths.Platform platform)
    {
        string Folder(Environment.SpecialFolder folder) => Path.Combine(Path.GetTempPath(), "identity-tests", folder.ToString());
        var expected = AppPaths.For(platform, _ => null, Folder);
        var inherited = AppPaths.For(platform, variable => variable == "COMPOSA_DATA_DIR"
            ? Path.GetFullPath(Path.Combine(Path.GetTempPath(), "upstream-profile")) : null, Folder);
        Assert.Equal(expected, inherited);
        Assert.Equal("dieying", Path.GetFileName(inherited.Config));
        Assert.Equal("dieying", Path.GetFileName(inherited.Cache));
    }

    [Fact]
    public void The_pipe_and_executable_have_the_forks_identity()
    {
        Assert.Equal("dieying", typeof(AppInfo).Assembly.GetName().Name);
        Assert.Equal("dieying-mcp", McpPipe.For(AppPaths.Platform.Windows, "/unused"));
        Assert.Equal(Path.Combine("/fork/cache", "mcp.sock"), McpPipe.For(AppPaths.Platform.Linux, "/fork/cache"));
        Assert.Equal("DIEYING_MCP_PIPE", McpPipe.Variable);
        Assert.Contains("Composa", AppInfo.Attribution);
        Assert.Contains("not an official", AppInfo.Attribution);
    }

    [AvaloniaTheory]
    [InlineData("en", "Dieying")]
    [InlineData("zh-CN", "叠影 · Dieying")]
    public void Window_title_identifies_the_fork_without_renaming_document_data(string language, string displayName)
    {
        var previousLanguage = L10n.CurrentLanguage;
        MainWindow? window = null;
        L10n.SetLanguage(language);
        try
        {
            window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(displayName, AppInfo.DisplayName);
            Assert.Equal(displayName, window.Title);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == displayName);
            if (language == "zh-CN") Assert.True(Screenshots.Save(window, "dieying-chinese-welcome"));
            var session = EditorSession.NewCanvas(30, 20, SKColors.White);
            window.AddSession(session);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Untitled - {AppInfo.DisplayName}", window.Title);
            Assert.Equal("Untitled", session.Title);
        }
        finally { window?.Close(); L10n.SetLanguage(previousLanguage); }
    }
}
