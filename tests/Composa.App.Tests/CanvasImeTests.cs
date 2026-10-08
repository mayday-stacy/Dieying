using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Composa.Editing;
using Composa.Model;
using Composa.Rendering;
using Composa.Text;
using SkiaSharp;

namespace Composa.App.Tests;

/// <summary>Exercises Avalonia's real text input client contract; native candidate windows still need an IME on Windows.</summary>
public class CanvasImeTests
{
    private readonly MainWindow window;
    private readonly EditorSession session;

    public CanvasImeTests()
    {
        window = new MainWindow { Width = 1280, Height = 800 };
        window.Show();
        session = EditorSession.NewCanvas(900, 500, new SKColor(30, 34, 46));
        window.AddSession(session);
        Dispatcher.UIThread.RunJobs();
    }

    private Layer OpenText(string text = "甲乙丙丁")
    {
        var layer = session.AddText(new SKPoint(100, 130), new TextStyle
        {
            Text = text, FontFamily = "Arial", Size = 40, Color = 0xFFFFC857
        });
        window.Canvas.EditText(layer);
        Dispatcher.UIThread.RunJobs();
        return layer;
    }

    private TextInputMethodClient? RequestedClient()
    {
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        window.Canvas.RaiseEvent(args);
        return args.Client;
    }

    private TextInputMethodClient Client() => Assert.IsAssignableFrom<TextInputMethodClient>(RequestedClient());

    [AvaloniaFact]
    public void The_client_exists_only_for_the_focused_open_text_edit_and_old_callbacks_are_ignored()
    {
        Assert.Null(RequestedClient());
        var layer = OpenText();
        var client = Client();
        Assert.Same(window.Canvas, client.TextViewVisual);
        Assert.True(client.SupportsPreedit);
        Assert.False(client.SupportsSurroundingText); // IMM32 must not delete the original selection on composition start.
        Assert.Equal("甲乙丙丁", client.SurroundingText);
        client.SetPreeditText("ni", 1);
        Assert.True(window.Canvas.IsImeComposing);
        var resets = 0;
        client.ResetRequested += (_, _) => resets++;

        // Moving into a real toolbar field releases the canvas client without committing the candidate.
        var size = window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Value == 40);
        var field = size.GetVisualDescendants().OfType<TextBox>().First();
        field.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(RequestedClient());
        Assert.False(window.Canvas.IsImeComposing);
        Assert.True(resets > 0);
        Assert.Equal("甲乙丙丁", layer.Text!.Text);
        client.SetPreeditText("late", 4);
        Assert.False(window.Canvas.IsImeComposing);

        window.Canvas.Focus();
        var fresh = Client();
        Assert.NotSame(client, fresh);
        client.Selection = new TextSelection(0, 1);
        Assert.Equal(4, session.TextEdit!.Caret);
        session.FinishText();
        Assert.Null(RequestedClient());
        Assert.Equal("", fresh.SurroundingText);
    }

    [AvaloniaFact]
    public void Preedit_previews_replacement_without_changing_pixels_text_history_or_the_original_selection()
    {
        var layer = OpenText();
        var editor = session.TextEdit!;
        editor.MoveTo(1, false);
        editor.MoveTo(3, true);
        var client = Client();
        var originalPixels = layer.Pixels;
        var originalComposite = session.Composite().Bytes;
        var history = session.History.CurrentId;
        client.SetPreeditText("ni hao", 2);
        Assert.True(window.Canvas.IsImeComposing);
        Assert.Equal("甲乙丙丁", editor.Text);
        Assert.Equal("乙丙", editor.SelectedText);
        Assert.Same(originalPixels, layer.Pixels);
        Assert.Equal(originalComposite, session.Composite().Bytes);
        Assert.Equal(history, session.History.CurrentId);
        Assert.False(editor.CanUndo);
        Assert.True(Screenshots.Save(window, "ime-preedit-selection"));

        client.SetPreeditText(null, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.Canvas.IsImeComposing);
        Assert.Equal("甲乙丙丁", layer.Text!.Text);
        Assert.Equal("乙丙", editor.SelectedText);
        Assert.Equal(history, session.History.CurrentId);
        Assert.False(editor.CanUndo);
        Assert.Same(originalPixels, layer.Pixels);
    }

    [AvaloniaFact]
    public void A_committed_candidate_replaces_the_selection_once_and_typing_undo_restores_it()
    {
        var layer = OpenText();
        var editor = session.TextEdit!;
        var history = session.History.Count;
        Client().Selection = new TextSelection(1, 3);
        var client = Client();
        client.SetPreeditText("ni hao", 6);
        // Windows clears its preedit before sending the final TextInput event.
        client.SetPreeditText(null, null);
        window.KeyTextInput("你好");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("甲你好丁", layer.Text!.Text);
        Assert.Equal(3, editor.Caret);
        Assert.False(editor.HasSelection);
        Assert.False(window.Canvas.IsImeComposing);
        Assert.Equal(history, session.History.Count);
        Assert.True(editor.Undo());
        Assert.Equal("甲乙丙丁", layer.Text.Text);
        Assert.Equal("乙丙", editor.SelectedText);
        Assert.True(editor.Redo());
        Assert.Equal("甲你好丁", layer.Text.Text);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.False(session.IsEditingText);
        Assert.Equal(history + 1, session.History.Count);
    }

    [AvaloniaFact]
    public void Composition_keys_are_left_for_the_platform_without_editing_or_closing_the_text()
    {
        var layer = OpenText();
        var editor = session.TextEdit!;
        editor.SelectAll();
        Client().SetPreeditText("zhong", 5);
        var handled = new List<bool>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled), RoutingStrategies.Bubble, handledEventsToo: true);
        foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Escape, PhysicalKey.Backspace, PhysicalKey.Delete,
            PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight, PhysicalKey.ArrowUp, PhysicalKey.ArrowDown, PhysicalKey.Home, PhysicalKey.End })
            window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(handled);
        Assert.All(handled, value => Assert.False(value));
        Assert.True(session.IsEditingText);
        Assert.True(window.Canvas.IsImeComposing);
        Assert.Equal("甲乙丙丁", layer.Text!.Text);
        Assert.Equal("甲乙丙丁", editor.SelectedText);
        Assert.False(editor.CanUndo);
    }

    [AvaloniaFact]
    public void Candidate_geometry_tracks_zoom_rotation_and_the_cursor_inside_the_preedit()
    {
        var layer = OpenText("候选位置");
        session.FinishText();
        // Finishing an unchanged edit restores its snapshot; resolve the live layer after that restore.
        layer = session.Document.Find(layer.Id)!;
        session.Begin("Rotate");
        session.SetTransform(layer, layer.Transform with { Rotation = 30 });
        session.Commit();
        window.Canvas.EditText(layer);
        Dispatcher.UIThread.RunJobs();
        var client = Client();
        var notifications = 0;
        client.CursorRectangleChanged += (_, _) => notifications++;
        var first = client.CursorRectangle;
        Assert.True(first.Width > 1 && first.Height > 1); // The rotated caret has an axis-aligned bounding rectangle.
        window.Canvas.ZoomTo(2);
        var zoomed = client.CursorRectangle;
        Assert.NotEqual(first, zoomed);
        Assert.True(notifications > 0);

        var caret = session.TextEdit!.Layout.CaretAt(session.TextEdit.Caret);
        var top = window.Canvas.ToScreen(layer.Matrix.MapPoint(caret.X, caret.Top));
        var bottom = window.Canvas.ToScreen(layer.Matrix.MapPoint(caret.X, caret.Bottom));
        Assert.Equal(Math.Min(top.X, bottom.X), zoomed.X, 0.01);
        Assert.Equal(Math.Max(top.Y, bottom.Y), zoomed.Bottom, 0.01);

        client.SetPreeditText("中文输入", 0);
        var start = client.CursorRectangle;
        client.SetPreeditText("中文输入", 2);
        var middle = client.CursorRectangle;
        client.SetPreeditText("中文输入", 4);
        Assert.NotEqual(start, middle);
        Assert.NotEqual(middle, client.CursorRectangle);
        Assert.Equal("候选位置", layer.Text!.Text);
        Assert.True(Screenshots.Save(window, "ime-preedit-rotated"));
    }

    [AvaloniaFact]
    public void Switching_layers_and_documents_discards_preedit_and_invalidates_the_old_client()
    {
        var firstLayer = OpenText("第一层");
        var first = Client();
        first.SetPreeditText("pending", 7);
        var secondLayer = session.AddText(new SKPoint(200, 200), new TextStyle { Text = "第二层", Size = 40 });
        window.Canvas.EditText(secondLayer);
        var second = Client();
        Assert.NotSame(first, second);
        Assert.Equal("第一层", firstLayer.Text!.Text);
        Assert.False(window.Canvas.IsImeComposing);
        first.SetPreeditText("late", 4);
        Assert.False(window.Canvas.IsImeComposing);
        second.SetPreeditText("xin", 3);
        window.AddSession(EditorSession.NewCanvas(400, 300, SKColors.White));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(RequestedClient());
        Assert.Equal("第二层", secondLayer.Text!.Text);
        second.SetPreeditText("late", 4);
        Assert.False(window.Canvas.IsImeComposing);
    }

    [AvaloniaFact]
    public void Cancelling_from_a_command_preserves_text_and_selection_and_accepts_a_new_composition()
    {
        OpenText();
        var editor = session.TextEdit!;
        editor.SelectAll();
        var old = Client();
        old.SetPreeditText("bu", 2);
        window.Canvas.CancelImeComposition();
        Assert.False(window.Canvas.IsImeComposing);
        Assert.Equal("甲乙丙丁", editor.SelectedText);
        old.SetPreeditText("late", 4);
        Assert.False(window.Canvas.IsImeComposing);
        var fresh = Client();
        Assert.NotSame(old, fresh);
        fresh.SetPreeditText("hao", 3);
        window.KeyTextInput("好"); // Other platforms may commit before clearing the preedit.
        Assert.Equal("好", editor.Text);
        Assert.False(window.Canvas.IsImeComposing);
    }

    private Layer OpenMaskedText(bool paragraph = false)
    {
        var layer = session.AddText(new SKPoint(100, 130), new TextStyle
        {
            Text = "原始文字", FontFamily = "Arial", Size = 40,
            BoxWidth = paragraph ? 320 : null, BoxHeight = paragraph ? 160 : null
        });
        session.AddMask(layer);
        window.Canvas.EditText(layer);
        session.TextEdit!.SelectAll();
        Dispatcher.UIThread.RunJobs();
        return layer;
    }

    // Read the client-owned session without adding a public API for transient UI resources.
    private static EditorSession PreviewSession(TextInputMethodClient client) =>
        Assert.IsType<EditorSession>(client.GetType().GetProperty("Preview")!.GetValue(client));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacing_and_cancelling_preedit_releases_only_its_own_pixels_and_resized_masks(bool paragraph)
    {
        var layer = OpenMaskedText(paragraph);
        var original = session.Document.Clone(); // Shares the committed pixels, just like undo and a save snapshot.
        var pixels = layer.Pixels!;
        var mask = layer.Mask!;
        var pixelBytes = pixels.Bytes;
        var maskBytes = mask.Bytes;
        var history = session.History.CurrentId;
        var client = Client();
        SKBitmap? previousPixels = null, previousMask = null;
        SKImage? cachedImage = null;
        foreach (var text in new[] { "n", "ni", "ni hao", "zhong wen", "zhong wen shu ru" })
        {
            client.SetPreeditText(text, text.Length);
            if (previousPixels != null)
            {
                Assert.Equal(IntPtr.Zero, previousPixels.Handle);
                Assert.Equal(IntPtr.Zero, cachedImage!.Handle); // No cached SKImage may keep using freed pixels.
                if (!ReferenceEquals(previousMask, mask)) Assert.Equal(IntPtr.Zero, previousMask!.Handle);
            }
            var preview = PreviewSession(client);
            Assert.True(preview.IsInteracting);
            Assert.Equal(0, preview.History.Count); // Preedit pixels never become committed snapshots.
            var previewLayer = preview.Document.Find(layer.Id)!;
            previousPixels = previewLayer.Pixels!;
            previousMask = previewLayer.Mask!;
            cachedImage = Pixels.ImageOf(previousPixels);
            Assert.NotSame(pixels, previousPixels);
            if (paragraph) Assert.Same(mask, previousMask);
            else Assert.NotSame(mask, previousMask);
            Assert.NotEqual(IntPtr.Zero, pixels.Handle);
            Assert.NotEqual(IntPtr.Zero, mask.Handle);
            Assert.Equal(history, session.History.CurrentId);
        }
        Assert.True(Screenshots.Save(window, paragraph ? "ime-owned-preview-box" : "ime-owned-preview-point"));
        window.Canvas.CancelImeComposition();
        Assert.Equal(IntPtr.Zero, previousPixels!.Handle);
        Assert.Equal(IntPtr.Zero, cachedImage!.Handle);
        if (!paragraph) Assert.Equal(IntPtr.Zero, previousMask!.Handle);
        Assert.False(window.Canvas.IsImeComposing);
        Assert.Equal("原始文字", session.TextEdit!.SelectedText);
        Assert.Same(pixels, original.Find(layer.Id)!.Pixels);
        Assert.Same(mask, original.Find(layer.Id)!.Mask);
        Assert.Same(pixels, layer.Pixels);
        Assert.Same(mask, layer.Mask);
        Assert.Equal(pixelBytes, pixels.Bytes);
        Assert.Equal(maskBytes, mask.Bytes);
        session.CancelText();
        Assert.Equal(pixelBytes, session.Document.Find(layer.Id)!.Pixels!.Bytes);
        Assert.Equal(maskBytes, session.Document.Find(layer.Id)!.Mask!.Bytes);
    }

    [AvaloniaFact]
    public void Losing_focus_releases_preedit_bitmaps_but_keeps_the_real_layer_and_mask_alive()
    {
        var layer = OpenMaskedText();
        var pixels = layer.Pixels!;
        var mask = layer.Mask!;
        var client = Client();
        client.SetPreeditText("n", 1);
        var previewLayer = PreviewSession(client).Document.Find(layer.Id)!;
        var previewPixels = previewLayer.Pixels!;
        var previewMask = previewLayer.Mask!;
        Assert.NotSame(mask, previewMask);
        var size = window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Value == 40);
        size.GetVisualDescendants().OfType<TextBox>().First().Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(IntPtr.Zero, previewPixels.Handle);
        Assert.Equal(IntPtr.Zero, previewMask.Handle);
        Assert.NotEqual(IntPtr.Zero, pixels.Handle);
        Assert.NotEqual(IntPtr.Zero, mask.Handle);
        Assert.False(window.Canvas.IsImeComposing);
        Assert.Equal("原始文字", session.TextEdit!.SelectedText);
        client.SetPreeditText("late", 4);
        Assert.Null(client.GetType().GetProperty("Preview")!.GetValue(client));
    }

    [AvaloniaFact]
    public void Committing_preedit_releases_the_preview_and_preserves_the_original_undo_pixels_and_mask()
    {
        var layer = OpenMaskedText();
        var originalPixels = layer.Pixels!;
        var originalMask = layer.Mask!;
        var client = Client();
        client.SetPreeditText("ni hao", 6);
        var previewLayer = PreviewSession(client).Document.Find(layer.Id)!;
        var previewPixels = previewLayer.Pixels!;
        var previewMask = previewLayer.Mask!;
        window.KeyTextInput("你好");
        Assert.Equal(IntPtr.Zero, previewPixels.Handle);
        Assert.Equal(IntPtr.Zero, previewMask.Handle);
        Assert.NotSame(previewPixels, layer.Pixels);
        Assert.NotEqual(IntPtr.Zero, layer.Pixels!.Handle);
        Assert.NotEqual(IntPtr.Zero, layer.Mask!.Handle);
        Assert.Equal("你好", layer.Text!.Text);
        session.FinishText();
        session.Undo();
        var restored = session.Document.Find(layer.Id)!;
        Assert.Same(originalPixels, restored.Pixels);
        Assert.Same(originalMask, restored.Mask);
        Assert.NotEqual(IntPtr.Zero, originalPixels.Handle);
        Assert.NotEqual(IntPtr.Zero, originalMask.Handle);
        Assert.Equal("原始文字", restored.Text!.Text);
    }
}
