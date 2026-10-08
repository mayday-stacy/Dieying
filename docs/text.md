# Text

Text lives on its own layer and stays editable: its wording, font, size and color can be changed at any time, and scaling the layer lays the text out again rather than stretching pixels.

## Adding text

With the Type tool (T), click on the canvas for a single line of text; the click sets the baseline, so the letters rise from where you clicked. Drag a box instead for a paragraph that wraps inside it. Type, then press Ctrl+Enter or click Done in the options bar to finish, or Escape or Cancel to give up. Text left empty is discarded. New text takes the foreground color and the style you used last.

## Editing text

Click existing text with the Type tool, double-click it with the Move tool, double-click the text layer's thumbnail in the Layers panel, or choose Layer > Edit Text. While editing, the box has handles to resize it (a single line becomes a box when you resize it), and a plus sign in the bottom-right handle shows that some text does not fit.

Keys while typing: Enter starts a new line; Tab inserts a tab; the arrow keys move, with Ctrl a word at a time and with Shift extending the selection; Home and End go to the start and end of the line, with Ctrl to the start and end of the text; Ctrl+A selects everything; Backspace and Delete remove, with Ctrl a word at a time; Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y undo and redo within the text; Ctrl+C, Ctrl+X and Ctrl+V copy, cut and paste plain text; Alt with Left and Right changes the tracking and Alt with Up and Down the leading, by one, or ten with Shift. A double-click selects a word, and Shift-click extends the selection.

## The Type bar

The options bar shows the text settings: the font family from the fonts installed on your machine; the size in pixels from 1 to 2000; Bold and Italic; the color; left, center and right alignment; Tracking, the extra space after every character, from -100 to 1000; and Leading, the distance between baselines, where 0 means automatic at 120 percent of the size.

A change in the bar applies to the text you are typing, or to the active text layer when none is being typed, or to the next text you add when no text layer is active. A run of changes on one layer undoes as a single step.

## Letters in their own colors

While typing, select some of the text and pick a color from the bar's swatch or the foreground swatch, and only those letters take it. With nothing selected, or on a text layer that is not open for typing, the color goes on all of the text. New letters take the color of the letter before them, and the swatch shows the color at the caret. Fill with Foreground Color or Fill with Background Color recolors the whole text and keeps it editable.

## Letters in their own fonts

The font, Bold and Italic work the same way: while typing, select some of the text and choose a family from the menu or tick Bold or Italic, and only those letters change. With nothing selected the change goes on all of the text, and a family chosen for all of it keeps the letters that were bold or italic as they were. When the selected letters use more than one family the menu says (Multiple), and choosing one from it puts all of them in that family. New letters take the face of the letter before them. Projects that use this are format version 5 and are supported by Dieying and by upstream Composa 1.3 or later.

If a font has no bold or italic face, Dieying substitutes one or synthesizes the weight and slant, so Bold and Italic always show.

## Chinese input, fallback fonts and Unicode

While editing text, a Windows input method can show preedit text and position its candidate list near the caret. Choosing a candidate commits text; cancelling preedit leaves the original text and selection intact. IME reconversion of already committed text is not implemented. Menu/tool shortcuts do not consume printable keys while the editor is typing.

Missing glyphs use system font fallback, with the same font choices used for measurement and rendering. If a project's requested font is not installed, the Type bar reports it while preserving its name in the project. Install the original font for the closest layout match on another computer.

Caret movement, selection and deletion follow Unicode grapheme clusters so combining marks, surrogate pairs and emoji sequences stay together. Chinese paragraph wrapping avoids common punctuation at inappropriate line boundaries. These features do not promise complete complex-script shaping or color-emoji rendering; additional IMEs and multi-monitor candidate positioning still need practical testing.
