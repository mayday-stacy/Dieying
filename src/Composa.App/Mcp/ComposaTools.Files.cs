using System.ComponentModel;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;
using Composa.IO.Psd;
using Composa.IO.Xcf;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Composa.App.Mcp;

/// <summary>
/// Files: opening, saving and exporting. These go through the window's own file commands with the dialogs cut out, so
/// a save from an agent is written in the background exactly as Ctrl+S is and close and quit wait for it. A file the
/// document did not come from is never overwritten unless the agent says so.
/// </summary>
public sealed partial class ComposaTools
{
    [McpServerTool(Name = "open_document")]
    [Description("Opens a file in a new tab and makes it the active document: a Dieying/Composa-compatible project (.cmps), an image (PNG, JPEG, WebP, BMP, GIF, SVG, and HEIC, AVIF or TIFF when ImageMagick is available) or a GIMP file (.xcf) that needs nothing converted. A file already open just becomes the active document. Photoshop and camera RAW files, and a GIMP file whose layers would be converted, need a dialog, so they are opened from the File menu instead.")]
    public async Task<string> OpenDocument([Description("Absolute path of the file")] string path)
    {
        path = Absolute(path);
        if (!File.Exists(path)) throw new McpException($"There is no file at {path}.");
        if (PsdImport.IsPsd(path) || RawImporter.IsRaw(path)) throw new McpException("Photoshop and camera RAW files need a dialog; open them from the File menu.");
        if (XcfImport.IsXcf(path))
        {
            // The window asks before converting anything; an agent cannot answer, so such a file is refused with what it would ask.
            var trial = await Task.Run(() => XcfImport.Load(path));
            var conversions = trial.Conversions.Select(c => $"{c.LayerName}: {c.Message}").ToList();
            trial.Discard();
            if (conversions.Count > 0) throw new McpException("This GIMP file needs conversions that the person has to approve in a dialog; open it from the File menu. " + string.Join(" ", conversions));
        }
        return await OnUi(async () =>
        {
            if (window.IsDragging) throw new McpException("The person is dragging on the canvas; try again in a moment.");
            var before = window.Sessions.Count;
            EditorSession s;
            try { s = await window.OpenPath(path) ?? throw new McpException($"{Path.GetFileName(path)} was not opened."); }
            catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't open {Path.GetFileName(path)}: {error.Message}"); }
            var number = window.Sessions.ToList().IndexOf(s) + 1;
            return window.Sessions.Count == before
                ? $"Document {number} \"{s.Title}\" was already open, now active."
                : $"Opened document {number}: \"{s.Title}\" {s.Document.Width}×{s.Document.Height} px, {s.Document.AllLayers().Count()} layers, now active.";
        });
    }

    [McpServerTool(Name = "save_document")]
    [Description("Saves the document as a Dieying/Composa-compatible project (.cmps) with all its layers, in the background as Ctrl+S does. Without a path it saves to the file the document came from.")]
    public Task<string> SaveDocument(
        [Description("Absolute path ending in .cmps; leave it out to save to the document's own file")] string? path = null,
        [Description("Replace a file that exists at a new path; the document's own file is always replaced")] bool overwrite = false,
        int? document = null) => OnUi(async () =>
    {
        var s = Editable(document);                             // Text being typed is committed first, so it is in what gets saved.
        if (path == null)
        {
            path = s.FilePath ?? throw new McpException($"\"{s.Title}\" has never been saved; give a path.");
        }
        else
        {
            path = Absolute(path);
            if (!path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"A project file ends in {ProjectFile.Extension}; export_image writes an image file.");
            Fresh(path, s.FilePath, overwrite);
        }
        if (await window.SaveTo(s, path) is { } error) throw new McpException($"Couldn't save {Path.GetFileName(path)}: {error.Message}");
        return $"Saved \"{s.Title}\" to {path}.";
    });

    [McpServerTool(Name = "export_image")]
    [Description("Exports the document flattened to an image file: PNG, JPEG or WebP, by the path's extension. The document itself is untouched; save_document keeps the layers.")]
    public async Task<string> ExportImage(
        [Description("Absolute path ending in .png, .jpg or .webp")] string path,
        [Description("JPEG and WebP quality from 1 to 100")] int quality = 90,
        [Description("Replace a file that exists at the path")] bool overwrite = false,
        int? document = null)
    {
        path = Absolute(path);
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ExportFormat.Png,
            ".jpg" or ".jpeg" => ExportFormat.Jpeg,
            ".webp" => ExportFormat.Webp,
            _ => throw new McpException("The path must end in .png, .jpg or .webp; save_document writes a project file.")
        };
        if (quality is < 1 or > 100) throw new McpException("quality is from 1 to 100.");
        Fresh(path, null, overwrite);
        var (title, flat) = await OnUi(() => { var s = Session(document); return (s.Title, s.Flatten()); });
        try
        {
            await Task.Run(() => ImageFiles.Save(flat, path, format, format == ExportFormat.Png ? 100 : quality));
            return $"Exported \"{title}\" as a {flat.Width}×{flat.Height} px {format.ToString().ToUpperInvariant()} to {path}.";
        }
        catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't export {Path.GetFileName(path)}: {error.Message}"); }
        finally { flat.Dispose(); }
    }

    [McpServerTool(Name = "export_look")]
    [Description("Bakes the document's visible adjustment layers into a .cube lookup table any editor can load, as File > Export Look does. Only what is a function of a pixel's color goes in: a masked, clipped or grouped layer and grain, noise and blurs are left out and named in the result. The document is untouched.")]
    public async Task<string> ExportLook(
        [Description("Absolute path ending in .cube")] string path,
        [Description("Points per axis: 17, 33 or 65")] int size = 33,
        [Description("Replace a file that exists at the path")] bool overwrite = false,
        int? document = null)
    {
        path = Absolute(path);
        if (!path.EndsWith(".cube", StringComparison.OrdinalIgnoreCase)) throw new McpException("The path must end in .cube.");
        if (!LookBake.Sizes.Contains(size)) throw new McpException($"size is one of {string.Join(", ", LookBake.Sizes)}.");
        Fresh(path, null, overwrite);
        // The bake reads the layers, so it runs where they are edited; the file is written off that thread.
        var (title, text, names, leftOut) = await OnUi(() =>
        {
            var s = Session(document);
            var (baked, skipped) = LookBake.Survey(s.Document);
            var reasons = skipped.Select(l => $"\"{l.Layer.Name}\" {l.Why}").ToList();
            if (baked.Count == 0)
                throw new McpException(reasons.Count > 0 ? $"None of the adjustment layers can be baked into a look: {string.Join("; ", reasons)}." : "There is no adjustment layer to bake into a look.");
            var baking = string.Join(", ", baked.Select(l => $"\"{l.Name}\""));
            return (s.Title, LookBake.Bake(baked, size, s.Title).ToCube(s.Title, $"Exported from {AppInfo.Name}: {string.Join(", ", baked.Select(l => l.Name))}"), baking, reasons);
        });
        try { await Task.Run(() => File.WriteAllText(path, text)); }
        catch (Exception error) when (error is not McpException) { throw new McpException($"Couldn't write {Path.GetFileName(path)}: {error.Message}"); }
        return $"Exported the look of \"{title}\" as a {size}-point .cube to {path}, baked from {names}." + (leftOut.Count > 0 ? $" Left out: {string.Join("; ", leftOut)}." : "");
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new McpException("Give an absolute path.");
        return Path.GetFullPath(path);
    }

    /// <summary>Refuses a path that would replace a file the agent did not ask to replace; a document's own file is fair game.</summary>
    private static void Fresh(string path, string? own, bool overwrite)
    {
        if (overwrite || !File.Exists(path)) return;
        if (own != null && string.Equals(Path.GetFullPath(own), path, StringComparison.Ordinal)) return;
        throw new McpException($"There is already a file at {path}; pass overwrite: true to replace it.");
    }
}
