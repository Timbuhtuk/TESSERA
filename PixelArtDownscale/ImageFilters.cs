using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace PixelArtDownscale;

public sealed record AsciiRampPreset(string Name, string Characters);

public static class ImageFilters
{
    public const string DefaultAsciiRamp = "@#S08Xx+=-;:, .";
    public const int DefaultAsciiFontSize = 14;
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

    public static string ToAscii(Bitmap source, int columns, string characters = DefaultAsciiRamp, bool invertSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns < 1)
            throw new ArgumentOutOfRangeException(nameof(columns), "Ширина ASCII должна быть положительной.");
        double calculatedRows = source.Height / (double)source.Width * columns * 0.5;
        if (calculatedRows > int.MaxValue)
            throw new InvalidOperationException("ASCII-текст не помещается в доступный размер строки.");
        int rows = Math.Max(1, (int)Math.Round(calculatedRows));
        return ToAscii(source, columns, rows, characters, invertSource);
    }

    public static string ToAscii(Bitmap source, string characters = DefaultAsciiRamp, bool invertSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cell = MeasureAsciiCell(DefaultAsciiFontSize);
        int columns = Math.Max(1, source.Width / cell.Width);
        int rows = Math.Max(1, source.Height / cell.Height);
        return ToAscii(source, columns, rows, characters, invertSource);
    }

    public static string ToAsciiPerPixel(Bitmap source, string characters = DefaultAsciiRamp, bool invertSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ToAscii(source, source.Width, source.Height, characters, invertSource);
    }

    public static Bitmap RenderAscii(string text, int fontSize = 14, bool invertColors = false)
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

    private static string ToAscii(Bitmap source, int columns, int rows, string characters, bool invertSource)
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
                int index = (int)Math.Round(brightness / coverage * (characters.Length - 1));
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

    private static (int Width, int Height) MeasureAsciiCell(int fontSize)
    {
        using var font = new Font(FontFamily.GenericMonospace, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measure = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measure);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        var size = graphics.MeasureString("@", font, PointF.Empty, StringFormat.GenericTypographic);
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
