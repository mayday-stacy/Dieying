using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.App.Controls;
using Composa.App.Dialogs;
using Composa.Editing;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Tests;

public class ImageExportTests
{
    private static async Task Pump(Func<bool> until)
    {
        for (var i = 0; i < 500 && !until(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(until());
    }

    private static string Temporary(string extension = "png") => Path.Combine(Path.GetTempPath(), $"composa-export-{Guid.NewGuid():N}.{extension}");

    [Theory]
    [InlineData(ExportFormat.Png)]
    [InlineData(ExportFormat.Webp)]
    public void Resized_png_and_webp_preserve_transparency_without_changing_the_source(ExportFormat format)
    {
        using var source = Pixels.NewColor(80, 40);
        source.Erase(new SKColor(220, 40, 80, 100));
        var before = source.GetPixel(20, 20);
        var bytes = ImageExport.Encode(source, new(format, 40, 20, 100));
        using var decoded = SKBitmap.Decode(bytes);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(20, decoded.Height);
        Assert.InRange(decoded.GetPixel(20, 10).Alpha, (byte)99, (byte)101);
        Assert.Equal(before, source.GetPixel(20, 20));
        Assert.Equal(80, source.Width);
    }

    [Fact]
    public void Jpeg_background_is_chosen_explicitly_and_quality_changes_the_encoded_file()
    {
        using var source = Pixels.NewColor(160, 80);
        for (var y = 0; y < 80; y++) for (var x = 80; x < 160; x++)
            source.SetPixel(x, y, new SKColor((byte)(x * 19), (byte)(y * 17), (byte)(x + y * 5)));
        var whiteBytes = ImageExport.Encode(source, new(ExportFormat.Jpeg, 160, 80, 100, SKColors.White));
        var blackBytes = ImageExport.Encode(source, new(ExportFormat.Jpeg, 160, 80, 10, SKColors.Black));
        using var white = SKBitmap.Decode(whiteBytes);
        using var black = SKBitmap.Decode(blackBytes);
        Assert.InRange(white.GetPixel(10, 10).Red, (byte)250, (byte)255);
        Assert.InRange(black.GetPixel(10, 10).Red, (byte)0, (byte)5);
        Assert.Equal(255, white.GetPixel(10, 10).Alpha);
        Assert.True(whiteBytes.Length > blackBytes.Length);
        Assert.Equal(0, source.GetPixel(10, 10).Alpha);
    }

    [Fact]
    public void Invalid_size_is_rejected_before_replacing_an_existing_file_and_failed_replace_cleans_up()
    {
        using var source = Pixels.NewColor(2, 2);
        var path = Temporary();
        File.WriteAllText(path, "existing file");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageExport.Save(source, path, new(ExportFormat.Png, DocumentLimits.MaxSide, DocumentLimits.MaxSide)));
            Assert.Equal("existing file", File.ReadAllText(path));
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                if (OperatingSystem.IsWindows())
                {
                    var error = Record.Exception(() => ImageExport.Save(source, path, new(ExportFormat.Png, 2, 2)));
                    Assert.True(error is IOException or UnauthorizedAccessException,
                        $"Expected an IO or access-denied error; received {error?.GetType().FullName ?? "no exception"}.");
                }
            }
            Assert.Equal("existing file", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".tmp-*"));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task Export_uses_a_snapshot_and_preserves_document_size_history_and_dirty_state()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        var session = EditorSession.NewCanvas(1200, 800, SKColors.Red);
        window.AddSession(session);
        session.Fill(SKColors.Green);
        var state = session.History.CurrentId;
        var path = Temporary();
        try
        {
            var task = window.ExportTo(session, path, new(ExportFormat.Png, 600, 400));
            Assert.Equal(state, session.History.CurrentId);
            Assert.True(session.IsModified);
            session.Fill(SKColors.Blue);
            Assert.Null(await task);
            Assert.False(window.IsExporting(session));
            using var saved = ImageFiles.Load(path);
            Assert.Equal(SKColors.Green, saved.GetPixel(20, 20));
            Assert.Equal((600, 400), (saved.Width, saved.Height));
            Assert.Equal((1200, 800), (session.Document.Width, session.Document.Height));
            Assert.True(session.IsModified);
            Assert.Equal(SKColors.Blue, session.ActiveLayer!.Pixels!.GetPixel(20, 20));
        }
        finally { File.Delete(path); session.MarkSaved(path); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Exports_to_one_path_are_ordered_and_quit_waits_for_the_last_file()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        var session = EditorSession.NewCanvas(1500, 1000, SKColors.Red);
        window.AddSession(session);
        var path = Temporary();
        try
        {
            var first = window.ExportTo(session, path, new(ExportFormat.Png, 1500, 1000));
            session.Fill(SKColors.Green);
            var second = window.ExportTo(session, path, new(ExportFormat.Png, 750, 500));
            session.Fill(SKColors.Blue);
            var third = window.ExportTo(session, path, new(ExportFormat.Png, 300, 200));
            session.MarkSaved(Temporary("cmps"));
            Assert.True(window.IsExporting(session));
            window.Close();
            Assert.True(window.IsVisible);
            Assert.All(await Task.WhenAll(first, second, third), error => Assert.Null(error));
            await Pump(() => !window.IsVisible);
            Assert.False(window.IsExporting(session));
            using var saved = ImageFiles.Load(path);
            Assert.Equal((300, 200), (saved.Width, saved.Height));
            Assert.Equal(SKColors.Blue, saved.GetPixel(20, 20));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".tmp-*"));
        }
        finally { File.Delete(path); session.MarkSaved(path); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Export_refuses_an_open_non_text_edit_without_writing_a_file()
    {
        var window = new MainWindow();
        window.Show();
        var session = EditorSession.NewCanvas(40, 20, SKColors.Red);
        window.AddSession(session);
        var path = Temporary();
        session.Begin("Drag");
        try
        {
            Assert.IsType<InvalidOperationException>(await window.ExportTo(session, path, new(ExportFormat.Png, 40, 20)));
            Assert.False(File.Exists(path));
            Assert.True(session.HasPendingEdit);
        }
        finally { session.Cancel(); session.MarkSaved(path); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Export_commits_typed_text_but_cancels_unconfirmed_ime_candidates()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        var session = EditorSession.NewCanvas(300, 160, SKColors.White);
        window.AddSession(session);
        var layer = session.AddText(new SKPoint(30, 40), new TextStyle { Text = "你", Size = 30 });
        window.Canvas.EditText(layer);
        session.TextEdit!.Insert("好");
        var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        window.Canvas.RaiseEvent(request);
        Assert.NotNull(request.Client);
        request.Client.SetPreeditText("ni", 2);
        Assert.True(window.Canvas.IsImeComposing);
        var path = Temporary();
        try
        {
            Assert.Null(await window.ExportTo(session, path, new(ExportFormat.Png, 300, 160)));
            Assert.False(window.Canvas.IsImeComposing);
            Assert.False(session.IsEditingText);
            Assert.Equal("你好", session.Document.Find(layer.Id)!.Text!.Text);
            using var expected = session.Flatten();
            using var saved = ImageFiles.Load(path);
            Assert.True(expected.GetPixelSpan().SequenceEqual(saved.GetPixelSpan()));
            Assert.True(session.IsModified);
        }
        finally { File.Delete(path); session.MarkSaved(path); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Chinese_webp_dialog_previews_controls_size_and_quality_and_returns_choices()
    {
        L10n.SetLanguage("zh-CN");
        var window = new MainWindow { Width = 1000, Height = 800 };
        window.Show();
        using var source = Pixels.NewColor(480, 240);
        using (var drawing = new SKCanvas(source))
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(480, 240), [SKColors.Teal, SKColors.Gold], null, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader }) drawing.DrawPaint(paint);
        try
        {
            var task = ImageExportDialog.Show(window, source, new(ExportFormat.Webp, 480, 240, 80));
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            T Named<T>(string name) where T : Control => dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            Assert.Equal("导出 WebP", dialog.Title);
            Named<NumericUpDown>("ExportWidth").Value = 240;
            Assert.Equal(120, Named<NumericUpDown>("ExportHeight").Value);
            var quality = Named<SliderField>("ExportQuality");
            quality.Focus();
            dialog.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            Assert.Equal(79, quality.Value);
            await Pump(() => Named<Image>("ExportPreview").Source != null && Named<TextBlock>("ExportFileSize").Text!.Contains("240 × 120"));
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "保留透明区域。");
            Screenshots.Save(dialog, "chinese-webp-export");
            dialog.Close(true);
            var result = await task;
            Assert.NotNull(result);
            Assert.Equal((240, 120, 79), (result.Width, result.Height, result.Quality));
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); L10n.SetLanguage("en"); }
    }

    [AvaloniaFact]
    public async Task Jpeg_dialog_offers_background_and_png_has_no_quality_control()
    {
        var window = new MainWindow { Width = 1000, Height = 800 };
        window.Show();
        using var source = Pixels.NewColor(80, 40);
        try
        {
            var jpeg = ImageExportDialog.Show(window, source, new(ExportFormat.Jpeg, 80, 40));
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ExportMatte").SelectedIndex = 1;
            dialog.Close(true);
            Assert.Equal(SKColors.Black, (await jpeg)!.Matte);

            var png = ImageExportDialog.Show(window, source, new(ExportFormat.Png, 80, 40));
            Dispatcher.UIThread.RunJobs();
            dialog = Assert.Single(window.OwnedWindows);
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<SliderField>(), c => c.Name == "ExportQuality");
            var width = dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ExportWidth");
            width.Value = 40;
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Original size").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(80, width.Value);
            dialog.Close();
            Assert.Null(await png);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }
}
