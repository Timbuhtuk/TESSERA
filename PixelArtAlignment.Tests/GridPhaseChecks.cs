using PixelArtAlignment;

namespace PixelArtAlignment.Tests;

internal static class GridPhaseChecks
{
    public static void Run()
    {
        var aligner = new PixelGridAligner();
        // Known reference images catch whole-cell translations even when the output
        // grid is perfectly regular. Include both phase wrapping and the drift limit.
        (int Style, int Pitch, string Kind)[] cases =
        [
            (2, 4, "local-drift"),
            (2, 5, "local-drift"), (2, 5, "antialias"),
            (2, 8, "local-drift"), (2, 8, "antialias"),
            (3, 8, "local-drift"),
            (1, 5, "local-drift"), (1, 5, "antialias"),
            (1, 8, "local-drift"), (1, 8, "antialias")
        ];
        foreach (var item in cases)
        {
            using var logical = Fixtures.Logical(item.Style, 101);
            using var reference = Fixtures.Expand(logical, item.Pitch);
            using var source = Fixtures.Distort(logical, item.Pitch, item.Kind, 77);
            using var snapshot = (Bitmap)source.Clone();
            using var result = aligner.Align(source, new() { CellSize = item.Pitch });
            double score = AlignmentQuality.Measure(result.Aligned, reference, item.Pitch).Score;
            Require(score == 100, $"Phase recovery {item}: {score:F6}, expected exact reconstruction");
            CheckSamples(source, snapshot, result.Aligned);
            using var twice = aligner.Align(result.Aligned, new() { CellSize = item.Pitch });
            Require(AlignmentQuality.Measure(twice.Aligned, result.Aligned, item.Pitch).Score == 100,
                $"Phase recovery is not idempotent: {item}");
        }

        // A single row/column can have no interior phase boundary at all.
        foreach (var size in new[] { new Size(5, 5), new Size(5, 17), new Size(17, 5) })
        {
            using var source = new Bitmap(size.Width, size.Height);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb((x * 17 + y * 31) % 256, x * 13 % 256, y * 19 % 256, 70));
            using var snapshot = (Bitmap)source.Clone();
            using var result = aligner.Align(source, new() { CellSize = 5 });
            CheckSamples(source, snapshot, result.Aligned);
            Require(AlignmentQuality.Measure(result.Aligned, result.Aligned, 5).GridPurity == 1,
                "Single-strip output is not a uniform grid");
        }
        CheckNoisyContour(aligner);
        Console.WriteLine($"PHASE RECOVERY: {cases.Length} exact reconstructions, 3 single-strip cases, 3 noisy contours");
    }

    private static void CheckNoisyContour(PixelGridAligner aligner)
    {
        const int pitch = 5;
        using var logical = new Bitmap(24, 20);
        using (var graphics = Graphics.FromImage(logical)) graphics.Clear(Color.FromArgb(0, 37, 73));
        for (int y = 2; y <= 17; y++)
        {
            int end = 5 + Math.Min(y - 2, 17 - y) * 2;
            for (int x = 4; x <= end; x++)
            {
                bool edge = x == 4 || x == end || y == 2 || y == 17;
                int shade = 80 + y * 3 + x;
                logical.SetPixel(x, y, edge ? Color.FromArgb(4, 6, 7) : Color.FromArgb(shade, shade, shade));
            }
        }
        using var expanded = Fixtures.Expand(logical, pitch);
        foreach (var (warp, noise) in new[] { (0.0, 6), (2.4, 3), (2.4, 6) })
        {
            var random = new Random(42);
            using var source = new Bitmap(expanded.Width, expanded.Height);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                {
                    double sx = x - warp * Math.Sin((double)y / source.Height * Math.PI * 3) * Math.Sin((double)x / source.Width * Math.PI);
                    double sy = y - warp * .8 * Math.Sin((double)x / source.Width * Math.PI * 2) * Math.Sin((double)y / source.Height * Math.PI);
                    int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                    double dx = sx - x0, dy = sy - y0;
                    var channels = new double[3];
                    for (int j = 0; j <= 1; j++)
                        for (int i = 0; i <= 1; i++)
                        {
                            Color c = expanded.GetPixel(Math.Clamp(x0 + i, 0, source.Width - 1), Math.Clamp(y0 + j, 0, source.Height - 1));
                            double weight = (i == 0 ? 1 - dx : dx) * (j == 0 ? 1 - dy : dy);
                            channels[0] += c.R * weight; channels[1] += c.G * weight; channels[2] += c.B * weight;
                        }
                    int[] rgb = channels.Select(c => Math.Clamp((int)Math.Round(c) + random.Next(-noise, noise + 1), 0, 255)).ToArray();
                    source.SetPixel(x, y, Color.FromArgb(rgb[0], rgb[1], rgb[2]));
                }
            using var snapshot = (Bitmap)source.Clone();
            using var result = aligner.Align(source, new() { CellSize = pitch });
            CheckSamples(source, snapshot, result.Aligned);
            for (int y = 0; y < logical.Height; y++)
                for (int x = 0; x < logical.Width; x++)
                {
                    Color expected = logical.GetPixel(x, y), actual = result.Aligned.GetPixel(x * pitch, y * pitch);
                    bool expectedDark = Math.Max(expected.R, Math.Max(expected.G, expected.B)) < 24;
                    bool actualDark = Math.Max(actual.R, Math.Max(actual.G, actual.B)) < 24;
                    Require(actualDark == expectedDark, $"Noisy contour moved at {x},{y}, warp {warp}, noise {noise}");
                }
        }
    }

    private static void CheckSamples(Bitmap source, Bitmap snapshot, Bitmap result)
    {
        Require(source.Size == result.Size, "Phase recovery changed the canvas");
        var colors = new HashSet<int>();
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Color before = snapshot.GetPixel(x, y);
                Require(source.GetPixel(x, y).ToArgb() == before.ToArgb(), "Phase recovery mutated the input");
                colors.Add(before.A == 0 ? 0 : before.ToArgb());
            }
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Color color = result.GetPixel(x, y);
                Require(colors.Contains(color.A == 0 ? 0 : color.ToArgb()), "Phase recovery invented a color or alpha");
            }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
