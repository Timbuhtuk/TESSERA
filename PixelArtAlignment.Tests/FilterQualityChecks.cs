using PixelArtDownscale;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class FilterQualityChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Cleanup reduces seeded shade noise while preserving thin lines, palette and alpha", Cleanup);
        check("Aggressive cleanup does not enter a period-two stripe oscillation", CleanupStability);
        check("ASCII calibrated tonal direction, alpha invisibility and size contracts", Ascii);
    }

    private static void Cleanup()
    {
        using var source = new Bitmap(49, 37, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(100, 100, 100));
        var noise = new List<(int X, int Y)>();
        for (int y = 4; y < 33; y += 4)
            for (int x = 4; x < 29; x += 4)
            {
                source.SetPixel(x, y, Color.FromArgb(108, 108, 108));
                noise.Add((x, y));
            }
        for (int y = 5; y < 32; y++) source.SetPixel(38, y, Color.FromArgb(130, 130, 130));
        source.SetPixel(0, 0, Color.FromArgb(0, 70, 20, 10));
        source.SetPixel(1, 0, Color.FromArgb(127, 100, 100, 100));
        int[] original = Pixels(source);
        using var output = IndependentImageProcessor.ApplyNeighborColors(source, new() { LocalColorPasses = 3, Cleanup = new() });
        int remaining = noise.Count(p => output.GetPixel(p.X, p.Y).R != 100);
        ToolImprovementsChecks.Require(remaining < noise.Count / 2, $"Cleanup did not materially reduce seeded shade speckles: {remaining}/{noise.Count}");
        for (int y = 5; y < 32; y++) ToolImprovementsChecks.Require(output.GetPixel(38, y).R == 130, "Cleanup removed a protected one-pixel line or end cap");
        var palette = original.Where(c => (uint)c >> 24 > 0).Select(c => c & 0xFFFFFF).ToHashSet();
        int[] actual = Pixels(output);
        for (int q = 0; q < actual.Length; q++)
        {
            ToolImprovementsChecks.Require((uint)actual[q] >> 24 == (uint)original[q] >> 24, "Cleanup altered alpha");
            ToolImprovementsChecks.Require((uint)actual[q] >> 24 == 0 ? actual[q] == original[q] : palette.Contains(actual[q] & 0xFFFFFF),
                "Cleanup invented a color or changed hidden transparent RGB");
        }
        using var noStrength = IndependentImageProcessor.ApplyNeighborColors(source, new() { Cleanup = new() { Strength = 0 } });
        using var noTolerance = IndependentImageProcessor.ApplyNeighborColors(source, new() { Cleanup = new() { ColorTolerance = 0 } });
        ToolImprovementsChecks.Require(Pixels(noStrength).SequenceEqual(original) && Pixels(noTolerance).SequenceEqual(original), "Zero cleanup strength/tolerance is not an exact copy");
        ToolImprovementsChecks.Require(Pixels(source).SequenceEqual(original), "Cleanup mutated source");
        source.Save(Path.Combine(ArtifactRoot, "quality-cleanup-source.png"));
        output.Save(Path.Combine(ArtifactRoot, "quality-cleanup-output.png"));
        File.WriteAllText(Path.Combine(ArtifactRoot, "quality-cleanup-metrics.json"),
            System.Text.Json.JsonSerializer.Serialize(new { seededSpeckles = noise.Count, remainingSpeckles = remaining, protectedLinePixels = 27 }));
    }

    private static void CleanupStability()
    {
        using var stripes = new Bitmap(25, 19);
        for (int y = 0; y < stripes.Height; y++)
            for (int x = 0; x < stripes.Width; x++) stripes.SetPixel(x, y, Color.FromArgb(100 + x % 2 * 15, 100 + x % 2 * 15, 100 + x % 2 * 15));
        var cores = new Dictionary<int, int[]>();
        foreach (int passes in new[] { 1, 2, 3, 100 })
        {
            using var result = IndependentImageProcessor.ApplyNeighborColors(stripes, new()
            {
                LocalColorPasses = passes, Cleanup = new() { Strength = 1, DetailProtection = 0, EdgeProtection = 0 }
            });
            cores[passes] = Enumerable.Range(7, 5).SelectMany(y => Enumerable.Range(7, 11).Select(x => result.GetPixel(x, y).ToArgb())).ToArray();
            result.Save(Path.Combine(ArtifactRoot, $"cleanup-stripes-passes-{passes}.png"));
        }
        ToolImprovementsChecks.Require(!(cores[1].SequenceEqual(cores[3]) && !cores[1].SequenceEqual(cores[2])),
            "Aggressive cleanup enters a period-two cycle; odd/even pass count reverses the stripe colors");
    }

    private static void Ascii()
    {
        using var gradient = new Bitmap(513, 257);
        for (int y = 0; y < gradient.Height; y++)
            for (int x = 0; x < gradient.Width; x++)
            {
                int value = x * 255 / (gradient.Width - 1);
                gradient.SetPixel(x, y, Color.FromArgb(value, value, value));
            }
        int[] original = Pixels(gradient);
        using var rendered = ImageFilters.RenderAscii(gradient);
        using var inverted = ImageFilters.RenderAscii(gradient, invertColors: true);
        using var legacy = ImageFilters.RenderAscii(ImageFilters.ToAscii(gradient), gradient.Width, gradient.Height);
        ToolImprovementsChecks.Require(rendered.Size == gradient.Size, "ASCII keep-source-size changed dimensions");
        double Band(Bitmap image, int band)
        {
            double total = 0; int count = 0;
            int left = band * image.Width / 8, right = (band + 1) * image.Width / 8;
            for (int y = 0; y < image.Height; y++)
                for (int x = left; x < right; x++) { total += image.GetPixel(x, y).R; count++; }
            return total / count;
        }
        double[] means = Enumerable.Range(0, 8).Select(b => Band(rendered, b)).ToArray();
        double[] oldMeans = Enumerable.Range(0, 8).Select(b => Band(legacy, b)).ToArray();
        ToolImprovementsChecks.Require(means[7] > means[0] + 30 && Enumerable.Range(1, 7).Count(q => means[q] >= means[q - 1] - 2) >= 6,
            "ASCII render does not preserve source brightness order");
        for (int y = 0; y < rendered.Height; y++)
            for (int x = 0; x < rendered.Width; x++)
                ToolImprovementsChecks.Require(rendered.GetPixel(x, y).R + inverted.GetPixel(x, y).R == 255, "Invert render is not a pixelwise complement");
        ToolImprovementsChecks.Require(Pixels(gradient).SequenceEqual(original), "ASCII renderer mutated source");
        rendered.Save(Path.Combine(ArtifactRoot, "quality-ascii-gradient.png"));
        legacy.Save(Path.Combine(ArtifactRoot, "quality-ascii-gradient-legacy.png"));
        gradient.Save(Path.Combine(ArtifactRoot, "quality-ascii-gradient-source.png"));
        File.WriteAllText(Path.Combine(ArtifactRoot, "quality-ascii-tone-metrics.json"),
            System.Text.Json.JsonSerializer.Serialize(new { currentBandLuminance = means, previousBandLuminance = oldMeans }));

        using var hiddenA = new Bitmap(40, 36, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var hiddenB = new Bitmap(40, 36, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 40; x++)
            {
                bool visible = x < 20;
                hiddenA.SetPixel(x, y, visible ? Color.Coral : Color.FromArgb(0, 240, 15, 70));
                hiddenB.SetPixel(x, y, visible ? Color.Coral : Color.FromArgb(0, 10, 210, 230));
            }
        foreach (bool invertSource in new[] { false, true })
            foreach (bool invertColors in new[] { false, true })
            {
                using var first = ImageFilters.RenderAscii(hiddenA, characters: "@#", invertSource: invertSource, invertColors: invertColors);
                using var second = ImageFilters.RenderAscii(hiddenB, characters: "@#", invertSource: invertSource, invertColors: invertColors);
                ToolImprovementsChecks.Require(Pixels(first).SequenceEqual(Pixels(second)), "Hidden transparent RGB leaked into ASCII glyph choice");
            }
        foreach (int side in new[] { 1, 32 })
        {
            using var tiny = new Bitmap(side, side);
            using var result = ImageFilters.RenderAscii(tiny);
            ToolImprovementsChecks.Require(result.Size == tiny.Size, "Small ASCII input changed keep-size dimensions");
        }
        using var sample = new Bitmap(Path.Combine(FindRepository(), "DomainColorTest", "Assets", "landscape.png"));
        using var ascii = ImageFilters.RenderAscii(sample);
        using var baseline = ImageFilters.RenderAscii(ImageFilters.ToAscii(sample), sample.Width, sample.Height);
        ascii.Save(Path.Combine(ArtifactRoot, "quality-ascii-landscape.png"));
        baseline.Save(Path.Combine(ArtifactRoot, "quality-ascii-landscape-legacy.png"));
        using var geometry = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(geometry))
        {
            graphics.Clear(Color.Black);
            graphics.FillEllipse(System.Drawing.Brushes.White, 90, 50, 140, 140);
            graphics.DrawLine(System.Drawing.Pens.White, 10, 220, 310, 20);
        }
        using var glyphGeometry = ImageFilters.RenderAscii(geometry, characters: ImageFilters.AsciiRampPresets[1].Characters);
        double RegionMean(int cx, int cy)
        {
            double sum = 0;
            for (int y = cy - 16; y < cy + 16; y++)
                for (int x = cx - 16; x < cx + 16; x++) sum += glyphGeometry.GetPixel(x, y).R;
            return sum / 1024;
        }
        ToolImprovementsChecks.Require(RegionMean(160, 120) > RegionMean(40, 120) + 20 && RegionMean(160, 120) > RegionMean(280, 120) + 20,
            "ASCII failed to retain a broad bright circular silhouette");
        geometry.Save(Path.Combine(ArtifactRoot, "quality-ascii-geometry-source.png"));
        glyphGeometry.Save(Path.Combine(ArtifactRoot, "quality-ascii-geometry.png"));
        string photoPath = @"C:\Windows\Web\Screen\img102.jpg";
        if (File.Exists(photoPath))
        {
            using var photo = new Bitmap(photoPath);
            using var resized = new Bitmap(768, Math.Max(1, photo.Height * 768 / photo.Width));
            using (var graphics = Graphics.FromImage(resized)) graphics.DrawImage(photo, 0, 0, resized.Width, resized.Height);
            using var photoAscii = ImageFilters.RenderAscii(resized);
            using var photoLegacy = ImageFilters.RenderAscii(ImageFilters.ToAscii(resized), resized.Width, resized.Height);
            resized.Save(Path.Combine(ArtifactRoot, "quality-ascii-photo-source.png"));
            photoAscii.Save(Path.Combine(ArtifactRoot, "quality-ascii-photo.png"));
            photoLegacy.Save(Path.Combine(ArtifactRoot, "quality-ascii-photo-legacy.png"));
            Comparison("quality-ascii-scenic-comparison.png", resized, photoLegacy, photoAscii);
        }
        using var geometryLegacy = ImageFilters.RenderAscii(ImageFilters.ToAscii(geometry), geometry.Width, geometry.Height);
        Comparison("quality-ascii-geometry-comparison.png", geometry, geometryLegacy, glyphGeometry);
        Comparison("quality-ascii-landscape-comparison.png", sample, baseline, ascii);
    }

    private static void Comparison(string name, Bitmap source, Bitmap previous, Bitmap current)
    {
        int width = Math.Min(768, Math.Max(320, source.Width));
        int height = source.Height * width / source.Width;
        using var comparison = new Bitmap(width * 3, height + 28);
        using var graphics = Graphics.FromImage(comparison);
        using var font = new Font(FontFamily.GenericSansSerif, 12, GraphicsUnit.Pixel);
        graphics.Clear(Color.FromArgb(24, 24, 24));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        Bitmap[] images = [source, previous, current];
        string[] labels = ["Source", "Previous renderer", "Current renderer"];
        for (int q = 0; q < 3; q++)
        {
            graphics.DrawString(labels[q], font, System.Drawing.Brushes.White, q * width + 8, 6);
            graphics.DrawImage(images[q], new Rectangle(q * width, 28, width, height));
        }
        comparison.Save(Path.Combine(ArtifactRoot, name));
    }
}
