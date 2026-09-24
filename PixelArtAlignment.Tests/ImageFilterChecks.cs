using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class ImageFilterChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Monochrome preserves dimensions, alpha and source", Monochrome);
        check("ASCII maps darkness, light and transparency to characters", AsciiText);
        check("ASCII render creates a black and white image", AsciiRender);
        check("ASCII validates positive width, characters and font size", Validation);
        check("ASCII ramp presets contain the requested palettes", RampPresets);
    }

    private static void Monochrome()
    {
        using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(255, 210, 30, 70));
        source.SetPixel(1, 0, Color.FromArgb(90, 20, 180, 60));
        source.SetPixel(2, 0, Color.Transparent);
        using var result = ImageFilters.Monochrome(source);
        if (result.Size != source.Size) throw new Exception("Monochrome changed dimensions.");
        for (int x = 0; x < result.Width; x++)
        {
            Color before = source.GetPixel(x, 0), after = result.GetPixel(x, 0);
            if (before.A != after.A) throw new Exception("Monochrome changed alpha.");
            if (after.A > 0 && (after.R != after.G || after.G != after.B))
                throw new Exception("Monochrome left a colored pixel.");
        }
        if (source.GetPixel(0, 0).ToArgb() != Color.FromArgb(255, 210, 30, 70).ToArgb())
            throw new Exception("Monochrome changed the source.");
    }

    private static void AsciiText()
    {
        using var source = new Bitmap(3, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.Black);
        source.SetPixel(1, 0, Color.White);
        source.SetPixel(2, 0, Color.Transparent);
        string text = ImageFilters.ToAscii(source, 3, "@. ");
        if (text != "@  ") throw new Exception($"Unexpected ASCII mapping: {text}");
        string inverted = ImageFilters.ToAscii(source, 3, "@. ", invertSource: true);
        if (inverted != " @ ") throw new Exception($"Unexpected inverted ASCII mapping: {inverted}");

        using var mixed = new Bitmap(2, 1, PixelFormat.Format32bppArgb);
        mixed.SetPixel(0, 0, Color.White);
        mixed.SetPixel(1, 0, Color.Transparent);
        if (ImageFilters.ToAscii(mixed, 1, "@. ") != " ")
            throw new Exception("A transparent pixel darkened a visible pixel in the same ASCII cell.");

        using var square = new Bitmap(8, 8);
        string[] lines = ImageFilters.ToAscii(square, 8).Split(Environment.NewLine);
        if (lines.Length != 4 || lines.Any(line => line.Length != 8))
            throw new Exception("ASCII aspect ratio is incorrect.");

        using var perPixelSource = new Bitmap(5, 3);
        string[] perPixel = ImageFilters.ToAsciiPerPixel(perPixelSource, ".@").Split(Environment.NewLine);
        if (perPixel.Length != 3 || perPixel.Any(line => line.Length != 5))
            throw new Exception("One-pixel-per-symbol mode changed the source grid.");
    }

    private static void AsciiRender()
    {
        using var result = ImageFilters.RenderAscii(".@\n@.", 14);
        if (result.Width <= 0 || result.Height <= 0 || result.PixelFormat != PixelFormat.Format24bppRgb)
            throw new Exception("ASCII render returned an invalid bitmap.");
        bool hasBlack = false, hasWhite = false;
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Color color = result.GetPixel(x, y);
                hasBlack |= color.R == 0 && color.G == 0 && color.B == 0;
                hasWhite |= color.R == 255 && color.G == 255 && color.B == 255;
            }
        if (!hasBlack || !hasWhite) throw new Exception("ASCII render lacks its black background or white symbols.");
        using var invertedRender = ImageFilters.RenderAscii("@", 14, invertColors: true);
        bool hasBlackSymbol = false, hasWhiteBackground = false;
        for (int y = 0; y < invertedRender.Height; y++)
            for (int x = 0; x < invertedRender.Width; x++)
            {
                Color color = invertedRender.GetPixel(x, y);
                hasBlackSymbol |= color.R == 0 && color.G == 0 && color.B == 0;
                hasWhiteBackground |= color.R == 255 && color.G == 255 && color.B == 255;
            }
        if (!hasBlackSymbol || !hasWhiteBackground)
            throw new Exception("Inverted ASCII render lacks its white background or black symbols.");

        using var source = new Bitmap(137, 91);
        using var graphics = Graphics.FromImage(source);
        graphics.Clear(Color.White);
        string automatic = ImageFilters.ToAscii(source, ".@");
        using var sourceSized = ImageFilters.RenderAscii(automatic, source.Width, source.Height);
        if (sourceSized.Size != source.Size)
            throw new Exception("Automatic ASCII render did not use the source resolution.");
        string perPixel = ImageFilters.ToAsciiPerPixel(source, ".@");
        using var expanded = ImageFilters.RenderAscii(perPixel, ImageFilters.DefaultAsciiFontSize);
        if (expanded.Width <= source.Width || expanded.Height <= source.Height)
            throw new Exception("One-pixel-per-symbol render did not increase the resolution.");
    }

    private static void Validation()
    {
        using var source = new Bitmap(1, 1);
        Throws<ArgumentOutOfRangeException>(() => ImageFilters.ToAscii(source, 0));
        Throws<ArgumentException>(() => ImageFilters.ToAscii(source, 1, ""));
        Throws<ArgumentException>(() => ImageFilters.ToAscii(source, 1, ".\n@"));
        Throws<ArgumentOutOfRangeException>(() => ImageFilters.RenderAscii("@", 0));
        string wide = ImageFilters.ToAscii(source, 513, ".@");
        if (wide.Split(Environment.NewLine).Any(line => line.Length != 513))
            throw new Exception("ASCII retained the old 512-column limit.");
        using var largeFont = ImageFilters.RenderAscii("@", 129);
        if (largeFont.Width <= 0 || largeFont.Height <= 0)
            throw new Exception("ASCII retained the old 128-pixel font limit.");
    }

    private static void RampPresets()
    {
        var presets = ImageFilters.AsciiRampPresets;
        if (presets.Select(preset => preset.Name).SequenceEqual(["Standard", "Detailed", "Minimal", "Blocks"]) == false)
            throw new Exception("ASCII ramp preset names or order changed.");
        if (presets[0].Characters != "@#S08Xx+=-;:, ." || presets[2].Characters != ". : -" ||
            presets[3].Characters != "█ ▓ ▒ ░" || !presets[1].Characters.Contains("WM#*oahk") ||
            presets.Any(preset => string.IsNullOrEmpty(preset.Characters) || preset.Characters.Contains('\n')))
            throw new Exception("ASCII ramp preset characters are incomplete.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
