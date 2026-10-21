using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DomainColorTest;

public sealed class TileCollectionSettings
{
    public Guid? Selected { get; set; }
    public Guid? Companion { get; set; }
    public bool RepeatX { get; set; } = true;
    public bool RepeatY { get; set; } = true;
    public int Copies { get; set; }
    public int Zoom { get; set; }
    public double? ZoomScale { get; set; }
    public bool Heat { get; set; } = true;
    public bool Guides { get; set; }
    public int Background { get; set; }
    public double Threshold { get; set; } = 5;
    public double Opacity { get; set; } = 70;
    public double Stripe { get; set; } = 1;
    public string Color { get; set; } = "#FFFFFFFF";
    public int BrushSize { get; set; }
    public int Passes { get; set; } = 1;
    public bool SmoothEdgesOnly { get; set; }
}

public static class TileCollectionFile
{
    private sealed record AssetEntry(Guid Id, string Name, string? SourcePath, bool Edited, string Image, string? AtlasSourcePath = null);
    private sealed record Manifest(int Version, TileCollectionSettings Settings, AssetEntry[] Assets, string[]? SourcePaths = null);

    public static void Save(string path, IReadOnlyList<TileAsset> assets, TileCollectionSettings settings) => Save(path, assets, settings, []);

    public static void Save(string path, IReadOnlyList<TileAsset> assets, TileCollectionSettings settings, IEnumerable<string>? sourcePaths)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".ttiles", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Localization.Text("Сохраните коллекцию в формате .ttiles."));
        var protectedPaths = (sourcePaths ?? []).Concat(assets.SelectMany(asset => new[] { asset.SourcePath, asset.AtlasSourcePath }).OfType<string>())
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (protectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            throw new IOException(Localization.Text("Проект не может заменить исходное изображение."));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var entries = new List<AssetEntry>();
                for (int q = 0; q < assets.Count; q++)
                {
                    var asset = assets[q];
                    string image = $"images/{q:D5}.png";
                    using (var stream = archive.CreateEntry(image, CompressionLevel.NoCompression).Open()) asset.WritePng(stream);
                    entries.Add(new(asset.Id, asset.Name, asset.SourcePath, asset.HasLocalEdits, image, asset.AtlasSourcePath));
                }
                using var manifest = archive.CreateEntry("collection.json").Open();
                JsonSerializer.Serialize(manifest, new Manifest(1, settings, entries.ToArray(), protectedPaths));
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static (List<TileAsset> Assets, TileCollectionSettings Settings) Load(string path)
    {
        var loaded = LoadWithSourcePaths(path);
        return (loaded.Assets, loaded.Settings);
    }

    internal static (List<TileAsset> Assets, TileCollectionSettings Settings, string[] SourcePaths) LoadWithSourcePaths(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var manifestStream = (archive.GetEntry("collection.json") ?? throw new InvalidDataException(Localization.Text("Нет описания коллекции."))).Open();
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream) ?? throw new InvalidDataException(Localization.Text("Пустой проект."));
        if (manifest.Version != 1 || manifest.Settings is null || manifest.Assets is null)
            throw new InvalidDataException(Localization.Text("Неизвестная версия проекта."));
        var assets = new List<TileAsset>();
        var ids = new HashSet<Guid>();
        foreach (var entry in manifest.Assets)
        {
            if (!ids.Add(entry.Id)) throw new InvalidDataException(Localization.Text("Повторяющийся идентификатор ассета."));
            using var stream = (archive.GetEntry(entry.Image) ?? throw new InvalidDataException(Localization.Text("Не найдено изображение тайла."))).Open();
            // Decode from memory: ZIP entry streams do not necessarily support seeking.
            using var imageStream = new MemoryStream(); stream.CopyTo(imageStream); imageStream.Position = 0;
            string? source = entry.SourcePath is null ? null : Path.GetFullPath(entry.SourcePath);
            string? atlas = entry.AtlasSourcePath is null ? null : Path.GetFullPath(entry.AtlasSourcePath);
            assets.Add(new TileAsset(TileAsset.Decode(imageStream), entry.Edited)
                { Id = entry.Id, Name = entry.Name ?? Localization.Text("Тайл"), SourcePath = source, AtlasSourcePath = atlas });
        }
        var sourcePaths = (manifest.SourcePaths ?? []).Concat(assets.SelectMany(asset => new[] { asset.SourcePath, asset.AtlasSourcePath }).OfType<string>())
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return (assets, manifest.Settings, sourcePaths);
    }
}
