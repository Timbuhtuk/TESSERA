using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelArtDownscale;

public sealed record AsciiRampPreset(string Name, string Characters);
public enum AsciiColorMode { Monochrome, Source, Custom }

public static class ImageFilters
{
    public const string DefaultAsciiRamp = "@#S08Xx+=-;:, .";
    public const int DefaultAsciiFontSize = 10;
    public static IReadOnlyList<AsciiRampPreset> AsciiRampPresets { get; } =
    [
        new("Standard", DefaultAsciiRamp),
        new("Detailed", "$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/\\|()1{}[]?-_+~<>i!lI;:,\"^`'. "),
        new("Minimal", ". : -"),
        new("Blocks", "█ ▓ ▒ ░")
    ];

    public static Bitmap Monochrome(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int[] pixels = ReadArgb(source);
        for (int q = 0; q < pixels.Length; q++)
        {
            int argb = pixels[q];
            int alpha = (int)((uint)argb >> 24);
            if (alpha == 0) continue;
            byte gray = (byte)Math.Clamp((int)Math.Round(ColorWeights.GetLuminance(argb & 0xFFFFFF)), 0, 255);
            pixels[q] = (alpha << 24) | ColorSpace.Pack(gray, gray, gray);
        }
        return WriteArgb(pixels, source.Width, source.Height);
    }

    /// <summary>Alpha-aware 3×3 unsharp mask, constrained to local channel extrema to avoid overshoot halos.</summary>
    public static Bitmap Sharpen(Bitmap source, int strength = 100, int threshold = 4, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (strength is < 0 or > 200) throw new ArgumentOutOfRangeException(nameof(strength));
        if (threshold is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(threshold));
        cancellationToken.ThrowIfCancellationRequested();
        int width = source.Width, height = source.Height;
        int[] pixels = ReadArgb(source);
        if (strength == 0) return WriteArgb(pixels, width, height);
        int[] result = new int[pixels.Length];
        int[] kernel = [1, 2, 1, 2, 4, 2, 1, 2, 1];
        double amount = strength / 100.0;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x, center = pixels[index];
                int alpha = (int)((uint)center >> 24);
                if (alpha == 0) { result[index] = center; continue; }
                long sumR = 0, sumG = 0, sumB = 0;
                int weightSum = 0, minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
                for (int e = -1; e <= 1; e++)
                for (int f = -1; f <= 1; f++)
                {
                    int px = pixels[Math.Clamp(y + e, 0, height - 1) * width + Math.Clamp(x + f, 0, width - 1)];
                    int neighborAlpha = (int)((uint)px >> 24);
                    if (neighborAlpha == 0) continue;
                    int weight = kernel[(e + 1) * 3 + f + 1] * neighborAlpha;
                    int r = px >> 16 & 255, g = px >> 8 & 255, b = px & 255;
                    sumR += (long)r * weight; sumG += (long)g * weight; sumB += (long)b * weight;
                    weightSum += weight;
                    minR = Math.Min(minR, r); minG = Math.Min(minG, g); minB = Math.Min(minB, b);
                    maxR = Math.Max(maxR, r); maxG = Math.Max(maxG, g); maxB = Math.Max(maxB, b);
                }
                double blurR = sumR / (double)weightSum, blurG = sumG / (double)weightSum, blurB = sumB / (double)weightSum;
                int oldR = center >> 16 & 255, oldG = center >> 8 & 255, oldB = center & 255;
                if (Math.Max(Math.Abs(oldR - blurR), Math.Max(Math.Abs(oldG - blurG), Math.Abs(oldB - blurB))) <= threshold)
                { result[index] = center; continue; }
                int red = SharpenChannel(oldR, blurR, minR, maxR, amount);
                int green = SharpenChannel(oldG, blurG, minG, maxG, amount);
                int blue = SharpenChannel(oldB, blurB, minB, maxB, amount);
                result[index] = alpha << 24 | red << 16 | green << 8 | blue;
            }
        }
        return WriteArgb(result, width, height);

        static int SharpenChannel(int value, double blur, int minimum, int maximum, double amount)
            => Math.Clamp((int)Math.Round(value + (value - blur) * amount), minimum, maximum);
    }

    public static string ToAscii(Bitmap source, int columns, string characters = DefaultAsciiRamp,
        bool invertSource = false, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns < 1)
            throw new ArgumentOutOfRangeException(nameof(columns), "Ширина ASCII должна быть положительной.");
        var cell = MeasureAsciiCell(DefaultAsciiFontSize);
        int rows = CalculateAsciiRows(source.Width, source.Height, columns, cell.Width, cell.Height);
        return ToAscii(source, columns, rows, characters, invertSource, dither);
    }

    public static string ToAscii(Bitmap source, string characters = DefaultAsciiRamp,
        bool invertSource = false, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cell = MeasureAsciiCell(DefaultAsciiFontSize);
        int columns = Math.Max(1, (int)Math.Round(source.Width / (double)cell.Width));
        int rows = CalculateAsciiRows(source.Width, source.Height, columns, cell.Width, cell.Height);
        return ToAscii(source, columns, rows, characters, invertSource, dither);
    }

    public static string ToAsciiPerPixel(Bitmap source, string characters = DefaultAsciiRamp,
        bool invertSource = false, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ToAscii(source, source.Width, source.Height, characters, invertSource, dither);
    }

    public static string ToAsciiAtColumns(Bitmap source, int columns, string characters = DefaultAsciiRamp,
        bool invertSource = false, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(columns));
        var cell = MeasureAsciiCell(DefaultAsciiFontSize);
        int rows = CalculateAsciiRows(source.Width, source.Height, columns, cell.Width, cell.Height);
        return ToAscii(source, columns, rows, characters, invertSource, dither);
    }

    internal static int CalculateAsciiRows(int width, int height, int columns, int cellWidth, int cellHeight)
    {
        double value = height / (double)width * columns * cellWidth / cellHeight;
        if (value > int.MaxValue)
            throw new InvalidOperationException("ASCII-текст не помещается в доступный размер строки.");
        return Math.Max(1, (int)Math.Round(value));
    }

    public static Bitmap RenderAscii(Bitmap source, bool keepSourceSize = true, string characters = DefaultAsciiRamp,
        bool invertSource = false, bool invertColors = false, AsciiColorMode colorMode = AsciiColorMode.Monochrome,
        int foregroundArgb = -1, int backgroundArgb = unchecked((int)0xFF000000), int columns = 0,
        bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(characters) || characters.Contains('\r') || characters.Contains('\n'))
            throw new ArgumentException("Набор ASCII должен содержать символы яркости в одной строке.", nameof(characters));
        if (!Enum.IsDefined(colorMode)) throw new ArgumentOutOfRangeException(nameof(colorMode));
        if (columns is < 0 or > 512) throw new ArgumentOutOfRangeException(nameof(columns));
        return AsciiRenderer.Render(source, ReadArgb(source), keepSourceSize, characters, invertSource,
            invertColors, colorMode, foregroundArgb, backgroundArgb, columns, dither);
    }

    public static Bitmap RenderAscii(string text, int fontSize = DefaultAsciiFontSize, bool invertColors = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (fontSize < 1)
            throw new ArgumentOutOfRangeException(nameof(fontSize), "Размер ASCII-шрифта должен быть положительным.");
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int columns = Math.Max(1, lines.Max(line => line.Length));

        using var font = new Font(FontFamily.GenericMonospace, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        int cellWidth;
        int cellHeight;
        using (var measure = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(measure))
        {
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            var size = graphics.MeasureString("@", font, PointF.Empty, StringFormat.GenericTypographic);
            cellWidth = Math.Max(1, (int)Math.Ceiling(size.Width));
            cellHeight = Math.Max(1, (int)Math.Ceiling(font.GetHeight(graphics)));
        }

        long width = (long)columns * cellWidth;
        long height = (long)Math.Max(1, lines.Length) * cellHeight;
        if (width > int.MaxValue || height > int.MaxValue)
            throw new InvalidOperationException("Размер ASCII-изображения превышает размер bitmap.");
        Bitmap result;
        try { result = new Bitmap((int)width, (int)height, PixelFormat.Format24bppRgb); }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            throw new InvalidOperationException($"Не удалось создать ASCII-изображение {width} × {height} px.", ex);
        }
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.Clear(invertColors ? Color.White : Color.Black);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var brush = new SolidBrush(invertColors ? Color.Black : Color.White);
            for (int y = 0; y < lines.Length; y++)
                for (int x = 0; x < lines[y].Length; x++)
                    graphics.DrawString(lines[y][x].ToString(), font, brush, x * cellWidth, y * cellHeight,
                        StringFormat.GenericTypographic);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public static Bitmap RenderAscii(string text, int width, int height, bool invertColors = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Размер ASCII-изображения должен быть положительным.");
        return RenderAscii(text, width, height, DefaultAsciiFontSize, invertColors);
    }

    private static string ToAscii(Bitmap source, int columns, int rows, string characters, bool invertSource, bool dither)
    {
        if (string.IsNullOrEmpty(characters) || characters.Contains('\r') || characters.Contains('\n'))
            throw new ArgumentException("Набор ASCII должен содержать символы яркости в одной строке.", nameof(characters));
        long characterCount = (long)rows * columns + (long)Math.Max(0, rows - 1) * Environment.NewLine.Length;
        if (characterCount > int.MaxValue)
            throw new InvalidOperationException("ASCII-текст превышает максимальный размер строки .NET.");

        int[] pixels = ReadArgb(source);
        var text = new StringBuilder((int)characterCount);
        for (int y = 0; y < rows; y++)
        {
            int y0 = y * source.Height / rows;
            int y1 = Math.Max(y0 + 1, (y + 1) * source.Height / rows);
            for (int x = 0; x < columns; x++)
            {
                int x0 = x * source.Width / columns;
                int x1 = Math.Max(x0 + 1, (x + 1) * source.Width / columns);
                double brightness = 0;
                double coverage = 0;
                for (int e = y0; e < Math.Min(y1, source.Height); e++)
                    for (int f = x0; f < Math.Min(x1, source.Width); f++)
                    {
                        int argb = pixels[e * source.Width + f];
                        double alpha = ((uint)argb >> 24) / 255.0;
                        if (alpha <= 0) continue;
                        double value = ColorWeights.GetNormalizedBrightness(argb & 0xFFFFFF);
                        brightness += (invertSource ? 1 - value : value) * alpha;
                        coverage += alpha;
                    }
                if (coverage <= 0)
                {
                    text.Append(GetMinimumFiller(characters));
                    continue;
                }
                double tone = brightness / coverage;
                if (dither) tone = AsciiDither.Adjust(tone, x, y, 1.0 / Math.Max(1, characters.Length - 1));
                int index = (int)Math.Round(tone * (characters.Length - 1));
                text.Append(characters[Math.Clamp(index, 0, characters.Length - 1)]);
            }
            if (y + 1 < rows) text.AppendLine();
        }
        return text.ToString();
    }

    private static char GetMinimumFiller(string characters)
        => characters.Contains(' ') ? ' ' : characters[^1];

    private static Bitmap RenderAscii(string text, int width, int height, int fontSize, bool invertColors)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.Clear(invertColors ? Color.White : Color.Black);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font(FontFamily.GenericMonospace, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(invertColors ? Color.Black : Color.White);
            var cell = MeasureAsciiCell(fontSize);
            for (int y = 0; y < lines.Length && y * cell.Height < height; y++)
                for (int x = 0; x < lines[y].Length && x * cell.Width < width; x++)
                    graphics.DrawString(lines[y][x].ToString(), font, brush, x * cell.Width, y * cell.Height,
                        StringFormat.GenericTypographic);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    internal static (int Width, int Height) MeasureAsciiCell(int fontSize)
    {
        using var font = new Font(FontFamily.GenericMonospace, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        using var measure = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measure);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        var size = graphics.MeasureString("@", font, PointF.Empty, format);
        return (Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(font.GetHeight(graphics))));
    }

    private static int[] ReadArgb(Bitmap source)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var pixels = new int[checked(copy.Width * copy.Height)];
        var bits = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < copy.Height; y++)
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * copy.Width, copy.Width);
        }
        finally { copy.UnlockBits(bits); }
        return pixels;
    }

    private static Bitmap WriteArgb(int[] pixels, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var bits = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width, IntPtr.Add(bits.Scan0, y * bits.Stride), width);
            }
            finally { result.UnlockBits(bits); }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
