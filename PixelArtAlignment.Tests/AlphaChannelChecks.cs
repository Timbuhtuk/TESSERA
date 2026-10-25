using System.Drawing.Imaging;
using PixelArtDownscale;
using PixelArtWorkflows;

namespace PixelArtAlignment.Tests;

internal static class AlphaChannelChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Resize keeps all 256 alpha values in both sampling modes", ResizeAlphaRamp);
        check("Resize selects a complete source pixel instead of maximizing alpha", ResizeSelectsArgb);
        check("Crop keeps exact RGBA pixels at every alignment and through PNG", CropAlpha);
        check("Combined processing keeps partial alpha with quantization, palettes and dithering", CombinedAlpha);
        check("Sprite processing keeps all 256 alpha values", SpriteAlpha);
        check("Indexed and premultiplied images retain partial alpha", OtherPixelFormats);
        check("Transparent-edge trim includes alpha one, preserves PNG, and replays in recipes", TrimTransparentEdges);
    }

    private static Bitmap AlphaRamp(int scale)
    {
        var source = new Bitmap(256 * scale, 2 * scale, PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(x / scale, 210, 70, 30));
        return source;
    }

    private static void ResizeAlphaRamp()
    {
        using var source = AlphaRamp(2);
        foreach (bool sprite in new[] { false, true })
        foreach (int threshold in new[] { 1, 50, 100 })
        {
            using var result = IndependentImageProcessor.Scale(source, new DownscaleOptions
            {
                TargetWidth = 256, TargetHeight = 2, SpriteMode = sprite, AlphaThreshold = threshold
            });
            VerifyRamp(result);
        }
        using var enlarged = IndependentImageProcessor.Scale(source, new DownscaleOptions { TargetWidth = 1024, TargetHeight = 8 });
        for (int x = 0; x < enlarged.Width; x++)
            Require(enlarged.GetPixel(x, 0).A == x / 4, "Enlargement changed alpha");
    }

    private static void ResizeSelectsArgb()
    {
        using var source = new Bitmap(4, 2, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++) source.SetPixel(x, y, Color.FromArgb(x == 3 ? 255 : 64, 210, 70, 30));
        using var result = IndependentImageProcessor.Scale(source, new DownscaleOptions { TargetWidth = 1, TargetHeight = 1 });
        Require(result.GetPixel(0, 0).ToArgb() == source.GetPixel(0, 0).ToArgb(), "A rare opaque pixel replaced the prevalent partial alpha");
    }

    private static void CropAlpha()
    {
        using var source = AlphaRamp(1);
        foreach (var horizontal in Enum.GetValues<CropHorizontalAlignment>())
        foreach (var vertical in Enum.GetValues<CropVerticalAlignment>())
        {
            using var result = BitmapFactory.Crop(source, 129, 1, horizontal, vertical);
            int start = horizontal == CropHorizontalAlignment.Left ? 0 : horizontal == CropHorizontalAlignment.Right ? 127 : 63;
            for (int x = 0; x < result.Width; x++)
                Require(result.GetPixel(x, 0).ToArgb() == source.GetPixel(start + x, 0).ToArgb(), "Crop changed RGBA");
            using var stream = new MemoryStream();
            result.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            using var reopened = new Bitmap(stream);
            for (int x = 0; x < result.Width; x++)
                Require(reopened.GetPixel(x, 0).ToArgb() == result.GetPixel(x, 0).ToArgb(), "PNG changed RGBA");
        }
    }

    private static void CombinedAlpha()
    {
        using var source = AlphaRamp(2);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(x / 2, x / 2, 210, 30));
        foreach (var method in Enum.GetValues<QuantizationMethod>())
        foreach (var palette in new[] { PaletteKind.None, PaletteKind.Step, PaletteKind.GameBoy })
        {
            var result = new PixelArtDownscaler().Process(source, new DownscaleOptions
            {
                TargetWidth = 256, TargetHeight = 2, Quantization = method, QuantizationColors = 1,
                Palette = palette, EnableDithering = palette == PaletteKind.GameBoy
            });
            using var output = result.Downscaled;
            using var cropped = result.CroppedSource;
            VerifyRamp(output, colors: false);
            for (int x = 0; x < cropped.Width; x++)
                Require(cropped.GetPixel(x, 0).ToArgb() == source.GetPixel(x, 0).ToArgb(), "Combined crop changed RGBA");
        }
    }

    private static void SpriteAlpha()
    {
        using var source = AlphaRamp(2);
        foreach (int threshold in new[] { 1, 50, 100 })
        {
            var result = new PixelArtDownscaler().Process(source, new DownscaleOptions
            {
                TargetWidth = 256, TargetHeight = 2, SpriteMode = true, AlphaThreshold = threshold,
                Palette = PaletteKind.None, QuantizationColors = 1
            });
            using var output = result.Downscaled;
            using var cropped = result.CroppedSource;
            VerifyRamp(output);
        }
        Require(source.GetPixel(1, 0).A == 0 && source.GetPixel(2, 0).A == 1, "Processing mutated the source");
    }

    private static void VerifyRamp(Bitmap result, bool colors = true)
    {
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < 256; x++)
            {
                Color actual = result.GetPixel(x, y);
                Require(actual.A == x, $"Alpha {x} became {actual.A}");
                if (colors && x > 0) Require(actual.R == 210 && actual.G == 70 && actual.B == 30, "RGB was composited or changed");
            }
    }

    private static void OtherPixelFormats()
    {
        using var indexed = new Bitmap(4, 1, PixelFormat.Format8bppIndexed);
        var palette = indexed.Palette;
        byte[] alpha = [0, 1, 97, 255];
        for (int x = 0; x < 4; x++) palette.Entries[x] = Color.FromArgb(alpha[x], 210, 70, 30);
        indexed.Palette = palette;
        var bits = indexed.LockBits(new Rectangle(0, 0, 4, 1), ImageLockMode.WriteOnly, indexed.PixelFormat);
        try { System.Runtime.InteropServices.Marshal.Copy(new byte[] { 0, 1, 2, 3 }, 0, bits.Scan0, 4); }
        finally { indexed.UnlockBits(bits); }
        using var premultiplied = new Bitmap(4, 1, PixelFormat.Format32bppPArgb);
        for (int x = 0; x < 4; x++) premultiplied.SetPixel(x, 0, Color.FromArgb(alpha[x], 210, 70, 30));
        foreach (var source in new[] { indexed, premultiplied })
        {
            using var trimmed = BitmapFactory.CropTransparentEdges(source);
            Require(trimmed.Width == 3 && trimmed.Height == 1 && trimmed.GetPixel(0, 0).A == 1 &&
                trimmed.GetPixel(1, 0).A == 97 && trimmed.GetPixel(2, 0).A == 255,
                $"{source.PixelFormat}: trim lost indexed or premultiplied alpha");
            using var scaled = IndependentImageProcessor.Scale(source, new DownscaleOptions { TargetWidth = 4, TargetHeight = 1 });
            using var crop = BitmapFactory.CropCenter(source, 4, 1);
            var processed = new PixelArtDownscaler().Process(source, new DownscaleOptions
            {
                TargetWidth = 4, TargetHeight = 1, Palette = PaletteKind.None, QuantizationColors = 4
            });
            using var output = processed.Downscaled;
            using var cropped = processed.CroppedSource;
            for (int x = 0; x < 4; x++)
                Require(scaled.GetPixel(x, 0).A == alpha[x] && crop.GetPixel(x, 0).A == alpha[x] &&
                    output.GetPixel(x, 0).A == alpha[x], $"{source.PixelFormat}: alpha {alpha[x]} changed");
        }
    }

    private static void TrimTransparentEdges()
    {
        using var source = new Bitmap(7, 6, PixelFormat.Format32bppArgb);
        source.SetPixel(1, 4, Color.FromArgb(1, 12, 34, 56));
        source.SetPixel(5, 2, Color.FromArgb(255, 210, 70, 30));
        using var cropped = BitmapFactory.CropTransparentEdges(source);
        Require(cropped.Width == 5 && cropped.Height == 3, "Inclusive alpha bounds are wrong");
        Require(cropped.GetPixel(0, 2).ToArgb() == source.GetPixel(1, 4).ToArgb(), "Alpha-one edge changed");
        Require(cropped.GetPixel(4, 0).ToArgb() == source.GetPixel(5, 2).ToArgb(), "Opaque edge changed");
        Require(source.Width == 7 && source.GetPixel(1, 4).A == 1, "Trim mutated the source");
        using var stream = new MemoryStream();
        cropped.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        using var reopened = new Bitmap(stream);
        Require(reopened.GetPixel(0, 2).ToArgb() == cropped.GetPixel(0, 2).ToArgb(), "PNG round trip changed partial alpha");

        var recipe = new ProcessingRecipe { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Trim }] };
        using var replayed = new WorkflowExecutor().Execute(source, recipe);
        Require(replayed.Size == cropped.Size && replayed.GetPixel(0, 2).ToArgb() == cropped.GetPixel(0, 2).ToArgb(),
            "Trim recipe differs from the direct operation");
        using var blank = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using var empty = BitmapFactory.CropTransparentEdges(blank);
        Require(empty.Width == 1 && empty.Height == 1 && empty.GetPixel(0, 0).A == 0, "Empty image did not produce a transparent pixel");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
