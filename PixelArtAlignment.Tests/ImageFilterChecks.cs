using System.Drawing.Imaging;
using PixelArtDownscale;
using PixelArtWorkflows;

namespace PixelArtAlignment.Tests;

internal static class ImageFilterChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Monochrome preserves dimensions, alpha and source", Monochrome);
        check("ASCII maps darkness, light and transparency to characters", AsciiText);
        check("ASCII render creates a black and white image", AsciiRender);
        check("ASCII colors, background and explicit columns", AsciiColors);
        check("ASCII uses rectangular glyph cells for text and image output", AsciiAspect);
        check("ASCII retains vertical and horizontal glyph features", AsciiGlyphShape);
        check("ASCII ordered dithering varies midtones deterministically", AsciiDithering);
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
        using var cell = ImageFilters.RenderAscii("@", ImageFilters.DefaultAsciiFontSize);
        int expectedRows = Math.Max(1, (int)Math.Round(8d * cell.Width / cell.Height));
        if (lines.Length != expectedRows || lines.Any(line => line.Length != 8))
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

    private static void AsciiAspect()
    {
        using var source = new Bitmap(240, 120);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        const int columns = 80;
        string[] lines = ImageFilters.ToAsciiAtColumns(source, columns, "@ ").Split(Environment.NewLine);
        using var expanded = ImageFilters.RenderAscii(source, keepSourceSize: false, characters: "@ ", columns: columns);
        if (lines.Length < 1 || lines.Any(line => line.Length != columns) ||
            expanded.Width % columns != 0 || expanded.Height % lines.Length != 0)
            throw new Exception("Text and image outputs disagree on the character grid.");
        double imageAspect = expanded.Width / (double)expanded.Height;
        double sourceAspect = source.Width / (double)source.Height;
        if (Math.Abs(imageAspect / sourceAspect - 1) > 0.04)
            throw new Exception($"Rectangular character cells distorted the image: {imageAspect:F3} vs {sourceAspect:F3}.");
        int cellWidth = expanded.Width / columns, cellHeight = expanded.Height / lines.Length;
        if (cellHeight <= cellWidth)
            throw new Exception("The monospace font was treated as square or wider than tall.");
        using var automatic = ImageFilters.RenderAscii(source, keepSourceSize: false, characters: "@ ");
        if (Math.Abs(automatic.Width / (double)automatic.Height / sourceAspect - 1) > 0.04)
            throw new Exception("Automatic expanded ASCII did not compensate for rectangular cells.");
    }

    private static void AsciiGlyphShape()
    {
        using var vertical = ImageFilters.RenderAscii("|", ImageFilters.DefaultAsciiFontSize);
        using var horizontal = ImageFilters.RenderAscii("-", ImageFilters.DefaultAsciiFontSize);
        using var verticalResult = ImageFilters.RenderAscii(vertical, characters: "|-", columns: 1);
        using var horizontalResult = ImageFilters.RenderAscii(horizontal, characters: "|-", columns: 1);
        if (verticalResult.Size != vertical.Size || horizontalResult.Size != horizontal.Size)
            throw new Exception("One-cell ASCII changed source dimensions.");
        if (Error(verticalResult, vertical) >= Error(verticalResult, horizontal) ||
            Error(horizontalResult, horizontal) >= Error(horizontalResult, vertical))
            throw new Exception("Glyph matching confused vertical and horizontal strokes.");

        static long Error(Bitmap actual, Bitmap reference)
        {
            long error = 0;
            for (int y = 0; y < actual.Height; y++)
                for (int x = 0; x < actual.Width; x++)
                    error += Math.Abs(actual.GetPixel(x, y).R - reference.GetPixel(x, y).R);
            return error;
        }
    }

    private static void AsciiDithering()
    {
        using var source = new Bitmap(160, 96);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(128, 128, 128));
        string plain = ImageFilters.ToAscii(source, 20, "@ ");
        string dithered = ImageFilters.ToAscii(source, 20, "@ ", dither: true);
        if (plain == dithered || dithered != ImageFilters.ToAscii(source, 20, "@ ", dither: true) ||
            !dithered.Contains('@') || !dithered.Contains(' '))
            throw new Exception("Text dithering did not distribute neighboring tones deterministically.");
        using var plainImage = ImageFilters.RenderAscii(source, characters: "@ ", columns: 20);
        using var ditheredImage = ImageFilters.RenderAscii(source, characters: "@ ", columns: 20, dither: true);
        bool different = false;
        for (int y = 0; y < source.Height && !different; y++)
            for (int x = 0; x < source.Width; x++)
                if (plainImage.GetPixel(x, y).ToArgb() != ditheredImage.GetPixel(x, y).ToArgb())
                { different = true; break; }
        if (!different) throw new Exception("Image dithering did not change a midtone rendering.");
        var recipe = WorkflowJson.Snapshot(new ProcessingRecipe { Steps = [new ProcessingStep
        {
            OperationId = ProcessingOperations.Ascii,
            Ascii = new AsciiSettings { Characters = "@ ", Columns = 20, Dither = true }
        }] });
        using var replayed = new WorkflowExecutor().Execute(source, recipe);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (ditheredImage.GetPixel(x, y).ToArgb() != replayed.GetPixel(x, y).ToArgb())
                    throw new Exception("Dither setting did not survive recipe replay.");
    }

    private static void AsciiColors()
    {
        using var source = new Bitmap(48, 24, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.Lime, 24, 0, 24, 24);
        }
        using var originalColors = ImageFilters.RenderAscii(source, characters: "@", colorMode: AsciiColorMode.Source, columns: 2);
        if (originalColors.Size != source.Size ||
            !HasColor(originalColors, color => color.R > 200 && color.G < 50 && color.B < 50) ||
            !HasColor(originalColors, color => color.G > 200 && color.R < 50 && color.B < 50))
            throw new Exception("Source colored glyphs or source dimensions are wrong.");

        using var glyphMask = ImageFilters.RenderAscii("|", ImageFilters.DefaultAsciiFontSize);
        using var coloredStroke = new Bitmap(glyphMask.Width, glyphMask.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < coloredStroke.Height; y++)
            for (int x = 0; x < coloredStroke.Width; x++)
                coloredStroke.SetPixel(x, y, glyphMask.GetPixel(x, y).R > 127 ? Color.Red : Color.Blue);
        using var isolatedStroke = ImageFilters.RenderAscii(coloredStroke, characters: "|",
            colorMode: AsciiColorMode.Source, columns: 1);
        if (!HasColor(isolatedStroke, color => color.R > 200 && color.B < 50))
            throw new Exception("Source color extraction blended a glyph stroke with its neighboring background.");

        using var custom = ImageFilters.RenderAscii(source, characters: "@", colorMode: AsciiColorMode.Custom,
            foregroundArgb: Color.Yellow.ToArgb(), backgroundArgb: Color.Navy.ToArgb(), columns: 2);
        if (!HasColor(custom, color => color.R > 200 && color.G > 200 && color.B < 50) ||
            !HasColor(custom, color => color.B > 100 && color.R < 50 && color.G < 50))
            throw new Exception("Custom ASCII foreground or background was not rendered.");
        var recipe = WorkflowJson.Snapshot(new ProcessingRecipe { Steps = [new ProcessingStep
        {
            OperationId = ProcessingOperations.Ascii,
            Ascii = new AsciiSettings { Characters = "@", ColorMode = AsciiColorMode.Custom,
                ForegroundArgb = Color.Yellow.ToArgb(), BackgroundArgb = Color.Navy.ToArgb(), Columns = 2 }
        }] });
        using var replayed = new WorkflowExecutor().Execute(source, recipe);
        for (int y = 0; y < custom.Height; y++)
            for (int x = 0; x < custom.Width; x++)
                if (custom.GetPixel(x, y).ToArgb() != replayed.GetPixel(x, y).ToArgb())
                    throw new Exception("Saved ASCII color settings did not replay exactly.");
        string[] text = ImageFilters.ToAsciiAtColumns(source, 2, "@ ").Split(Environment.NewLine);
        if (text.Length == 0 || text.Any(line => line.Length != 2))
            throw new Exception("Explicit text column count was ignored.");
        Throws<ArgumentOutOfRangeException>(() => ImageFilters.RenderAscii(source, columns: 513));

        static bool HasColor(Bitmap image, Func<Color, bool> predicate)
        {
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (predicate(image.GetPixel(x, y))) return true;
            return false;
        }
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
