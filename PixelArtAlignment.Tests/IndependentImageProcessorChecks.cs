using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class IndependentImageProcessorChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Size-only operation keeps source colors despite palette settings", ScaleKeepsColors);
        check("Color-only operation keeps dimensions and alpha", ColorsKeepCanvasAndAlpha);
    }

    private static void ScaleKeepsColors()
    {
        using var source = new Bitmap(4, 2, PixelFormat.Format32bppArgb);
        Color[] colors = [Color.Red, Color.Green, Color.Blue, Color.Yellow];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, colors[x]);
        var options = new DownscaleOptions
        {
            TargetWidth = 2, TargetHeight = 1, Palette = PaletteKind.GameBoy,
            QuantizationColors = 1, Quantization = QuantizationMethod.MedianCut
        };
        using var result = IndependentImageProcessor.Scale(source, options);
        if (result.Width != 2 || result.Height != 1) throw new Exception("Scaling dimensions are wrong.");
        var original = colors.Select(color => color.ToArgb()).ToHashSet();
        for (int x = 0; x < result.Width; x++)
            if (!original.Contains(result.GetPixel(x, 0).ToArgb()))
                throw new Exception("Scaling introduced a color absent from the source.");
    }

    private static void ColorsKeepCanvasAndAlpha()
    {
        using var source = new Bitmap(3, 2, PixelFormat.Format32bppArgb);
        Color[] colors =
        [
            Color.FromArgb(255, 231, 47, 18), Color.FromArgb(128, 20, 172, 222), Color.Transparent,
            Color.FromArgb(255, 245, 233, 50), Color.FromArgb(64, 44, 118, 72), Color.FromArgb(255, 168, 30, 158)
        ];
        for (int q = 0; q < colors.Length; q++) source.SetPixel(q % 3, q / 3, colors[q]);
        var options = new DownscaleOptions
        {
            TargetWidth = 1, TargetHeight = 1, Palette = PaletteKind.GameBoy,
            QuantizationColors = 2, Quantization = QuantizationMethod.MedianCut
        };
        using var result = IndependentImageProcessor.ApplyColors(source, options);
        if (result.Width != source.Width || result.Height != source.Height)
            throw new Exception("Color processing changed canvas dimensions.");
        var palette = Palettes.GetPalette(PaletteKind.GameBoy).ToHashSet();
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Color before = source.GetPixel(x, y), after = result.GetPixel(x, y);
                if (before.A != after.A) throw new Exception("Color processing changed alpha.");
                if (after.A > 0 && !palette.Contains(after.ToArgb() & 0xFFFFFF))
                    throw new Exception("Color processing produced a color outside the selected palette.");
            }
    }
}
