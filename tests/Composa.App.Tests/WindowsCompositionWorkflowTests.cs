using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Composa.Editing;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>A reproducible editable sample and an acceptance check for the Windows composition workflow.</summary>
public class WindowsCompositionWorkflowTests
{
    [AvaloniaFact]
    public void Chinese_text_layers_masks_undo_save_reopen_and_export_keep_the_same_composition()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/acceptance"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Windows 中文合成.cmps");
        var window = new MainWindow { Width = 1440, Height = 960 };
        window.Show();
        var session = EditorSession.NewCanvas(1200, 800, new SKColor(18, 25, 40));
        window.AddSession(session);
        session.Rename(session.ActiveLayer!, "深蓝背景");

        // Original procedural artwork: no external image or model is required for this fixture.
        var pixels = Pixels.NewColor(600, 600);
        using (var canvas = new SKCanvas(pixels))
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(600, 600),
            [new SKColor(125, 211, 252), new SKColor(56, 91, 214), new SKColor(170, 93, 214)], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawPaint(paint);
        var artwork = session.AddImageLayer("可编辑图像 + 圆形蒙版", pixels, new SKPoint(850, 445));
        session.SelectEllipse(new SKRect(570, 165, 1130, 725));
        session.AddMask(artwork);
        session.Deselect();
        session.EditingMask = false;
        Assert.NotNull(artwork.Mask);

        session.TextDefaults = new TextStyle { FontFamily = "Arial", Size = 58, Color = 0xFFF1F5F9 };
        session.Foreground = new SKColor(241, 245, 249);
        window.SelectTool(Tool.Text);
        Dispatcher.UIThread.RunJobs();
        var point = window.Canvas.TranslatePoint(window.Canvas.ToScreen(new SKPoint(85, 225)), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.KeyTextInput("你的创意\n自由表达");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        var title = session.ActiveLayer!;
        Assert.Equal("你的创意\n自由表达", title.Text!.Text);
        Assert.False(session.IsEditingText);
        session.AddText(new SKPoint(88, 445), new TextStyle
        {
            Text = "LAYERS / MASKS / EDITABLE TYPE", FontFamily = "Arial", Size = 17,
            Tracking = 1, Color = 0xFF7DD3FC
        });
        session.AddText(new SKPoint(88, 700), new TextStyle
        {
            Text = "Windows 开发样例  ·  所有图层均可继续编辑", FontFamily = "Arial", Size = 17,
            Color = 0xFF94A3B8
        });
        window.SelectTool(Tool.Move);
        Assert.Equal(5, session.Document.Layers.Count);

        // Undo restores document snapshots, so always resolve layers again after undo/redo.
        session.Begin("Layer Opacity");
        session.SetOpacity(session.Document.Find(artwork.Id)!, 0.65);
        session.Commit();
        session.Undo();
        Assert.Equal(1, session.Document.Find(artwork.Id)!.Opacity);
        Assert.Equal(5, session.Document.Layers.Count);
        session.Redo();
        Assert.Equal(0.65, session.Document.Find(artwork.Id)!.Opacity);
        session.Undo();
        Assert.Equal(5, session.Document.Layers.Count);

        var saving = window.SaveTo(session, path);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!saving.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(saving.IsCompleted, "Saving the composition did not finish.");
        Assert.Null(saving.GetAwaiter().GetResult());
        Assert.False(session.IsModified);

        var reopened = new EditorSession(ProjectFile.Load(path));
        Assert.Equal(session.Document.Layers.Count, reopened.Document.Layers.Count);
        Assert.Equal("你的创意\n自由表达", reopened.Document.Find(title.Id)!.Text!.Text);
        Assert.NotNull(reopened.Document.Find(artwork.Id)!.Mask);
        Assert.Equal(session.Composite().Bytes, reopened.Composite().Bytes);
        var export = Path.Combine(directory, "Windows 中文合成.png");
        ImageFiles.Save(reopened.Composite(), export, ExportFormat.Png);
        using var decoded = ImageFiles.Load(export);
        Assert.Equal(reopened.Composite().Bytes, decoded.Bytes);
        Screenshots.Save(window, "windows-composition-workflow");
        window.Close();
    }
}
