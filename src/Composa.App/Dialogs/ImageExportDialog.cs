using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Composa.IO;
using Composa.Model;
using SkiaSharp;

namespace Composa.App.Dialogs;

public static class ImageExportDialog
{
    /// <summary>The caller owns flattened and keeps it alive until this dialog's last preview has finished.</summary>
    public static async Task<ImageExportOptions?> Show(Window owner, SKBitmap flattened, ImageExportOptions initial)
    {
        initial.Validate();
        var chosen = initial;
        var proportional = true;
        var changingSize = false;
        var closed = false;
        var generation = 0;
        Task inFlight = Task.CompletedTask;
        DialogWindow? dialog = null;
        var preview = new Image { Name = "ExportPreview", Width = 520, Height = 300, Stretch = Stretch.Uniform };
        var info = Ui.Label("Measuring…", Palette.Secondary);
        info.Name = "ExportFileSize";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

        bool Validate()
        {
            var valid = DocumentLimits.FitsSurface(chosen.Width, chosen.Height);
            if (dialog != null) dialog.CanAccept = valid;
            if (!valid) info.Text = L10n.Format("Export size must fit within {0:N0} pixels a side and {1} megapixels.", DocumentLimits.MaxSide, DocumentLimits.MaxSurfaceMegapixels);
            return valid;
        }

        void QueueRefresh()
        {
            generation++;
            timer.Stop();
            if (Validate()) { info.Text = L10n.Text("Measuring…"); timer.Start(); }
        }

        async Task Refresh()
        {
            var mine = generation;
            var options = chosen;
            try
            {
                var (bytes, shown) = await Task.Run(() =>
                {
                    var encoded = ImageExport.Encode(flattened, options);
                    using var decoded = SKBitmap.Decode(encoded) ?? throw new InvalidDataException("The export preview could not be decoded.");
                    return (encoded.LongLength, Ui.ToAvaloniaBitmap(decoded, 1040));
                });
                if (closed || mine != generation) { shown.Dispose(); return; }
                var old = preview.Source as Bitmap;
                preview.Source = shown;
                old?.Dispose();
                info.Text = $"{options.Width} × {options.Height} px · {(bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0} KB")}";
            }
            catch (Exception error)
            {
                if (closed || mine != generation) return;
                info.Text = L10n.Format("Preview unavailable: {0}", error.Message);
                if (dialog != null) dialog.CanAccept = false;
            }
        }

        void StartRefresh()
        {
            timer.Stop();
            if (closed || !Validate()) return;
            // Large images get one encoder at a time; only the newest choice receives its preview.
            if (!inFlight.IsCompleted) { timer.Start(); return; }
            inFlight = Refresh();
        }
        timer.Tick += (_, _) => StartRefresh();

        NumericUpDown? width = null, height = null;
        void Resize(bool fromWidth, double value)
        {
            if (changingSize) return;
            changingSize = true;
            try
            {
                var w = fromWidth ? (int)value : chosen.Width;
                var h = fromWidth ? chosen.Height : (int)value;
                if (proportional)
                {
                    if (fromWidth)
                    {
                        h = Math.Max(1, (int)Math.Round(w * (double)flattened.Height / flattened.Width));
                        if (h > DocumentLimits.MaxSide) { h = DocumentLimits.MaxSide; w = Math.Max(1, (int)Math.Round(h * (double)flattened.Width / flattened.Height)); }
                    }
                    else
                    {
                        w = Math.Max(1, (int)Math.Round(h * (double)flattened.Width / flattened.Height));
                        if (w > DocumentLimits.MaxSide) { w = DocumentLimits.MaxSide; h = Math.Max(1, (int)Math.Round(w * (double)flattened.Height / flattened.Width)); }
                    }
                }
                chosen = chosen with { Width = w, Height = h };
                if (width != null) width.Value = w;
                if (height != null) height.Value = h;
            }
            finally { changingSize = false; }
            QueueRefresh();
        }
        width = Ui.Number(chosen.Width, 1, DocumentLimits.MaxSide, v => Resize(true, v), width: 130);
        height = Ui.Number(chosen.Height, 1, DocumentLimits.MaxSide, v => Resize(false, v), width: 130);
        width.Name = "ExportWidth";
        height.Name = "ExportHeight";
        var original = Ui.TextButton("Original size", () =>
        {
            changingSize = true;
            chosen = chosen with { Width = flattened.Width, Height = flattened.Height };
            width.Value = chosen.Width;
            height.Value = chosen.Height;
            changingSize = false;
            QueueRefresh();
        });
        var lockRatio = Ui.Check("Constrain proportions", true, value =>
        {
            proportional = value;
            if (value) Resize(true, chosen.Width);
        });
        lockRatio.Name = "ExportConstrainProportions";
        var rows = Ui.Column(10,
            CanvasDialogs.Form(("Width", Ui.Row(6, width, Ui.Label("px", Palette.Secondary))),
                ("Height", Ui.Row(6, height, Ui.Label("px", Palette.Secondary)))),
            Ui.Row(12, lockRatio, original));
        if (initial.Format != ExportFormat.Png)
        {
            var quality = Ui.SliderField("Quality", initial.Quality, 1, 100,
                value => { chosen = chosen with { Quality = (int)value }; QueueRefresh(); }, width: 520);
            quality.Name = "ExportQuality";
            rows.Children.Add(quality);
        }
        if (initial.Format == ExportFormat.Jpeg)
        {
            var matte = Ui.Combo(new[] { "White", "Black" }, initial.Matte == SKColors.Black ? "Black" : "White", value => value,
                value => { chosen = chosen with { Matte = value == "Black" ? SKColors.Black : SKColors.White }; QueueRefresh(); }, 130);
            matte.Name = "ExportMatte";
            rows.Children.Add(CanvasDialogs.Form(("Transparency background", matte)));
        }
        else rows.Children.Add(Ui.Label("Transparency is preserved.", Palette.Secondary));
        rows.Children.Add(Ui.Label("Export size does not change your project.", Palette.Secondary));
        var frame = new Border { Child = preview, Background = new SolidColorBrush(Color.Parse("#242424")), Padding = new Thickness(1) };
        var formatName = initial.Format switch { ExportFormat.Jpeg => "JPEG", ExportFormat.Webp => "WebP", _ => "PNG" };
        dialog = new DialogWindow(L10n.Format("Export {0}", formatName), Ui.Column(12, frame, rows, info), "Export…");
        dialog.Opened += (_, _) => StartRefresh();
        var accepted = await dialog.Ask(owner);
        closed = true;
        timer.Stop();
        await inFlight;
        (preview.Source as Bitmap)?.Dispose();
        preview.Source = null;
        if (!accepted) return null;
        chosen.Validate();
        return chosen;
    }
}
