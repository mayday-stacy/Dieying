using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Composa.Editing;
using Composa.Model;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>Tool toggles that follow the person rather than the document.</summary>
public class SettingsTests
{
    [Fact]
    public void Old_development_preferences_are_read_only_and_subsequent_saves_use_the_new_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), "dieying-settings-tests", Guid.NewGuid().ToString("N"));
        var oldPath = Path.Combine(root, "image-editor-dev", "settings.json");
        var newPath = Path.Combine(root, "dieying", "settings.json");
        const string oldJson = """{ "Language": "zh-CN", "JpegQuality": 72, "RecentFiles": ["old.cmps"] }""";
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, oldJson);
        try
        {
            var inherited = Settings.LoadFrom(newPath, oldPath);
            Assert.Equal("zh-CN", inherited.Language);
            Assert.Equal(72, inherited.JpegQuality);
            Assert.Equal(new[] { "old.cmps" }, inherited.RecentFiles);
            Assert.False(File.Exists(newPath)); // Loading alone never writes or moves anything.
            inherited.JpegQuality = 85;
            inherited.SaveTo(newPath);
            Assert.Equal(oldJson, File.ReadAllText(oldPath));
            Assert.Equal(85, Settings.LoadFrom(newPath, oldPath).JpegQuality);
            File.WriteAllText(oldPath, """{ "JpegQuality": 40 }""");
            Assert.Equal(85, Settings.LoadFrom(newPath, oldPath).JpegQuality); // The new profile always wins.
            File.WriteAllText(newPath, "broken json");
            Assert.Equal(90, Settings.LoadFrom(newPath, oldPath).JpegQuality); // A damaged new profile is not an old profile.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void View_options_and_move_tool_toggles_survive_the_settings_file()
    {
        var settings = new Settings
        {
            ShowTransformControls = false, AutoSelect = false, ShowPixelGrid = false,
            View = new ViewOptions
            {
                ShowRulers = true, ShowGrid = true, Snap = false, SnapToGrid = true, LockGuides = true,
                Grid = new LayoutGrid { Spacing = 50, Subdivisions = 5 },
                GridAppearance = new GridAppearance { Preset = GridColorPreset.Custom, CustomColor = 0xFF102030, Style = GridStyle.Dots, Opacity = 60 }
            }
        };
        var json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"Dots\"", json); // Named, so the file survives the enums being reordered.
        var loaded = JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal((false, false, false), (loaded.ShowTransformControls, loaded.AutoSelect, loaded.ShowPixelGrid));
        Assert.Equal(settings.View, loaded.View);
        // A settings file from before these were remembered keeps the defaults.
        var old = JsonSerializer.Deserialize<Settings>("""{ "JpegQuality": 80 }""")!;
        Assert.Equal((true, true, true), (old.ShowTransformControls, old.AutoSelect, old.View.Snap));
    }

    [AvaloniaFact]
    public void Toggling_view_options_and_transform_controls_is_remembered_and_seeds_the_first_document()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        window.Show();
        window.Settings.View = new ViewOptions { ShowRulers = true, SnapToLayers = false };
        window.Settings.AutoSelect = false;
        window.AddSession(EditorSession.NewCanvas(200, 100, SKColors.White));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Session!.View.ShowRulers);
        Assert.False(window.Session.View.SnapToLayers);
        Assert.True(window.Canvas.AutoSelect); // The canvas was built before the test changed the settings; startup reads them.

        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.Control);      // Rulers off
        window.KeyPressQwerty(PhysicalKey.Quote, RawInputModifiers.Control);  // Grid on
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);      // Transform controls off
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.Settings.View.ShowRulers);
        Assert.True(window.Settings.View.ShowGrid);
        Assert.False(window.Settings.ShowTransformControls);
        Assert.False(window.Canvas.ShowTransformControls);
    }
}
