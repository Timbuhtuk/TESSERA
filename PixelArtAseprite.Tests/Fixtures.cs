using System.IO.Compression;
using System.Text;

namespace PixelArtAseprite.Tests;

internal static class Fixtures
{
    internal sealed record Frame(int Duration, params byte[][] Chunks);
    public static byte[] File(int width, int height, Frame[] frames, uint flags = 1, int speed = 100, int depth = 32,
        int pixelWidth = 1, int pixelHeight = 1, bool newCount = true)
    {
        return Bytes(writer =>
        {
            writer.Write(new byte[128]);
            foreach (var frame in frames)
            {
                writer.Write(16 + frame.Chunks.Sum(c => c.Length)); writer.Write((ushort)0xF1FA);
                writer.Write((ushort)(newCount ? 0 : frame.Chunks.Length)); writer.Write((ushort)frame.Duration);
                writer.Write((ushort)0); writer.Write(newCount ? (uint)frame.Chunks.Length : 0);
                foreach (var chunk in frame.Chunks) writer.Write(chunk);
            }
            long size = writer.BaseStream.Length;
            writer.BaseStream.Position = 0;
            writer.Write((uint)size); writer.Write((ushort)0xA5E0); writer.Write((ushort)frames.Length);
            writer.Write((ushort)width); writer.Write((ushort)height); writer.Write((ushort)depth);
            writer.Write(flags); writer.Write((ushort)speed);
            writer.BaseStream.Position = 34; writer.Write((byte)pixelWidth); writer.Write((byte)pixelHeight);
        });
    }

    public static byte[] Layer(int flags = 3, int type = 0, int level = 0, int blend = 0, int opacity = 255, bool uuid = false)
        => Chunk(0x2004, Bytes(w =>
        {
            w.Write((ushort)flags); w.Write((ushort)type); w.Write((ushort)level); w.Write(0);
            w.Write((ushort)blend); w.Write((byte)opacity); w.Write(new byte[3]); String(w, "Слой 🦊");
            if (uuid) w.Write(new byte[16]);
        }));

    public static byte[] Cel(int width, int height, byte[] pixels, short x = 0, short y = 0, int type = 2,
        int opacity = 255, short z = 0, int layer = 0, byte[]? compressed = null, CompressionLevel level = CompressionLevel.Optimal)
        => Chunk(0x2005, Bytes(w =>
        {
            CelHeader(w, x, y, type, opacity, z, layer);
            w.Write((ushort)width); w.Write((ushort)height);
            w.Write(type == 0 ? pixels : compressed ?? Zlib(pixels, level));
        }));
    public static byte[] Link(int frame, short x = 0, short y = 0)
        => Chunk(0x2005, Bytes(w => { CelHeader(w, x, y, 1, 255, 0, 0); w.Write((ushort)frame); }));
    public static byte[] Profile(int type = 1, int flags = 0)
        => Chunk(0x2007, Bytes(w => { w.Write((ushort)type); w.Write((ushort)flags); w.Write(new byte[12]); }));
    public static byte[] Tags(int from, int to, int direction = 0, int repeat = 0)
        => Chunk(0x2018, Bytes(w =>
        {
            w.Write((ushort)1); w.Write(new byte[8]); w.Write((ushort)from); w.Write((ushort)to);
            w.Write((byte)direction); w.Write((ushort)repeat); w.Write(new byte[10]); String(w, "Бег 🦊");
        }));
    public static byte[] Chunk(ushort type, byte[] data) => Bytes(w => { w.Write(data.Length + 6); w.Write(type); w.Write(data); });
    public static byte[] Zlib(byte[] pixels, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, level, leaveOpen: true)) zlib.Write(pixels);
        return output.ToArray();
    }
    private static void CelHeader(BinaryWriter w, short x, short y, int type, int opacity, short z, int layer)
    {
        w.Write((ushort)layer); w.Write(x); w.Write(y); w.Write((byte)opacity); w.Write((ushort)type); w.Write(z); w.Write(new byte[5]);
    }
    private static void String(BinaryWriter w, string value) { byte[] data = Encoding.UTF8.GetBytes(value); w.Write((ushort)data.Length); w.Write(data); }
    public static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true)) write(writer);
        return output.ToArray();
    }
}
