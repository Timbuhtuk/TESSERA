using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PixelArtAseprite;

public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Записывает straight RGBA PNG без изменения цвета и альфы. Поток остаётся открытым.</summary>
    public static void Write(Stream output, RgbaImage image, bool isSrgb = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(image);
        if (!output.CanWrite) throw new ArgumentException("Поток не допускает запись.", nameof(output));
        if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height * 4 != image.Rgba.Length)
            throw new ArgumentException("Размер RGBA-буфера не совпадает.", nameof(image));
        cancellationToken.ThrowIfCancellationRequested();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8; header[9] = 6;
        WriteChunk(output, "IHDR", header);
        if (isSrgb) WriteChunk(output, "sRGB", new byte[] { 0 });
        using (var idat = new IdatStream(output, cancellationToken))
        {
            using var zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true);
            int stride = checked(image.Width * 4);
            for (int y = 0; y < image.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zlib.WriteByte(0);
                zlib.Write(image.Rgba.Span.Slice(y * stride, stride));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in header[4..]) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (byte value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ uint.MaxValue);
        output.Write(checksum);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint q = 0; q < table.Length; q++)
        {
            uint value = q;
            for (int e = 0; e < 8; e++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xEDB88320u);
            table[q] = value;
        }
        return table;
    }

    // Все IDAT принадлежат одному потоку zlib. Размер буфера не зависит от площади PNG.
    private sealed class IdatStream(Stream output, CancellationToken token) : Stream
    {
        private readonly byte[] _buffer = new byte[65536];
        private int _count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(buffer.Length, _buffer.Length - _count);
                buffer[..count].CopyTo(_buffer.AsSpan(_count));
                _count += count;
                buffer = buffer[count..];
                if (_count == _buffer.Length) Flush();
            }
        }
        public override void Flush()
        {
            if (_count == 0) return;
            token.ThrowIfCancellationRequested();
            WriteChunk(output, "IDAT", _buffer.AsSpan(0, _count));
            _count = 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) Flush(); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
