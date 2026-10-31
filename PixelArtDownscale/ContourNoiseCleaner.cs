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
    public double SimilarColorDistance { get; init; } = 24;
    public double MinimumPixelGradient { get; init; } = 120;
    public double MinimumNeighborGradient { get; init; } = 80;
    public int MinimumSimilarNeighbors { get; init; } = 2;
    public double MinimumAgreement { get; init; } = 0.35;
    public double MinimumOpposition { get; init; } = 0.1;
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
    private readonly record struct Gradient(double X, double Y)
    {
        public double Length => Math.Sqrt(X * X + Y * Y);
    }

    private const double ModelColorScale = 40;
    private const double MinimumSideDifference = 12;
    private static readonly double[] AngleOffsets = [-Math.PI / 4, -Math.PI / 8, 0, Math.PI / 8, Math.PI / 4];
    private static readonly double[] BoundaryOffsets = [-1.5, -1, -0.5, 0.5, 1, 1.5];

    public static ContourCleanResult Process(Bitmap source, ContourCleanOptions options)
        => Process(source, options, progress: null);

    public static ContourCleanResult Process(Bitmap source, ContourCleanOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ProcessWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static ContourCleanResult ProcessWithProgress(Bitmap source, ContourCleanOptions options, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        Validate(source, options);
        int width = source.Width, height = source.Height;
        int[] original = Read(source);
        int[] output = (int[])original.Clone();
        var labs = new Dictionary<int, Lab>();
        var proposals = new List<Proposal>();
        int radius = options.WindowSize / 2;
        var samples = new List<Sample>(options.WindowSize * options.WindowSize - 1);
        var candidates = FindCandidates(original, width, height, options, labs, tracking.Slice(0, 2));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!candidates.Contains(index)) continue;
                int current = original[index];
                int alpha = (int)((uint)current >> 24);
                if (alpha == 0) continue;

                samples.Clear();
                for (int e = Math.Max(0, y - radius); e <= Math.Min(height - 1, y + radius); e++)
                    for (int f = Math.Max(0, x - radius); f <= Math.Min(width - 1, x + radius); f++)
                    {
                        if (e == y && f == x) continue;
                        int neighbor = original[e * width + f];
                        if ((uint)neighbor >> 24 != (uint)alpha) continue;
                        int rgb = neighbor & 0xFFFFFF;
                        samples.Add(new Sample(f - x, e - y, rgb, GetLab(rgb, labs)));
                    }
                // The boundary model only decides whether a detected candidate can be replaced safely.
                if (samples.Count < (radius == 1 ? 4 : 12)) continue;
                double baseTwoColorCoverage = options.Strictness switch
                {
                    ContourCleanStrictness.Cautious => 0.80,
                    ContourCleanStrictness.Normal => 0.70,
                    _ => 0.60
                };
                double minimumTwoColorCoverage = Math.Clamp(baseTwoColorCoverage -
                    0.025 * (options.WindowSize - 7), 0.40, 0.90);
                if (TwoGroupCoverage(samples, options.SimilarColorDistance) < minimumTwoColorCoverage) continue;
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
                        if (TryFit(samples, nx, ny, offset, labs, options.SimilarColorDistance, out Model model))
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
            tracking.Report((1 + Interlocked.Increment(ref progressRows0) / (double)height) / 2.0);
        }

        using var candidateImage = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var candidateBuffer = BitmapBuffer.FromWritableBitmap(candidateImage))
        {
            foreach (int index in candidates)
            {
                int x = index % width, y = index / width;
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
        return new ContourCleanResult(Write(output, width, height), (Bitmap)candidateImage.Clone(), candidates.Count, changed);
    }

    private static void Validate(Bitmap source, ContourCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.WindowSize is < 3 or > 15 || options.WindowSize % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Размер окна должен быть нечётным числом от 3 до 15.");
        if (!Enum.IsDefined(options.Strictness))
            throw new ArgumentOutOfRangeException(nameof(options), "Неизвестная строгость очистки.");
        if (!double.IsFinite(options.SimilarColorDistance) || options.SimilarColorDistance is < 0 or > 100 ||
            !double.IsFinite(options.MinimumPixelGradient) || options.MinimumPixelGradient is < 0 or > 20000 ||
            !double.IsFinite(options.MinimumNeighborGradient) || options.MinimumNeighborGradient is < 0 or > 20000 ||
            options.MinimumSimilarNeighbors is < 1 or > 224 ||
            !double.IsFinite(options.MinimumAgreement) || options.MinimumAgreement is < 0 or > 1 ||
            !double.IsFinite(options.MinimumOpposition) || options.MinimumOpposition is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Некорректные пороги поиска кандидатов.");
    }

    public static ContourAnalysisResult Analyze(Bitmap source, ContourCleanOptions options)
        => Analyze(source, options, progress: null);

    public static ContourAnalysisResult Analyze(Bitmap source, ContourCleanOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = AnalyzeWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static ContourAnalysisResult AnalyzeWithProgress(Bitmap source, ContourCleanOptions options, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        Validate(source, options);
        int width = source.Width, height = source.Height;
        int[] original = Read(source);
        var labs = new Dictionary<int, Lab>();
        var candidates = FindCandidates(original, width, height, options, labs, tracking.Slice(0, 2));
        var heatmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var buffer = BitmapBuffer.FromWritableBitmap(heatmap))
        {
            for (int y = 0; y < height; y++)
            {
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
                    double strength = Math.Min(0.45, Math.Sqrt(horizontal * horizontal + vertical * vertical) / (80 * 80) * 0.45);
                    if (candidates.Contains(index)) strength = 1;
                    int other = (int)Math.Round(255 * (1 - strength));
                    buffer.SetPackedColor(x, y, (255 << 16) | (other << 8) | other);
                    buffer.SetAlpha(x, y, (byte)alpha);
                }
                tracking.Report((1 + Interlocked.Increment(ref progressRows0) / (double)height) / 2.0);
            }
        }
        return new ContourAnalysisResult(heatmap, candidates.Count);
    }

    private static HashSet<int> FindCandidates(int[] image, int width, int height,
        ContourCleanOptions options, Dictionary<int, Lab> labs)
        => FindCandidates(image, width, height, options, labs, progress: null);

    private static HashSet<int> FindCandidates(int[] image, int width, int height,
        ContourCleanOptions options, Dictionary<int, Lab> labs, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = FindCandidatesWithProgress(image, width, height, options, labs, tracking);
        tracking.Report(1);
        return result;
    }

    private static HashSet<int> FindCandidatesWithProgress(int[] image, int width, int height, ContourCleanOptions options, Dictionary<int, Lab> labs, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        var candidates = new HashSet<int>();
        int radius = options.WindowSize / 2;
        double colorLimit = options.SimilarColorDistance * options.SimilarColorDistance;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int alpha = (int)((uint)image[index] >> 24);
                if (alpha == 0 || options.ProtectDetails && IsProtectedDetail(image, width, height, x, y)) continue;
                int left = Math.Max(0, x - radius), right = Math.Min(width - 1, x + radius);
                int top = Math.Max(0, y - radius), bottom = Math.Min(height - 1, y + radius);
                Gradient pixelGradient = LocalGradient(image, width, height, x, y, alpha, labs);
                double pixelLength = pixelGradient.Length;
                if (pixelLength < options.MinimumPixelGradient || pixelLength <= 1e-9) continue;

                Lab color = GetLab(image[index] & 0xFFFFFF, labs);
                int count = 0;
                double commonX = 0, commonY = 0;
                for (int e = top; e <= bottom; e++)
                    for (int f = left; f <= right; f++)
                    {
                        if (e == y && f == x) continue;
                        int neighbor = image[e * width + f];
                        if ((uint)neighbor >> 24 != (uint)alpha ||
                            DistanceSquared(color, GetLab(neighbor & 0xFFFFFF, labs)) > colorLimit) continue;
                        Gradient gradient = LocalGradient(image, width, height, f, e, alpha, labs);
                        double length = gradient.Length;
                        if (length < options.MinimumNeighborGradient || length <= 1e-9) continue;
                        commonX += gradient.X / length;
                        commonY += gradient.Y / length;
                        count++;
                    }
                if (count < options.MinimumSimilarNeighbors) continue;
                double commonLength = Math.Sqrt(commonX * commonX + commonY * commonY);
                if (commonLength <= 1e-9 || commonLength / count < options.MinimumAgreement) continue;
                double opposition = -(pixelGradient.X * commonX + pixelGradient.Y * commonY) /
                    (pixelLength * commonLength);
                if (opposition >= options.MinimumOpposition) candidates.Add(index);
            }
            tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)height) / 1.0);
        }
        return candidates;
    }

    private static Gradient LocalGradient(int[] image, int width, int height, int x, int y,
        int alpha, Dictionary<int, Lab> labs)
    {
        Lab color = GetLab(image[y * width + x] & 0xFFFFFF, labs);
        double gx = 0, gy = 0, wx = 0, wy = 0;
        for (int e = Math.Max(0, y - 1); e <= Math.Min(height - 1, y + 1); e++)
            for (int f = Math.Max(0, x - 1); f <= Math.Min(width - 1, x + 1); f++)
            {
                int dx = f - x, dy = e - y;
                if (dx == 0 && dy == 0) continue;
                int neighbor = image[e * width + f];
                if ((uint)neighbor >> 24 != (uint)alpha) continue;
                double weight = 1.0 / (dx * dx + dy * dy);
                double difference = DistanceSquared(color, GetLab(neighbor & 0xFFFFFF, labs));
                gx += dx * weight * difference;
                gy += dy * weight * difference;
                wx += Math.Abs(dx) * weight;
                wy += Math.Abs(dy) * weight;
            }
        return new Gradient(wx > 0 ? gx / wx : 0, wy > 0 ? gy / wy : 0);
    }

    private static double TwoGroupCoverage(List<Sample> samples, double tolerance)
    {
        double limit = tolerance * tolerance;
        int first = -1, firstCount = 0;
        for (int q = 0; q < samples.Count; q++)
        {
            int count = 0;
            foreach (Sample sample in samples)
                if (DistanceSquared(sample.Lab, samples[q].Lab) <= limit) count++;
            if (count > firstCount) { first = q; firstCount = count; }
        }
        if (first < 0) return 0;
        int second = -1, bestCoverage = firstCount;
        for (int q = 0; q < samples.Count; q++)
        {
            if (DistanceSquared(samples[q].Lab, samples[first].Lab) <= limit) continue;
            int count = 0;
            foreach (Sample sample in samples)
                if (DistanceSquared(sample.Lab, samples[first].Lab) <= limit ||
                    DistanceSquared(sample.Lab, samples[q].Lab) <= limit) count++;
            if (count > bestCoverage) { second = q; bestCoverage = count; }
        }
        return second < 0 ? 0 : bestCoverage / (double)samples.Count;
    }

    private static double GradientAt(int[] original, int width, int height, int x, int y,
        int alpha, Lab center, Dictionary<int, Lab> labs)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return 0;
        int neighbor = original[y * width + x];
        if ((uint)neighbor >> 24 != (uint)alpha) return 0;
        return DistanceSquared(center, GetLab(neighbor & 0xFFFFFF, labs));
    }

    private static bool TryOrientation(int[] image, int width, int height, int x, int y, int radius,
        int alpha, Dictionary<int, Lab> labs, out double angle, out double coherence)
    {
        if (radius == 1) return TrySmallOrientation(image, width, height, x, y, alpha, labs, out angle, out coherence);
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

    private static bool TrySmallOrientation(int[] image, int width, int height, int x, int y,
        int alpha, Dictionary<int, Lab> labs, out double angle, out double coherence)
    {
        double xl = 0, xa = 0, xb = 0, yl = 0, ya = 0, yb = 0;
        // Собелевские пары 3×3 используют только соседей: проверяемый пиксель не участвует.
        AddPair(image, width, height, x - 1, y, x + 1, y, alpha, labs, .25, ref xl, ref xa, ref xb);
        AddPair(image, width, height, x - 1, y - 1, x + 1, y - 1, alpha, labs, .125, ref xl, ref xa, ref xb);
        AddPair(image, width, height, x - 1, y + 1, x + 1, y + 1, alpha, labs, .125, ref xl, ref xa, ref xb);
        AddPair(image, width, height, x, y - 1, x, y + 1, alpha, labs, .25, ref yl, ref ya, ref yb);
        AddPair(image, width, height, x - 1, y - 1, x - 1, y + 1, alpha, labs, .125, ref yl, ref ya, ref yb);
        AddPair(image, width, height, x + 1, y - 1, x + 1, y + 1, alpha, labs, .125, ref yl, ref ya, ref yb);
        double jxx = xl * xl + xa * xa + xb * xb;
        double jxy = xl * yl + xa * ya + xb * yb;
        double jyy = yl * yl + ya * ya + yb * yb;
        double energy = jxx + jyy;
        double axis = Math.Sqrt((jxx - jyy) * (jxx - jyy) + 4 * jxy * jxy);
        angle = 0.5 * Math.Atan2(2 * jxy, jxx - jyy);
        coherence = axis / (energy + 1e-9);
        return energy > 25;
    }

    private static void AddPair(int[] image, int width, int height, int x1, int y1, int x2, int y2,
        int alpha, Dictionary<int, Lab> labs, double weight, ref double l, ref double a, ref double b)
    {
        if (x1 < 0 || y1 < 0 || x2 >= width || y2 >= height) return;
        int first = image[y1 * width + x1], second = image[y2 * width + x2];
        if ((uint)first >> 24 != (uint)alpha || (uint)second >> 24 != (uint)alpha) return;
        Lab left = GetLab(first & 0xFFFFFF, labs), right = GetLab(second & 0xFFFFFF, labs);
        l += (right.L - left.L) * weight;
        a += (right.A - left.A) * weight;
        b += (right.B - left.B) * weight;
    }

    private static bool TryFit(List<Sample> samples, double nx, double ny, double offset,
        Dictionary<int, Lab> labs, double colorTolerance, out Model model)
    {
        var left = new List<Sample>();
        var right = new List<Sample>();
        foreach (Sample sample in samples)
        {
            if (sample.X * nx + sample.Y * ny < offset) left.Add(sample);
            else right.Add(sample);
        }
        int minimumSupport = samples.Count <= 8 ? 2 : Math.Max(4, samples.Count / 8);
        if (left.Count < minimumSupport || right.Count < minimumSupport)
        {
            model = default;
            return false;
        }
        int leftColor = Representative(left, labs, colorTolerance), rightColor = Representative(right, labs, colorTolerance);
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

    private static int Representative(List<Sample> samples, Dictionary<int, Lab> labs, double colorTolerance)
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
                support += Math.Max(0, 1 - DistanceSquared(sample.Lab, lab) /
                    Math.Max(colorTolerance * colorTolerance, 1));
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
        return Math.Sqrt(DistanceSquared(a, b));
    }

    private static double DistanceSquared(Lab a, Lab b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
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
