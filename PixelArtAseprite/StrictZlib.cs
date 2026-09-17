using System.Buffers.Binary;
using System.IO.Compression;

namespace PixelArtAseprite;

// ZLibStream распаковывает пиксели. Отдельный проход проверяет BFINAL, точную границу
// DEFLATE и ссылки на уже выданные байты; Adler-32 проверяется по готовому RGBA.
internal static class StrictZlib
{
    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly int[] LengthBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly int[] DistanceBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly int[] CodeOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
    private static readonly Huffman FixedLiteral = new(Enumerable.Range(0, 288).Select(q => q < 144 ? 8 : q < 256 ? 9 : q < 280 ? 7 : 8).ToArray());
    private static readonly Huffman FixedDistance = new(Enumerable.Repeat(5, 32).ToArray());

    public static byte[] Inflate(ReadOnlySpan<byte> compressed, int expected, CancellationToken token)
    {
        if (compressed.Length < 6) throw new InvalidDataException("Оборванная оболочка zlib.");
        int cmf = compressed[0], flags = compressed[1];
        if ((cmf & 15) != 8 || (cmf >> 4) > 7 || ((cmf << 8) | flags) % 31 != 0 || (flags & 32) != 0)
            throw new InvalidDataException("Некорректный заголовок zlib или словарь не поддерживается.");
        ValidateDeflate(compressed[2..^4], expected, 1 << ((cmf >> 4) + 8), token);
        uint checksum = BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]);
        var pixels = new byte[expected];
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        try
        {
            for (int q = 0; q < pixels.Length; q += 65536)
            {
                token.ThrowIfCancellationRequested();
                zlib.ReadExactly(pixels.AsSpan(q, Math.Min(65536, pixels.Length - q)));
            }
            if (zlib.ReadByte() != -1) throw new InvalidDataException("Лишние RGBA-байты в cel.");
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Недостаточно RGBA-байтов в cel.", ex); }
        uint a = 1, b = 0;
        for (int q = 0; q < pixels.Length;)
        {
            token.ThrowIfCancellationRequested();
            int end = Math.Min(q + 5552, pixels.Length);
            for (; q < end; q++) { a += pixels[q]; b += a; }
            a %= 65521; b %= 65521;
        }
        if (((b << 16) | a) != checksum) throw new InvalidDataException("Неверная контрольная сумма Adler-32.");
        return pixels;
    }

    private static void ValidateDeflate(ReadOnlySpan<byte> data, int expected, int window, CancellationToken token)
    {
        var bits = new BitReader(data);
        long produced = 0;
        bool final;
        do
        {
            token.ThrowIfCancellationRequested();
            final = bits.Read(1) != 0;
            int type = bits.Read(2);
            if (type == 0)
            {
                bits.Align();
                int length = bits.Read(16), inverse = bits.Read(16);
                if ((length ^ inverse) != 65535) throw new InvalidDataException("Неверная длина блока DEFLATE.");
                bits.SkipBytes(length);
                produced += length;
            }
            else if (type is 1 or 2)
            {
                Huffman literal = FixedLiteral, distance = FixedDistance;
                if (type == 2) ReadTrees(ref bits, out literal, out distance);
                int operations = 0;
                while (true)
                {
                    if ((operations++ & 4095) == 0) token.ThrowIfCancellationRequested();
                    int symbol = literal.Decode(ref bits);
                    if (symbol == 256) break;
                    if (symbol < 256) produced++;
                    else
                    {
                        if (symbol > 285) throw new InvalidDataException("Неверный код длины DEFLATE.");
                        int length = LengthBase[symbol - 257] + bits.Read(LengthBits[symbol - 257]);
                        int code = distance.Decode(ref bits);
                        if (code >= 30) throw new InvalidDataException("Неверный код расстояния DEFLATE.");
                        int offset = DistanceBase[code] + bits.Read(DistanceBits[code]);
                        if (offset > produced || offset > window) throw new InvalidDataException("Ссылка DEFLATE выходит за окно распаковки.");
                        produced += length;
                    }
                    if (produced > expected) throw new InvalidDataException("Лишние RGBA-байты в cel.");
                }
            }
            else throw new InvalidDataException("Зарезервированный тип блока DEFLATE.");
            if (produced > expected) throw new InvalidDataException("Лишние RGBA-байты в cel.");
        } while (!final);
        bits.Align();
        if (!bits.AtEnd) throw new InvalidDataException("Хвост или склеенные потоки после DEFLATE.");
        if (produced != expected) throw new InvalidDataException("Число RGBA-байтов не совпадает с размером cel.");
    }

    private static void ReadTrees(ref BitReader bits, out Huffman literal, out Huffman distance)
    {
        int literalCount = bits.Read(5) + 257, distanceCount = bits.Read(5) + 1, codeCount = bits.Read(4) + 4;
        if (literalCount > 286) throw new InvalidDataException("Неверный размер алфавита DEFLATE.");
        var codeLengths = new int[19];
        for (int q = 0; q < codeCount; q++) codeLengths[CodeOrder[q]] = bits.Read(3);
        var codes = new Huffman(codeLengths, requireComplete: true);
        var lengths = new int[literalCount + distanceCount];
        for (int q = 0; q < lengths.Length;)
        {
            int code = codes.Decode(ref bits);
            if (code < 16) { lengths[q++] = code; continue; }
            if (code == 16 && q == 0) throw new InvalidDataException("Повтор длины без предыдущего кода.");
            int count = code == 16 ? bits.Read(2) + 3 : code == 17 ? bits.Read(3) + 3 : bits.Read(7) + 11;
            if (count > lengths.Length - q) throw new InvalidDataException("Повтор выходит за таблицу DEFLATE.");
            int value = code == 16 ? lengths[q - 1] : 0;
            for (int e = 0; e < count; e++) lengths[q++] = value;
        }
        if (lengths[256] == 0) throw new InvalidDataException("Нет кода конца блока DEFLATE.");
        literal = new Huffman(lengths[..literalCount]);
        distance = new Huffman(lengths[literalCount..], allowEmpty: true);
    }

    private sealed class Huffman
    {
        private readonly int[] _counts = new int[16], _firstCode = new int[16], _firstSymbol = new int[16];
        private readonly int[] _symbols;
        public Huffman(int[] lengths, bool requireComplete = false, bool allowEmpty = false)
        {
            foreach (int length in lengths)
            {
                if (length is < 0 or > 15) throw new InvalidDataException("Неверная длина кода Хаффмана.");
                if (length > 0) _counts[length]++;
            }
            int total = _counts.Sum(), left = 1, code = 0, position = 0;
            for (int length = 1; length <= 15; length++)
            {
                left = (left << 1) - _counts[length];
                if (left < 0) throw new InvalidDataException("Переполненное дерево Хаффмана.");
                code = (code + _counts[length - 1]) << 1;
                _firstCode[length] = code;
                _firstSymbol[length] = position;
                position += _counts[length];
            }
            if (left != 0 && !(allowEmpty && total == 0) && !(total == 1 && _counts[1] == 1 && !requireComplete))
                throw new InvalidDataException("Неполное дерево Хаффмана.");
            _symbols = new int[total];
            var next = (int[])_firstSymbol.Clone();
            for (int q = 0; q < lengths.Length; q++)
                if (lengths[q] > 0) _symbols[next[lengths[q]]++] = q;
        }
        public int Decode(ref BitReader bits)
        {
            int code = 0;
            for (int length = 1; length <= 15; length++)
            {
                code = (code << 1) | bits.Read(1);
                int offset = code - _firstCode[length];
                if ((uint)offset < (uint)_counts[length]) return _symbols[_firstSymbol[length] + offset];
            }
            throw new InvalidDataException("Неизвестный код Хаффмана.");
        }
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private long _position;
        public bool AtEnd => _position == _data.Length * 8L;
        public BitReader(ReadOnlySpan<byte> data) { _data = data; _position = 0; }
        public int Read(int count)
        {
            if (_position + count > _data.Length * 8L) throw new InvalidDataException("DEFLATE оборван до конца потока.");
            int value = 0;
            for (int q = 0; q < count; q++, _position++) value |= ((_data[(int)(_position >> 3)] >> (int)(_position & 7)) & 1) << q;
            return value;
        }
        public void Align() => _position = (_position + 7) & ~7L;
        public void SkipBytes(int count)
        {
            if (_position + count * 8L > _data.Length * 8L) throw new InvalidDataException("Несжатый блок DEFLATE оборван.");
            _position += count * 8L;
        }
    }
}
