using PixelArtDownscale;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DomainColorTest;

internal sealed record MoodboardRenderLayer(byte[] ImageData, double Width, double Height,
    double X, double Y, double Angle, double Scale);

internal static class MoodboardPngExporter
{
    private const int BandHeight = 128;
    private const long ImageCacheLimit = 128L * 1024 * 1024;

    public static void Export(string path, int width, int height, Color background, IReadOnlyList<MoodboardRenderLayer> layers)
        => Export(path, width, height, background, layers, progress: null);

    public static void Export(string path, int width, int height, Color background, IReadOnlyList<MoodboardRenderLayer> layers, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        ExportWithProgress(path, width, height, background, layers, tracking);
        tracking.Report(1);
    }

    private static void ExportWithProgress(string path, int width, int height, Color background, IReadOnlyList<MoodboardRenderLayer> layers, ProcessingProgress tracking)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var png = new StreamingPngWriter(output, width, height))
            {
                var cache = new DecodedImageCache(layers);
                var active = new List<int>();
                byte[] pixels = new byte[width * 4];
                byte[] rgb = new byte[width * 3];
                byte[] backgroundRow = new byte[width * 3];
                for (int x = 0; x < width; x++)
                {
                    backgroundRow[x * 3] = background.R;
                    backgroundRow[x * 3 + 1] = background.G;
                    backgroundRow[x * 3 + 2] = background.B;
                }
                for (int top = 0; top < height; top += BandHeight)
                {
                    int bandHeight = Math.Min(BandHeight, height - top);
                    active.Clear();
                    for (int index = 0; index < layers.Count; index++)
                        if (IntersectsBand(layers[index], top, bandHeight, width)) active.Add(index);
                    if (active.Count == 0)
                    {
                        for (int row = 0; row < bandHeight; row++) png.WriteRow(backgroundRow);
                        tracking.Report((top + bandHeight) / (double)height);
                        continue;
                    }
                    var band = new RenderTargetBitmap(width, bandHeight, 96, 96, PixelFormats.Pbgra32);
                    RenderBackground(band, width, bandHeight, background);
                    foreach (int index in active)
                    {
                        var layer = layers[index];
                        RenderLayer(band, cache.Get(index), layer, top);
                    }
                    for (int row = 0; row < bandHeight; row++)
                    {
                        band.CopyPixels(new Int32Rect(0, row, width, 1), pixels, width * 4, 0);
                        for (int x = 0; x < width; x++)
                        {
                            rgb[x * 3] = pixels[x * 4 + 2];
                            rgb[x * 3 + 1] = pixels[x * 4 + 1];
                            rgb[x * 3 + 2] = pixels[x * 4];
                        }
                        png.WriteRow(rgb);
                        tracking.Report((top + row + 1.0) / height);
                    }
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RenderBackground(RenderTargetBitmap target, int width, int height, Color background)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
        target.Render(visual);
    }

    private static void RenderLayer(RenderTargetBitmap target, BitmapSource image, MoodboardRenderLayer layer, int top)
    {
        double width = layer.Width * layer.Scale, height = layer.Height * layer.Scale;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(layer.X + width / 2, layer.Y + height / 2 - top));
            context.PushTransform(new RotateTransform(layer.Angle));
            context.DrawImage(image, new Rect(-width / 2, -height / 2, width, height));
            context.Pop(); context.Pop();
        }
        target.Render(visual);
    }

    private static bool IntersectsBand(MoodboardRenderLayer layer, int top, int bandHeight, int boardWidth)
    {
        double halfWidth = layer.Width * layer.Scale / 2, halfHeight = layer.Height * layer.Scale / 2;
        double angle = layer.Angle * Math.PI / 180;
        double extentX = Math.Abs(Math.Cos(angle)) * halfWidth + Math.Abs(Math.Sin(angle)) * halfHeight;
        double extentY = Math.Abs(Math.Sin(angle)) * halfWidth + Math.Abs(Math.Cos(angle)) * halfHeight;
        double centerX = layer.X + halfWidth, centerY = layer.Y + halfHeight;
        return centerX + extentX > 0 && centerX - extentX < boardWidth &&
            centerY + extentY > top && centerY - extentY < top + bandHeight;
    }

    private sealed class DecodedImageCache(IReadOnlyList<MoodboardRenderLayer> layers)
    {
        private readonly Dictionary<int, BitmapSource> _images = [];
        private readonly LinkedList<int> _order = [];
        private long _size;

        public BitmapSource Get(int index)
        {
            if (_images.TryGetValue(index, out var existing))
            {
                _order.Remove(index);
                _order.AddLast(index);
                return existing;
            }
            var layer = layers[index];
            var image = Decode(layer.ImageData, layer.Width * layer.Scale, layer.Height * layer.Scale);
            long size = (long)image.PixelWidth * image.PixelHeight * 4;
            if (size > ImageCacheLimit) return image;
            while (_size + size > ImageCacheLimit && _order.First is not null)
            {
                int removed = _order.First.Value;
                _order.RemoveFirst();
                var previous = _images[removed];
                _size -= (long)previous.PixelWidth * previous.PixelHeight * 4;
                _images.Remove(removed);
            }
            _images.Add(index, image);
            _order.AddLast(index);
            _size += size;
            return image;
        }

        private static BitmapSource Decode(byte[] bytes, double displayWidth, double displayHeight)
        {
            using var metadataStream = new MemoryStream(bytes, writable: false);
            using var metadata = System.Drawing.Image.FromStream(metadataStream, useEmbeddedColorManagement: false, validateImageData: false);
            double ratio = Math.Min(1, Math.Max(displayWidth / metadata.Width, displayHeight / metadata.Height));
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes, writable: false);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (ratio < 1)
            {
                if (metadata.Width >= metadata.Height) image.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(metadata.Width * ratio));
                else image.DecodePixelHeight = Math.Max(1, (int)Math.Ceiling(metadata.Height * ratio));
            }
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    private sealed class StreamingPngWriter : IDisposable
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly uint[] CrcTable = CreateCrcTable();
        private readonly Stream _output;
        private readonly IdatStream _idat;
        private readonly ZLibStream _compressor;
        private bool _disposed;

        public StreamingPngWriter(Stream output, int width, int height)
        {
            _output = output;
            output.Write(Signature);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
            BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
            header[8] = 8;
            header[9] = 2;
            WriteChunk(output, "IHDR"u8, header);
            _idat = new IdatStream(output);
            _compressor = new ZLibStream(_idat, CompressionLevel.Fastest, leaveOpen: true);
        }

        public void WriteRow(ReadOnlySpan<byte> rgb)
        {
            _compressor.WriteByte(0);
            _compressor.Write(rgb);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _compressor.Dispose();
            _idat.Finish();
            WriteChunk(_output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        }

        private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);
            output.Write(type);
            output.Write(data);
            uint crc = UpdateCrc(UpdateCrc(uint.MaxValue, type), data) ^ uint.MaxValue;
            BinaryPrimitives.WriteUInt32BigEndian(number, crc);
            output.Write(number);
        }

        private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            return crc;
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint q = 0; q < table.Length; q++)
            {
                uint crc = q;
                for (int e = 0; e < 8; e++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                table[q] = crc;
            }
            return table;
        }

        private sealed class IdatStream(Stream output) : Stream
        {
            private readonly byte[] _buffer = new byte[64 * 1024];
            private int _count;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { if (_count > 0) FlushChunk(); }
            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> data)
            {
                while (!data.IsEmpty)
                {
                    int count = Math.Min(data.Length, _buffer.Length - _count);
                    data[..count].CopyTo(_buffer.AsSpan(_count));
                    _count += count;
                    data = data[count..];
                    if (_count == _buffer.Length) FlushChunk();
                }
            }
            public void Finish() => Flush();
            private void FlushChunk()
            {
                WriteChunk(output, "IDAT"u8, _buffer.AsSpan(0, _count));
                _count = 0;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
