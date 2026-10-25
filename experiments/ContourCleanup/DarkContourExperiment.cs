using System.Drawing;
using System.Drawing.Imaging;
using PixelArtDownscale;

internal static class DarkContourExperiment
{
    internal sealed record Options(double DarkLightness, int MaximumDarkNeighbors,
        double BrightColorTolerance, double MinimumLightnessGain, int AlphaClearance = 1,
        double MinimumBrightConsensus = 0);

    internal sealed record Result(Bitmap Image, int ChangedPixels);

    internal static Result Clean(Bitmap source, Options options)
    {
        int width = source.Width, height = source.Height;
        var original = new Color[width * height];
        var dark = new bool[original.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color color = source.GetPixel(x, y);
                original[index] = color;
                dark[index] = color.A > 0 && Lab(color).L < options.DarkLightness;
            }

        var output = (Color[])original.Clone();
        int changed = 0;
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int index = y * width + x;
                if (!dark[index]) continue;
                if (!HasAlphaClearance(original, width, height, x, y, options.AlphaClearance)) continue;
                var surrounding = new List<Color>(8);
                int darkNeighbors = 0;
                bool allVisible = true;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int neighborIndex = (y + dy) * width + x + dx;
                        Color neighbor = original[neighborIndex];
                        if (neighbor.A == 0) { allVisible = false; continue; }
                        if (dark[neighborIndex]) darkNeighbors++;
                        else surrounding.Add(neighbor);
                    }
                if (!allVisible || darkNeighbors > options.MaximumDarkNeighbors ||
                    DarkRingComponents(dark, width, x, y) > 1 || surrounding.Count < 5) continue;

                Color replacement = BrightRepresentative(surrounding, options.BrightColorTolerance);
                int matching = surrounding.Count(color => DistanceSquared(Lab(color), Lab(replacement)) <=
                    options.BrightColorTolerance * options.BrightColorTolerance);
                if (matching / (double)surrounding.Count < options.MinimumBrightConsensus) continue;
                if (Lab(replacement).L - Lab(original[index]).L < options.MinimumLightnessGain) continue;
                output[index] = Color.FromArgb(original[index].A, replacement.R, replacement.G, replacement.B);
                changed++;
            }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) result.SetPixel(x, y, output[y * width + x]);
        return new Result(result, changed);
    }

    private static bool HasAlphaClearance(Color[] original, int width, int height, int x, int y, int radius)
    {
        if (x - radius < 0 || y - radius < 0 || x + radius >= width || y + radius >= height)
            return false;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (original[(y + dy) * width + x + dx].A == 0) return false;
        return true;
    }

    private static Color BrightRepresentative(List<Color> colors, double tolerance)
    {
        int selected = 0;
        double best = double.NegativeInfinity;
        for (int q = 0; q < colors.Count; q++)
        {
            var candidate = Lab(colors[q]);
            double support = 0;
            foreach (Color color in colors)
            {
                var lab = Lab(color);
                double distance = Math.Sqrt((candidate.L - lab.L) * (candidate.L - lab.L) +
                    (candidate.A - lab.A) * (candidate.A - lab.A) +
                    (candidate.B - lab.B) * (candidate.B - lab.B));
                support += Math.Max(0, 1 - distance / Math.Max(tolerance, 1));
            }
            if (support > best) { best = support; selected = q; }
        }
        return colors[selected];
    }

    private static int DarkRingComponents(bool[] dark, int width, int x, int y)
    {
        Span<bool> present = stackalloc bool[9], seen = stackalloc bool[9];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    present[(dy + 1) * 3 + dx + 1] = dark[(y + dy) * width + x + dx];
        int components = 0;
        Span<int> queue = stackalloc int[8];
        for (int q = 0; q < 9; q++)
        {
            if (!present[q] || seen[q]) continue;
            components++;
            int head = 0, count = 0;
            queue[count++] = q; seen[q] = true;
            while (head < count)
            {
                int pixel = queue[head++], py = pixel / 3, px = pixel % 3;
                for (int e = Math.Max(0, py - 1); e <= Math.Min(2, py + 1); e++)
                    for (int f = Math.Max(0, px - 1); f <= Math.Min(2, px + 1); f++)
                    {
                        int neighbor = e * 3 + f;
                        if (neighbor == 4 || !present[neighbor] || seen[neighbor]) continue;
                        seen[neighbor] = true;
                        queue[count++] = neighbor;
                    }
            }
        }
        return components;
    }

    private static (double L, double A, double B) Lab(Color color)
        => ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF);

    private static double DistanceSquared((double L, double A, double B) a, (double L, double A, double B) b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
    }
}
