using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tessera.Desktop;

internal sealed class WorkspaceStorage(string root)
{
    internal const int PageSize = 128;
    private const int CurrentVersion = 2;
    private readonly string _path = Path.Combine(root, "workspace.json");
    private readonly string _pages = Path.Combine(root, ".workspace-data");
    private Index? _saved;

    private sealed record ViewSettings(string View, Guid? ProjectId, Guid? FolderId, Guid? SelectedAsset,
        string Search, int Kind, string Width, string Height, int Transparency, int AssetView)
    {
        public static ViewSettings Capture(WorkspaceState state) => new(state.View, state.ProjectId, state.FolderId,
            state.SelectedAsset, state.Search, state.Kind, state.Width, state.Height, state.Transparency, state.AssetView);

        public void Apply(WorkspaceState state)
        {
            state.View = View;
            state.ProjectId = ProjectId;
            state.FolderId = FolderId;
            state.SelectedAsset = SelectedAsset;
            state.Search = Search;
            state.Kind = Kind;
            state.Width = Width;
            state.Height = Height;
            state.Transparency = Transparency;
            state.AssetView = AssetView;
        }
    }

    private sealed class Index
    {
        public int Version { get; init; }
        public Guid Revision { get; init; }
        public ViewSettings? Settings { get; init; }
        public string[] AssetPages { get; init; } = [];
        public string[] ProjectPages { get; init; } = [];
        public string[] FolderPages { get; init; } = [];
        public string[] LinkPages { get; init; } = [];
        public string[] MaterialSessionPages { get; init; } = [];

        public IEnumerable<string> Pages() => AssetPages.Concat(ProjectPages).Concat(FolderPages).Concat(LinkPages).Concat(MaterialSessionPages);
    }

    public WorkspaceState Load()
    {
        if (!File.Exists(_path)) return new();
        using var lease = OpenLock();
        Index? index = ReadIndex();
        _saved = index;
        if (index is null) return new();

        var state = new WorkspaceState();
        index.Settings!.Apply(state);
        ReadPages(index.AssetPages, state.Assets);
        ReadPages(index.ProjectPages, state.Projects);
        ReadPages(index.FolderPages, state.Folders);
        ReadPages(index.LinkPages, state.Links);
        ReadPages(index.MaterialSessionPages, state.MaterialSessions);
        return state;
    }

    public void Save(WorkspaceState state)
    {
        using var lease = OpenLock();
        Index? current = ReadIndex();
        if (current?.Version != _saved?.Version || current?.Revision != _saved?.Revision)
            throw new IOException(Localization.Text("AnotherTesseraInstanceChangedTheCatalog"));

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        var next = new Index
        {
            Version = CurrentVersion,
            Revision = Guid.NewGuid(),
            Settings = ViewSettings.Capture(state),
            AssetPages = SavePages(state.Assets, buffer, writer),
            ProjectPages = SavePages(state.Projects, buffer, writer),
            FolderPages = SavePages(state.Folders, buffer, writer),
            LinkPages = SavePages(state.Links, buffer, writer),
            MaterialSessionPages = SavePages(state.MaterialSessions, buffer, writer)
        };

        // Only this replacement publishes the new catalog. Until then all old pages are readable.
        if (current?.Version != CurrentVersion || current.Settings != next.Settings ||
            !current.AssetPages.SequenceEqual(next.AssetPages) || !current.ProjectPages.SequenceEqual(next.ProjectPages) ||
            !current.FolderPages.SequenceEqual(next.FolderPages) || !current.LinkPages.SequenceEqual(next.LinkPages) ||
            !current.MaterialSessionPages.SequenceEqual(next.MaterialSessionPages))
        {
            LibraryJson.Write(_path, next);
            _saved = next;
        }
        CollectUnusedPages(next);
    }

    private Index? ReadIndex()
    {
        if (!File.Exists(_path)) return null;
        var index = LibraryJson.Read<Index>(_path);
        if (index.Version != CurrentVersion || index.Revision == Guid.Empty || index.Settings is null)
            throw new InvalidDataException(Localization.Text("UnknownWorkspaceVersion"));
        return index;
    }

    private FileStream OpenLock()
    {
        Directory.CreateDirectory(_pages);
        return new FileStream(Path.Combine(_pages, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private void ReadPages<T>(IEnumerable<string> hashes, List<T> target)
    {
        foreach (string hash in hashes)
        {
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid workspace page name.");
            target.AddRange(LibraryJson.Read<List<T>>(PagePath(hash)));
        }
    }

    private string[] SavePages<T>(List<T> items, ArrayBufferWriter<byte> buffer, Utf8JsonWriter writer)
    {
        var hashes = new List<string>();
        var page = new List<T>(Math.Min(PageSize, items.Count));
        for (int start = 0; start < items.Count; start += PageSize)
        {
            int end = Math.Min(start + PageSize, items.Count);
            for (int q = start; q < end; q++) page.Add(items[q]);
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            JsonSerializer.Serialize(writer, page);
            writer.Flush();
            string hash = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
            string path = PagePath(hash);
            if (!File.Exists(path)) LibraryJson.WriteFile(path, file => file.Write(buffer.WrittenSpan));
            hashes.Add(hash);
            page.Clear();
        }
        return hashes.ToArray();
    }

    private string PagePath(string hash) => Path.Combine(_pages, hash + ".json");

    private void CollectUnusedPages(Index index)
    {
        var live = index.Pages().ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string path in Directory.EnumerateFiles(_pages, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length == 64 && name.All(Uri.IsHexDigit) && !live.Contains(name)) File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
