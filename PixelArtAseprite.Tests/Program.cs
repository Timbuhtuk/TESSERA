using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelArtAseprite;
using PixelArtAseprite.Tests;
using static PixelArtAseprite.Tests.Fixtures;

int passed = 0, failed = 0;
if (args.Length != 0 && args is not ["--samples", _])
{
    Console.Error.WriteLine("Usage: PixelArtAseprite.Tests [--samples DIRECTORY]");
    return 2;
}
Check("Raw/zlib preserve every alpha value, hidden RGB and PNG pixels", () =>
{
    byte[] pixels = Enumerable.Range(0, 256).SelectMany(q => new byte[] { (byte)q, (byte)(255 - q), 73, (byte)q }).ToArray();
    foreach (int type in new[] { 0, 2 })
    {
        var document = Read(256, 1, [new(17, Layer(), Cel(256, 1, pixels, type: type))]);
        Require(document.RenderFrame(0).Rgba.Span.SequenceEqual(pixels), "RGBA changed");
        var sheet = SpriteSheet.Create(document);
        using var output = new MemoryStream();
        PngWriter.Write(output, sheet.Image);
        byte[] decoded = DecodePng(output.ToArray(), out int width, out int height, out bool srgb);
        Require(width == 256 && height == 1 && !srgb && decoded.SequenceEqual(pixels), "PNG roundtrip changed RGBA");
    }
});
Check("Negative/off-canvas cels, blank frames and links retain their own coordinates", () =>
{
    byte[] pixels = [11, 12, 13, 150, 21, 22, 23, 181, 31, 32, 33, 206, 41, 42, 43, 216];
    var document = Read(4, 3, [new(30, Layer(), Cel(2, 2, pixels, -1, 0)), new(40, Link(0, 2, 1)),
        new(50, Link(1, 0, 1)), new(60), new(70, Link(0, 20, -5))]);
    byte[] expected = new byte[4 * 3 * 4];
    pixels.AsSpan(4, 4).CopyTo(expected.AsSpan(0)); pixels.AsSpan(12, 4).CopyTo(expected.AsSpan(16));
    Require(document.RenderFrame(0).Rgba.Span.SequenceEqual(expected), "Negative offset clipped incorrectly");
    Array.Clear(expected); pixels.AsSpan(0, 8).CopyTo(expected.AsSpan(24)); pixels.AsSpan(8, 8).CopyTo(expected.AsSpan(40));
    Require(document.RenderFrame(1).Rgba.Span.SequenceEqual(expected), "Link reused previous frame position");
    Array.Clear(expected); pixels.AsSpan(0, 8).CopyTo(expected.AsSpan(16)); pixels.AsSpan(8, 8).CopyTo(expected.AsSpan(32));
    Require(document.RenderFrame(2).Rgba.Span.SequenceEqual(expected), "Linked chain failed");
    Require(document.RenderFrame(3).Rgba.ToArray().All(b => b == 0) && document.RenderFrame(4).Rgba.ToArray().All(b => b == 0), "Empty/off-canvas frame inherited pixels");
    var forward = Read(2, 2, [new(1, Layer(), Link(2)), new(1), new(1, Cel(2, 2, pixels))]);
    Require(forward.RenderFrame(0).Rgba.Span.SequenceEqual(pixels), "Forward link failed");
    var longChain = Enumerable.Range(0, 4000).Select(q => new Frame(1, q == 3999 ? Cel(1, 1, [1, 2, 3, 255]) : Link(q + 1))).ToArray();
    longChain[0] = new Frame(1, Layer(), Link(1));
    Require(Read(1, 1, longChain).RenderFrame(0).Rgba.Span.SequenceEqual(new byte[] { 1, 2, 3, 255 }), "Long chain failed");
});
Check("All layouts, padding, incomplete rows, durations, UTF-8 tags and sRGB", () =>
{
    var frames = Enumerable.Range(0, 5).Select(q => new Frame(q * 10, Cel(2, 2, Enumerable.Repeat((byte)(q + 1), 16).ToArray()))).ToArray();
    frames[0] = new Frame(0, Layer(uuid: true), Profile(), Tags(1, 4, 3, 0), frames[0].Chunks[0]);
    byte[] bytes = Fixtures.File(3, 2, frames, flags: 5, speed: 80, newCount: false, pixelWidth: 0, pixelHeight: 2);
    var document = AsepriteReader.Read(bytes, "прыжок.aseprite");
    Require(document.TotalDurationMs == 180 && document.LayerName == "Слой 🦊", "Timing/UTF-8 changed");
    Require(document.Tags.Single() == new AsepriteTag("Бег 🦊", 1, 4, "pingpong_reverse", 0), "Tag changed");
    foreach (var layout in Enum.GetValues<SpriteSheetLayout>())
    {
        var sheet = SpriteSheet.Create(document, new() { Layout = layout, Columns = 3, Padding = 2 });
        int columns = layout == SpriteSheetLayout.Horizontal ? 5 : layout == SpriteSheetLayout.Vertical ? 1 : 3;
        int rows = (5 + columns - 1) / columns;
        Require(sheet.Image.Width == columns * 3 + (columns - 1) * 2 && sheet.Image.Height == rows * 2 + (rows - 1) * 2, "Layout dimensions wrong");
        byte[] expected = new byte[sheet.Image.Rgba.Length];
        for (int q = 0; q < 5; q++)
        {
            var cell = sheet.Metadata.Frames[q];
            Require(cell.X == q % columns * 5 && cell.Y == q / columns * 4, "Frame metadata position wrong");
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    expected.AsSpan(((cell.Y + y) * sheet.Image.Width + cell.X + x) * 4, 4).Fill((byte)(q + 1));
        }
        Require(sheet.Image.Rgba.Span.SequenceEqual(expected), "Pixels leaked to padding/unused cells");
        using var png = new MemoryStream(); PngWriter.Write(png, sheet.Image, document.IsSrgb);
        Require(DecodePng(png.ToArray(), out _, out _, out bool srgb).SequenceEqual(expected) && srgb, "sRGB PNG mismatch");
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(sheet.Metadata, AsepriteConverter.JsonOptions));
        var root = json.RootElement;
        Require(root.GetProperty("schema").GetString() == "aseprite-offline/v1" && root.GetProperty("sourceSha256").GetString() == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Metadata schema/hash changed");
        Require(root.GetProperty("frameSize").GetProperty("w").GetInt32() == 3 && root.GetProperty("layout").GetString() == layout.ToString().ToLowerInvariant(), "Metadata field names changed");
    }
    var oversizedColumns = SpriteSheet.Create(document, new() { Layout = SpriteSheetLayout.Grid, Columns = int.MaxValue });
    Require(oversizedColumns.Metadata.Columns == 5, "Column count not clamped to frame count");
});
Check("Stored, fixed and dynamic DEFLATE; corruption, truncation, tails and concatenation", () =>
{
    var random = new Random(1729);
    foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
        foreach (int size in new[] { 4, 1024, 65536 })
        {
            byte[] pixels = new byte[size]; random.NextBytes(pixels);
            if (level != CompressionLevel.NoCompression)
                for (int q = 0; q < pixels.Length; q++) pixels[q] %= 5;
            byte[] compressed = Zlib(pixels, level);
            var document = Read(size / 4, 1, [new(1, Layer(), Cel(size / 4, 1, pixels, compressed: compressed))]);
            Require(document.RenderFrame(0).Rgba.Span.SequenceEqual(pixels), "Valid zlib rejected or pixels changed");
        }
    byte[] rgba = [1, 2, 3, 4];
    byte[] valid = Zlib(rgba);
    for (int q = 0; q < valid.Length; q++) BadZlib(valid[..q]);
    byte[] corrupt = (byte[])valid.Clone(); corrupt[^1] ^= 1; BadZlib(corrupt);
    corrupt = (byte[])valid.Clone(); corrupt[0] = 0; BadZlib(corrupt);
    BadZlib(valid.Concat(new byte[] { 0 }).ToArray());
    BadZlib(valid.Concat(valid).ToArray());
    BadZlib(Zlib([1, 2, 3])); BadZlib(Zlib([1, 2, 3, 4, 5]));
    // Поток, который выдал все RGBA, но не имеет финального блока/полного трейлера.
    byte[] stored = Zlib(rgba, CompressionLevel.NoCompression);
    for (int q = 1; q <= 5; q++) BadZlib(stored[..^q]);
    byte[] missingFinal = (byte[])stored.Clone(); missingFinal[2] &= 0xFE; BadZlib(missingFinal);
    BadZlib([0x78, 0x9C, 0x07, 0, 0, 0, 0]); // Reserved BTYPE=3.
    BadZlib(valid[..^4].Concat(new byte[] { 0, 0 }).Concat(valid[^4..]).ToArray());
    void BadZlib(byte[] bytes) => Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer(), Cel(1, 1, rgba, compressed: bytes))]));
});
Check("Malformed sizes/signatures/counts, raw lengths and linked cycles are rejected", () =>
{
    byte[] valid = Fixtures.File(1, 1, [new(1, Layer(), Cel(1, 1, [1, 2, 3, 4], type: 0))]);
    foreach (int offset in new[] { 0, 4, 6, 8, 10, 12, 128, 132, 140, 144 })
    {
        byte[] bad = (byte[])valid.Clone(); bad[offset] = 0; bad[offset + 1] = 0;
        Expect<InvalidDataException>(() => AsepriteReader.Read(bad));
    }
    for (int q = 0; q < valid.Length; q++) Expect<InvalidDataException>(() => AsepriteReader.Read(valid.AsMemory(0, q)));
    byte[] trailing = valid.Concat(new byte[] { 0 }).ToArray(); BinaryPrimitives.WriteInt32LittleEndian(trailing, trailing.Length);
    Expect<InvalidDataException>(() => AsepriteReader.Read(trailing));
    byte[] frameTail = (byte[])trailing.Clone(); BinaryPrimitives.WriteInt32LittleEndian(frameTail.AsSpan(128), frameTail.Length - 128);
    Expect<InvalidDataException>(() => AsepriteReader.Read(frameTail));
    foreach (var pixels in new[] { new byte[3], new byte[5] })
        Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer(), Cel(1, 1, pixels, type: 0))]));
    foreach (Frame[] frames in new Frame[][] { [new(1, Layer(), Link(0))], [new(1, Layer(), Link(1)), new(1, Link(0))],
        [new(1, Layer(), Link(20))], [new(1, Layer(), Link(1)), new(1)] })
        Expect<InvalidDataException>(() => Read(1, 1, frames));
});
Check("Unsupported layers, modes, profiles, chunks and frame timings fail explicitly", () =>
{
    foreach (byte[] layer in new[] { Layer(flags: 0), Layer(flags: 9), Layer(flags: 65), Layer(flags: 129), Layer(type: 1),
        Layer(type: 2), Layer(level: 1), Layer(blend: 1), Layer(opacity: 180) })
        Expect<InvalidDataException>(() => Read(1, 1, [new(1, layer)]));
    Require(AsepriteReader.Read(Fixtures.File(1, 1, [new(1, Layer(opacity: 0))], flags: 0)).Frames.Count == 1, "Reserved opacity interpreted as transparency");
    Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer(), Layer())]));
    Expect<InvalidDataException>(() => Read(1, 1, [new(1)]));
    Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer()), new(1, Layer())]));
    foreach (int depth in new[] { 8, 16 }) Expect<InvalidDataException>(() => AsepriteReader.Read(Fixtures.File(1, 1, [new(1, Layer())], depth: depth)));
    Expect<InvalidDataException>(() => AsepriteReader.Read(Fixtures.File(1, 1, [new(1, Layer())], flags: 8)));
    Expect<InvalidDataException>(() => AsepriteReader.Read(Fixtures.File(1, 1, [new(0, Layer())], speed: 0)));
    Expect<InvalidDataException>(() => AsepriteReader.Read(Fixtures.File(1, 1, [new(1, Layer())], pixelWidth: 2)));
    foreach (var chunk in new[] { Profile(2), Profile(1, 1), Profile(3), Tags(1, 0), Tags(0, 1), Tags(0, 0, 4),
        Cel(1, 1, [1, 2, 3, 4], opacity: 180), Cel(1, 1, [1, 2, 3, 4], z: 1), Cel(1, 1, [1, 2, 3, 4], layer: 1), Cel(1, 1, [1, 2, 3, 4], type: 3) })
        Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer(), chunk)]));
    foreach (ushort type in new ushort[] { 0x2006, 0x2008, 0x2022, 0x2023, 0x7777 })
        Expect<InvalidDataException>(() => Read(1, 1, [new(1, Layer(), Chunk(type, []))]));
    var skipped = Read(1, 1, [new(1, Layer(), Chunk(0x2020, []), Chunk(0x2019, []))]);
    Require(skipped.Warnings.Count == 2, "Skipped metadata not reported");
});
Check("Limits are checked before allocation; cancellation leaves inputs unchanged", () =>
{
    byte[] valid = Fixtures.File(2, 2, [new(1, Layer(), Cel(2, 2, new byte[16]))]);
    foreach (var limits in new[] { new AsepriteLimits { MaxInputBytes = 128 }, new() { MaxFramePixels = 1 }, new() { MaxCelBytes = 4 }, new() { MaxWorkingBytes = 32 } })
        Expect<InvalidDataException>(() => AsepriteReader.Read(valid, limits: limits));
    var document = AsepriteReader.Read(valid, limits: new() { MaxSheetSide = 3, MaxSheetPixels = 4 });
    byte[] twoFrames = Fixtures.File(2, 2, [new(1, Layer()), new(1)]);
    Expect<InvalidDataException>(() => SpriteSheet.Create(AsepriteReader.Read(twoFrames, limits: new() { MaxSheetSide = 3 })));
    Expect<InvalidDataException>(() => SpriteSheet.Create(AsepriteReader.Read(twoFrames, limits: new() { MaxSheetPixels = 4 })));
    var smallBudget = AsepriteReader.Read(Fixtures.File(1024, 1024, [new(1, Layer())]), limits: new() { MaxWorkingBytes = 3 * 1024 * 1024 });
    Expect<InvalidDataException>(() => SpriteSheet.Create(smallBudget));
    Expect<InvalidDataException>(() => smallBudget.RenderFrame(0));
    Expect<InvalidDataException>(() => AsepriteReader.Read(Fixtures.File(65535, 65535, [new(1, Layer())])));
    Expect<InvalidDataException>(() => SpriteSheet.Create(Read(2, 2, [new(1, Layer()), new(1)]), new() { Padding = int.MaxValue }));
    Expect<ArgumentOutOfRangeException>(() => SpriteSheet.Create(document, new() { Columns = 0 }));
    Expect<ArgumentOutOfRangeException>(() => SpriteSheet.Create(document, new() { Padding = -1 }));
    Expect<ArgumentOutOfRangeException>(() => SpriteSheet.Create(document, new() { Layout = (SpriteSheetLayout)99 }));
    Expect<OperationCanceledException>(() => AsepriteReader.Read(valid, cancellationToken: new(true)));
    Expect<OperationCanceledException>(() => SpriteSheet.Create(document, cancellationToken: new(true)));
    Expect<OperationCanceledException>(() => document.RenderFrame(0, new(true)));
    using var output = new MemoryStream();
    Expect<OperationCanceledException>(() => PngWriter.Write(output, document.RenderFrame(0), cancellationToken: new(true)));
    Require(output.Length == 0, "Cancelled PNG wrote data");
});
Check("Export publishes PNG+JSON+inspection safely and batch continues after failures", () =>
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Tessera-Aseprite-Tests"));
    string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        byte[] input = Fixtures.File(1, 1, [new(75, Layer(), Cel(1, 1, [12, 34, 56, 150]))]);
        string path = Path.Combine(directory, "герой.aseprite"); System.IO.File.WriteAllBytes(path, input);
        string target = Path.Combine(directory, "out");
        var result = AsepriteConverter.Convert(path, target, new() { IncludeInspection = true });
        Require(System.IO.File.Exists(result.ImagePath) && System.IO.File.Exists(result.JsonPath) && System.IO.File.Exists(result.InspectionPath), "Incomplete export");
        Require(DecodePng(System.IO.File.ReadAllBytes(result.ImagePath), out _, out _, out _).SequenceEqual(new byte[] { 12, 34, 56, 150 }), "File PNG mismatch");
        byte[] saved = System.IO.File.ReadAllBytes(result.ImagePath);
        Expect<IOException>(() => AsepriteConverter.Convert(path, target));
        Require(System.IO.File.ReadAllBytes(result.ImagePath).SequenceEqual(saved) && System.IO.File.ReadAllBytes(path).SequenceEqual(input), "Existing files changed");
        string conflict = Path.Combine(directory, "conflict"); Directory.CreateDirectory(conflict);
        System.IO.File.WriteAllText(Path.Combine(conflict, "герой.json"), "keep");
        Expect<IOException>(() => AsepriteConverter.Convert(path, conflict));
        Require(!System.IO.File.Exists(Path.Combine(conflict, "герой.png")), "Conflict left orphan PNG");
        string blocked = Path.Combine(directory, "blocked"); System.IO.File.WriteAllText(blocked, "keep");
        Expect<IOException>(() => AsepriteConverter.Convert(path, blocked));
        Require(System.IO.File.ReadAllText(blocked) == "keep", "Output directory failure damaged a file");
        string bad = Path.Combine(directory, "bad.ase"); System.IO.File.WriteAllText(bad, "invalid");
        var batch = AsepriteConverter.ConvertBatch([bad, path, path], Path.Combine(directory, "batch"));
        Require(batch.Count == 3 && !batch[0].Success && batch[1].Success && !batch[2].Success, "Batch stopped or overwrote duplicate output");
        Expect<OperationCanceledException>(() => AsepriteConverter.Convert(path, Path.Combine(directory, "cancel"), cancellationToken: new(true)));
        Require(!Directory.Exists(Path.Combine(directory, "cancel")) && !Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any(), "Cancelled/failed export leaked files");
    }
    finally { if (Path.GetDirectoryName(Path.GetFullPath(directory)) == root) Directory.Delete(directory, true); }
});

if (args is ["--samples", var sampleDirectory]) Check("All ten real Tamara documents: horizontal and padded grid exports", () =>
{
    (string Name, int Frames)[] samples = [("TamaraAura", 9), ("TamaraAuraTrans", 4), ("TamaraClimb", 6), ("TamaraDeath", 6),
        ("TamaraHurt", 7), ("TamaraIdle", 8), ("TamaraJump", 12), ("TamaraPunch", 9), ("TamaraSitting", 12), ("TamaraWalk1", 10)];
    string directory = Path.GetFullPath(Path.Combine("artifacts", "aseprite", "validation-" + Guid.NewGuid().ToString("N")));
    foreach (var sample in samples)
    {
        string path = Path.Combine(sampleDirectory, sample.Name + ".aseprite");
        var document = AsepriteReader.Read(path);
        Require(document.Width == 64 && document.Height == 64 && document.Frames.Count == sample.Frames && document.TotalDurationMs == sample.Frames * 100, $"Unexpected sample header: {sample.Name}");
        if (sample.Name is "TamaraDeath" or "TamaraHurt")
        {
            int[] alpha = document.Frames.SelectMany(f => document.RenderFrame(f.Index).Rgba.ToArray().Where((_, q) => q % 4 == 3)).Select(b => (int)b).Distinct().Order().ToArray();
            Require(alpha.SequenceEqual(sample.Name == "TamaraDeath" ? new[] { 0, 150, 181, 255 } : new[] { 0, 181, 206, 216, 255 }), "Sample alpha changed");
        }
        foreach (var layout in new[] { SpriteSheetLayout.Horizontal, SpriteSheetLayout.Grid })
        {
            var result = AsepriteConverter.Convert(path, Path.Combine(directory, layout.ToString().ToLowerInvariant()), new()
            {
                Layout = layout, Columns = 4, Padding = layout == SpriteSheetLayout.Grid ? 2 : 0, IncludeInspection = true
            });
            byte[] actual = DecodePng(System.IO.File.ReadAllBytes(result.ImagePath), out int width, out int height, out _);
            var sheet = SpriteSheet.Create(document, new() { Layout = layout, Columns = 4, Padding = layout == SpriteSheetLayout.Grid ? 2 : 0 });
            Require(width == sheet.Image.Width && height == sheet.Image.Height && actual.AsSpan().SequenceEqual(sheet.Image.Rgba.Span), "Sample PNG roundtrip failed");
        }
        Console.WriteLine($"  {sample.Name}: {sample.Frames} frames, {document.TotalDurationMs}ms");
    }
    Console.WriteLine($"SAMPLE_OUTPUT={directory}");
});
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

AsepriteDocument Read(int width, int height, Frame[] frames) => AsepriteReader.Read(Fixtures.File(width, height, frames));
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Expect<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

// Независимое чтение PNG: проверки CRC побитовым алгоритмом, один zlib-поток и фильтр None.
byte[] DecodePng(byte[] bytes, out int width, out int height, out bool srgb)
{
    Require(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Bad PNG signature");
    width = height = 0; srgb = false;
    using var compressed = new MemoryStream();
    int position = 8;
    while (position < bytes.Length)
    {
        int count = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position));
        string type = Encoding.ASCII.GetString(bytes, position + 4, 4);
        uint crc = uint.MaxValue;
        for (int q = position + 4; q < position + 8 + count; q++)
        {
            crc ^= bytes[q];
            for (int e = 0; e < 8; e++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        Require((crc ^ uint.MaxValue) == BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + count)), "PNG CRC mismatch");
        if (type == "IHDR")
        {
            width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position + 8)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position + 12));
            Require(bytes[position + 16] == 8 && bytes[position + 17] == 6, "PNG is not RGBA8");
        }
        else if (type == "sRGB") srgb = true;
        else if (type == "IDAT") compressed.Write(bytes, position + 8, count);
        else if (type == "IEND") Require(position + 12 == bytes.Length && count == 0, "Bad IEND");
        position += count + 12;
    }
    compressed.Position = 0;
    using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++) { Require(zlib.ReadByte() == 0, "Unexpected PNG filter"); zlib.ReadExactly(pixels.AsSpan(y * width * 4, width * 4)); }
    Require(zlib.ReadByte() == -1, "PNG has surplus pixels");
    return pixels;
}
