using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Tessera.Desktop;

// TESSERA_LEGACY Extract original PNGs from the retired tile inspector's v1 .ttiles archive.
internal static class LegacyTileArchive
{
    private const string Extension = ".ttiles";
    private sealed record Manifest(int Version, AssetEntry[] Assets);
    private sealed record AssetEntry(string? Name, string Image);

    public static bool IsArchive(string path) => string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    public static int Extract(string path, string destination)
    {
        using var archive = ZipFile.OpenRead(path);
        using var manifestStream = (archive.GetEntry("collection.json") ?? throw new InvalidDataException(Localization.Text("CollectionDescriptionMissing"))).Open();
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream);
        if (manifest is not { Version: 1, Assets: not null })
            throw new InvalidDataException(Localization.Text("UnknownProjectVersion"));
        if (manifest.Assets.Length == 0) return 0;

        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            for (int q = 0; q < manifest.Assets.Length; q++)
            {
                var item = manifest.Assets[q];
                using var input = (archive.GetEntry(item.Image) ?? throw new InvalidDataException(Localization.Text("TileImageNotFound"))).Open();
                string name = FileName(item.Name);
                string fileName = name.Length == 0 ? $"{q + 1:D4}.png" : $"{q + 1:D4} {name}.png";
                using var output = new FileStream(Path.Combine(staging, fileName), FileMode.CreateNew);
                input.CopyTo(output);
            }
            Directory.Move(staging, destination);
            return manifest.Assets.Length;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static string FileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).Take(80).ToArray());
        return value.Trim().TrimEnd('.');
    }
}
