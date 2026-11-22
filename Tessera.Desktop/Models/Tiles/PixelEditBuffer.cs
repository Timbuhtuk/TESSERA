using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tessera.Desktop;

internal sealed record TileEditSnapshot(byte[] Pixels, bool HasLocalEdits);

internal sealed class PixelEditBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; private set; }
    public WriteableBitmap Image { get; }
    public bool HasLocalEdits { get; private set; }
    public long Version { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    private readonly List<TileEditSnapshot> _undo = [], _redo = [];

    public PixelEditBuffer(BitmapSource image)
    {
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        Width = image.PixelWidth; Height = image.PixelHeight;
        Pixels = new byte[checked(Width * Height * 4)];
        image.CopyPixels(Pixels, Width * 4, 0);
        Image = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
        UpdateImage();
    }

    private PixelEditBuffer(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        UpdateImage();
    }

    internal void WriteDraft(BinaryWriter writer)
    {
        writer.Write(Version);
        WriteState(writer, Pixels, HasLocalEdits);
        WriteHistory(writer, _undo);
        WriteHistory(writer, _redo);
    }

    internal static PixelEditBuffer ReadDraft(BinaryReader reader, int width, int height)
    {
        int length = checked(width * height * 4);
        long version = reader.ReadInt64();
        var current = ReadState(reader, length);
        var buffer = new PixelEditBuffer(width, height, current.Pixels)
        {
            Version = version,
            HasLocalEdits = current.HasLocalEdits
        };
        ReadHistory(reader, buffer._undo, length);
        ReadHistory(reader, buffer._redo, length);
        return buffer;
    }

    private static void WriteState(BinaryWriter writer, byte[] pixels, bool hasEdits)
    {
        writer.Write(hasEdits);
        writer.Write(pixels);
    }

    private static TileEditSnapshot ReadState(BinaryReader reader, int length)
    {
        bool hasEdits = reader.ReadBoolean();
        byte[] pixels = reader.ReadBytes(length);
        if (pixels.Length != length) throw new EndOfStreamException();
        return new TileEditSnapshot(pixels, hasEdits);
    }

    private static void WriteHistory(BinaryWriter writer, List<TileEditSnapshot> states)
    {
        writer.Write(states.Count);
        foreach (var state in states) WriteState(writer, state.Pixels, state.HasLocalEdits);
    }

    private static void ReadHistory(BinaryReader reader, List<TileEditSnapshot> states, int length)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > 20) throw new InvalidDataException("Invalid tile draft history.");
        for (int q = 0; q < count; q++) states.Add(ReadState(reader, length));
    }

    public static BitmapSource DecodeFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0];
        if (image.Format != PixelFormats.Bgra32) image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        image.Freeze();
        return image;
    }

    public TileEditSnapshot Capture() => new((byte[])Pixels.Clone(), HasLocalEdits);

    public bool FinishEdit(TileEditSnapshot before)
    {
        if (before.Pixels.AsSpan().SequenceEqual(Pixels)) return false;
        _undo.Add(before); _redo.Clear(); Trim(_undo);
        HasLocalEdits = true;
        return true;
    }

    public bool ReplacePixels(byte[] pixels)
    {
        if (pixels.Length != Pixels.Length) throw new ArgumentException(Localization.Text("PixelBufferSizeMismatch"));
        var before = Capture();
        Pixels = (byte[])pixels.Clone();
        if (!FinishEdit(before)) return false;
        Version++; UpdateImage();
        return true;
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(List<TileEditSnapshot> from, List<TileEditSnapshot> to)
    {
        if (from.Count == 0) return;
        to.Add(Capture()); Trim(to);
        var state = from[^1]; from.RemoveAt(from.Count - 1);
        Pixels = state.Pixels; HasLocalEdits = state.HasLocalEdits;
        Version++; UpdateImage();
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
    public void PaintLine(int x0, int y0, int x1, int y1, Color color, int size, TileEditSnapshot? before = null, bool erase = false)
    {
        if (size is < 1 or > 32 || x0 < 0 || x0 >= Width || x1 < 0 || x1 >= Width || y0 < 0 || y0 >= Height || y1 < 0 || y1 >= Height)
            throw new ArgumentOutOfRangeException(nameof(size), Localization.Text("CoordinatesMustInsideTileBrushSize"));
        if (!erase && color.A == 0) return;
        byte[] source = before?.Pixels ?? (byte[])Pixels.Clone();
        if (source.Length != Pixels.Length) throw new ArgumentException("Snapshot dimensions do not match.", nameof(before));
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
                    if (erase) Array.Clear(Pixels, q, 4);
                    else BlendPixel(q, color, source);
                }
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
        Version++; UpdateImage();
    }

    public bool Fill(int x, int y, Color color, bool entireTile = false)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        if (color.A == 0) return false;
        var before = Capture();
        if (entireTile)
        {
            for (int q = 0; q < Pixels.Length; q += 4) BlendPixel(q, color, before.Pixels);
        }
        else
        {
            int seed = (y * Width + x) * 4;
            var visited = new System.Collections.BitArray(Width * Height);
            var pending = new Stack<int>();
            Add(x, y);
            while (pending.Count > 0)
            {
                int pixel = pending.Pop();
                BlendPixel(pixel * 4, color, before.Pixels);
                int px = pixel % Width, py = pixel / Width;
                Add(px - 1, py); Add(px + 1, py); Add(px, py - 1); Add(px, py + 1);
            }

            void Add(int px, int py)
            {
                if (px < 0 || px >= Width || py < 0 || py >= Height) return;
                int pixel = py * Width + px, q = pixel * 4;
                if (visited[pixel]) return;
                visited[pixel] = true;
                byte[] source = before.Pixels;
                if (source[q + 3] != source[seed + 3]) return;
                // Hidden RGB does not split a fully transparent area.
                if (source[q + 3] != 0 && (source[q] != source[seed] || source[q + 1] != source[seed + 1] || source[q + 2] != source[seed + 2])) return;
                pending.Push(pixel);
            }
        }
        if (!FinishEdit(before)) return false;
        Version++; UpdateImage();
        return true;
    }

    private void BlendPixel(int q, Color color, byte[] source)
    {
        // Straight-alpha source-over. Pre-stroke pixels keep overlapping dabs even.
        int remaining = source[q + 3] * (255 - color.A);
        int alpha = color.A * 255 + remaining;
        if (alpha == 0) return;
        Pixels[q] = Channel(color.B, source[q]);
        Pixels[q + 1] = Channel(color.G, source[q + 1]);
        Pixels[q + 2] = Channel(color.R, source[q + 2]);
        Pixels[q + 3] = (byte)((alpha + 127) / 255);

        byte Channel(byte foreground, byte background) => (byte)((foreground * color.A * 255 + background * remaining + alpha / 2) / alpha);
    }

    private void UpdateImage() => Image.WritePixels(new Int32Rect(0, 0, Width, Height), Pixels, Width * 4, 0);

}
