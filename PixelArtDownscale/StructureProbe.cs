using System.Drawing.Imaging;

namespace PixelArtDownscale;

public sealed class StructureProbeOptions
{
    public double SimilarColorDistance { get; init; } = 12;
    public int MaximumComponentSize { get; init; } = 8;
    public double MinimumNeighborAgreement { get; init; } = 0.7;
    public double MaximumReplacementDistance { get; init; } = 60;
    public double MinimumConfidence { get; init; } = 0.65;
    public bool ProtectTransparentBoundary { get; init; } = true;
    public bool ProtectLines { get; init; } = true;
    public double DistinctDetailDistance { get; init; } = 25;
}

public sealed record StructureProbeResult(Bitmap Image, Bitmap Heatmap, int ComponentCount,
    int CandidateComponents, int CandidatePixels, int ChangedPixels);

/// <summary>
/// Experimental component analysis. Color components are measured before any pixel is changed.
/// The image and heatmap keep the source dimensions and every alpha value.
/// </summary>
public static class StructureProbe
{
    private static readonly (int X, int Y)[] Neighbors8 =
    [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];
    private static readonly (int X, int Y)[] Neighbors4 =
    [(0, -1), (-1, 0), (1, 0), (0, 1)];

    public static StructureProbeResult Process(Bitmap source, StructureProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.SimilarColorDistance is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options.SimilarColorDistance));
        if (options.MaximumComponentSize is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumComponentSize));
        if (options.MinimumNeighborAgreement is < 0 or > 1 || options.MinimumConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MaximumReplacementDistance is <= 0 or > 150)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumReplacementDistance));
        if (options.DistinctDetailDistance is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options.DistinctDetailDistance));

        int width = source.Width, height = source.Height;
        int[] input = Read(source), output = (int[])input.Clone();
        int[] components = new int[input.Length];
        Array.Fill(components, -1);
        byte[] heat = new byte[input.Length];
        var labs = new Dictionary<int, (double L, double A, double B)>();
        var queue = new Queue<int>();
        var members = new List<int>();
        int componentCount = 0, candidateComponents = 0, candidatePixels = 0, changedPixels = 0;

        for (int start = 0; start < input.Length; start++)
        {
            int seed = input[start];
            if ((uint)seed >> 24 == 0 || components[start] >= 0) continue;
            int id = componentCount++;
            int alpha = (int)((uint)seed >> 24);
            int seedColor = seed & 0xFFFFFF;
            int size = 0, minX = width, minY = height, maxX = -1, maxY = -1;
            bool transparentBoundary = false;
            components[start] = id;
            queue.Enqueue(start);
            members.Clear();
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                size++;
                if (size <= options.MaximumComponentSize) members.Add(index);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                int color = input[index] & 0xFFFFFF;
                foreach (var (dx, dy) in Neighbors8)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    int neighborIndex = ny * width + nx, neighbor = input[neighborIndex];
                    if ((uint)neighbor >> 24 == 0)
                    {
                        transparentBoundary = true;
                        continue;
                    }
                    if ((uint)neighbor >> 24 != (uint)alpha || components[neighborIndex] >= 0) continue;
                    int neighborColor = neighbor & 0xFFFFFF;
                    if (Distance(seedColor, neighborColor, labs) > options.SimilarColorDistance ||
                        Distance(color, neighborColor, labs) > options.SimilarColorDistance) continue;
                    components[neighborIndex] = id;
                    queue.Enqueue(neighborIndex);
                }
            }

            if (size > options.MaximumComponentSize) continue;
            var votes = new Dictionary<int, int>();
            int boundary = 0;
            foreach (int index in members)
            {
                int x = index % width, y = index / width;
                foreach (var (dx, dy) in Neighbors4)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                    int neighborIndex = ny * width + nx, neighbor = input[neighborIndex];
                    if ((uint)neighbor >> 24 != (uint)alpha || components[neighborIndex] == id) continue;
                    int color = neighbor & 0xFFFFFF;
                    votes[color] = votes.GetValueOrDefault(color) + 1;
                    boundary++;
                }
            }
            if (boundary == 0) continue;

            var best = votes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First();
            double agreement = (double)best.Value / boundary;
            double distance = members.Average(index => Distance(input[index] & 0xFFFFFF, best.Key, labs));
            if (agreement < options.MinimumNeighborAgreement || distance > options.MaximumReplacementDistance) continue;

            int spanX = maxX - minX + 1, spanY = maxY - minY + 1;
            int cardinalEdges = 0, diagonalEdges = 0;
            foreach (int index in members)
            {
                int x = index % width, y = index / width;
                foreach (var (dx, dy) in Neighbors8)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height ||
                        components[ny * width + nx] != id) continue;
                    if (dx == 0 || dy == 0) cardinalEdges++;
                    else diagonalEdges++;
                }
            }
            bool line = size >= 3 && (Math.Max(spanX, spanY) >= 3 * Math.Max(1, Math.Min(spanX, spanY)) ||
                (diagonalEdges >= 2 * (size - 1) && cardinalEdges <= 2));
            bool protectedShape = (options.ProtectTransparentBoundary && transparentBoundary) ||
                (options.ProtectLines && line) ||
                (size <= 2 && distance >= options.DistinctDetailDistance);
            double confidence = agreement * (1 - 0.25 * distance / options.MaximumReplacementDistance) *
                (1 - 0.15 * (size - 1) / options.MaximumComponentSize);
            if (protectedShape) confidence *= 0.35;
            candidateComponents++;
            candidatePixels += size;
            byte intensity = (byte)Math.Clamp((int)Math.Round(255 * confidence), 1, 255);
            foreach (int index in members) heat[index] = intensity;
            if (protectedShape || confidence < options.MinimumConfidence) continue;
            foreach (int index in members)
            {
                int replacement = (input[index] & unchecked((int)0xFF000000)) | best.Key;
                if (replacement == input[index]) continue;
                output[index] = replacement;
                changedPixels++;
            }
        }

        // All replacement proposals use the untouched source; one proposal never affects another.
        return new StructureProbeResult(Write(output, width, height), WriteHeatmap(input, heat, width, height),
            componentCount, candidateComponents, candidatePixels, changedPixels);
    }

    private static double Distance(int a, int b, Dictionary<int, (double L, double A, double B)> labs)
    {
        if (a == b) return 0;
        if (!labs.TryGetValue(a, out var first)) labs[a] = first = ColorSpace.RgbToLab(a);
        if (!labs.TryGetValue(b, out var second)) labs[b] = second = ColorSpace.RgbToLab(b);
        double dl = first.L - second.L, da = first.A - second.A, db = first.B - second.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    private static int[] Read(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        using var buffer = BitmapBuffer.FromBitmap(copy);
        int[] pixels = new int[source.Width * source.Height];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                pixels[y * source.Width + x] = (buffer.GetAlpha(x, y) << 24) | buffer.GetPackedColor(x, y);
        return pixels;
    }

    private static Bitmap Write(int[] pixels, int width, int height)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var buffer = BitmapBuffer.FromWritableBitmap(image);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int argb = pixels[y * width + x];
                buffer.SetPackedColor(x, y, argb & 0xFFFFFF);
                buffer.SetAlpha(x, y, (byte)((uint)argb >> 24));
            }
        return image;
    }

    private static Bitmap WriteHeatmap(int[] input, byte[] heat, int width, int height)
    {
        int[] pixels = new int[input.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            int alpha = (int)((uint)input[i] >> 24);
            if (alpha == 0) { pixels[i] = input[i]; continue; }
            int gray = (int)Math.Round(0.2126 * ColorSpace.GetR(input[i]) +
                0.7152 * ColorSpace.GetG(input[i]) + 0.0722 * ColorSpace.GetB(input[i]));
            int red = heat[i] == 0 ? gray : 255;
            int other = heat[i] == 0 ? gray : (int)Math.Round(gray * (1 - heat[i] / 255.0));
            pixels[i] = (alpha << 24) | (red << 16) | (other << 8) | other;
        }
        return Write(pixels, width, height);
    }
}
