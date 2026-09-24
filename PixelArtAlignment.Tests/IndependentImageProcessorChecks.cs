using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class IndependentImageProcessorChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Size-only operation keeps source colors despite palette settings", ScaleKeepsColors);
        check("Size-only enlargement repeats exact source pixels and alpha", UpscaleRepeatsPixels);
        check("Color-only operation keeps dimensions and alpha", ColorsKeepCanvasAndAlpha);
        check("Color reduction modes apply quantization or palette exclusively", ColorModesAreExclusive);
        check("Neighbor colors merge local shades without palette or scaling", NeighborColorsKeepSourcePaletteAndAlpha);
        check("Neighbor color passes extend a local change", NeighborColorPasses);
        check("Neighbor color brightness setting changes transition direction", NeighborColorBrightness);
        check("Neighbor colors use their own criteria independently of size settings", NeighborColorsUseOwnCriteria);
        check("Neighbor colors do not exchange equal-support pixels", NeighborColorsDoNotSwap);
        check("Neighbor color passes reject values outside range", NeighborColorPassRange);
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

    private static void UpscaleRepeatsPixels()
    {
        using var source = new Bitmap(3, 2, PixelFormat.Format32bppArgb);
        Color[] colors =
        [
            Color.FromArgb(255, 240, 30, 20), Color.FromArgb(128, 20, 160, 230), Color.Transparent,
            Color.FromArgb(255, 12, 42, 86), Color.FromArgb(64, 210, 118, 40), Color.FromArgb(255, 72, 33, 210)
        ];
        for (int q = 0; q < colors.Length; q++) source.SetPixel(q % 3, q / 3, colors[q]);

        foreach (var size in new[] { new Size(6, 4), new Size(7, 5), new Size(6, 1), new Size(4097, 1), new Size(1, 2161) })
        {
            using var result = IndependentImageProcessor.Scale(source, new DownscaleOptions
            {
                TargetWidth = size.Width, TargetHeight = size.Height, SpriteMode = true,
                Palette = PaletteKind.GameBoy, QuantizationColors = 1
            });
            if (result.Size != size) throw new Exception("Enlargement returned the wrong dimensions.");
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    int sourceX = (int)((long)x * source.Width / size.Width);
                    int sourceY = (int)((long)y * source.Height / size.Height);
                    if (result.GetPixel(x, y).ToArgb() != source.GetPixel(sourceX, sourceY).ToArgb())
                        throw new Exception("Enlargement blended colors or changed alpha.");
                }
        }
        for (int q = 0; q < colors.Length; q++)
            if (source.GetPixel(q % 3, q / 3).ToArgb() != colors[q].ToArgb())
                throw new Exception("Enlargement changed the source.");
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

    private static void ColorModesAreExclusive()
    {
        using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(96, 20, 20, 20));
        source.SetPixel(1, 0, Color.FromArgb(160, 100, 100, 100));
        source.SetPixel(2, 0, Color.FromArgb(255, 220, 220, 220));
        using var quantized = IndependentImageProcessor.ApplyColors(source, new DownscaleOptions
        {
            IndependentColorMode = IndependentColorMode.Quantization,
            Quantization = QuantizationMethod.MedianCut, QuantizationColors = 1,
            Palette = PaletteKind.Step, PaletteStep = 64
        });
        using var mapped = IndependentImageProcessor.ApplyColors(source, new DownscaleOptions
        {
            IndependentColorMode = IndependentColorMode.Palette,
            QuantizationColors = 0, Palette = PaletteKind.Step, PaletteStep = 64
        });
        if (Enumerable.Range(0, 3).Select(x => quantized.GetPixel(x, 0).R).Distinct().Count() != 1 ||
            Enumerable.Range(0, 3).Select(x => mapped.GetPixel(x, 0).R).Distinct().Count() != 3)
            throw new Exception("Inactive color controls affected the selected operation.");
        for (int x = 0; x < 3; x++)
            if (quantized.GetPixel(x, 0).A != source.GetPixel(x, 0).A ||
                mapped.GetPixel(x, 0).A != source.GetPixel(x, 0).A)
                throw new Exception("Color mode changed alpha.");
    }

    private static void NeighborColorsKeepSourcePaletteAndAlpha()
    {
        using var source = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        Color baseColor = Color.FromArgb(255, 100, 100, 100);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) source.SetPixel(x, y, baseColor);
        source.SetPixel(1, 1, Color.FromArgb(128, 110, 110, 110));
        source.SetPixel(0, 0, Color.Transparent);

        using var result = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            LocalColorPasses = 1, Palette = PaletteKind.GameBoy, QuantizationColors = 1
        });
        if (result.Size != source.Size) throw new Exception("Neighbor processing changed canvas dimensions.");
        if (result.GetPixel(1, 1).ToArgb() != Color.FromArgb(128, 100, 100, 100).ToArgb())
            throw new Exception("The isolated shade did not join its neighbors or alpha changed.");
        if (result.GetPixel(0, 0).A != 0) throw new Exception("A transparent pixel became visible.");
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                if (result.GetPixel(x, y).A != source.GetPixel(x, y).A)
                    throw new Exception("Neighbor processing changed alpha.");
    }

    private static void NeighborColorPasses()
    {
        using var source = new Bitmap(5, 5, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                int shade = x is >= 1 and <= 3 && y is >= 1 and <= 3 ? 105 : 100;
                source.SetPixel(x, y, Color.FromArgb(shade, shade, shade));
            }
        source.SetPixel(2, 2, Color.FromArgb(110, 110, 110));
        using var once = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions { LocalColorPasses = 1 });
        using var three = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions { LocalColorPasses = 3 });
        if (once.GetPixel(2, 2).R != 105 || three.GetPixel(2, 2).R != 100)
            throw new Exception($"Three passes should carry the surrounding color to the center: {once.GetPixel(2, 2).R}, {three.GetPixel(2, 2).R}.");
    }

    private static void NeighborColorBrightness()
    {
        using var source = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) source.SetPixel(x, y, Color.FromArgb(180, 180, 180));
        source.SetPixel(1, 1, Color.FromArgb(200, 200, 200));

        using var automatic = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions());
        using var bright = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            LocalColorCriteria = new ManualBlockCriteria { TargetBrightness = 1, BrightnessImportance = 1 }
        });
        if (automatic.GetPixel(1, 1).R != 180 || bright.GetPixel(1, 1).R != 200)
            throw new Exception("High brightness preference did not resist a darker transition.");
    }

    private static void NeighborColorsUseOwnCriteria()
    {
        using var source = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) source.SetPixel(x, y, Color.FromArgb(180, 180, 180));
        source.SetPixel(1, 1, Color.FromArgb(200, 200, 200));

        using var sizeOnly = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            BlockMode = BlockSelectionMode.Manual,
            ManualCriteria = new ManualBlockCriteria { TargetBrightness = 1 }
        });
        using var localBright = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            LocalColorCriteria = new ManualBlockCriteria { TargetBrightness = 1 }
        });
        using var localDark = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
        {
            BlockMode = BlockSelectionMode.Manual,
            ManualCriteria = new ManualBlockCriteria { TargetBrightness = 1 },
            LocalColorCriteria = new ManualBlockCriteria { TargetBrightness = 0 }
        });
        if (sizeOnly.GetPixel(1, 1).R != 180 ||
            localBright.GetPixel(1, 1).R != 200 || localDark.GetPixel(1, 1).R != 180)
            throw new Exception("Local 3×3 criteria were ignored or inherited size-tool preferences.");
    }

    private static void NeighborColorPassRange()
    {
        using var source = new Bitmap(1, 1);
        foreach (int passes in new[] { 0, DownscaleOptions.MaxLocalColorPasses + 1 })
        {
            try
            {
                using var result = IndependentImageProcessor.ApplyNeighborColors(source,
                    new DownscaleOptions { LocalColorPasses = passes });
                throw new Exception("Invalid pass count was accepted.");
            }
            catch (ArgumentOutOfRangeException) { }
        }
    }

    private static void NeighborColorsDoNotSwap()
    {
        using var source = new Bitmap(2, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(100, 100, 100));
        source.SetPixel(1, 0, Color.FromArgb(101, 101, 101));
        using var result = IndependentImageProcessor.ApplyNeighborColors(source,
            new DownscaleOptions { LocalColorPasses = 10 });
        if (result.GetPixel(0, 0).ToArgb() != source.GetPixel(0, 0).ToArgb() ||
            result.GetPixel(1, 0).ToArgb() != source.GetPixel(1, 0).ToArgb())
            throw new Exception("Equally supported neighboring colors exchanged places.");
    }
}
