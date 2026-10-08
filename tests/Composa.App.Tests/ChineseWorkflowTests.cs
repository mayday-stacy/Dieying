using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Controls;
using Composa.Editing;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>Chinese is a display choice: editing, document data and saved command IDs keep their contracts.</summary>
public class ChineseWorkflowTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static Menu Menu(MainWindow window) => window.GetLogicalDescendants().OfType<Menu>().First();

    private static void CloseAndReset(MainWindow? window)
    {
        try
        {
            if (window == null) return;
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            window.Session?.CancelText();
            // This only marks the test document clean, so closing never opens a save prompt.
            window.Session?.MarkSaved(Path.Combine(Path.GetTempPath(), "composa-chinese-ui-test.cmps"));
            window.Close();
            Pump();
        }
        finally { L10n.SetLanguage("en"); }
    }

    [AvaloniaFact]
    public void Chinese_shell_translates_labels_but_keeps_user_layer_names_history_and_dock_ids()
    {
        MainWindow? window = null;
        L10n.SetLanguage("zh-CN");
        try
        {
            window = new MainWindow { Width = 1360, Height = 900 };
            window.Show();
            var session = EditorSession.NewCanvas(800, 500, new SKColor(28, 38, 55));
            window.AddSession(session);
            var namedLayer = session.ActiveLayer!;
            session.Rename(namedLayer, "Normal");
            session.AddBlankLayer();
            Pump();

            var headers = Menu(window).Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
            Assert.Contains("文件(_F)", headers);
            Assert.Contains("编辑(_E)", headers);
            Assert.Contains("图层(_L)", headers);
            Assert.Contains("帮助(_H)", headers);
            var layers = window.GetVisualDescendants().OfType<LayersPanel>().Single();
            Assert.Contains(layers.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "图层");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "历史记录");

            // "Normal" is also a translatable blend mode. The person's identically named layer is data.
            Assert.Equal("正常", L10n.Text("Normal"));
            Assert.Equal("Normal", namedLayer.Name);
            var layerRow = layers.GetVisualDescendants().OfType<Border>()
                .Single(row => row.Tag is Layer layer && layer.Id == namedLayer.Id);
            Assert.Contains(layerRow.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "Normal");
            Assert.DoesNotContain(layerRow.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "正常");

            var history = window.GetVisualDescendants().OfType<HistoryPanel>().Single();
            var added = history.GetVisualDescendants().OfType<Border>()
                .Single(row => row.Tag is History.Step { Name: "New Layer" });
            Assert.Contains(added.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "新建图层");
            Assert.Equal("New Canvas", session.History.Steps[0].Name);
            Assert.Equal("New Layer", session.History.UndoName);

            var historyMenu = Menu(window).GetLogicalDescendants().OfType<MenuItem>()
                .Single(item => item.Header as string == "历史记录");
            historyMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert.False(window.Settings.Dock["History"].Visible);
            Assert.False(window.Settings.Dock.ContainsKey("历史记录"));
            historyMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert.True(window.Settings.Dock["History"].Visible);

            var file = Menu(window).Items.OfType<MenuItem>().Single(item => item.Header as string == "文件(_F)");
            file.IsSubMenuOpen = true;
            Pump();
            Assert.True(file.IsSubMenuOpen);
            Assert.True(Screenshots.Save(window, "chinese-menu"));
            // Native popup backends give the dropdown its own frame; keep that too for visual review.
            var popup = TopLevel.GetTopLevel(file.Items.OfType<MenuItem>().First());
            if (popup != null && !ReferenceEquals(popup, window))
                Assert.True(Screenshots.Save(popup, "chinese-menu-popup"));
            file.IsSubMenuOpen = false;
        }
        finally { CloseAndReset(window); }
    }

    [AvaloniaFact]
    public void Chinese_shortcut_rebinding_and_language_preferences_round_trip_without_changing_the_running_ui()
    {
        MainWindow? window = null;
        L10n.SetLanguage("zh-CN");
        try
        {
            Assert.False(Settings.Persist); // No real preferences file is read or written by this acceptance check.
            window = new MainWindow { Width = 1280, Height = 860 };
            window.Show();
            var session = EditorSession.NewCanvas(300, 200, SKColors.White);
            window.AddSession(session);
            window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
            Pump();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal(L10n.Text("Keyboard Shortcuts"), dialog.Title);
            dialog.GetVisualDescendants().OfType<TextBox>().First().Text = L10n.Text("Hand tool");
            Pump();
            var recorder = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "H");
            var point = recorder.TranslatePoint(new Point(recorder.Bounds.Width / 2, recorder.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);
            dialog.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None);
            Pump();
            Assert.Equal("K", recorder.Content);
            dialog.Close(true);
            Pump();
            Assert.Equal("K", window.Settings.Shortcuts["Hand tool"]);
            Assert.DoesNotContain(L10n.Text("Hand tool"), window.Settings.Shortcuts.Keys);
            window.Canvas.Focus();
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None);
            Assert.Equal(Tool.Hand, session.Tool);

            var dock = window.GetVisualDescendants().OfType<SideDock>().Single();
            dock.SetCollapsed(dock.Section("History"), true);
            window.SetUiLanguage("en");
            Assert.Equal("en", window.Settings.Language);
            Assert.Equal("zh-CN", L10n.CurrentLanguage);
            Assert.Equal("文件(_F)", Menu(window).Items.OfType<MenuItem>().First().Header);

            var json = JsonSerializer.Serialize(window.Settings);
            var restored = JsonSerializer.Deserialize<Settings>(json)!;
            Assert.Equal("en", restored.Language);
            Assert.Equal("K", restored.Shortcuts["Hand tool"]);
            Assert.True(restored.Dock["History"].Collapsed);
            Assert.False(restored.Dock.ContainsKey("历史记录"));
            restored.Language = "zh-CN";
            Assert.Equal("zh-CN", JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(restored))!.Language);
            Assert.Equal("system", JsonSerializer.Deserialize<Settings>("{}")!.Language);
        }
        finally { CloseAndReset(window); }
    }

    [AvaloniaFact]
    public void A_missing_text_font_is_identified_without_silently_replacing_the_document_font()
    {
        MainWindow? window = null;
        L10n.SetLanguage("zh-CN");
        try
        {
            const string missingFamily = "Composa Missing Font 4E0CB952";
            Assert.DoesNotContain(missingFamily, EditorSession.FontFamilies);
            window = new MainWindow { Width = 1500, Height = 900 };
            window.Show();
            var session = EditorSession.NewCanvas(700, 300, SKColors.White);
            window.AddSession(session);
            var layer = session.AddText(new SKPoint(50, 90), new TextStyle { Text = "中文字体回退", FontFamily = missingFamily, Size = 48 });
            window.SelectTool(Tool.Text);
            Pump();
            var font = window.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "TextFontFamily");
            var warning = window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "MissingTextFont");
            Assert.True(warning.IsVisible);
            Assert.Equal(L10n.Text("Missing font"), warning.Text);
            Assert.Contains(missingFamily, Assert.IsType<string>(ToolTip.GetTip(warning)));
            Assert.Equal(-1, font.SelectedIndex);
            Assert.Equal(missingFamily, font.PlaceholderText);
            Assert.Equal(missingFamily, layer.Text!.FontFamily);
            Assert.True(Screenshots.Save(window, "chinese-missing-font"));

            var installedFamily = EditorSession.FontFamilies.First();
            font.SelectedIndex = 0;
            Pump();
            Assert.Equal(installedFamily, session.Document.Find(layer.Id)!.Text!.FontFamily);
            Assert.False(window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "MissingTextFont").IsVisible);
            session.Undo();
            Pump();
            Assert.Equal(missingFamily, session.Document.Find(layer.Id)!.Text!.FontFamily);
            Assert.True(window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "MissingTextFont").IsVisible);
        }
        finally { CloseAndReset(window); }
    }

    [AvaloniaFact]
    public void Chinese_poster_input_color_undo_masks_save_reopen_and_png_export_preserve_editable_content()
    {
        MainWindow? window = null;
        L10n.SetLanguage("zh-CN");
        try
        {
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/acceptance"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "中文海报工作流.cmps");
            window = new MainWindow { Width = 1440, Height = 960 };
            window.Show();
            var session = EditorSession.NewCanvas(1200, 800, new SKColor(18, 25, 40));
            window.AddSession(session);
            session.Rename(session.ActiveLayer!, "深蓝背景");

            var pixels = Pixels.NewColor(600, 600);
            using (var canvas = new SKCanvas(pixels))
            using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(600, 600),
                [new SKColor(125, 211, 252), new SKColor(56, 91, 214), new SKColor(170, 93, 214)], SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader }) canvas.DrawPaint(paint);
            var artwork = session.AddImageLayer("渐变图像与圆形蒙版", pixels, new SKPoint(850, 445));
            session.SelectEllipse(new SKRect(570, 165, 1130, 725));
            session.AddMask(artwork);
            session.Deselect();
            session.EditingMask = false;

            session.TextDefaults = new TextStyle { FontFamily = "Arial", Size = 58, Color = 0xFFF1F5F9 };
            session.Foreground = new SKColor(241, 245, 249);
            window.SelectTool(Tool.Text);
            Pump();
            var point = window.Canvas.TranslatePoint(window.Canvas.ToScreen(new SKPoint(85, 225)), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.KeyTextInput("让创意，");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyTextInput("自由生长。");
            const string content = "让创意，\n自由生长。";
            Assert.Equal(content, session.TextEdit!.Text);

            var accentStart = content.IndexOf("自由", StringComparison.Ordinal);
            session.TextEdit.MoveTo(accentStart, select: false);
            session.TextEdit.MoveTo(accentStart + 4, select: true);
            session.SetTextColor(0xFF7DD3FC);
            Assert.Equal([new TextColorRun(accentStart, 4, 0xFF7DD3FC)], session.TextEdit.Style.ColorRuns);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Assert.Null(session.TextEdit.Style.ColorRuns);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal([new TextColorRun(accentStart, 4, 0xFF7DD3FC)], session.TextEdit.Style.ColorRuns);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump();
            var title = session.ActiveLayer!;
            Assert.False(session.IsEditingText);
            Assert.Equal(content, title.Text!.Text);
            session.AddText(new SKPoint(88, 470), new TextStyle
            {
                Text = "图层 · 蒙版 · 可编辑文字", FontFamily = "Arial", Size = 23, Color = 0xFF94A3B8
            });
            session.AddText(new SKPoint(88, 700), new TextStyle
            {
                Text = "Windows 中文开发版  /  保存后可继续编辑", FontFamily = "Arial", Size = 17, Color = 0xFF94A3B8
            });
            window.SelectTool(Tool.Move);

            var saving = window.SaveTo(session, path);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!saving.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
            Pump();
            Assert.True(saving.IsCompleted, "Saving the Chinese poster did not finish.");
            Assert.Null(saving.GetAwaiter().GetResult());
            Assert.False(session.IsModified);
            var reopened = new EditorSession(ProjectFile.Load(path));
            reopened.MarkSaved(path);
            Assert.Equal(5, reopened.Document.Layers.Count);
            Assert.Equal(title.Text, reopened.Document.Find(title.Id)!.Text);
            Assert.NotNull(reopened.Document.Find(artwork.Id)!.Mask);
            Assert.Equal(session.Composite().Bytes, reopened.Composite().Bytes);

            var export = Path.Combine(directory, "中文海报工作流.png");
            ImageFiles.Save(reopened.Composite(), export, ExportFormat.Png);
            using var decoded = ImageFiles.Load(export);
            Assert.Equal(reopened.Composite().Bytes, decoded.Bytes);
            window.AddSession(reopened);
            Assert.True(Screenshots.Save(window, "chinese-workflow"));
        }
        finally { CloseAndReset(window); }
    }
}
