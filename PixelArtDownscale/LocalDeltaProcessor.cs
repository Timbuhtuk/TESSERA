using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public sealed record LocalDeltaOptions(double Delta = 8, int Radius = 2, int Passes = 1, bool ProtectDetails = true);

public sealed record LocalDeltaResult(Bitmap Image, int ChangedPixels, int ProtectedPixels, double MaximumDelta,
    int ColorsBefore, int ColorsAfter);

public static class LocalDeltaProcessor
{
    private readonly record struct Lab(double L, double A, double B)
    {
        public double DistanceSquared(Lab other)
            => (L - other.L) * (L - other.L) + (A - other.A) * (A - other.A) + (B - other.B) * (B - other.B);
    }

    public static LocalDeltaResult Process(Bitmap source, LocalDeltaOptions options)
    {
        if (!double.IsFinite(options.Delta) || options.Delta is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(options.Delta));
        if (options.Radius is < 1 or > 5 || options.Passes is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(options));

        int width = source.Width, height = source.Height;
        int[] original = Read(source), input = (int[])original.Clone();
        var indices = new Dictionary<int, int>();
        var colors = new List<int>();
        var labs = new List<Lab>();
        var originalIndices = new int[original.Length];
        for (int q = 0; q < original.Length; q++)
        {
            int rgb = original[q] & 0xFFFFFF;
            if (!indices.TryGetValue(rgb, out int index))
            {
                index = colors.Count;
                indices.Add(rgb, index);
                colors.Add(rgb);
                var lab = ColorSpace.RgbToLab(rgb);
                labs.Add(new Lab(lab.L, lab.A, lab.B));
            }
            originalIndices[q] = index;
        }
        Lab[] palette = labs.ToArray();
        int[] rgbColors = colors.ToArray(), currentIndices = (int[])originalIndices.Clone();
        double deltaSquared = options.Delta * options.Delta;
        double lineToleranceSquared = Math.Pow(Math.Min(.75, options.Delta * .2), 2);
        int diameter = options.Radius * 2 + 1, capacity = diameter * diameter;
        var spatial = new double[capacity];
        double sigma = Math.Max(1, options.Radius * .8);
        for (int y = -options.Radius; y <= options.Radius; y++)
            for (int x = -options.Radius; x <= options.Radius; x++)
                spatial[(y + options.Radius) * diameter + x + options.Radius] = Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
        var protectedMask = new bool[input.Length];
        for (int pass = 0; pass < options.Passes && options.Delta > 0; pass++)
        {
            int[] output = (int[])input.Clone(), nextIndices = (int[])currentIndices.Clone();
            Parallel.For(0, height, y =>
            {
                Span<int> candidates = stackalloc int[capacity];
                Span<double> weights = stackalloc double[capacity];
                for (int x = 0; x < width; x++)
                {
                    int q = y * width + x, alpha = (int)((uint)original[q] >> 24);
                    if (alpha == 0) continue;
                    int current = currentIndices[q];
                    Lab reference = palette[originalIndices[q]];
                    if (options.ProtectDetails && (HasStablePatch(x, y, width, height, original) ||
                        IsThinLine(x, y, width, height, original, originalIndices, palette, lineToleranceSquared)))
                    {
                        protectedMask[q] = true;
                        continue;
                    }
                    int count = 0;
                    double totalWeight = 0, l = 0, a = 0, b = 0;
                    for (int e = Math.Max(0, y - options.Radius); e <= Math.Min(height - 1, y + options.Radius); e++)
                        for (int f = Math.Max(0, x - options.Radius); f <= Math.Min(width - 1, x + options.Radius); f++)
                        {
                            int p = e * width + f;
                            // Alpha is a boundary, not a color to be averaged or expanded.
                            if ((uint)original[p] >> 24 != (uint)alpha) continue;
                            int candidate = currentIndices[p];
                            Lab color = palette[candidate];
                            // Bound every pass against the original, preventing cumulative color drift.
                            if (reference.DistanceSquared(color) > deltaSquared + 1e-10) continue;
                            double weight = spatial[(e - y + options.Radius) * diameter + f - x + options.Radius];
                            int index = 0;
                            while (index < count && candidates[index] != candidate) index++;
                            if (index == count) { candidates[count] = candidate; weights[count++] = 0; }
                            weights[index] += weight;
                            totalWeight += weight;
                            l += color.L * weight; a += color.A * weight; b += color.B * weight;
                        }
                    if (count < 2) continue;
                    int selected = current;
                    double maximumWeight = -1;
                    for (int e = 0; e < count; e++)
                    {
                        if (weights[e] > maximumWeight + 1e-10 ||
                            (Math.Abs(weights[e] - maximumWeight) < 1e-10 && candidates[e] == current))
                        { maximumWeight = weights[e]; selected = candidates[e]; }
                    }
                    if (maximumWeight < totalWeight * .5)
                    {
                        // Select an existing local color nearest the weighted mean; create no new RGB values.
                        var mean = new Lab(l / totalWeight, a / totalWeight, b / totalWeight);
                        selected = current;
                        double cost = palette[current].DistanceSquared(mean);
                        for (int e = 0; e < count; e++)
                        {
                            int candidate = candidates[e];
                            double candidateCost = palette[candidate].DistanceSquared(mean);
                            if (candidateCost < cost - 1e-10) { cost = candidateCost; selected = candidate; }
                        }
                    }
                    output[q] = (original[q] & unchecked((int)0xFF000000)) | rgbColors[selected];
                    nextIndices[q] = selected;
                }
            });
            input = output;
            currentIndices = nextIndices;
        }
        int changed = 0, protectedPixels = 0;
        double maximumDelta = 0;
        var before = new HashSet<int>(); var after = new HashSet<int>();
        for (int q = 0; q < input.Length; q++)
        {
            if (original[q] != input[q]) changed++;
            if (protectedMask[q]) protectedPixels++;
            if ((uint)original[q] >> 24 == 0) continue;
            before.Add(original[q] & 0xFFFFFF); after.Add(input[q] & 0xFFFFFF);
            maximumDelta = Math.Max(maximumDelta, Math.Sqrt(palette[originalIndices[q]].DistanceSquared(palette[currentIndices[q]])));
        }
        return new LocalDeltaResult(Write(input, width, height), changed, protectedPixels, maximumDelta, before.Count, after.Count);
    }

    private static bool HasStablePatch(int x, int y, int width, int height, int[] original)
    {
        int current = original[y * width + x];
        // Protect coherent cells, including their corners, while allowing tiny 2x2
        // low-contrast speckles to be cleaned. Contrast still protects smaller bright details.
        for (int e = -2; e <= 0; e++)
            for (int f = -2; f <= 0; f++)
            {
                int left = x + f, top = y + e;
                if (left < 0 || top < 0 || left + 2 >= width || top + 2 >= height) continue;
                bool same = true;
                for (int y1 = 0; y1 < 3 && same; y1++)
                    for (int x1 = 0; x1 < 3; x1++)
                        if (original[(top + y1) * width + left + x1] != current) { same = false; break; }
                if (same) return true;
            }
        return false;
    }

    private static bool IsThinLine(int x, int y, int width, int height, int[] original, int[] indices, Lab[] labs, double tolerance)
    {
        int q = y * width + x;
        int alpha = (int)((uint)original[q] >> 24);
        Lab current = labs[indices[q]];
        bool Similar(int px, int py)
        {
            if (px < 0 || py < 0 || px >= width || py >= height) return false;
            int p = py * width + px;
            return (uint)original[p] >> 24 == (uint)alpha && current.DistanceSquared(labs[indices[p]]) <= tolerance + 1e-10;
        }
        bool Ridge(int px, int py)
            => (Similar(px - 1, py) && Similar(px + 1, py) && !Similar(px, py - 1) && !Similar(px, py + 1)) ||
                (Similar(px, py - 1) && Similar(px, py + 1) && !Similar(px - 1, py) && !Similar(px + 1, py)) ||
                (Similar(px - 1, py - 1) && Similar(px + 1, py + 1) && !Similar(px + 1, py - 1) && !Similar(px - 1, py + 1)) ||
                (Similar(px + 1, py - 1) && Similar(px - 1, py + 1) && !Similar(px - 1, py - 1) && !Similar(px + 1, py + 1));
        // A consistent narrow ridge is stronger evidence of a detail than its area.
        if (Ridge(x, y)) return true;
        // Include the endpoints and corners attached to that ridge.
        for (int e = -1; e <= 1; e++)
            for (int f = -1; f <= 1; f++)
                if ((e != 0 || f != 0) && Similar(x + f, y + e) && Ridge(x + f, y + e)) return true;
        return false;
    }

    internal static int[] Read(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, copy.Width, copy.Height);
        BitmapData data = copy.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new int[copy.Width * copy.Height];
            for (int y = 0; y < copy.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, result, y * copy.Width, copy.Width);
            return result;
        }
        finally { copy.UnlockBits(data); }
    }

    internal static Bitmap Write(int[] pixels, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally { result.UnlockBits(data); }
        return result;
    }
}
