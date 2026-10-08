using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Composa.Editing;
using Composa.Model;
using Composa.Rendering;
using Composa.Text;
using SkiaSharp;

namespace Composa.App.Controls;

/// <summary>The platform input method edits a transient preview; only committed TextInput reaches the document.</summary>
public sealed partial class CanvasView
{
    private CanvasTextInputClient? textInputClient;
    private TopLevel? imeTopLevel;

    /// <summary>The input method owns editing keys while it is choosing uncommitted text.</summary>
    public bool IsImeComposing => textInputClient?.IsComposing == true;

    private void InitializeTextInput()
    {
        TextInputOptions.SetMultiline(this, true);
        TextInputMethodClientRequested += (_, e) =>
        {
            SynchronizeTextInput(requery: false);
            e.Client = textInputClient;
        };
        GotFocus += (_, _) => SynchronizeTextInput();
        LostFocus += (_, _) => ReleaseTextInput();
        ViewChanged += NotifyImeGeometry;
        LayoutUpdated += (_, _) => NotifyImeGeometry();
        AttachedToVisualTree += (_, _) =>
        {
            imeTopLevel = TopLevel.GetTopLevel(this);
            if (imeTopLevel != null) imeTopLevel.ScalingChanged += OnImeScalingChanged;
            SynchronizeTextInput();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ReleaseTextInput();
            if (imeTopLevel != null) imeTopLevel.ScalingChanged -= OnImeScalingChanged;
            imeTopLevel = null;
        };
    }

    private void OnImeScalingChanged(object? sender, EventArgs e) => NotifyImeGeometry();

    private void NotifyImeGeometry() => textInputClient?.NotifyGeometry();

    private void SynchronizeTextInput(bool requery = true)
    {
        var editor = IsFocused ? session?.TextEdit : null;
        if (ReferenceEquals(textInputClient?.Editor, editor)) return;
        var previous = textInputClient;
        textInputClient = null; // Invalidate old callbacks before asking the platform to reset.
        previous?.Deactivate();
        if (editor != null) textInputClient = new CanvasTextInputClient(this, editor);
        if (requery) RequeryTextInput();
    }

    private void ReleaseTextInput()
    {
        var previous = textInputClient;
        textInputClient = null;
        previous?.Deactivate();
    }

    private void RequeryTextInput() => RaiseEvent(new TextInputMethodClientRequeryRequestedEventArgs
    {
        RoutedEvent = InputMethod.TextInputMethodClientRequeryRequestedEvent
    });

    private void OnImeTextChanged()
    {
        SynchronizeTextInput();
        textInputClient?.NotifyEditorChanged();
    }

    /// <summary>
    /// Discards the uncommitted composition, retaining the original text and selection. Commands such as Save can
    /// call this before finishing the text edit. A fresh client prevents a late callback from reviving the preview.
    /// </summary>
    public void CancelImeComposition()
    {
        if (!IsImeComposing) return;
        ReleaseTextInput();
        SynchronizeTextInput();
    }

    private sealed class CanvasTextInputClient(CanvasView owner, TextEditor editor) : TextInputMethodClient
    {
        public TextEditor Editor { get; } = editor;
        private bool active = true;
        private string preedit = "";
        private int compositionStart, compositionEnd, compositionCursor;
        private TextStyle? compositionStyle;
        private Rect? lastCursorRectangle;
        private TextPreview? previewResources;

        // A preedit is not a history state. Its one edited layer owns only the new pixels and, when resized,
        // its new mask. Everything else still belongs to the real document or its undo snapshots.
        private sealed class TextPreview : IDisposable
        {
            public EditorSession Session { get; }
            public Layer Layer { get; }
            private readonly SKBitmap? originalPixels, originalMask;
            private bool disposed;

            public TextPreview(EditorSession source, Guid layerId)
            {
                Session = new EditorSession(source.Document.Clone()) { SoloLayerId = source.SoloLayerId };
                Layer = Session.Document.Find(layerId)!;
                originalPixels = Layer.Pixels;
                originalMask = Layer.Mask;
                Session.Begin("Text Preview");
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                var pixels = Layer.Pixels;
                var mask = Layer.Mask;
                Session.Cancel(); // Detach the uncommitted resources before releasing them.
                if (pixels != null && !ReferenceEquals(pixels, originalPixels))
                {
                    Pixels.Invalidate(pixels);
                    pixels.Dispose();
                }
                if (mask != null && !ReferenceEquals(mask, originalMask) && !ReferenceEquals(mask, pixels))
                {
                    Pixels.Invalidate(mask);
                    mask.Dispose();
                }
            }
        }

        public EditorSession? Preview { get; private set; }
        public TextLayout? PreviewLayout { get; private set; }
        public Layer? PreviewLayer => Preview?.Document.Find(owner.session?.TextEditLayer?.Id ?? Guid.Empty);
        public int PreviewCaret => compositionStart + compositionCursor;
        public int PreviewStart => compositionStart;
        public int PreviewEnd => compositionStart + preedit.Length;
        private bool IsCurrent => active && owner.IsFocused && ReferenceEquals(owner.textInputClient, this)
            && ReferenceEquals(owner.session?.TextEdit, Editor);
        public bool IsComposing => IsCurrent && preedit.Length > 0;

        public override Visual TextViewVisual => owner;
        public override bool SupportsPreedit => true;
        // Avalonia 12's IMM32 backend synthesizes Delete on composition start when this is true and text is
        // selected. Keep replacement atomic until TextInput, so cancelling a candidate preserves the selection.
        // System reconversion of surrounding text is not advertised until that backend behavior can be handled.
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => IsCurrent ? Editor.Text : "";
        public override TextSelection Selection
        {
            get => IsCurrent ? new TextSelection(Editor.Anchor, Editor.Caret) : default;
            set
            {
                if (!IsCurrent) return;
                owner.CancelImeComposition();
                Editor.MoveTo(value.Start, select: false);
                Editor.MoveTo(value.End, select: true);
            }
        }

        public override Rect CursorRectangle
        {
            get
            {
                if (!IsCurrent || (PreviewLayer ?? owner.session?.TextEditLayer) is not { } layer) return default;
                var layout = PreviewLayout ?? Editor.Layout;
                var (x, top, bottom) = layout.CaretAt(IsComposing ? PreviewCaret : Editor.Caret);
                var matrix = layer.Matrix;
                var a = owner.ToScreen(matrix.MapPoint(x, top));
                var b = owner.ToScreen(matrix.MapPoint(x, bottom));
                // Control units, not device pixels: Avalonia maps the visual to the top level and applies DPI.
                return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(1, Math.Abs(b.X - a.X)), Math.Max(1, Math.Abs(b.Y - a.Y)));
            }
        }

        public override void SetPreeditText(string? text) => SetPreeditText(text, null);

        public override void SetPreeditText(string? text, int? cursorPos)
        {
            if (!IsCurrent) return;
            if (string.IsNullOrEmpty(text)) { ClearPreview(); return; }
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > TextStyle.MaxLength - (Editor.Text.Length - (Editor.SelectionEnd - Editor.SelectionStart)))
            {
                ClearPreview();
                return;
            }
            var start = Editor.SelectionStart;
            var end = Editor.SelectionEnd;
            if (preedit != text || compositionStyle != Editor.Style || compositionStart != start || compositionEnd != end)
            {
                preedit = text;
                compositionStyle = Editor.Style;
                compositionStart = start;
                compositionEnd = end;
                var style = Editor.Style.WithReplacedCharacters(start, end, text.Length) with
                {
                    Text = Editor.Text.Remove(start, end - start).Insert(start, text)
                };
                // This session owns only the preview. SetText reuses the editor's alignment, transform and mask
                // resizing behavior, while the real session's pixels, history and save snapshot stay untouched.
                var preview = new TextPreview(owner.session!, owner.session!.TextEditLayer!.Id);
                try
                {
                    preview.Session.SetText(preview.Layer, style);
                    var layout = new TextLayout(style);
                    ReleasePreviewResources();
                    previewResources = preview;
                    Preview = preview.Session;
                    PreviewLayout = layout;
                }
                catch
                {
                    preview.Dispose();
                    ClearPreview();
                    throw;
                }
                owner.viewStale = true;
            }
            compositionCursor = Math.Clamp(cursorPos ?? text.Length, 0, text.Length);
            // Platform offsets are UTF-16. A malformed offset must not put a caret between a surrogate pair.
            if (compositionCursor > 0 && compositionCursor < text.Length
                && char.IsHighSurrogate(text[compositionCursor - 1]) && char.IsLowSurrogate(text[compositionCursor])) compositionCursor--;
            owner.InvalidateVisual();
            NotifyGeometry();
        }

        public void ClearPreview()
        {
            var hadPreview = Preview != null;
            preedit = "";
            compositionStyle = null;
            ReleasePreviewResources();
            if (hadPreview)
            {
                owner.viewStale = true;
                owner.InvalidateVisual();
            }
            NotifyGeometry();
        }

        private void ReleasePreviewResources()
        {
            var resources = previewResources;
            previewResources = null;
            Preview = null;
            PreviewLayout = null;
            resources?.Dispose();
        }

        public void Deactivate()
        {
            active = false;
            ClearPreview();
            RequestReset();
        }

        public void NotifyEditorChanged()
        {
            if (!IsCurrent) return;
            if (IsComposing && (compositionStyle != Editor.Style || compositionStart != Editor.SelectionStart || compositionEnd != Editor.SelectionEnd))
            {
                owner.CancelImeComposition();
                return;
            }
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
            NotifyGeometry();
        }

        public void NotifyGeometry()
        {
            if (!IsCurrent) return;
            var rectangle = CursorRectangle;
            if (lastCursorRectangle == rectangle) return;
            lastCursorRectangle = rectangle;
            RaiseCursorRectangleChanged();
        }

        public override void ExecuteContextMenuAction(ContextMenuAction action)
        {
            if (!IsCurrent) return;
            owner.CancelImeComposition();
            switch (action)
            {
                case ContextMenuAction.Copy: _ = owner.CopyText(Editor, cut: false); break;
                case ContextMenuAction.Cut: _ = owner.CopyText(Editor, cut: true); break;
                case ContextMenuAction.Paste: _ = owner.PasteText(Editor); break;
                case ContextMenuAction.SelectAll: Editor.SelectAll(); break;
            }
        }
    }
}
