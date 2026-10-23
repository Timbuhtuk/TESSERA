using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace PixelArtDownscale;

public static class MaterialMapFile
{
    private sealed record Header(int Version, int Width, int Height, float NormalStrength, float AoStrength, int AoRadius, bool DirectX);

    public static void Save(MaterialMapDocument document, string path, bool markSaved = true)
    {
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var header = archive.CreateEntry("document.json").Open())
                    JsonSerializer.Serialize(header, new Header(1, document.Width, document.Height, document.NormalStrength,
                        document.AoStrength, document.AoRadius, document.DirectX));
                using var writer = new BinaryWriter(archive.CreateEntry("layers.bin", CompressionLevel.Fastest).Open());
                writer.Write(document.Source);
                for (int q = 0; q < document.Heights.Length; q++)
                {
                    writer.Write(document.Heights[q]);
                    writer.Write(document.NormalLayer[q].X); writer.Write(document.NormalLayer[q].Y); writer.Write(document.NormalLayer[q].Z);
                    writer.Write(document.NormalMask[q]); writer.Write(document.AoLayer[q]); writer.Write(document.AoMask[q]);
                }
            }
            File.Move(temporary, fullPath, true);
            if (markSaved) document.MarkSaved();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static MaterialMapDocument Load(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var headerEntry = archive.GetEntry("document.json") ?? throw new InvalidDataException("Missing material header.");
        if (headerEntry.Length > 4096) throw new InvalidDataException("Material header is too large.");
        Header header;
        using (var stream = headerEntry.Open()) header = JsonSerializer.Deserialize<Header>(stream) ?? throw new InvalidDataException("Invalid material header.");
        if (header.Version != 1 || header.Width <= 0 || header.Height <= 0) throw new InvalidDataException("Unsupported material document.");
        int count = checked(header.Width * header.Height);
        var entry = archive.GetEntry("layers.bin") ?? throw new InvalidDataException("Missing material layers.");
        if (entry.Length != (long)count * 32) throw new InvalidDataException("Material layer size does not match its dimensions.");
        using var reader = new BinaryReader(entry.Open());
        byte[] pixels = reader.ReadBytes(checked(count * 4));
        if (pixels.Length != count * 4) throw new InvalidDataException("Truncated material image.");
        var document = new MaterialMapDocument(header.Width, header.Height, pixels);
        float[] heights = new float[count], normalMask = new float[count], ao = new float[count], aoMask = new float[count];
        var normals = new Vector3[count];
        for (int q = 0; q < count; q++)
        {
            heights[q] = Unit(reader.ReadSingle());
            normals[q] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            normalMask[q] = Unit(reader.ReadSingle()); ao[q] = Unit(reader.ReadSingle()); aoMask[q] = Unit(reader.ReadSingle());
            float length = normals[q].LengthSquared();
            if (!float.IsFinite(length) || (normalMask[q] > 0 && (length < .99f || length > 1.01f)))
                throw new InvalidDataException("Invalid painted normal.");
        }
        document.Restore(new(heights, normals, normalMask, ao, aoMask, header.NormalStrength, header.AoStrength, header.AoRadius, header.DirectX));
        document.MarkSaved();
        return document;
    }

    private static float Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1
        ? value : throw new InvalidDataException("Invalid material layer value.");
}
