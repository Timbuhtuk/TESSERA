using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Tessera.Desktop;

internal sealed record AtlasDraft(TileSetLayout Layout, Dictionary<TileSetRegion, PixelEditBuffer> Edits);

internal sealed class AtlasDraftStore : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Tessera", "AtlasDrafts", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<(Guid Source, Guid? Parent), TileSetLayout> _layouts = [];
    public bool HasDrafts => _layouts.Count != 0;

    public void Save((Guid Source, Guid? Parent) basis, AtlasDraft draft)
    {
        LibraryJson.WriteFile(PathFor(basis), file =>
        {
            using var compressed = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true);
            using var writer = new BinaryWriter(compressed);
            writer.Write(JsonSerializer.Serialize(draft.Layout));
            writer.Write(draft.Edits.Count);
            foreach (var (region, pixels) in draft.Edits)
            {
                writer.Write(region.Column);
                writer.Write(region.Row);
                pixels.WriteDraft(writer);
            }
        });
        _layouts[basis] = draft.Layout;
    }

    public AtlasDraft? Load((Guid Source, Guid? Parent) basis)
    {
        if (!_layouts.TryGetValue(basis, out var layout)) return null;
        using var file = File.OpenRead(PathFor(basis));
        using var compressed = new DeflateStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed);
        if (JsonSerializer.Deserialize<TileSetLayout>(reader.ReadString()) != layout) throw new InvalidDataException("Invalid atlas draft layout.");
        int count = reader.ReadInt32();
        if (count < 0 || count > layout.Count) throw new InvalidDataException("Invalid atlas draft tile count.");
        var edits = new Dictionary<TileSetRegion, PixelEditBuffer>(count);
        for (int q = 0; q < count; q++)
        {
            int column = reader.ReadInt32(), row = reader.ReadInt32();
            if ((uint)column >= (uint)layout.Columns || (uint)row >= (uint)layout.Rows)
                throw new InvalidDataException("Invalid atlas draft tile position.");
            var region = new TileSetRegion(layout.MarginX + column * (layout.TileWidth + layout.SpacingX),
                layout.MarginY + row * (layout.TileHeight + layout.SpacingY), layout.TileWidth, layout.TileHeight, column, row);
            edits.Add(region, PixelEditBuffer.ReadDraft(reader, region.Width, region.Height));
        }
        return new AtlasDraft(layout, edits);
    }

    public void Delete((Guid Source, Guid? Parent) basis)
    {
        if (!_layouts.ContainsKey(basis)) return;
        File.Delete(PathFor(basis));
        _layouts.Remove(basis);
    }

    private string PathFor((Guid Source, Guid? Parent) basis) => Path.Combine(_directory, $"{basis.Source:N}-{basis.Parent:N}.draft");

    public void Dispose()
    {
        _layouts.Clear();
        try
        {
            if (Directory.Exists(_directory) && (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
