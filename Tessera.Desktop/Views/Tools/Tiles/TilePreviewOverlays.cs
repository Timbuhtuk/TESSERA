using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

internal readonly record struct PreviewTileGrid(int Width, int Height, int OffsetX, int OffsetY)
{
    public Int32Rect CompleteCells(BitmapSource source)
    {
        int columns = Math.Max(0, source.PixelWidth - OffsetX) / Width;
        int rows = Math.Max(0, source.PixelHeight - OffsetY) / Height;
        return new Int32Rect(OffsetX, OffsetY, columns * Width, rows * Height);
    }
}

internal sealed class TileGridHeatmapOverlay(BitmapSource source, PreviewTileGrid grid, double threshold)
{
    private static readonly BitmapSource MatchingPixel = TileHeatmapOverlay.HeatStrip([0], false, 0);
    public Int32Rect Area { get; } = grid.CompleteCells(source);

    // One strip per column or row of tiles keeps the comparisons in source pixels
    // and avoids creating two separate bitmaps per tile.
    private readonly Dictionary<int, BitmapSource> _columns = [];
    private readonly Dictionary<int, BitmapSource> _rows = [];

    public void Draw(DrawingContext drawing, Rect bounds, Rect visible, PreviewTileHeatmap settings)
    {
        if (visible.IsEmpty || settings.Opacity <= 0 || !(settings.Horizontal || settings.Vertical)) return;
        double width = bounds.Width / Area.Width * grid.Width;
        double height = bounds.Height / Area.Height * grid.Height;
        drawing.PushOpacity(settings.Opacity);
        if (settings.Horizontal)
        {
            if (grid.Width == 1) drawing.DrawImage(MatchingPixel, bounds);
            else
            {
                double stripe = Math.Min(settings.Stripe / grid.Width, .5) * width;
                int first = (int)Math.Floor((visible.Left - bounds.Left) / width);
                int end = Math.Min(Area.Width / grid.Width, (int)Math.Ceiling((visible.Right - bounds.Left) / width));
                for (int q = first; q < end; q++)
                {
                    double x = bounds.Left + q * width;
                    if (!_columns.TryGetValue(q, out var strip))
                    {
                        int pixelX = Area.X + q * grid.Width;
                        strip = ReadStrip(new(pixelX, Area.Y, 1, Area.Height),
                            new(pixelX + grid.Width - 1, Area.Y, 1, Area.Height), true);
                        _columns.Add(q, strip);
                    }
                    drawing.DrawImage(strip, new Rect(x, bounds.Top, stripe, bounds.Height));
                    drawing.DrawImage(strip, new Rect(x + width - stripe, bounds.Top, stripe, bounds.Height));
                }
            }
        }
        if (settings.Vertical)
        {
            if (grid.Height == 1) drawing.DrawImage(MatchingPixel, bounds);
            else
            {
                double stripe = Math.Min(settings.Stripe / grid.Height, .5) * height;
                int first = (int)Math.Floor((visible.Top - bounds.Top) / height);
                int end = Math.Min(Area.Height / grid.Height, (int)Math.Ceiling((visible.Bottom - bounds.Top) / height));
                for (int q = first; q < end; q++)
                {
                    double y = bounds.Top + q * height;
                    if (!_rows.TryGetValue(q, out var strip))
                    {
                        int pixelY = Area.Y + q * grid.Height;
                        strip = ReadStrip(new(Area.X, pixelY, Area.Width, 1),
                            new(Area.X, pixelY + grid.Height - 1, Area.Width, 1), false);
                        _rows.Add(q, strip);
                    }
                    drawing.DrawImage(strip, new Rect(bounds.Left, y, bounds.Width, stripe));
                    drawing.DrawImage(strip, new Rect(bounds.Left, y + height - stripe, bounds.Width, stripe));
                }
            }
        }
        drawing.Pop();
    }

    private BitmapSource ReadStrip(Int32Rect first, Int32Rect second, bool vertical)
    {
        int stride = checked(first.Width * 4);
        var a = new byte[checked(stride * first.Height)];
        var b = new byte[a.Length];
        source.CopyPixels(first, a, stride, 0);
        source.CopyPixels(second, b, stride, 0);
        return TileHeatmapOverlay.HeatStrip(TileSeamAnalyzer.CompareEdges(a, b), vertical, threshold);
    }
}

internal sealed class TileHeatmapOverlay(TileSeamAnalysis analysis, double threshold)
{
    private readonly BitmapSource _horizontal = HeatStrip(analysis.Horizontal, true, threshold);
    private readonly BitmapSource _vertical = HeatStrip(analysis.Vertical, false, threshold);

    internal sealed record Edges(byte[] Left, byte[] Right, byte[] Top, byte[] Bottom);

    internal static Edges ReadEdges(BitmapSource source)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        var left = new byte[checked(height * 4)];
        var right = new byte[left.Length];
        var top = new byte[checked(width * 4)];
        var bottom = new byte[top.Length];
        source.CopyPixels(new Int32Rect(0, 0, 1, height), left, 4, 0);
        source.CopyPixels(new Int32Rect(width - 1, 0, 1, height), right, 4, 0);
        source.CopyPixels(new Int32Rect(0, 0, width, 1), top, top.Length, 0);
        source.CopyPixels(new Int32Rect(0, height - 1, width, 1), bottom, bottom.Length, 0);
        return new(left, right, top, bottom);
    }

    internal void DrawEdges(DrawingContext drawing, Rect tile, BitmapSource source, TileHeatmapOverlay? left,
        TileHeatmapOverlay? top, bool right, bool bottom, double opacity, double stripe)
    {
        if (opacity <= 0 || tile.Width <= 0 || tile.Height <= 0) return;
        drawing.PushOpacity(opacity);
        if (left is not null || right)
        {
            double width = Math.Min(stripe / source.PixelWidth, .5) * tile.Width;
            if (left is not null) drawing.DrawImage(left._horizontal, new Rect(tile.Left, tile.Top, width, tile.Height));
            if (right) drawing.DrawImage(_horizontal, new Rect(tile.Right - width, tile.Top, width, tile.Height));
        }
        if (top is not null || bottom)
        {
            double height = Math.Min(stripe / source.PixelHeight, .5) * tile.Height;
            if (top is not null) drawing.DrawImage(top._vertical, new Rect(tile.Left, tile.Top, tile.Width, height));
            if (bottom) drawing.DrawImage(_vertical, new Rect(tile.Left, tile.Bottom - height, tile.Width, height));
        }
        drawing.Pop();
    }

    internal static BitmapSource HeatStrip(double[] values, bool vertical, double threshold)
    {
        var pixels = new byte[values.Length * 4];
        for (int q = 0; q < values.Length; q++)
        {
            // Green at or below the threshold, rising to red at four times the threshold.
            double amount = Math.Clamp((values[q] - threshold) / Math.Max(1, threshold * 3), 0, 1);
            Color color = amount < .5
                ? Color.FromRgb((byte)(48 + amount * 2 * 207), (byte)(210 + amount * 2 * 5), 80)
                : Color.FromRgb(255, (byte)(215 - (amount - .5) * 2 * 165), 50);
            pixels[q * 4] = color.B; pixels[q * 4 + 1] = color.G;
            pixels[q * 4 + 2] = color.R; pixels[q * 4 + 3] = 255;
        }
        int width = vertical ? 1 : values.Length, height = vertical ? values.Length : 1;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
