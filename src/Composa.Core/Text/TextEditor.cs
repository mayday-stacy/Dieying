using Composa.Model;
using SkiaSharp;

namespace Composa.Text;

/// <summary>
/// The state of text being typed on the canvas: the style (with the content), the caret, the selection anchor and a
/// local undo history for typing. It knows nothing about layers; the session re-renders the layer after each change.
/// </summary>
public sealed class TextEditor
{
    private readonly List<(TextStyle Style, int Caret, int Anchor)> undo = [];
    private readonly List<(TextStyle Style, int Caret, int Anchor)> redo = [];
    private TextLayout? layout;
    private TextElements? characters;
    private bool lastWasTyping;

    private TextElements Characters => characters ??= new TextElements(Text);

    public TextEditor(TextStyle style)
    {
        Style = style.Clamped();
        Caret = Anchor = Style.Text.Length;
    }

    public TextStyle Style { get; private set; }
    public string Text => Style.Text;
    public int Caret { get; private set; }
    public int Anchor { get; private set; }
    public bool HasSelection => Caret != Anchor;
    public int SelectionStart => Math.Min(Caret, Anchor);
    public int SelectionEnd => Math.Max(Caret, Anchor);
    public string SelectedText => Text.Substring(SelectionStart, SelectionEnd - SelectionStart);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    /// <summary>The layout of the current style, rebuilt only when the style changes.</summary>
    public TextLayout Layout => layout ??= new TextLayout(Style);

    /// <summary>Raised after every change to the style or the content.</summary>
    public event Action? Changed;

    // ---- Editing --------------------------------------------------------------------------------------------------

    /// <summary>Types text over the selection. Consecutive typing undoes as one step.</summary>
    public void Insert(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
        if (text.Length == 0 && !HasSelection) return;
        int start = SelectionStart, end = SelectionEnd;
        if (text.Length > TextStyle.MaxLength - (Text.Length - (end - start))) return;
        var content = Text.Remove(start, end - start).Insert(start, text);
        Record(typing: text.Length <= 2 && !text.Contains('\n'));
        Apply(Style.WithReplacedCharacters(start, end, text.Length) with { Text = content }, start + text.Length);
    }

    public void Backspace(bool word = false)
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (Caret == 0) return;
        var to = word ? Layout.WordStart(Caret) : Characters.Previous(Caret);
        Record(typing: !word);
        Apply(Style.WithReplacedCharacters(to, Caret, 0) with { Text = Text.Remove(to, Caret - to) }, to);
    }

    public void Delete(bool word = false)
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (Caret >= Text.Length) return;
        var to = word ? Layout.WordEnd(Caret) : Characters.Next(Caret);
        Record(typing: false);
        Apply(Style.WithReplacedCharacters(Caret, to, 0) with { Text = Text.Remove(Caret, to - Caret) }, Caret);
    }

    private void DeleteSelection()
    {
        Record(typing: false);
        int start = SelectionStart, end = SelectionEnd;
        Apply(Style.WithReplacedCharacters(start, end, 0) with { Text = Text.Remove(start, end - start) }, start);
    }

    /// <summary>Changes anything but the content (font, size, color, spacing, box). Undoable within the editor.</summary>
    public void ChangeStyle(Func<TextStyle, TextStyle> change)
    {
        var next = change(Style) with { Text = Text };
        next = next.Clamped();
        if (next == Style) return;
        Record(typing: false);
        Apply(next, Caret, Anchor);
    }

    /// <summary>Paints the selected letters in a color, or all of the text (dropping its per-letter colors) when nothing is selected. Undoable within the editor.</summary>
    public void SetColor(uint color)
    {
        var next = Style.WithColor(color, SelectionStart, SelectionEnd);
        if (next == Style) return;
        Record(typing: false);
        Apply(next, Caret, Anchor);
    }

    /// <summary>The color the Type bar shows: the first selected letter's, otherwise the letter before the caret's, which is what typing next takes.</summary>
    public uint ColorAtCaret => Style.ColorAt(HasSelection ? SelectionStart : Math.Max(0, Caret - 1));

    /// <summary>Changes the face (family, weight or slant) of the selected letters, or of all of the text when nothing is selected. Undoable within the editor.</summary>
    public void SetFace(Func<TextFace, TextFace> change)
    {
        var next = Style.WithFace(change, SelectionStart, SelectionEnd);
        if (next == Style) return;
        Record(typing: false);
        Apply(next, Caret, Anchor);
    }

    /// <summary>The face the Type bar's Bold and Italic show: the first selected letter's, otherwise the letter before the caret's.</summary>
    public TextFace FaceAtCaret => Style.FaceAt(HasSelection ? SelectionStart : Math.Max(0, Caret - 1));

    /// <summary>The family the Type bar's font menu shows: the one every selected letter is in, or null when they mix families.</summary>
    public string? UniformFamilyInSelection => HasSelection ? Style.UniformFamilyIn(SelectionStart, SelectionEnd) : FaceAtCaret.FontFamily;

    // ---- Caret ----------------------------------------------------------------------------------------------------

    public void MoveTo(int index, bool select)
    {
        // A collapsed caret snaps left; extending a selection includes the whole touched element.
        Caret = select && index > Anchor ? Characters.Ceiling(index) : Characters.Floor(index);
        if (!select) Anchor = Caret;
        lastWasTyping = false;
        Changed?.Invoke();
    }

    public void MoveHorizontal(int direction, bool select, bool word = false)
    {
        if (direction == 0) return;
        if (!select && HasSelection && !word) { MoveTo(direction < 0 ? SelectionStart : SelectionEnd, false); return; }
        var target = word
            ? (direction < 0 ? Layout.WordStart(Caret) : Layout.WordEnd(Caret))
            : (direction < 0 ? Characters.Previous(Caret) : Characters.Next(Caret));
        MoveTo(target, select);
    }

    public void MoveVertical(int direction, bool select) => MoveTo(Layout.IndexOnAdjacentLine(Caret, direction), select);

    public void MoveToLineEdge(bool end, bool select)
    {
        var line = Layout.Lines[Layout.LineOf(Caret)];
        MoveTo(end ? line.End : line.Start, select);
    }

    public void MoveToDocumentEdge(bool end, bool select) => MoveTo(end ? Text.Length : 0, select);

    public void SelectAll()
    {
        Anchor = 0;
        Caret = Text.Length;
        lastWasTyping = false;
        Changed?.Invoke();
    }

    public void SelectWordAt(int index)
    {
        Anchor = Layout.WordStart(Math.Min(index + 1, Text.Length));
        Caret = Layout.WordEnd(Anchor);
        lastWasTyping = false;
        Changed?.Invoke();
    }

    /// <summary>Puts the caret at a point in layout pixels; with <paramref name="select"/> the anchor stays.</summary>
    public void ClickAt(SKPoint point, bool select) => MoveTo(Layout.IndexAt(point), select);

    // ---- Undo -----------------------------------------------------------------------------------------------------

    private void Record(bool typing)
    {
        if (typing && lastWasTyping && undo.Count > 0) return;
        undo.Add((Style, Caret, Anchor));
        if (undo.Count > 200) undo.RemoveAt(0);
        redo.Clear();
        lastWasTyping = typing;
    }

    private void Apply(TextStyle style, int caret, int? anchor = null)
    {
        if (style.Text != Text) characters = null;
        Style = style;
        layout = null;
        if (anchor.HasValue && anchor.Value != caret)
        {
            Caret = caret > anchor.Value ? Characters.Ceiling(caret) : Characters.Floor(caret);
            Anchor = caret > anchor.Value ? Characters.Floor(anchor.Value) : Characters.Ceiling(anchor.Value);
        }
        else
        {
            // Insertion can join the characters on either side (a combining mark or a ZWJ, for example).
            // Keep the caret after that complete element, never in the newly formed sequence.
            Caret = Anchor = Characters.Ceiling(caret);
        }
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (undo.Count == 0) return false;
        redo.Add((Style, Caret, Anchor));
        var (style, caret, anchor) = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        lastWasTyping = false;
        Apply(style, caret, anchor);
        return true;
    }

    public bool Redo()
    {
        if (redo.Count == 0) return false;
        undo.Add((Style, Caret, Anchor));
        var (style, caret, anchor) = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        lastWasTyping = false;
        Apply(style, caret, anchor);
        return true;
    }
}
