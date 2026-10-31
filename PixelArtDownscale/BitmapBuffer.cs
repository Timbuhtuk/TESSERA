using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public sealed class BitmapBuffer : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly BitmapData _data;
    private readonly byte[] _bytes;
    private readonly int _stride;
    private readonly bool _writable;
    private readonly int _bytesPerPixel;

    public int Width { get; }
    public int Height { get; }

    private BitmapBuffer(Bitmap bitmap, bool writable)
    {
        _bitmap = bitmap;
        _writable = writable;
        Width = bitmap.Width;
        Height = bitmap.Height;

        var format = bitmap.PixelFormat;
        if (format is not (PixelFormat.Format24bppRgb or PixelFormat.Format32bppArgb))
            throw new InvalidOperationException($"Expected Format24bppRgb or Format32bppArgb, got {format}.");
        _bytesPerPixel = format == PixelFormat.Format32bppArgb ? 4 : 3;

        _data = bitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            writable ? ImageLockMode.ReadWrite : ImageLockMode.ReadOnly,
            format);

        _stride = Math.Abs(_data.Stride);
        _bytes = new byte[_stride * Height];
        for (int y = 0; y < Height; y++)
            Marshal.Copy(IntPtr.Add(_data.Scan0, y * _data.Stride), _bytes, y * _stride, _stride);
    }

    public static BitmapBuffer FromBitmap(Bitmap bitmap) => new(bitmap, writable: false);

    public static BitmapBuffer FromWritableBitmap(Bitmap bitmap) => new(bitmap, writable: true);

    public int GetPackedColor(int x, int y)
    {
        int offset = y * _stride + x * _bytesPerPixel;
        byte b = _bytes[offset];
        byte g = _bytes[offset + 1];
        byte r = _bytes[offset + 2];
        return ColorSpace.Pack(r, g, b);
    }

    public void SetPackedColor(int x, int y, int packed)
    {
        if (!_writable)
            throw new InvalidOperationException("Buffer is read-only.");

        int offset = y * _stride + x * _bytesPerPixel;
        _bytes[offset] = ColorSpace.GetB(packed);
        _bytes[offset + 1] = ColorSpace.GetG(packed);
        _bytes[offset + 2] = ColorSpace.GetR(packed);
    }

    public void Flush()
    {
        if (!_writable)
            return;

        for (int y = 0; y < Height; y++)
            Marshal.Copy(_bytes, y * _stride, IntPtr.Add(_data.Scan0, y * _data.Stride), _stride);
    }

    public byte GetAlpha(int x, int y)
        => _bytesPerPixel == 4 ? _bytes[y * _stride + x * _bytesPerPixel + 3] : (byte)255;

    public void SetAlpha(int x, int y, byte alpha)
    {
        if (!_writable) throw new InvalidOperationException("Buffer is read-only.");
        if (_bytesPerPixel == 4) _bytes[y * _stride + x * _bytesPerPixel + 3] = alpha;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_writable)
            Flush();

        _bitmap.UnlockBits(_data);
        _disposed = true;
    }
}

public static class BitmapFactory
{
    public static Bitmap CropTransparentEdges(Bitmap source)
        => CropTransparentEdges(source, progress: null);

    public static Bitmap CropTransparentEdges(Bitmap source, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = CropTransparentEdgesWithProgress(source, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap CropTransparentEdgesWithProgress(Bitmap source, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        ArgumentNullException.ThrowIfNull(source);
        if (!Image.IsAlphaPixelFormat(source.PixelFormat) && !source.PixelFormat.HasFlag(PixelFormat.Indexed))
            return (Bitmap)source.Clone();

        using var argb = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        int left = source.Width, top = source.Height, right = -1, bottom = -1;
        using (var pixels = BitmapBuffer.FromBitmap(argb))
        {
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (pixels.GetAlpha(x, y) == 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
                tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)source.Height) / 1.0);
            }
        }
        if (right < 0) return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        return argb.Clone(new Rectangle(left, top, right - left + 1, bottom - top + 1), PixelFormat.Format32bppArgb);
    }

    internal static Bitmap CreateLike(Bitmap source, int width, int height)
        => new Bitmap(width, height, Image.IsAlphaPixelFormat(source.PixelFormat)
            ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);

    public static Bitmap Create24Bpp(int width, int height)
        => new Bitmap(width, height, PixelFormat.Format24bppRgb);

    public static Bitmap Ensure24Bpp(Bitmap source)
    {
        if (source.PixelFormat == PixelFormat.Format24bppRgb)
            return source;

        var converted = Create24Bpp(source.Width, source.Height);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(source, 0, 0, source.Width, source.Height);
        return converted;
    }

    public static Bitmap CropCenter(Bitmap source, int cropWidth, int cropHeight)
        => Crop(source, cropWidth, cropHeight, CropHorizontalAlignment.Center, CropVerticalAlignment.Center);

    public static Bitmap Crop(
        Bitmap source,
        int cropWidth,
        int cropHeight,
        CropHorizontalAlignment horizontal,
        CropVerticalAlignment vertical)
    {
        int x = horizontal switch
        {
            CropHorizontalAlignment.Left => 0,
            CropHorizontalAlignment.Right => source.Width - cropWidth,
            _ => (source.Width - cropWidth) / 2
        };

        int y = vertical switch
        {
            CropVerticalAlignment.Top => 0,
            CropVerticalAlignment.Bottom => source.Height - cropHeight,
            _ => (source.Height - cropHeight) / 2
        };

        return source.Clone(new Rectangle(x, y, cropWidth, cropHeight),
            source.PixelFormat == PixelFormat.Format24bppRgb ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb);
    }
}
