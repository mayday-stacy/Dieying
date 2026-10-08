using Avalonia.Platform.Storage;
using Composa.App.Dialogs;
using Composa.Editing;
using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, (EditorSession Session, Task<Exception?> Task)> exporting =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<(EditorSession Session, Task Task)> exportOperations = [];

    public bool IsExporting(EditorSession target) => exportOperations.Any(value => ReferenceEquals(value.Session, target));

    private async Task WaitForExports(EditorSession? target = null)
    {
        while (exportOperations.Where(value => target == null || ReferenceEquals(value.Session, target)).Select(value => value.Task).ToArray() is { Length: > 0 } pending)
            await Task.WhenAll(pending);
    }

    private Document ExportSnapshot(EditorSession target)
    {
        if (ReferenceEquals(target, session))
        {
            if (canvas.IsDragging) throw new InvalidOperationException(L10n.Text("Finish the current drag before exporting."));
            canvas.CancelImeComposition();
        }
        target.FinishText();
        if (target.HasPendingEdit) throw new InvalidOperationException(L10n.Text("Finish the current edit before exporting."));
        return target.Document.Clone();
    }

    /// <summary>Capture now and export off the UI thread. Export never marks the project saved or changes its size.</summary>
    public async Task<Exception?> ExportTo(EditorSession target, string path, ImageExportOptions options)
    {
        try
        {
            options.Validate();
            var snapshot = ExportSnapshot(target);
            return await WriteExport(target, path, () =>
            {
                using var flat = DocumentRenderer.Flatten(snapshot);
                ImageExport.Save(flat, Path.GetFullPath(path), options);
            });
        }
        catch (Exception error) { return error; }
    }

    private async Task<Exception?> WriteExport(EditorSession target, string path, Action write)
    {
        var key = Path.GetFullPath(path);
        // Reserve this path before awaiting any previous writer: a third export must queue behind this one.
        var previous = exporting.TryGetValue(key, out var earlier) ? earlier.Task : Task.FromResult<Exception?>(null);
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        exporting[key] = (target, completion.Task);
        exportOperations.Add((target, completion.Task));
        try
        {
            await previous;
            await Task.Run(write);
            return null;
        }
        catch (Exception error) { return error; }
        finally
        {
            if (exporting.TryGetValue(key, out var current) && current.Task == completion.Task) exporting.Remove(key);
            exportOperations.Remove((target, completion.Task));
            completion.TrySetResult(null);
        }
    }

    private async Task Export(ExportFormat format)
    {
        if (session is not { } target) return;
        try
        {
            var snapshot = ExportSnapshot(target);
            using var flat = await Busy(() => Task.Run(() => DocumentRenderer.Flatten(snapshot)));
            if (!IsVisible) return;
            var initial = new ImageExportOptions(format, flat.Width, flat.Height,
                format == ExportFormat.Webp ? Math.Clamp(settings.WebpQuality, 1, 100) : jpegQuality);
            if (await ImageExportDialog.Show(this, flat, initial) is not { } options || !IsVisible) return;
            if (format == ExportFormat.Jpeg) settings.JpegQuality = jpegQuality = options.Quality;
            if (format == ExportFormat.Webp) settings.WebpQuality = options.Quality;
            settings.Save();
            var extension = format switch { ExportFormat.Jpeg => "jpg", ExportFormat.Webp => "webp", _ => "png" };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L10n.Format("Export {0}", extension.ToUpperInvariant()),
                SuggestedFileName = target.Title + "." + extension, DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType(L10n.Format("{0} image", extension.ToUpperInvariant())) { Patterns = ["*." + extension] }]
            });
            if (file?.TryGetLocalPath() is not { } path || !IsVisible) return;
            if (await Busy(() => WriteExport(target, path, () => ImageExport.Save(flat, path, options))) is { } error)
            {
                if (IsVisible) await Prompts.Alert(this, "Couldn't export", error.Message);
            }
            else ShowNote(L10n.Format("Exported {0} × {1} px", options.Width, options.Height));
        }
        catch (Exception error) { if (IsVisible) await Prompts.Alert(this, "Couldn't export", error.Message); }
    }
}
