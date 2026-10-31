using System.Drawing.Imaging;

namespace PixelArtDownscale;

public static class PaletteProcessor
{
    public static Bitmap Apply(Bitmap source, DownscaleOptions options, ConnectivityPaletteOptions? connected = null, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ApplyWithProgress(source, options, connected, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap ApplyWithProgress(Bitmap source, DownscaleOptions options, ConnectivityPaletteOptions? connected, ProcessingProgress tracking)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (connected is null) return IndependentImageProcessor.ApplyColors(source, options, tracking);
        if (connected.TargetColors is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(connected));

        Bitmap candidates;
        if (options.IndependentColorMode == IndependentColorMode.Palette)
        {
            candidates = IndependentImageProcessor.ApplyColors(source, options, tracking.Slice(0, 2));
            if (HasMoreThan4096Colors(candidates))
            {
                candidates.Dispose();
                using var reduced = IndependentImageProcessor.ApplyColors(source, PrequantizationOptions(options, connected.TargetColors), tracking.Slice(0, 2));
                candidates = IndependentImageProcessor.ApplyColors(reduced, options, tracking.Slice(0, 2));
            }
        }
        else if (HasMoreThan4096Colors(source))
            candidates = IndependentImageProcessor.ApplyColors(source, PrequantizationOptions(options, connected.TargetColors), tracking.Slice(0, 2));
        else candidates = (Bitmap)source.Clone();

        using (candidates)
            return ConnectivityPaletteReducer.Process(candidates, connected, tracking.Slice(1, 2)).Image;
    }

    private static DownscaleOptions PrequantizationOptions(DownscaleOptions options, int targetColors) => new()
    {
        IndependentColorMode = IndependentColorMode.Quantization,
        QuantizationColors = Math.Min(4096, Math.Max(1024, targetColors * 4)),
        Quantization = options.Quantization,
        UseColorWeights = options.UseColorWeights
    };

    private static bool HasMoreThan4096Colors(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        using var pixels = BitmapBuffer.FromBitmap(copy);
        var colors = new HashSet<int>();
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (pixels.GetAlpha(x, y) > 0 && colors.Add(pixels.GetPackedColor(x, y)) && colors.Count > 4096)
                    return true;
        return false;
    }
}
