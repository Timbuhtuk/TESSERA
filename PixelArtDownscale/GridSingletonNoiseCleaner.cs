using System.Drawing;
using System.Drawing.Imaging;

namespace PixelArtDownscale;

public static class GridSingletonNoiseCleaner
{
    public sealed record Result(Bitmap Image, int ChangedPixels);

    // Every pixel in a regular cell of size 2 or more belongs to at least one 2x2 square.
    private static readonly (int Ax, int Ay, int Bx, int By, int Cx, int Cy)[] Squares =
    [
        (-1, -1, -1, 0, 0, -1),
        (1, -1, 1, 0, 0, -1),
        (-1, 1, -1, 0, 0, 1),
        (1, 1, 1, 0, 0, 1)
    ];

    public static Result Clean(Bitmap source, int cellSize, double colorTolerance = 0,
        Action<int, int, string>? audit = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (cellSize < 2 || cellSize > Math.Min(source.Width, source.Height))
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        if (!double.IsFinite(colorTolerance) || colorTolerance is < 0 or > 442)
            throw new ArgumentOutOfRangeException(nameof(colorTolerance));

        int width = source.Width, height = source.Height;
        var original = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                original[y * width + x] = source.GetPixel(x, y);

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        double maximumDifference = colorTolerance * colorTolerance;
        int changed = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color center = original[index];
                Color replacement = center;
                if (x > 0 && y > 0 && x < width - 1 && y < height - 1 &&
                    IsAlone(original, width, x, y, center, maximumDifference))
                {
                    Color? agreed = null;
                    bool ambiguous = false;
                    foreach (var square in Squares)
                    {
                        Color a = original[(y + square.Ay) * width + x + square.Ax];
                        Color b = original[(y + square.By) * width + x + square.Bx];
                        Color c = original[(y + square.Cy) * width + x + square.Cx];
                        if (!Similar(a, b, maximumDifference) ||
                            !Similar(a, c, maximumDifference) ||
                            !Similar(b, c, maximumDifference)) continue;
                        if (agreed is not null && !Similar(agreed.Value, a, maximumDifference))
                        {
                            ambiguous = true;
                            break;
                        }
                        agreed ??= a;
                    }
                    if (!ambiguous && agreed is not null)
                    {
                        replacement = agreed.Value;
                        changed++;
                        audit?.Invoke(x, y, "accepted-grid-singleton");
                    }
                    else audit?.Invoke(x, y, ambiguous ? "grid-ambiguous" : "grid-no-square");
                }
                result.SetPixel(x, y, replacement);
            }
        return new Result(result, changed);
    }

    private static bool IsAlone(Color[] pixels, int width, int x, int y, Color center,
        double maximumDifference)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) &&
                    Similar(center, pixels[(y + dy) * width + x + dx], maximumDifference))
                    return false;
        return true;
    }

    private static bool Similar(Color a, Color b, double maximumDifference)
    {
        if (a.A != b.A) return false;
        if (a.A == 0 && b.A == 0) return true;
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return dr * dr + dg * dg + db * db <= maximumDifference;
    }
}
