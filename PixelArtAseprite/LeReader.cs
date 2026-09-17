using System.Buffers.Binary;
using System.Text;

namespace PixelArtAseprite;

internal ref struct LeReader
{
    private readonly ReadOnlySpan<byte> _data;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public int Position { get; private set; }
    public int Remaining => _data.Length - Position;
    public LeReader(ReadOnlySpan<byte> data) { _data = data; Position = 0; }
    public ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException("Блок данных оборван.");
        var bytes = _data.Slice(Position, count);
        Position += count;
        return bytes;
    }
    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public void Skip(int count) => Take(count);
    public string ReadString()
    {
        try { return Utf8.GetString(Take(U16())); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Некорректная строка UTF-8.", ex); }
    }
    public void RequireEnd()
    {
        if (Remaining != 0) throw new InvalidDataException("Лишние данные в блоке.");
    }
}
