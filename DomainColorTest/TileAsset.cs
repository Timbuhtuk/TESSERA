using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtDownscale;

namespace DomainColorTest;

public sealed record TileEditSnapshot(byte[] Pixels, bool HasLocalEdits);

public sealed class TileAsset : INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Тайл";
    public string? SourcePath { get; init; }
    public string? AtlasSourcePath { get; init; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; private set; }
    public WriteableBitmap Image { get; }
    public bool HasLocalEdits { get; private set; }
    public long Version { get; private set; }
    public string Label => $"{Name}{(HasLocalEdits ? " *" : "")} · {Width} × {Height}";
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly List<TileEditSnapshot> _undo = [], _redo = [];

    public TileAsset(BitmapSource image, bool edited = false)
    {
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        Width = image.PixelWidth; Height = image.PixelHeight;
        Pixels = new byte[checked(Width * Height * 4)];
        image.CopyPixels(Pixels, Width * 4, 0);
        Image = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        HasLocalEdits = edited;
        UpdateImage();
    }

    public static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0];
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        image.Freeze();
        return image;
    }

    public static BitmapSource DecodeFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Decode(stream);
    }

    public TileEditSnapshot Capture() => new((byte[])Pixels.Clone(), HasLocalEdits);

    public bool FinishEdit(TileEditSnapshot before)
    {
        if (before.Pixels.AsSpan().SequenceEqual(Pixels)) return false;
        _undo.Add(before); _redo.Clear(); Trim(_undo);
        HasLocalEdits = true;
        Notify();
        return true;
    }

    public bool ReplacePixels(byte[] pixels)
    {
        if (pixels.Length != Pixels.Length) throw new ArgumentException("Размер пиксельного буфера не совпадает.");
        var before = Capture();
        Pixels = (byte[])pixels.Clone();
        if (!FinishEdit(before)) return false;
        Version++; UpdateImage();
        return true;
    }

    public void Reload(BitmapSource image)
    {
        if (image.PixelWidth != Width || image.PixelHeight != Height) throw new ArgumentException("Размер тайла изменился.");
        image.CopyPixels(Pixels, Width * 4, 0);
        _undo.Clear(); _redo.Clear(); HasLocalEdits = false;
        Version++; UpdateImage(); Notify();
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(List<TileEditSnapshot> from, List<TileEditSnapshot> to)
    {
        if (from.Count == 0) return;
        to.Add(Capture()); Trim(to);
        var state = from[^1]; from.RemoveAt(from.Count - 1);
        Pixels = state.Pixels; HasLocalEdits = state.HasLocalEdits;
        Version++; UpdateImage(); Notify();
    }

    private static void Trim(List<TileEditSnapshot> history)
    {
        long bytes = history.Sum(state => (long)state.Pixels.Length);
        while (history.Count > 1 && (history.Count > 20 || bytes > 64L * 1024 * 1024))
        {
            bytes -= history[0].Pixels.Length;
            history.RemoveAt(0);
        }
    }

    public Color GetPixel(int x, int y)
    {
        int q = (y * Width + x) * 4;
        return Color.FromArgb(Pixels[q + 3], Pixels[q + 2], Pixels[q + 1], Pixels[q]);
    }

    // Bresenham connects mouse samples without gaps; coordinates are source pixels.
    public void PaintLine(int x0, int y0, int x1, int y1, Color color, int size)
    {
        if (size is < 1 or > 32 || x0 < 0 || x0 >= Width || x1 < 0 || x1 >= Width || y0 < 0 || y0 >= Height || y1 < 0 || y1 >= Height)
            throw new ArgumentOutOfRangeException(nameof(size), "Координаты должны быть внутри тайла; размер кисти — 1–32.");
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            int start = -(size / 2);
            for (int e = start; e < start + size; e++)
                for (int f = start; f < start + size; f++)
                {
                    int x = x0 + f, y = y0 + e;
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    int q = (y * Width + x) * 4;
                    Pixels[q] = color.B; Pixels[q + 1] = color.G; Pixels[q + 2] = color.R; Pixels[q + 3] = color.A;
                }
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
        Version++; UpdateImage();
    }

    public void UpdateImage() => Image.WritePixels(new Int32Rect(0, 0, Width, Height), Pixels, Width * 4, 0);
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));

    public static TileSeamAnalysis Compare(TileAsset first, TileAsset second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
            throw new ArgumentException("Для сравнения нужны тайлы одинакового размера.");
        return TileSeamAnalyzer.AnalyzeEdges(first.Edge(false, true), second.Edge(false, false),
            first.Edge(true, true), second.Edge(true, false));
    }

    private byte[] Edge(bool horizontal, bool last)
    {
        int length = horizontal ? Width : Height;
        byte[] edge = new byte[length * 4];
        for (int q = 0; q < length; q++)
        {
            int x = horizontal ? q : last ? Width - 1 : 0;
            int y = horizontal ? last ? Height - 1 : 0 : q;
            Buffer.BlockCopy(Pixels, (y * Width + x) * 4, edge, q * 4, 4);
        }
        return edge;
    }

    public void WritePng(Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Image));
        // WIC encoders require seeking; compressed ZIP entry streams cannot seek.
        using var buffer = new MemoryStream();
        encoder.Save(buffer); buffer.Position = 0;
        buffer.CopyTo(stream);
    }
}
