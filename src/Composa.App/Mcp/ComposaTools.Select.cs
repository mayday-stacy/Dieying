using System.ComponentModel;
using Composa.Editing;
using Composa.Selections;
using Composa.Vision;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace Composa.App.Mcp;

/// <summary>
/// The selection tools: the marquees and lasso, the Magic tool's two modes, the Select menu, and its modifiers. A
/// selection limits fill_layer, paint_stroke, the adjustments and the filters to what is inside it, and a new
/// adjustment layer takes it as its mask, exactly as in the window.
/// </summary>
public sealed partial class ComposaTools
{
    private const string Mode = "replace (the default), add, subtract or intersect, as Shift, Alt and Shift+Alt do with the marquee";
    private const string FeatherEdge = "Softens the edge by this many pixels";

    [McpServerTool(Name = "select_shape")]
    [Description("Selects a rectangle or ellipse given by x, y, width and height, or a polygon given by points, in canvas pixels.")]
    public Task<string> SelectShape(
        [Description("rectangle, ellipse or polygon")] string kind,
        double? x = null, double? y = null, double? width = null, double? height = null,
        [Description("For a polygon: [[x, y], [x, y], ...], at least three")] double[][]? points = null,
        [Description(Mode)] string mode = "replace",
        [Description(FeatherEdge)] double feather = 0,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var how = ParseMode(mode);
        WithFeather(s, feather, () =>
        {
            switch (kind.Trim().ToLowerInvariant())
            {
                case "rectangle": s.SelectRect(Frame(x, y, width, height), how); break;
                case "ellipse": s.SelectEllipse(Frame(x, y, width, height), how); break;
                case "polygon":
                    if (points == null || points.Length < 3 || points.Any(p => p.Length != 2)) throw new McpException("A polygon needs points: at least three [x, y] pairs.");
                    s.SelectPolygon(points.Select(p => new SKPoint((float)p[0], (float)p[1])).ToList(), how);
                    break;
                default: throw new McpException("kind is rectangle, ellipse or polygon.");
            }
        });
        return Selected(s);
    });

    [McpServerTool(Name = "select_wand")]
    [Description("Magic Wand: selects the pixels of a similar color around a canvas point. Tolerance 0 to 255 says how different a color may be; contiguous limits it to the connected area.")]
    public Task<string> SelectWand(
        int x, int y, int tolerance = 32, bool contiguous = true,
        [Description("Sample every visible layer rather than the active layer alone")] bool allLayers = true,
        [Description(Mode)] string mode = "replace",
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        InCanvas(s, x, y);
        var (savedTolerance, savedContiguous, savedAll) = (s.WandTolerance, s.WandContiguous, s.SampleAllLayers);
        (s.WandTolerance, s.WandContiguous, s.SampleAllLayers) = (Math.Clamp(tolerance, 0, 255), contiguous, allLayers);
        try { s.SelectWand(x, y, ParseMode(mode)); }
        finally { (s.WandTolerance, s.WandContiguous, s.SampleAllLayers) = (savedTolerance, savedContiguous, savedAll); }
        return Selected(s);
    });

    [McpServerTool(Name = "select_object")]
    [Description("Selects the object under a canvas point: the connected piece of the subject there, with its soft edge. On the backdrop itself it selects nothing. With width and height, x and y are the top left of a box and the model runs on that box alone, which finds a small object far better than a point does: everything it finds inside the box is selected. " + DetectHelp)]
    public Task<string> SelectObject(
        int x, int y,
        [Description("With height: the box to look in, from x and y as its top left")] int? width = null,
        [Description("With width: the box's height")] int? height = null,
        [Description("Sample every visible layer rather than the active layer alone")] bool allLayers = true,
        [Description(Mode)] string mode = "replace",
        [Description(Detect)] string? detect = null,
        int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);
        InCanvas(s, x, y);
        var savedAll = s.SampleAllLayers;
        var savedDetect = s.Detect;
        s.SampleAllLayers = allLayers;
        s.Detect = ParseDetect(detect) ?? savedDetect;
        try
        {
            if (width is { } w && height is { } h)
            {
                if (w < 2 || h < 2) throw new McpException("The box must be at least 2×2 pixels.");
                await s.SelectObjectInBoxAsync(new SKRectI(x, y, x + w, y + h), ParseMode(mode));
            }
            else await s.SelectObjectAsync(x, y, ParseMode(mode));
        }
        finally { s.SampleAllLayers = savedAll; s.Detect = savedDetect; }
        return Selected(s);
    });

    [McpServerTool(Name = "select_subject")]
    [Description("Select > Subject: the subject of the whole picture. " + DetectHelp)]
    public Task<string> SelectSubject([Description(Mode)] string mode = "replace", [Description(Detect)] string? detect = null, int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);
        var saved = s.Detect;
        s.Detect = ParseDetect(detect) ?? saved;
        try
        {
            if (!await s.SelectSubjectAsync(ParseMode(mode))) throw new McpException(SubjectFinder.Resolve(s.Detect) == SubjectDetect.Backdrop ? "No subject stands out from the backdrop." : "The model found no subject in the picture.");
        }
        finally { s.Detect = saved; }
        return Selected(s);
    });

    private const string DetectHelp = "detect picks how: 'any' runs the U²-Net model on this machine for any subject, 'person' runs MODNet for people with soft hair, 'plain' takes everything that is not the near-uniform backdrop touching the picture's edges (fast, exact on product shots, defeated by busy backgrounds). Left out, the choice in the Object Selection options applies. A model that is not available here falls back to 'plain'.";
    private const string Detect = "any, person or plain; see the tool description";

    private static SubjectDetect? ParseDetect(string? detect) => detect?.ToLowerInvariant() switch
    {
        null or "" => null,
        "any" or "subject" => SubjectDetect.Any,
        "person" or "people" or "portrait" => SubjectDetect.Person,
        "plain" or "backdrop" or "none" => SubjectDetect.Backdrop,
        _ => throw new McpException($"Unknown detect \"{detect}\": use any, person or plain.")
    };

    [McpServerTool(Name = "select_layer_pixels")]
    [Description("Selects the shape of a layer's pixels (its mask with fromMask), as Ctrl-clicking its thumbnail does.")]
    public Task<string> SelectLayerPixels(string layer, bool fromMask = false, [Description(Mode)] string mode = "replace", int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        var target = Find(s, layer);
        if (fromMask ? target.Mask == null : target.Pixels == null) throw new McpException($"\"{target.Name}\" has no {(fromMask ? "mask" : "pixels")}.");
        if (fromMask) s.SelectLayerMask(target, ParseMode(mode)); else s.SelectLayerPixels(target, ParseMode(mode));
        return Selected(s);
    });

    [McpServerTool(Name = "select_all")]
    [Description("Selects the whole canvas.")]
    public Task<string> SelectAll(int? document = null) => OnUi(() => { var s = Editable(document); s.SelectAll(); return Selected(s); });

    [McpServerTool(Name = "deselect")]
    [Description("Drops the selection, so the next edit works on the whole layer again.")]
    public Task<string> Deselect(int? document = null) => OnUi(() => { var s = Editable(document); s.Deselect(); return "Nothing is selected."; });

    [McpServerTool(Name = "select_inverse")]
    [Description("Selects what was not selected.")]
    public Task<string> SelectInverse(int? document = null) => OnUi(() => { var s = Editable(document); s.InvertSelection(); return Selected(s); });

    [McpServerTool(Name = "modify_selection")]
    [Description("Changes the selection: grow or shrink it by pixels, soften its edge, or move it. Give what should happen; several apply in that order.")]
    public Task<string> ModifySelection(
        [Description("Pixels to grow by")] int expand = 0,
        [Description("Pixels to shrink by")] int contract = 0,
        [Description(FeatherEdge)] double feather = 0,
        [Description("Pixels to move right")] int moveX = 0,
        [Description("Pixels to move down")] int moveY = 0,
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (s.Selection == null) throw new McpException("Nothing is selected.");
        if (expand <= 0 && contract <= 0 && feather <= 0 && moveX == 0 && moveY == 0) throw new McpException("Nothing to do: give expand, contract, feather, moveX or moveY.");
        if (expand > 0) s.ExpandSelection(Math.Min(expand, 500));
        if (contract > 0) s.ContractSelection(Math.Min(contract, 500));
        if (feather > 0) s.FeatherSelection((float)Math.Min(feather, 250));
        if (moveX != 0 || moveY != 0) s.MoveSelection(moveX, moveY);
        return Selected(s);
    });

    // ---- Shared -------------------------------------------------------------------------------------------------------

    private static SelectionMode ParseMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "replace" or "new" => SelectionMode.Replace, "add" => SelectionMode.Add, "subtract" => SelectionMode.Subtract, "intersect" => SelectionMode.Intersect,
        _ => throw new McpException("mode is replace, add, subtract or intersect.")
    };

    private static SKRect Frame(double? x, double? y, double? width, double? height)
    {
        if (x is not { } left || y is not { } top || width is not { } w || height is not { } h) throw new McpException("A rectangle or ellipse needs x, y, width and height.");
        if (w < 1 || h < 1) throw new McpException("The width and height must be at least 1 px.");
        return SKRect.Create((float)left, (float)top, (float)w, (float)h);
    }

    private static void InCanvas(EditorSession s, int x, int y)
    {
        if (x < 0 || y < 0 || x >= s.Document.Width || y >= s.Document.Height) throw new McpException($"{x},{y} is outside the {s.Document.Width}×{s.Document.Height} px canvas.");
    }

    /// <summary>Runs a marquee with a feather of its own, leaving the tool's Feather setting as it was.</summary>
    private static void WithFeather(EditorSession s, double feather, Action select)
    {
        var saved = s.Feather;
        s.Feather = Math.Clamp(feather, 0, 250);
        try { select(); }
        finally { s.Feather = saved; }
    }

    /// <summary>What is selected now, as the bounds of the selection.</summary>
    [McpServerTool(Name = "select_color_range")]
    [Description("Color Range: selects every pixel near the given colors anywhere in the picture, as Select > Color Range does. Fuzziness says how far a color may be on each channel and still count; invert selects everything else, such as the subject in front of a green screen.")]
    public Task<string> SelectColorRange(
        [Description("The colors to select, as #RRGGBB or names")] string[] colors,
        [Description("Colors to leave out even when they are near the selected ones")] string[]? exclude = null,
        [Description("0 to 200, 40 by default")] int fuzziness = ColorRange.DefaultFuzziness,
        bool invert = false,
        [Description(Mode)] string mode = "replace",
        int? document = null) => OnUi(() =>
    {
        var s = Editable(document);
        if (colors == null || colors.Length == 0) throw new McpException("Give at least one color to select.");
        var how = ParseMode(mode);
        var (mask, count) = ColorRange.Match(s.Composite(), colors.Select(ParseColor).ToList(), (exclude ?? []).Select(ParseColor).ToList(),
            Math.Clamp(fuzziness, ColorRange.MinFuzziness, ColorRange.MaxFuzziness), invert);
        if (count == 0)
        {
            mask.Dispose();
            if (how == SelectionMode.Replace) s.Deselect();
            return "No pixels are near those colors. " + Selected(s);
        }
        s.Select(mask, how, "Color Range");
        return Selected(s);
    });

    private static string Selected(EditorSession s)
    {
        if (s.Selection == null) return "Nothing is selected.";
        var b = SelectionMask.Bounds(s.Selection);
        return b.IsEmpty ? "Nothing is selected." : $"Selected the area at {b.Left},{b.Top} size {b.Width}×{b.Height}.";
    }
}
