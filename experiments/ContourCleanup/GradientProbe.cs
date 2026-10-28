using System.Drawing;
using PixelArtDownscale;

internal static class GradientProbe
{
    internal static void Run(string path, int x, int y)
    {
        using var bitmap = new Bitmap(path);
        var colors = new Color[bitmap.Width * bitmap.Height];
        var labs = new (double L, double A, double B)[colors.Length];
        for (int e = 0; e < bitmap.Height; e++)
            for (int f = 0; f < bitmap.Width; f++)
            {
                int index = e * bitmap.Width + f;
                colors[index] = bitmap.GetPixel(f, e);
                labs[index] = ColorSpace.RgbToLab(colors[index].ToArgb() & 0xFFFFFF);
            }
        (double X, double Y) center = Gradient(x, y, -1, -1);
        double centerLength = Length(center);
        for (int radius = 1; radius <= 7; radius += 2)
        {
            double commonX = 0, commonY = 0, meanLength = 0;
            int count = 0;
            for (int e = Math.Max(1, y - radius); e <= Math.Min(bitmap.Height - 2, y + radius); e++)
                for (int f = Math.Max(1, x - radius); f <= Math.Min(bitmap.Width - 2, x + radius); f++)
                {
                    if (e == y && f == x) continue;
                    int index = e * bitmap.Width + f;
                    if (colors[index].A != colors[y * bitmap.Width + x].A ||
                        DistanceSquared(labs[index], labs[y * bitmap.Width + x]) > 24 * 24) continue;
                    var neighbor = Gradient(f, e, x, y);
                    double length = Length(neighbor);
                    if (length <= 1e-9) continue;
                    commonX += neighbor.X / length;
                    commonY += neighbor.Y / length;
                    meanLength += length;
                    count++;
                }
            double commonLength = Math.Sqrt(commonX * commonX + commonY * commonY);
            double agreement = count > 0 ? commonLength / count : 0;
            double opposition = centerLength > 0 && commonLength > 0 ?
                -(center.X * commonX + center.Y * commonY) / (centerLength * commonLength) : 0;
            Console.WriteLine($"radius={radius} center={centerLength:F0} similar={count} " +
                $"neighbor={meanLength / Math.Max(count, 1):F0} agreement={agreement:F3} opposition={opposition:F3}");
        }

        (double X, double Y) Gradient(int px, int py, int excludeX, int excludeY)
        {
            var origin = labs[py * bitmap.Width + px];
            double gx = 0, gy = 0, wx = 0, wy = 0;
            for (int e = py - 1; e <= py + 1; e++)
                for (int f = px - 1; f <= px + 1; f++)
                {
                    int dx = f - px, dy = e - py;
                    if (dx == 0 && dy == 0 || f == excludeX && e == excludeY ||
                        f < 0 || e < 0 || f >= bitmap.Width || e >= bitmap.Height) continue;
                    int index = e * bitmap.Width + f;
                    if (colors[index].A != colors[py * bitmap.Width + px].A) continue;
                    double weight = 1.0 / (dx * dx + dy * dy);
                    double difference = DistanceSquared(origin, labs[index]);
                    gx += dx * weight * difference;
                    gy += dy * weight * difference;
                    wx += Math.Abs(dx) * weight;
                    wy += Math.Abs(dy) * weight;
                }
            return (wx > 0 ? gx / wx : 0, wy > 0 ? gy / wy : 0);
        }
    }

    private static double Length((double X, double Y) vector)
        => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double DistanceSquared((double L, double A, double B) a, (double L, double A, double B) b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
    }
}
