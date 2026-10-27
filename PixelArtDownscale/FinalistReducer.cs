using System.Drawing.Imaging;

namespace PixelArtDownscale;

public enum FinalistMode { T3P44, P5Off, HalfAndHalf }

/// <summary>The two owner-selected TCR-Lab reducers and their equal sRGB/alpha blend.</summary>
public static class FinalistReducer
{
    private const int CellSize = 8;

    public static Bitmap Reduce(Bitmap source, FinalistMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (source.Width < 1 || source.Height < 1) throw new ArgumentException("Image dimensions must be positive.", nameof(source));

        using var input = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        using var pixels = BitmapBuffer.FromBitmap(input);
        int width = (source.Width + CellSize - 1) / CellSize;
        int height = (source.Height + CellSize - 1) / CellSize;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var output = BitmapBuffer.FromWritableBitmap(result))
        {
            int[] regularX = Boundaries(source.Width, width, 0);
            int[] regularY = Boundaries(source.Height, height, 0);
            int[] shiftedX = Boundaries(source.Width, width, 4);
            int[] shiftedY = Boundaries(source.Height, height, 4);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var color = mode switch
                {
                    FinalistMode.T3P44 => ReduceCell(pixels, shiftedX[x], shiftedY[y], shiftedX[x + 1], shiftedY[y + 1], false),
                    FinalistMode.P5Off => ReduceCell(pixels, regularX[x], regularY[y], regularX[x + 1], regularY[y + 1], true),
                    _ => Blend(
                        ReduceCell(pixels, shiftedX[x], shiftedY[y], shiftedX[x + 1], shiftedY[y + 1], false),
                        ReduceCell(pixels, regularX[x], regularY[y], regularX[x + 1], regularY[y + 1], true))
                };
                output.SetPackedColor(x, y, ColorSpace.Pack(color.R, color.G, color.B));
                output.SetAlpha(x, y, color.A);
            }
        }
        return result;
    }

    private static int[] Boundaries(int size, int count, int phase)
    {
        var boundaries = new int[count + 1];
        for (int q = 1; q < count; q++)
        {
            // Laboratory p44 uses 8*q+4. For a shorter final cell, keep each cell nonempty.
            boundaries[q] = Math.Min(CellSize * q + phase, size - (count - q));
        }
        boundaries[count] = size;
        return boundaries;
    }

    private static (byte R, byte G, byte B, byte A) ReduceCell(BitmapBuffer pixels, int x0, int y0, int x1, int y1, bool zeroRgbWhenRoundedAlphaIsZero)
    {
        long alpha = 0, red = 0, green = 0, blue = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int a = pixels.GetAlpha(x, y);
            if (a == 0) continue;
            int rgb = pixels.GetPackedColor(x, y);
            alpha += a;
            red += (long)a * ColorSpace.GetR(rgb);
            green += (long)a * ColorSpace.GetG(rgb);
            blue += (long)a * ColorSpace.GetB(rgb);
        }
        byte outAlpha = Round(alpha, (x1 - x0) * (y1 - y0));
        if (alpha == 0 || zeroRgbWhenRoundedAlphaIsZero && outAlpha == 0)
            return (0, 0, 0, outAlpha);
        return (Round(red, alpha), Round(green, alpha), Round(blue, alpha), outAlpha);
    }

    public static (byte R, byte G, byte B, byte A) Blend(
        (byte R, byte G, byte B, byte A) first, (byte R, byte G, byte B, byte A) second)
    {
        int alphaSum = first.A + second.A;
        byte alpha = (byte)((alphaSum + 1) / 2);
        if (alpha == 0) return (0, 0, 0, 0);
        byte Channel(int a, int b) => (byte)((a * first.A + b * second.A + alphaSum / 2) / alphaSum);
        return (Channel(first.R, second.R), Channel(first.G, second.G), Channel(first.B, second.B), alpha);
    }

    private static byte Round(long numerator, long denominator)
        => (byte)Math.Clamp((2 * numerator + denominator) / (2 * denominator), 0, 255);
}
