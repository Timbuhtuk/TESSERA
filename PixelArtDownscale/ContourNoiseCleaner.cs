using System.Drawing.Imaging;

namespace PixelArtDownscale;

public enum ContourCleanStrictness
{
    Cautious,
    Normal,
    Strong
}

public sealed class ContourCleanOptions
{
    public int WindowSize { get; init; } = 7;
    public ContourCleanStrictness Strictness { get; init; } = ContourCleanStrictness.Cautious;
    public bool ProtectDetails { get; init; } = true;
}

public sealed record ContourCleanResult(Bitmap Image, Bitmap CandidateMap, int CandidateCount, int ChangedPixels);
public sealed record ContourAnalysisResult(Bitmap Heatmap, int CandidateCount);

/// <summary>Repairs confidently misplaced colors along two-sided pixel-art contours.</summary>
public static class ContourNoiseCleaner
{
    private readonly record struct Lab(double L, double A, double B);
    private readonly record struct Sample(int X, int Y, int Color, Lab Lab);
    private readonly record struct Model(int PredictedColor, double Error);
    private readonly record struct Proposal(int Index, int Color, double Confidence);

    private const double ModelColorScale = 40;
    private const double MinimumSideDifference = 12;
    private static readonly double[] AngleOffsets = [-Math.PI / 4, -Math.PI / 8, 0, Math.PI / 8, Math.PI / 4];
    private static readonly double[] BoundaryOffsets = [-1.5, -1, -0.5, 0.5, 1, 1.5];

    public static ContourCleanResult Process(Bitmap source, ContourCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.WindowSize is < 5 or > 15 || options.WindowSize % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Размер окна должен быть нечётным числом от 5 до 15.");
        if (!Enum.IsDefined(options.Strictness))
            throw new ArgumentOutOfRangeException(nameof(options), "Неизвестная строгость очистки.");

        int width = source.Width, height = source.Height;
        int[] original = Read(source);
        int[] output = (int[])original.Clone();
        var labs = new Dictionary<int, Lab>();
        var proposals = new List<Proposal>();
        int radius = options.WindowSize / 2;
        var samples = new List<Sample>(options.WindowSize * options.WindowSize - 1);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int current = original[index];
                int alpha = (int)((uint)current >> 24);
                if (alpha == 0) continue;

                samples.Clear();
                var colors = new Dictionary<int, int>();
                for (int e = Math.Max(0, y - radius); e <= Math.Min(height - 1, y + radius); e++)
                    for (int f = Math.Max(0, x - radius); f <= Math.Min(width - 1, x + radius); f++)
                    {
                        if (e == y && f == x) continue;
                        int neighbor = original[e * width + f];
                        if ((uint)neighbor >> 24 != (uint)alpha) continue;
                        int rgb = neighbor & 0xFFFFFF;
                        samples.Add(new Sample(f - x, e - y, rgb, GetLab(rgb, labs)));
                        colors[rgb] = colors.GetValueOrDefault(rgb) + 1;
                    }
                // A uniform surround belongs to the preceding cleanup stage.
                if (samples.Count < 12 || colors.Count < 2) continue;
                int largest = 0, secondLargest = 0;
                foreach (int count in colors.Values)
                {
                    if (count > largest) { secondLargest = largest; largest = count; }
                    else if (count > secondLargest) secondLargest = count;
                }
                double baseTwoColorCoverage = options.Strictness switch
                {
                    ContourCleanStrictness.Cautious => 0.80,
                    ContourCleanStrictness.Normal => 0.70,
                    _ => 0.60
                };
                double minimumTwoColorCoverage = Math.Clamp(baseTwoColorCoverage -
                    0.025 * (options.WindowSize - 7), 0.40, 0.90);
                if ((largest + secondLargest) / (double)samples.Count < minimumTwoColorCoverage) continue;
                if (options.ProtectDetails && IsProtectedDetail(original, width, height, x, y)) continue;
                if (!TryOrientation(original, width, height, x, y, radius, alpha, labs,
                    out double angle, out double coherence)) continue;

                double maximumError = options.Strictness switch
                {
                    ContourCleanStrictness.Cautious => 0.08,
                    ContourCleanStrictness.Normal => 0.14,
                    _ => 0.22
                };
                double minimumCoherence = options.Strictness switch
                {
                    ContourCleanStrictness.Cautious => 0.40,
                    ContourCleanStrictness.Normal => 0.30,
                    _ => 0.20
                };
                if (coherence < minimumCoherence) continue;

                var models = new List<Model>();
                foreach (double turn in AngleOffsets)
                {
                    double nx = Math.Cos(angle + turn), ny = Math.Sin(angle + turn);
                    foreach (double offset in BoundaryOffsets)
                    {
                        if (TryFit(samples, nx, ny, offset, labs, out Model model))
                            models.Add(model);
                    }
                }
                if (models.Count == 0) continue;
                double bestError = models.Min(model => model.Error);
                if (bestError > maximumError) continue;
                double tolerance = options.Strictness switch
                {
                    ContourCleanStrictness.Cautious => 0.015,
                    ContourCleanStrictness.Normal => 0.025,
                    _ => 0.04
                };
                var good = models.Where(model => model.Error <= bestError + tolerance).ToArray();
                int predicted = good[0].PredictedColor;
                if (good.Any(model => model.PredictedColor != predicted)) continue;
                if (Distance(GetLab(current & 0xFFFFFF, labs), GetLab(predicted, labs)) <
                    (options.Strictness == ContourCleanStrictness.Strong ? 8 : 12)) continue;
                proposals.Add(new Proposal(index, predicted, coherence * (1 - bestError)));
            }

        using var candidateImage = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var candidateBuffer = BitmapBuffer.FromWritableBitmap(candidateImage))
        {
            foreach (Proposal proposal in proposals)
            {
                int x = proposal.Index % width, y = proposal.Index / width;
                candidateBuffer.SetPackedColor(x, y, 0xFF00FF);
                candidateBuffer.SetAlpha(x, y, 255);
            }
        }

        int changed = 0;
        foreach (Proposal proposal in proposals.OrderByDescending(item => item.Confidence))
        {
            int x = proposal.Index % width, y = proposal.Index / width;
            if (options.ProtectDetails && ChangesConnections(output, width, height, x, y, proposal.Color)) continue;
            output[proposal.Index] = (original[proposal.Index] & unchecked((int)0xFF000000)) | proposal.Color;
            changed++;
        }
        return new ContourCleanResult(Write(output, width, height), (Bitmap)candidateImage.Clone(), proposals.Count, changed);
    }

    public static ContourAnalysisResult Analyze(Bitmap source, ContourCleanOptions options)
    {
        var result = Process(source, options);
        using (result.Image)
        using (result.CandidateMap)
        {
            int width = source.Width, height = source.Height;
            int[] original = Read(source);
            int[] proposed = Read(result.Image);
            var labs = new Dictionary<int, Lab>();
            var heatmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var buffer = BitmapBuffer.FromWritableBitmap(heatmap);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    int alpha = (int)((uint)original[index] >> 24);
                    if (alpha == 0) continue;

                    Lab center = GetLab(original[index] & 0xFFFFFF, labs);
                    double horizontal = GradientAt(original, width, height, x - 1, y, alpha, center, labs) +
                        GradientAt(original, width, height, x + 1, y, alpha, center, labs);
                    double vertical = GradientAt(original, width, height, x, y - 1, alpha, center, labs) +
                        GradientAt(original, width, height, x, y + 1, alpha, center, labs);
                    double strength = Math.Min(0.45, Math.Sqrt(horizontal * horizontal + vertical * vertical) / 80 * 0.45);
                    if (original[index] != proposed[index]) strength = 1;
                    int other = (int)Math.Round(255 * (1 - strength));
                    buffer.SetPackedColor(x, y, (255 << 16) | (other << 8) | other);
                    buffer.SetAlpha(x, y, (byte)alpha);
                }
            return new ContourAnalysisResult(heatmap, result.ChangedPixels);
        }
    }

    private static double GradientAt(int[] original, int width, int height, int x, int y,
        int alpha, Lab center, Dictionary<int, Lab> labs)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return 0;
        int neighbor = original[y * width + x];
        if ((uint)neighbor >> 24 != (uint)alpha) return 0;
        return Distance(center, GetLab(neighbor & 0xFFFFFF, labs));
    }

    private static bool TryOrientation(int[] image, int width, int height, int x, int y, int radius,
        int alpha, Dictionary<int, Lab> labs, out double angle, out double coherence)
    {
        double jxx = 0, jxy = 0, jyy = 0;
        for (int e = Math.Max(1, y - radius + 1); e <= Math.Min(height - 2, y + radius - 1); e++)
            for (int f = Math.Max(1, x - radius + 1); f <= Math.Min(width - 2, x + radius - 1); f++)
            {
                // Neither the gradient nor its operands may depend on the pixel being tested.
                if (Math.Abs(f - x) + Math.Abs(e - y) <= 1) continue;
                int left = image[e * width + f - 1], right = image[e * width + f + 1];
                int top = image[(e - 1) * width + f], bottom = image[(e + 1) * width + f];
                if ((uint)left >> 24 != (uint)alpha || (uint)right >> 24 != (uint)alpha ||
                    (uint)top >> 24 != (uint)alpha || (uint)bottom >> 24 != (uint)alpha) continue;
                Lab a = GetLab(left & 0xFFFFFF, labs), b = GetLab(right & 0xFFFFFF, labs);
                Lab c = GetLab(top & 0xFFFFFF, labs), d = GetLab(bottom & 0xFFFFFF, labs);
                double xl = (b.L - a.L) * .5, xa = (b.A - a.A) * .5, xb = (b.B - a.B) * .5;
                double yl = (d.L - c.L) * .5, ya = (d.A - c.A) * .5, yb = (d.B - c.B) * .5;
                jxx += xl * xl + xa * xa + xb * xb;
                jxy += xl * yl + xa * ya + xb * yb;
                jyy += yl * yl + ya * ya + yb * yb;
            }
        double energy = jxx + jyy;
        double axis = Math.Sqrt((jxx - jyy) * (jxx - jyy) + 4 * jxy * jxy);
        angle = 0.5 * Math.Atan2(2 * jxy, jxx - jyy);
        coherence = axis / (energy + 1e-9);
        return energy > 25;
    }

    private static bool TryFit(List<Sample> samples, double nx, double ny, double offset,
        Dictionary<int, Lab> labs, out Model model)
    {
        var left = new List<Sample>();
        var right = new List<Sample>();
        foreach (Sample sample in samples)
        {
            if (sample.X * nx + sample.Y * ny < offset) left.Add(sample);
            else right.Add(sample);
        }
        int minimumSupport = Math.Max(4, samples.Count / 8);
        if (left.Count < minimumSupport || right.Count < minimumSupport)
        {
            model = default;
            return false;
        }
        int leftColor = Representative(left, labs), rightColor = Representative(right, labs);
        Lab leftLab = GetLab(leftColor, labs), rightLab = GetLab(rightColor, labs);
        if (Distance(leftLab, rightLab) < MinimumSideDifference)
        {
            model = default;
            return false;
        }
        double error = 0;
        foreach (Sample sample in left) error += Loss(sample.Lab, leftLab);
        foreach (Sample sample in right) error += Loss(sample.Lab, rightLab);
        model = new Model(0 < offset ? leftColor : rightColor, error / samples.Count);
        return true;
    }

    private static int Representative(List<Sample> samples, Dictionary<int, Lab> labs)
    {
        var counts = new Dictionary<int, int>();
        foreach (Sample sample in samples)
            counts[sample.Color] = counts.GetValueOrDefault(sample.Color) + 1;
        int selected = -1;
        double best = double.NegativeInfinity;
        foreach (int color in counts.Keys)
        {
            Lab lab = GetLab(color, labs);
            double support = 0;
            foreach (Sample sample in samples)
                support += Math.Max(0, 1 - Math.Pow(Distance(sample.Lab, lab) / 12, 2));
            if (support > best + 1e-9 || (Math.Abs(support - best) <= 1e-9 && color < selected))
            {
                best = support;
                selected = color;
            }
        }
        return selected;
    }

    private static double Loss(Lab a, Lab b)
    {
        double distance = Distance(a, b) / ModelColorScale;
        return Math.Min(distance * distance, 1);
    }

    private static double Distance(Lab a, Lab b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return Math.Sqrt(l * l + c * c + d * d);
    }

    private static Lab GetLab(int color, Dictionary<int, Lab> cache)
    {
        if (cache.TryGetValue(color, out Lab lab)) return lab;
        var value = ColorSpace.RgbToLab(color);
        lab = new Lab(value.L, value.A, value.B);
        cache.Add(color, lab);
        return lab;
    }

    private static bool IsProtectedDetail(int[] image, int width, int height, int x, int y)
    {
        int current = image[y * width + x];
        int matches = 0, lastX = 0, lastY = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !Same(image, width, height, x + dx, y + dy, current)) continue;
                matches++; lastX = dx; lastY = dy;
            }
        if (matches == 1 && Same(image, width, height, x + lastX * 2, y + lastY * 2, current)) return true;
        foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
            if (Same(image, width, height, x + dx, y + dy, current) &&
                Same(image, width, height, x - dx, y - dy, current) && matches <= 4) return true;
        return false;
    }

    private static bool ChangesConnections(int[] image, int width, int height, int x, int y, int replacement)
    {
        int alpha = image[y * width + x] & unchecked((int)0xFF000000);
        int original = image[y * width + x];
        int next = alpha | replacement;
        return RingComponents(image, width, height, x, y, original) > 1 ||
               RingComponents(image, width, height, x, y, next) > 1;
    }

    private static int RingComponents(int[] image, int width, int height, int x, int y, int color)
    {
        Span<bool> present = stackalloc bool[9], visited = stackalloc bool[9];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    present[(dy + 1) * 3 + dx + 1] = Same(image, width, height, x + dx, y + dy, color);
        int components = 0;
        Span<int> queue = stackalloc int[8];
        for (int q = 0; q < 9; q++)
        {
            if (!present[q] || visited[q]) continue;
            components++;
            int head = 0, count = 0;
            queue[count++] = q; visited[q] = true;
            while (head < count)
            {
                int current = queue[head++], cy = current / 3, cx = current % 3;
                for (int e = Math.Max(0, cy - 1); e <= Math.Min(2, cy + 1); e++)
                    for (int f = Math.Max(0, cx - 1); f <= Math.Min(2, cx + 1); f++)
                    {
                        int neighbor = e * 3 + f;
                        if (neighbor == 4 || !present[neighbor] || visited[neighbor]) continue;
                        visited[neighbor] = true;
                        queue[count++] = neighbor;
                    }
            }
        }
        return components;
    }

    private static bool Same(int[] image, int width, int height, int x, int y, int color)
        => x >= 0 && y >= 0 && x < width && y < height && image[y * width + x] == color;

    private static int[] Read(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        using var buffer = BitmapBuffer.FromBitmap(copy);
        int[] image = new int[checked(source.Width * source.Height)];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                image[y * source.Width + x] = buffer.GetPackedColor(x, y) | (buffer.GetAlpha(x, y) << 24);
        return image;
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
}
