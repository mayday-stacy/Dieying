using Composa.IO;
using Composa.Model;
using Composa.Rendering;
using SkiaSharp;

namespace Composa.App;

/// <summary>Output choices, independent of the document's own size and saved state.</summary>
public sealed record ImageExportOptions(ExportFormat Format, int Width, int Height, int Quality = 90, SKColor? Matte = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Format)) throw new ArgumentOutOfRangeException(nameof(Format));
        if (!DocumentLimits.FitsSurface(Width, Height))
            throw new ArgumentOutOfRangeException(nameof(Width), L10n.Format("Export size must fit within {0:N0} pixels a side and {1} megapixels.", DocumentLimits.MaxSide, DocumentLimits.MaxSurfaceMegapixels));
        if (Quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(Quality));
    }
}

/// <summary>The preview and saved file use exactly the same resize, matte and encoder.</summary>
public static class ImageExport
{
    public static byte[] Encode(SKBitmap flattened, ImageExportOptions options)
    {
        options.Validate();
        if (flattened.Width == options.Width && flattened.Height == options.Height)
            return ImageFiles.Encode(flattened, options.Format, options.Quality, options.Matte?.WithAlpha(255));
        using var resized = Pixels.NewColor(options.Width, options.Height);
        using (var canvas = new SKCanvas(resized))
        using (var image = SKImage.FromPixels(flattened.PeekPixels()))
            canvas.DrawImage(image, new SKRect(0, 0, options.Width, options.Height),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        return ImageFiles.Encode(resized, options.Format, options.Quality, options.Matte?.WithAlpha(255));
    }

    /// <summary>Replace only after a complete encode and write. A failure leaves the prior file intact.</summary>
    public static void Save(SKBitmap flattened, string path, ImageExportOptions options)
    {
        var encoded = Encode(flattened, options);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(encoded);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
