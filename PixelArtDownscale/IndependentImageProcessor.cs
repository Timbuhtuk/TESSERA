using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public static class IndependentImageProcessor
{
    private static readonly int[,] Bayer4x4 = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    public static Bitmap Scale(Bitmap source, DownscaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        int width = options.TargetWidth, height = options.TargetHeight;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Размер результата должен быть положительным.");
        if (options.BlockMode == BlockSelectionMode.Manual && options.ManualCriteria is null)
            throw new ArgumentException("Для ручного выбора пикселя нужны критерии.", nameof(options));
        if (width == source.Width && height == source.Height) return CopyArgb(source);
        if (width > source.Width || height > source.Height)
        {
            int[] enlargedInput = ReadArgb(source);
            int[] enlargedOutput = new int[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                int sourceY = (int)((long)y * source.Height / height);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = (int)((long)x * source.Width / width);
                    enlargedOutput[y * width + x] = enlargedInput[sourceY * source.Width + sourceX];
                }
            }
            return WriteArgb(enlargedOutput, width, height);
        }

        int cropWidth = options.SpriteMode ? source.Width : width * (source.Width / width);
        int cropHeight = options.SpriteMode ? source.Height : height * (source.Height / height);
        int cropX = options.SpriteMode ? 0 : options.CropHorizontal switch
        {
            CropHorizontalAlignment.Left => 0,
            CropHorizontalAlignment.Right => source.Width - cropWidth,
            _ => (source.Width - cropWidth) / 2
        };
        int cropY = options.SpriteMode ? 0 : options.CropVertical switch
        {
            CropVerticalAlignment.Top => 0,
            CropVerticalAlignment.Bottom => source.Height - cropHeight,
            _ => (source.Height - cropHeight) / 2
        };
        int[] input = ReadArgb(source);
        int[] output = new int[checked(width * height)];
        for (int oy = 0; oy < height; oy++)
        {
            double y0 = cropY + oy * (double)cropHeight / height;
            double y1 = cropY + (oy + 1) * (double)cropHeight / height;
            for (int ox = 0; ox < width; ox++)
            {
                double x0 = cropX + ox * (double)cropWidth / width;
                double x1 = cropX + (ox + 1) * (double)cropWidth / width;
                double coverage = 0;
                var pixels = new List<BlockPixel>();
                var originalColors = new Dictionary<int, int>();
                for (int y = (int)y0; y < Math.Min(source.Height, (int)Math.Ceiling(y1)); y++)
                    for (int x = (int)x0; x < Math.Min(source.Width, (int)Math.Ceiling(x1)); x++)
                    {
                        double area = (Math.Min(x + 1, x1) - Math.Max(x, x0)) * (Math.Min(y + 1, y1) - Math.Max(y, y0));
                        if (area <= 0) continue;
                        int argb = input[y * source.Width + x];
                        int alpha = (int)((uint)argb >> 24);
                        coverage += area * alpha / 255;
                        if (alpha == 0) continue;
                        int rgb = argb & 0xFFFFFF;
                        pixels.Add(new BlockPixel(rgb, 0));
                        if (!originalColors.TryGetValue(rgb, out int previous) || (uint)argb >> 24 > (uint)previous >> 24)
                            originalColors[rgb] = argb;
                    }
                double threshold = options.SpriteMode ? options.AlphaThreshold / 100.0 : 0.5;
                if (pixels.Count == 0 || coverage + 1e-9 < (x1 - x0) * (y1 - y0) * threshold) continue;
                int selected = BlockAnalyzer.SelectRepresentative(pixels, options);
                output[oy * width + ox] = originalColors[selected];
            }
        }
        return WriteArgb(output, width, height);
    }

    public static Bitmap ApplyColors(Bitmap source, DownscaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        bool useQuantization = options.IndependentColorMode != IndependentColorMode.Palette;
        bool usePalette = options.IndependentColorMode != IndependentColorMode.Quantization;
        if (useQuantization) ColorQuantizer.ValidateColorCount(options.QuantizationColors);
        int[] input = ReadArgb(source);
        Dictionary<int, int>? lookup = null;
        if (useQuantization)
        {
            int[] visible = input.Where(argb => (uint)argb >> 24 != 0).Select(argb => argb & 0xFFFFFF).ToArray();
            var unique = visible.ToHashSet();
            var weights = options.UseColorWeights ? ColorQuantizer.CountColors(visible) : null;
            lookup = ColorQuantizer.BuildLookup(unique, options.QuantizationColors, options.Quantization, weights);
        }
        var palette = Palettes.GetPalette(options.Palette);
        int[] output = new int[input.Length];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int q = y * source.Width + x;
                int argb = input[q];
                if ((uint)argb >> 24 == 0) { output[q] = argb; continue; }
                int rgb = lookup is null ? argb & 0xFFFFFF : lookup[argb & 0xFFFFFF];
                if (usePalette)
                    rgb = options.Palette switch
                    {
                        PaletteKind.None => rgb,
                        PaletteKind.Step => ColorSpace.RoundToStep(rgb, options.PaletteStep),
                        _ => ColorSpace.NearestPacked(palette, rgb)
                    };
                if (usePalette && options.EnableDithering && options.Palette is not PaletteKind.None and not PaletteKind.Step && palette.Count > 1)
                {
                    var lab = ColorSpace.RgbToLab(rgb);
                    int nearestIndex = 0;
                    for (int i = 0; i < palette.Count; i++) if (palette[i] == rgb) { nearestIndex = i; break; }
                    double error = Math.Abs(ColorSpace.RgbToLab(argb & 0xFFFFFF).L - lab.L) / 100.0;
                    if (error > Bayer4x4[y % 4, x % 4] / 16.0) rgb = palette[(nearestIndex + 1) % palette.Count];
                }
                output[q] = (argb & unchecked((int)0xFF000000)) | rgb;
            }
        return WriteArgb(output, source.Width, source.Height);
    }

    public static Bitmap ApplyNeighborColors(Bitmap source, DownscaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.LocalColorPasses is < 1 or > DownscaleOptions.MaxLocalColorPasses)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"Число проходов должно быть от 1 до {DownscaleOptions.MaxLocalColorPasses}.");
        ManualBlockCriteria? localCriteria = options.LocalColorCriteria;

        int width = source.Width, height = source.Height;
        int[] input = ReadArgb(source);
        var features = new Dictionary<int, NeighborColorFeatures>();
        Span<int> colors = stackalloc int[9];
        Span<int> counts = stackalloc int[9];
        for (int pass = 0; pass < options.LocalColorPasses; pass++)
        {
            int[] output = new int[input.Length];
            int changed = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int q = y * width + x;
                    int argb = input[q];
                    if ((uint)argb >> 24 == 0) { output[q] = argb; continue; }

                    int current = argb & 0xFFFFFF;
                    counts.Clear();
                    int colorCount = 0;
                    int visible = 0;
                    for (int e = Math.Max(0, y - 1); e <= Math.Min(height - 1, y + 1); e++)
                        for (int f = Math.Max(0, x - 1); f <= Math.Min(width - 1, x + 1); f++)
                        {
                            int neighbor = input[e * width + f];
                            if ((uint)neighbor >> 24 == 0) continue;
                            int color = neighbor & 0xFFFFFF;
                            int index = 0;
                            while (index < colorCount && colors[index] != color) index++;
                            if (index == colorCount) colors[colorCount++] = color;
                            counts[index]++;
                            visible++;
                        }

                    int selected = current;
                    double bestCost = 0;
                    NeighborColorFeatures original = GetFeatures(current, features);
                    int currentCount = 0;
                    for (int e = 0; e < colorCount; e++)
                        if (colors[e] == current) { currentCount = counts[e]; break; }
                    for (int e = 0; e < colorCount; e++)
                    {
                        int color = colors[e], count = counts[e];
                        if (color == current) continue;
                        NeighborColorFeatures candidate = GetFeatures(color, features);
                        double dl = original.L - candidate.L;
                        double da = original.A - candidate.A;
                        double db = original.B - candidate.B;
                        double cost = Math.Sqrt(dl * dl + da * da + db * db) / 100.0;
                        cost -= 0.5 * (count - currentCount) / visible;

                        if (localCriteria is { } criteria)
                        {
                            cost += 4.0 * criteria.BrightnessImportance *
                                (Math.Abs(candidate.Brightness - criteria.TargetBrightness) -
                                 Math.Abs(original.Brightness - criteria.TargetBrightness));
                            cost += 0.5 * criteria.ContrastImportance *
                                (Math.Abs(candidate.Contrast - criteria.TargetContrast) -
                                 Math.Abs(original.Contrast - criteria.TargetContrast));
                            cost += 0.5 * criteria.SaturationImportance *
                                (Math.Abs(candidate.Saturation - criteria.TargetSaturation) -
                                 Math.Abs(original.Saturation - criteria.TargetSaturation));
                            cost += 0.5 * criteria.EdgeImportance * criteria.TargetEdge *
                                Math.Abs(candidate.Brightness - original.Brightness);
                        }

                        if (cost >= bestCost - 1e-9) continue;
                        bestCost = cost;
                        selected = color;
                    }
                    output[q] = (argb & unchecked((int)0xFF000000)) | selected;
                    if (selected != current) changed++;
                }
            input = output;
            if (changed == 0) break;
        }
        return WriteArgb(input, width, height);
    }

    private static NeighborColorFeatures GetFeatures(int color, Dictionary<int, NeighborColorFeatures> cache)
    {
        if (cache.TryGetValue(color, out NeighborColorFeatures found)) return found;
        var (l, a, b) = ColorSpace.RgbToLab(color);
        var features = new NeighborColorFeatures(l, a, b,
            ColorWeights.GetNormalizedBrightness(color), ColorWeights.GetContrastWeight(color),
            ColorWeights.GetSaturationWeight(color));
        cache[color] = features;
        return features;
    }

    private readonly record struct NeighborColorFeatures(double L, double A, double B,
        double Brightness, double Contrast, double Saturation);

    private static Bitmap CopyArgb(Bitmap source)
        => source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);

    private static int[] ReadArgb(Bitmap source)
    {
        using var copy = CopyArgb(source);
        var pixels = new int[checked(copy.Width * copy.Height)];
        var bounds = new Rectangle(0, 0, copy.Width, copy.Height);
        var bits = copy.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < copy.Height; y++) Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * copy.Width, copy.Width);
        }
        finally { copy.UnlockBits(bits); }
        return pixels;
    }

    private static Bitmap WriteArgb(int[] pixels, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var bits = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width, IntPtr.Add(bits.Scan0, y * bits.Stride), width);
            }
            finally { result.UnlockBits(bits); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
