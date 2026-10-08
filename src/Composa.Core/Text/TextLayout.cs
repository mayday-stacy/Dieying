using System.Globalization;
using System.Text;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.Text;

/// <summary>
/// Lays a <see cref="TextStyle"/> out in layer pixels: which characters make each line, where every character
/// boundary sits, and how big the bitmap is. Point text is as wide as its longest line; paragraph text wraps at
/// word boundaries inside its box. The same layout draws the pixels and places the caret, so what is edited is
/// exactly what is rendered.
/// </summary>
public sealed class TextLayout
{
    /// <summary>The gap between the text and the edge of its bitmap, in layer pixels.</summary>
    public const float Padding = 12;

    public sealed class Line
    {
        /// <summary>Character range, end exclusive. A hard line break's newline is not part of any line.</summary>
        public int Start { get; init; }
        public int End { get; init; }
        /// <summary>Where the line starts, after alignment.</summary>
        public float X { get; init; }
        public float Baseline { get; init; }
        /// <summary>The x offset of each character boundary from <see cref="X"/>: <c>Positions[k]</c> is where character <c>Start + k</c> begins, and the last entry is where the line ends.</summary>
        public float[] Positions { get; init; } = [];
        /// <summary>The width without trailing spaces, which alignment ignores.</summary>
        public float VisibleWidth { get; init; }
        public int Length => End - Start;
    }

    public TextStyle Style { get; }
    public string Text { get; }
    public int Width { get; }
    public int Height { get; }
    public float LineHeight { get; }
    public float Ascent { get; }
    public float Descent { get; }
    public IReadOnlyList<Line> Lines { get; }
    /// <summary>Paragraph text that does not fit its box; the lines beyond it are laid out but clipped.</summary>
    public bool Overflows { get; }

    /// <summary>Faces looked up once per process: matching a family through the font manager is slow, and a layout asks for every stretch it draws.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<TextFace, SKTypeface> typefaces = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(TextFace Face, int Codepoint), SKTypeface> fallbackTypefaces = new();
    private readonly TextElements characters;

    /// <summary>
    /// The closest face in the family. A family Skia does not know (Inter is Avalonia's font for the interface and is
    /// not installed for Skia) goes through the platform's substitution, which keeps the weight and slant; the plain
    /// default typeface would drop both.
    /// </summary>
    public static SKTypeface TypefaceFor(TextStyle style) => TypefaceFor(style.Face);

    public static SKTypeface TypefaceFor(TextFace face) => typefaces.GetOrAdd(face, static f =>
        SKFontManager.Default.MatchFamily(f.FontFamily, FontStyle(f)) ?? SKTypeface.FromFamilyName(f.FontFamily, FontStyle(f)) ?? SKTypeface.Default);

    private static SKFontStyle FontStyle(TextFace face) => new(
        face.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
        face.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

    /// <summary>The font for layout and drawing alike. A face without a bold or italic variant gets them synthesized, as Photoshop's faux styles do.</summary>
    private SKFont MakeFont(TextFace face, SKTypeface typeface)
    {
        var font = new SKFont(typeface, (float)Math.Clamp(Style.Size, 1, 4000)) { Subpixel = true, Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.None };
        if (face.Bold && typeface.FontWeight < (int)SKFontStyleWeight.SemiBold) font.Embolden = true;
        if (face.Italic && typeface.FontSlant == SKFontStyleSlant.Upright) font.SkewX = -0.25f;
        return font;
    }

    /// <summary>One font per resolved face the text uses, made as they are needed and disposed together.</summary>
    private sealed class Fonts(TextLayout layout) : IDisposable
    {
        private readonly Dictionary<(TextFace Face, IntPtr Typeface), SKFont> fonts = [];
        public SKFont For(TextFace face) => For(face, TypefaceFor(face));

        // Skia's glyph lookup does not perform font fallback. Resolve missing characters before measuring and
        // drawing them, so a Latin font can still display Chinese text and the caret follows the actual glyphs.
        public SKFont For(TextFace face, int codepoint)
        {
            var primary = For(face);
            if (codepoint < ' ' || primary.ContainsGlyph(codepoint)) return primary;
            var typeface = fallbackTypefaces.GetOrAdd((face, codepoint), static key =>
                SKFontManager.Default.MatchCharacter(key.Face.FontFamily, FontStyle(key.Face), null, key.Codepoint)
                ?? TypefaceFor(key.Face));
            return For(face, typeface);
        }

        private SKFont For(TextFace face, SKTypeface typeface)
        {
            var key = (face, typeface.Handle);
            return fonts.TryGetValue(key, out var font) ? font : fonts[key] = layout.MakeFont(face, typeface);
        }
        public void Dispose() { foreach (var font in fonts.Values) font.Dispose(); }
    }

    public TextLayout(TextStyle style)
    {
        Style = style = style.Clamped();
        Text = style.Text.Replace("\r", "");
        characters = new TextElements(Text);
        using var fonts = new Fonts(this);
        // Line metrics are the style's own face's, whatever faces the letters are in, so lines do not shift as letters change.
        var metrics = fonts.For(style.Face).Metrics;
        Ascent = -metrics.Ascent;
        Descent = metrics.Descent;
        LineHeight = (float)style.LineHeight;
        var tracking = (float)style.Tracking;
        var advances = Advances(fonts, Text, tracking);

        var available = style.IsBox ? (float)Math.Max(1, style.BoxWidth!.Value - 2 * Padding) : float.PositiveInfinity;
        var ranges = new List<(int Start, int End)>();
        var paragraphStart = 0;
        for (var i = 0; i <= Text.Length; i++)
        {
            if (i < Text.Length && Text[i] != '\n') continue;
            Wrap(paragraphStart, i, advances, available, ranges);
            paragraphStart = i + 1;
        }

        float contentWidth = 0;
        var visible = new float[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            var (start, end) = ranges[i];
            var last = end;
            while (last > start && Text[last - 1] == ' ') last--;
            visible[i] = Sum(advances, start, last);
            contentWidth = Math.Max(contentWidth, visible[i]);
        }
        if (style.IsBox)
        {
            Width = (int)Math.Round(style.BoxWidth!.Value);
            Height = (int)Math.Round(style.BoxHeight!.Value);
            contentWidth = available;
        }
        else
        {
            // A caret's worth of width so an empty line still has somewhere to type.
            Width = (int)Math.Max(TextStyle.MinBox, Math.Ceiling(contentWidth + Padding * 2 + style.Size * 0.1));
            var contentHeight = Math.Max(ranges.Count * LineHeight, Ascent + Descent);
            Height = (int)Math.Max(TextStyle.MinBox, Math.Ceiling(contentHeight + Padding * 2));
        }
        Width = Math.Min(Width, Document.MaxSide);
        Height = Math.Min(Height, Document.MaxSide);

        var lines = new List<Line>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            var (start, end) = ranges[i];
            var positions = new float[end - start + 1];
            for (var k = 1; k <= end - start; k++) positions[k] = positions[k - 1] + advances[start + k - 1];
            var x = Padding + style.Alignment switch
            {
                TextAlignment.Center => (contentWidth - visible[i]) / 2,
                TextAlignment.Right => contentWidth - visible[i],
                _ => 0
            };
            lines.Add(new Line { Start = start, End = end, X = x, Baseline = Padding + Ascent + i * LineHeight, Positions = positions, VisibleWidth = visible[i] });
        }
        Lines = lines;
        Overflows = style.IsBox && lines.Count > 0 && lines[^1].Baseline + Descent > Height - Padding + 0.5f;
    }

    /// <summary>The advance of every character (a surrogate pair's second half advances nothing), tracking included, each stretch of one face measured with its own font.</summary>
    private float[] Advances(Fonts fonts, string text, float tracking)
    {
        var advances = new float[text.Length];
        for (var start = 0; start < text.Length;)
        {
            var face = Style.FaceAt(start);
            var font = fonts.For(face, CodepointAt(text, start));
            var end = start + CharacterLengthAt(text, start);
            while (end < text.Length && Style.FaceAt(end) == face && ReferenceEquals(fonts.For(face, CodepointAt(text, end)), font))
                end += CharacterLengthAt(text, end);
            var segment = text.Substring(start, end - start);
            var widths = font.GetGlyphWidths(font.GetGlyphs(segment));
            var glyph = 0;
            for (var i = 0; i < segment.Length && glyph < widths.Length; i++)
            {
                advances[start + i] = widths[glyph++] + tracking;
                if (char.IsHighSurrogate(segment[i]) && i + 1 < segment.Length && char.IsLowSurrogate(segment[i + 1])) i++;
            }
            start = end;
        }
        return advances;
    }

    private static float Sum(float[] advances, int start, int end)
    {
        float total = 0;
        for (var i = start; i < end; i++) total += advances[i];
        return total;
    }

    /// <summary>Greedy word wrap of one paragraph; a word wider than the box breaks between complete text elements.</summary>
    private void Wrap(int start, int end, float[] advances, float available, List<(int, int)> lines)
    {
        if (start == end) { lines.Add((start, end)); return; }
        var position = start;
        while (position < end)
        {
            float width = 0;
            int k = position, lastSpace = -1;
            for (; k < end; k = characters.Next(k))
            {
                var advance = Sum(advances, k, characters.Next(k));
                if (width + advance > available && k > position) break;
                width += advance;
                if (Text[k] == ' ') lastSpace = k;
            }
            var lineEnd = k;
            if (k < end)
            {
                // Spaces at the break stay on this line (they take no visible width); mid-word, go back to the last
                // space so the next line starts with a letter.
                if (Text[k] == ' ') { while (lineEnd < end && Text[lineEnd] == ' ') lineEnd = characters.Next(lineEnd); }
                else if (lastSpace >= position) lineEnd = characters.Next(lastSpace);
                lineEnd = PunctuationBreak(position, lineEnd, end);
            }
            lines.Add((position, lineEnd));
            position = lineEnd;
        }
    }

    // Common Chinese line-breaking prohibitions. These are deliberately a small punctuation policy,
    // not a claim to implement every language's line-breaking or shaping rules.
    private const string CannotStartLine = "),.:;!?]}，。、；：！？）］｝〉》」』】〕〗〙〛’”";
    private const string CannotEndLine = "([{（［｛〈《「『【〔〖〘〚‘“";

    private int PunctuationBreak(int start, int proposed, int end)
    {
        bool Prohibited(int at) => at < end && (CannotStartLine.Contains(Text[at]) ||
            CannotEndLine.Contains(Text[characters.Previous(at)]));

        var candidate = proposed;
        while (candidate > start && Prohibited(candidate)) candidate = characters.Previous(candidate);
        if (candidate > start) return candidate;

        // In a box narrower than a punctuation pair there is no fitting legal break. Keep the pair
        // together and allow it to overflow (drawing clips the box), always consuming text.
        candidate = proposed;
        while (candidate < end && Prohibited(candidate)) candidate = characters.Next(candidate);
        return candidate;
    }

    // ---- Drawing --------------------------------------------------------------------------------------------------

    /// <summary>Draws the text into a fresh bitmap of exactly the layout's size.</summary>
    public SKBitmap Render()
    {
        var bitmap = Pixels.NewColor(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        Draw(canvas);
        return bitmap;
    }

    public void Draw(SKCanvas canvas)
    {
        using var fonts = new Fonts(this);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.Save();
        if (Style.IsBox) canvas.ClipRect(new SKRect(0, 0, Width, Height));
        // Letters are drawn a stretch at a time, one stretch per face and color; text in one face and color is one stretch per line.
        foreach (var line in Lines)
        {
            if (line.Length == 0) continue;
            for (var k = 0; k < line.Length;)
            {
                var face = Style.FaceAt(line.Start + k);
                var color = Style.ColorAt(line.Start + k);
                var font = fonts.For(face, CodepointAt(Text, line.Start + k));
                var end = k + CharacterLengthAt(Text, line.Start + k);
                while (end < line.Length && Style.FaceAt(line.Start + end) == face && Style.ColorAt(line.Start + end) == color
                    && ReferenceEquals(fonts.For(face, CodepointAt(Text, line.Start + end)), font))
                    end += CharacterLengthAt(Text, line.Start + end);
                var segment = Text.Substring(line.Start + k, end - k);
                var glyphs = font.GetGlyphs(segment);
                if (glyphs.Length > 0)
                {
                    var positions = new SKPoint[glyphs.Length];
                    var glyph = 0;
                    for (var i = 0; i < segment.Length && glyph < glyphs.Length; i++)
                    {
                        positions[glyph++] = new SKPoint(line.X + line.Positions[k + i], line.Baseline);
                        if (char.IsHighSurrogate(segment[i]) && i + 1 < segment.Length && char.IsLowSurrogate(segment[i + 1])) i++;
                    }
                    using var builder = new SKTextBlobBuilder();
                    var run = builder.AllocatePositionedRun(font, glyphs.Length);
                    run.SetGlyphs(glyphs);
                    run.SetPositions(positions);
                    using var blob = builder.Build();
                    paint.Color = new SKColor(color);
                    if (blob != null) canvas.DrawText(blob, 0, 0, paint);
                }
                k = end;
            }
        }
        canvas.Restore();
    }

    // ---- Caret geometry -------------------------------------------------------------------------------------------

    private static int CharacterLengthAt(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

    private static int CodepointAt(string text, int index) => Rune.TryGetRuneAt(text, index, out var rune) ? rune.Value : Rune.ReplacementChar.Value;

    /// <summary>The line holding a character index. At a soft wrap the caret belongs to the start of the next line.</summary>
    public int LineOf(int index)
    {
        index = characters.Floor(index);
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            if (index < line.Start) return Math.Max(0, i - 1);
            if (index > line.End) continue;
            if (index == line.End && i + 1 < Lines.Count && Lines[i + 1].Start == line.End) return i + 1;
            return i;
        }
        return Lines.Count - 1;
    }

    /// <summary>Where the caret is drawn before a character index: its x and the top and bottom of the line.</summary>
    public (float X, float Top, float Bottom) CaretAt(int index)
    {
        index = characters.Floor(index);
        var line = Lines[LineOf(index)];
        var k = Math.Clamp(index - line.Start, 0, line.Positions.Length - 1);
        return (line.X + line.Positions[k], line.Baseline - Ascent, line.Baseline + Descent);
    }

    /// <summary>The character boundary nearest a point in layout pixels.</summary>
    public int IndexAt(SKPoint point)
    {
        if (Lines.Count == 0) return 0;
        var row = (int)Math.Floor((point.Y - Padding) / Math.Max(1e-3f, LineHeight));
        var line = Lines[Math.Clamp(row, 0, Lines.Count - 1)];
        return line.Start + NearestBoundary(line, point.X);
    }

    /// <summary>The index on the line above or below that keeps the caret's x, for the up and down arrows.</summary>
    public int IndexOnAdjacentLine(int index, int direction)
    {
        var current = LineOf(index);
        var target = current + direction;
        if (target < 0) return 0;
        if (target >= Lines.Count) return Text.Length;
        var x = CaretAt(index).X;
        var line = Lines[target];
        return line.Start + NearestBoundary(line, x);
    }

    private int NearestBoundary(Line line, float x)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var index = line.Start;; index = characters.Next(index))
        {
            var k = index - line.Start;
            var distance = Math.Abs(line.X + line.Positions[k] - x);
            if (distance < bestDistance) { bestDistance = distance; best = k; }
            if (index == line.End) break;
        }
        return best;
    }

    /// <summary>The highlighted rectangles for a character range, one per line it touches.</summary>
    public List<SKRect> SelectionRects(int start, int end)
    {
        var rects = new List<SKRect>();
        if (end <= start) return rects;
        start = characters.Floor(start);
        end = characters.Ceiling(end);
        if (end <= start) return rects;
        foreach (var line in Lines)
        {
            if (line.End < start || line.Start >= end) { if (!(line.Start == line.End && start <= line.Start && line.Start < end)) continue; }
            var from = Math.Clamp(start, line.Start, line.End);
            var to = Math.Clamp(end, line.Start, line.End);
            var left = line.X + line.Positions[from - line.Start];
            var right = line.X + line.Positions[to - line.Start];
            // A newline taken into the selection shows as a sliver past the line's end.
            if (end > line.End && to == line.End) right += Math.Max(4, Ascent * 0.3f);
            rects.Add(new SKRect(left, line.Baseline - Ascent, right, line.Baseline + Descent));
        }
        return rects;
    }

    /// <summary>The start of the word the index is in (or before it), for Ctrl+Left and double-click.</summary>
    public int WordStart(int index)
    {
        index = characters.Ceiling(index);
        while (index > 0 && !IsWordAt(characters.Previous(index))) index = characters.Previous(index);
        while (index > 0 && IsWordAt(characters.Previous(index))) index = characters.Previous(index);
        return index;
    }

    public int WordEnd(int index)
    {
        index = characters.Floor(index);
        while (index < Text.Length && !IsWordAt(index)) index = characters.Next(index);
        while (index < Text.Length && IsWordAt(index)) index = characters.Next(index);
        return index;
    }

    private bool IsWordAt(int index) => Rune.TryGetRuneAt(Text, index, out var rune) && Rune.IsLetterOrDigit(rune);

    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var line in Lines) builder.Append('[').Append(Text, line.Start, line.Length).Append(']');
        return builder.ToString();
    }
}

/// <summary>
/// Extended Unicode grapheme boundaries, still expressed as UTF-16 offsets for styles and file IO.
/// The runtime handles combining marks, variation selectors, emoji modifiers, ZWJ sequences and flags.
/// Kept separate from font layout so plain caret movement never needs to measure or render text.
/// </summary>
internal sealed class TextElements
{
    private readonly int[] boundaries;

    public TextElements(string text) => boundaries = [.. StringInfo.ParseCombiningCharacters(text), text.Length];

    public int Floor(int index)
    {
        var at = Array.BinarySearch(boundaries, Math.Clamp(index, 0, boundaries[^1]));
        return boundaries[at >= 0 ? at : ~at - 1];
    }

    public int Ceiling(int index)
    {
        var at = Array.BinarySearch(boundaries, Math.Clamp(index, 0, boundaries[^1]));
        return boundaries[at >= 0 ? at : ~at];
    }

    public int Previous(int index)
    {
        var at = Array.BinarySearch(boundaries, Math.Clamp(index, 0, boundaries[^1]));
        return boundaries[Math.Max(0, at >= 0 ? at - 1 : ~at - 1)];
    }

    public int Next(int index)
    {
        var at = Array.BinarySearch(boundaries, Math.Clamp(index, 0, boundaries[^1]));
        return boundaries[Math.Min(boundaries.Length - 1, at >= 0 ? at + 1 : ~at)];
    }
}
