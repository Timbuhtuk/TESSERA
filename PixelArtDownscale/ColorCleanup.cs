using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public sealed class ColorCleanupOptions
{
    public double ColorTolerance { get; init; } = 12;
    public double Strength { get; init; } = .65;
    public double DetailProtection { get; init; } = .8;
    public double EdgeProtection { get; init; } = .8;
}

/// <summary>Consolidates nearby shades using local perceptual consensus without blending colors or alpha.</summary>
public static class ColorCleanup
{
    public static Bitmap Apply(Bitmap source, DownscaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.LocalColorPasses is < 1 or > DownscaleOptions.MaxLocalColorPasses)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"Число проходов должно быть от 1 до {DownscaleOptions.MaxLocalColorPasses}.");
        ColorCleanupOptions cleanup = options.Cleanup ?? new();
        Validate(cleanup);
        int width = source.Width, height = source.Height;
        int[] input = ReadArgb(source);
        if (cleanup.Strength == 0 || cleanup.ColorTolerance == 0) return WriteArgb(input, width, height);

        var features = new Dictionary<int, Features>();
        Span<int> colors = stackalloc int[9];
        Span<double> weights = stackalloc double[9];
        Span<Features> labs = stackalloc Features[9];
        double toleranceSquared = cleanup.ColorTolerance * cleanup.ColorTolerance;
        double anchor = 1 + (1 - cleanup.Strength) * 8;
        // Legacy callers can still specify directional color preferences. New controls are independent.
        ManualBlockCriteria? legacy = options.Cleanup is null ? options.LocalColorCriteria : null;
        for (int pass = 0; pass < options.LocalColorPasses; pass++)
        {
            int[] output = (int[])input.Clone();
            int changed = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int q = y * width + x;
                    int argb = output[q];
                    if ((uint)argb >> 24 == 0) continue;
                    int current = argb & 0xFFFFFF;
                    Features original = GetFeatures(current, features);
                    if (ProtectThinDetail(input, width, height, x, y, original, cleanup, features)) continue;

                    int count = 0, same = 1;
                    double totalWeight = 0;
                    for (int e = Math.Max(0, y - 1); e <= Math.Min(height - 1, y + 1); e++)
                        for (int f = Math.Max(0, x - 1); f <= Math.Min(width - 1, x + 1); f++)
                        {
                            if (e == y && f == x) continue;
                            // Read accepted changes immediately. Simultaneous updates can exchange
                            // colors indefinitely on alternating stripes, even with a strict local gain.
                            int neighbor = output[e * width + f];
                            int alpha = (int)((uint)neighbor >> 24);
                            if (alpha == 0) continue;
                            int rgb = neighbor & 0xFFFFFF;
                            if (rgb == current) same++;
                            Features lab = GetFeatures(rgb, features);
                            // Transparent borders cannot outweigh solid pixels. Diagonals carry less support.
                            double weight = alpha / 255.0 * (e != y && f != x ? .7 : 1);
                            colors[count] = rgb;
                            labs[count] = lab;
                            weights[count++] = weight;
                            totalWeight += weight;
                        }
                    if (count < 2) continue;

                    double centerWeight = ((uint)argb >> 24) / 255.0;
                    // A full opaque neighborhood has 4 cardinal + 4 diagonal votes (6.8).
                    // Scale all resistance, including former self support, to the available votes.
                    // Otherwise an isolated shade at a canvas/transparent edge can never join its neighbors.
                    double localAnchor = (anchor + centerWeight) * totalWeight / 6.8;

                    int selected = current;
                    double bestCost = Cost(original, original, labs[..count], weights[..count], localAnchor, toleranceSquared);
                    for (int e = 0; e < count; e++)
                    {
                        if (colors[e] == current) continue;
                        double distance = DistanceSquared(original, labs[e]);
                        if (distance > toleranceSquared) continue;
                        double edgeThreshold = cleanup.ColorTolerance * (1 - cleanup.EdgeProtection * .8);
                        if (same >= 3 && cleanup.EdgeProtection > 0 && distance > edgeThreshold * edgeThreshold) continue;
                        double cost = Cost(labs[e], original, labs[..count], weights[..count], localAnchor, toleranceSquared);
                        if (legacy is not null)
                            cost += LegacyPreference(colors[e], current, legacy) * toleranceSquared * (totalWeight + centerWeight);
                        // Sequential updates strictly lower symmetric, bounded pairwise LAB energy.
                        // Normalized resistance is nonnegative, so it cannot create a cycle.
                        if (cost >= bestCost - Math.Max(.0001, toleranceSquared * .002 * (1 - cleanup.Strength))) continue;
                        bestCost = cost;
                        selected = colors[e];
                    }
                    if (selected == current) continue;
                    output[q] = (argb & unchecked((int)0xFF000000)) | selected;
                    changed++;
                }
            input = output;
            if (changed == 0) break;
        }
        return WriteArgb(input, width, height);
    }

    private static double Cost(Features candidate, Features original, ReadOnlySpan<Features> neighbors,
        ReadOnlySpan<double> weights, double anchor, double toleranceSquared)
    {
        double result = anchor * DistanceSquared(candidate, original);
        for (int q = 0; q < neighbors.Length; q++)
            // Cap every pair instead of dropping neighbors relative to the current color.
            // This keeps strong boundaries bounded and the objective identical from both endpoints.
            result += weights[q] * Math.Min(DistanceSquared(candidate, neighbors[q]), toleranceSquared);
        return result;
    }

    private static bool ProtectThinDetail(int[] pixels, int width, int height, int x, int y, Features current,
        ColorCleanupOptions options, Dictionary<int, Features> cache)
    {
        if (options.DetailProtection == 0) return false;
        double match = Math.Max(.4, options.ColorTolerance * .025);
        double contrast = Math.Max(1, options.ColorTolerance * (1 - options.DetailProtection) * .6);
        ReadOnlySpan<(int X, int Y)> axes = [(1, 0), (0, 1), (1, 1), (1, -1)];
        foreach (var axis in axes)
        {
            bool before = TryGet(x - axis.X, y - axis.Y, out var a) && DistanceSquared(current, a) <= match * match;
            bool after = TryGet(x + axis.X, y + axis.Y, out var b) && DistanceSquared(current, b) <= match * match;
            if (!before && !after) continue;
            // Preserve one-pixel lines and their end caps when both flanks describe another surface.
            if (!TryGet(x - axis.Y, y + axis.X, out var left) || !TryGet(x + axis.Y, y - axis.X, out var right)) continue;
            if (DistanceSquared(current, left) > contrast * contrast && DistanceSquared(current, right) > contrast * contrast &&
                DistanceSquared(left, right) < Math.Max(match * match * 4, contrast * contrast)) return true;
        }
        return false;

        bool TryGet(int px, int py, out Features value)
        {
            value = default;
            if (px < 0 || py < 0 || px >= width || py >= height) return false;
            int pixel = pixels[py * width + px];
            if ((uint)pixel >> 24 == 0) return false;
            value = GetFeatures(pixel & 0xFFFFFF, cache);
            return true;
        }
    }

    private static double LegacyPreference(int candidate, int current, ManualBlockCriteria criteria)
        => 4 * criteria.BrightnessImportance * (Math.Abs(ColorWeights.GetNormalizedBrightness(candidate) - criteria.TargetBrightness) -
                Math.Abs(ColorWeights.GetNormalizedBrightness(current) - criteria.TargetBrightness)) +
            .5 * criteria.ContrastImportance * (Math.Abs(ColorWeights.GetContrastWeight(candidate) - criteria.TargetContrast) -
                Math.Abs(ColorWeights.GetContrastWeight(current) - criteria.TargetContrast)) +
            .5 * criteria.SaturationImportance * (Math.Abs(ColorWeights.GetSaturationWeight(candidate) - criteria.TargetSaturation) -
                Math.Abs(ColorWeights.GetSaturationWeight(current) - criteria.TargetSaturation));

    private static Features GetFeatures(int rgb, Dictionary<int, Features> cache)
    {
        if (cache.TryGetValue(rgb, out var result)) return result;
        var (l, a, b) = ColorSpace.RgbToLab(rgb);
        result = new Features(l, a, b);
        cache[rgb] = result;
        return result;
    }

    private static double DistanceSquared(Features a, Features b)
        => (a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B);

    private static void Validate(ColorCleanupOptions options)
    {
        if (!double.IsFinite(options.ColorTolerance) || options.ColorTolerance is < 0 or > 40 ||
            !double.IsFinite(options.Strength) || options.Strength is < 0 or > 1 ||
            !double.IsFinite(options.DetailProtection) || options.DetailProtection is < 0 or > 1 ||
            !double.IsFinite(options.EdgeProtection) || options.EdgeProtection is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Сходство цветов: 0–40 ΔE; сила и защита: 0–1.");
    }

    private readonly record struct Features(double L, double A, double B);

    private static int[] ReadArgb(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var pixels = new int[checked(copy.Width * copy.Height)];
        var bits = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
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
