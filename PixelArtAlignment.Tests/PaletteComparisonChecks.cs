using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DomainColorTest;
using PixelArtDownscale;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;

namespace PixelArtAlignment.Tests;

internal static class PaletteComparisonChecks
{
    public static void Run()
    {
        var assembly = typeof(MainWindow).Assembly;
        Type renderer = assembly.GetType("DomainColorTest.PalettePreviewTooltips", true)!;
        Type settingsType = assembly.GetType("DomainColorTest.ColorPreviewSettings", true)!;
        MethodInfo create = renderer.GetMethod("CreateSample", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo generate = renderer.GetMethod("Generate", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var sample = (Bitmap)Invoke(create, null)!;
        sample.Save(Path.Combine(ArtifactRoot, "palette-hover-original.png"));
        var outputs = new List<byte[]>();
        foreach (PaletteKind palette in new[] { PaletteKind.DB16, PaletteKind.DB32, PaletteKind.NES, PaletteKind.GameBoy, PaletteKind.Step })
            Verify(palette, null, 7, 64, false);
        foreach (QuantizationMethod method in Enum.GetValues<QuantizationMethod>())
            Verify(PaletteKind.None, method, 16, 16, true);
        ToolImprovementsChecks.Require(outputs.Skip(1).All(p => !p.SequenceEqual(outputs[0])), "Every tooltip reused the same example");

        void Verify(PaletteKind palette, QuantizationMethod? quantization, int step, int count, bool weighted)
        {
            object settings = settingsType.GetConstructors().Single().Invoke(new object?[] { palette, step, false, quantization, count, weighted });
            var actual = (BitmapSource)Invoke(generate, new[] { settings })!;
            ToolImprovementsChecks.Require(actual.PixelWidth > actual.PixelHeight && actual.PixelWidth >= 300 && actual.IsFrozen,
                "Option tooltip is not a usable frozen rectangular comparison");
            var bgra = new FormatConvertedBitmap(actual, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[actual.PixelWidth * actual.PixelHeight * 4];
            bgra.CopyPixels(pixels, actual.PixelWidth * 4, 0);
            using var processed = IndependentImageProcessor.ApplyColors(sample, new()
            {
                Palette = palette, PaletteStep = step, IndependentColorMode = quantization.HasValue ? IndependentColorMode.Quantization : IndependentColorMode.Palette,
                Quantization = quantization ?? QuantizationMethod.KMeansLab, QuantizationColors = count, UseColorWeights = weighted, ThreadCount = 1
            });
            int changedOnRight = 0, originalOnLeft = 0;
            for (int y = 4; y < actual.PixelHeight - 4; y++)
                for (int x = 4; x < actual.PixelWidth - 4; x++)
                {
                    int sx = x * sample.Width / actual.PixelWidth, sy = y * sample.Height / actual.PixelHeight;
                    int q = (y * actual.PixelWidth + x) * 4;
                    int rgb = pixels[q + 2] << 16 | pixels[q + 1] << 8 | pixels[q];
                    int before = sample.GetPixel(sx, sy).ToArgb() & 0xFFFFFF;
                    int after = processed.GetPixel(sx, sy).ToArgb() & 0xFFFFFF;
                    if (x < actual.PixelWidth / 4)
                    {
                        ToolImprovementsChecks.Require(rgb == before && pixels[q + 3] == 255, "Tooltip original half is altered or transparent");
                        originalOnLeft++;
                    }
                    if (x > actual.PixelWidth * 3 / 4)
                    {
                        ToolImprovementsChecks.Require(rgb == after && pixels[q + 3] == 255, $"Tooltip processed half differs from {palette}/{quantization} processing");
                        if (before != after) changedOnRight++;
                    }
                }
            ToolImprovementsChecks.Require(originalOnLeft > 1000 && changedOnRight > 1000, "Tooltip lacks substantial original/processed samples");
            int DividerAt(int y)
            {
                var whites = new List<int>();
                for (int x = actual.PixelWidth / 4; x < actual.PixelWidth * 3 / 4; x++)
                {
                    int q = (y * actual.PixelWidth + x) * 4;
                    if (pixels[q] == 255 && pixels[q + 1] == 255 && pixels[q + 2] == 255) whites.Add(x);
                }
                ToolImprovementsChecks.Require(whites.Count > 0, "Comparison diagonal separator is missing");
                return whites[whites.Count / 2];
            }
            ToolImprovementsChecks.Require(Math.Abs(DividerAt(5) - DividerAt(actual.PixelHeight - 6)) > actual.PixelWidth / 5,
                "Original and processed halves do not have a diagonal split");
            outputs.Add(pixels);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(actual));
            using var output = File.Create(Path.Combine(ArtifactRoot, $"hover-{palette}-{quantization?.ToString() ?? "palette"}.png"));
            encoder.Save(output);
        }
    }

    private static object? Invoke(MethodInfo method, object?[]? args)
    {
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException exception) { throw new Exception(exception.InnerException?.ToString() ?? exception.ToString()); }
    }
}
