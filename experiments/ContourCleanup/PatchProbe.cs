using System.Drawing;
using PixelArtDownscale;

internal static class PatchProbe
{
    internal static void Run(string path, int x, int y)
    {
        using var image = new Bitmap(path);
        for (int radius = 1; radius <= 3; radius++)
        {
            UInt128 target = Pattern(x, y, radius);
            var bright = new List<Point>();
            var dark = new List<Point>();
            for (int e = radius; e < image.Height - radius; e++)
                for (int f = radius; f < image.Width - radius; f++)
                {
                    if (Pattern(f, e, radius) != target) continue;
                    Color center = image.GetPixel(f, e);
                    if (center.A == 0) continue;
                    if (ColorSpace.RgbToLab(center.ToArgb() & 0xFFFFFF).L < 5)
                        dark.Add(new Point(f, e));
                    else bright.Add(new Point(f, e));
                }
            Console.WriteLine($"radius={radius} bright={bright.Count} dark={dark.Count} " +
                $"bright-at={string.Join(' ', bright.Take(12))} dark-at={string.Join(' ', dark.Take(12))}");
        }

        UInt128 Pattern(int px, int py, int radius)
        {
            UInt128 result = 0;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Color color = image.GetPixel(px + dx, py + dy);
                    int value = color.A == 0 ? 0 :
                        ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L < 5 ? 1 : 2;
                    result = result * 3 + (uint)value;
                }
            return result;
        }
    }
}
