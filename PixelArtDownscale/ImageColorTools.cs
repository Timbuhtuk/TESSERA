using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public static class ImageColorTools
{
    public const int MaxPaletteColors = 1024;

    /// <summary>Возвращает видимые RGB-цвета в порядке первого появления; при 1025-м цвете выдаёт ошибку.</summary>
    public static IReadOnlyList<Color> GetPalette(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var colors = new List<Color>();
        var unique = new HashSet<int>();

        ReadVisibleColors(source, color =>
        {
            if (!unique.Add(color)) return;
            if (unique.Count > MaxPaletteColors)
                throw new InvalidOperationException($"Палитра изображения содержит более {MaxPaletteColors} цветов.");
            colors.Add(ColorSpace.ToColor(color));
        });
        return colors;
    }

    /// <summary>Считает уникальные видимые RGB-цвета без ограничения размера палитры.</summary>
    public static int CountColors(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var unique = new HashSet<int>();
        ReadVisibleColors(source, color => unique.Add(color));
        return unique.Count;
    }

    /// <summary>Заменяет RGB-цвет у видимых пикселей, сохраняя прозрачность и исходное изображение.</summary>
    public static Bitmap ReplaceColor(Bitmap source, Color oldColor, Color newColor)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var area = new Rectangle(0, 0, result.Width, result.Height);
        try
        {
            var bits = result.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[result.Width];
                int oldRgb = oldColor.ToArgb() & 0xFFFFFF;
                int newRgb = newColor.ToArgb() & 0xFFFFFF;
                for (int y = 0; y < result.Height; y++)
                {
                    IntPtr address = IntPtr.Add(bits.Scan0, y * bits.Stride);
                    Marshal.Copy(address, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++)
                        if ((uint)row[x] >> 24 != 0 && (row[x] & 0xFFFFFF) == oldRgb)
                            row[x] = (row[x] & unchecked((int)0xFF000000)) | newRgb;
                    Marshal.Copy(row, 0, address, row.Length);
                }
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

    private static void ReadVisibleColors(Bitmap source, Action<int> visit)
    {
        using var copy = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var bits = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[copy.Width];
            for (int y = 0; y < copy.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                for (int x = 0; x < row.Length; x++)
                    if ((uint)row[x] >> 24 != 0)
                        visit(row[x] & 0xFFFFFF);
            }
        }
        finally { copy.UnlockBits(bits); }
    }
}
