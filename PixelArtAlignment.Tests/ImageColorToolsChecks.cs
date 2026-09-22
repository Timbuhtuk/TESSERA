using System.Drawing.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class ImageColorToolsChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Palette accepts 1024 colors and reports the 1025th", PaletteLimit);
        check("Unrestricted color count includes colors beyond palette limit", UnrestrictedCount);
        check("Color replacement preserves alpha and source image", Replacement);
    }

    private static void PaletteLimit()
    {
        using var source = CreateColors(1025);
        using var withinLimit = source.Clone(new Rectangle(0, 0, 1024, 1), PixelFormat.Format32bppArgb);
        var palette = ImageColorTools.GetPalette(withinLimit);
        if (palette.Count != 1024 || palette[0].ToArgb() != Color.Black.ToArgb() ||
            palette[1023].ToArgb() != Color.FromArgb(0, 3, 255).ToArgb())
            throw new Exception("Palette omitted or reordered colors within the limit.");
        try
        {
            ImageColorTools.GetPalette(source);
            throw new Exception("The 1025th color did not cause an error.");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("1024")) { }
    }

    private static void UnrestrictedCount()
    {
        using var source = CreateColors(1025);
        if (ImageColorTools.CountColors(source) != 1025)
            throw new Exception("Unrestricted count stopped at the palette limit.");
        source.SetPixel(1024, 0, Color.FromArgb(128, 0, 0, 0));
        if (ImageColorTools.CountColors(source) != 1024)
            throw new Exception("Different opacity was counted as another RGB color.");
        source.SetPixel(1024, 0, Color.Transparent);
        if (ImageColorTools.CountColors(source) != 1024)
            throw new Exception("A transparent pixel was included in color count.");
    }

    private static void Replacement()
    {
        using var source = new Bitmap(4, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        source.SetPixel(1, 0, Color.FromArgb(70, 10, 20, 30));
        source.SetPixel(2, 0, Color.FromArgb(255, 10, 20, 31));
        source.SetPixel(3, 0, Color.Transparent);
        using var result = ImageColorTools.ReplaceColor(source,
            Color.FromArgb(10, 20, 30), Color.FromArgb(5, 200, 210, 220));
        if (result.GetPixel(0, 0).ToArgb() != Color.FromArgb(255, 200, 210, 220).ToArgb() ||
            result.GetPixel(1, 0).ToArgb() != Color.FromArgb(70, 200, 210, 220).ToArgb() ||
            result.GetPixel(2, 0).ToArgb() != source.GetPixel(2, 0).ToArgb() ||
            result.GetPixel(3, 0).A != 0)
            throw new Exception("Replacement changed opacity or an unrelated pixel.");
        if (source.GetPixel(0, 0).ToArgb() != Color.FromArgb(255, 10, 20, 30).ToArgb())
            throw new Exception("Replacement modified the source bitmap.");
    }

    private static Bitmap CreateColors(int count)
    {
        var source = new Bitmap(count, 1, PixelFormat.Format32bppArgb);
        for (int q = 0; q < count; q++)
            source.SetPixel(q, 0, Color.FromArgb((q >> 16) & 255, (q >> 8) & 255, q & 255));
        return source;
    }
}
