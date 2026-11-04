using System.IO;
using System.Text.Json;

namespace DomainColorTest;

public enum WorkspaceProjectKind { Local, Unreal }

public sealed class UnrealTextureLink
{
    public Guid ProjectId { get; set; }
    public string Path { get; set; } = "";
    public string Revision { get; set; } = "";
    public string LoadedRevision { get; set; } = "";
    public string? MapKind { get; set; }
    public bool Missing { get; set; }
    public bool NeedsDestination { get; set; }
    public bool CreatePending { get; set; }
    public Dictionary<Guid, string> ResultRevisions { get; set; } = [];
}

internal sealed class UnrealProjectMailbox(string projectPath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(Path.GetDirectoryName(projectPath)!, "Saved", "TesseraSync", "v2");

    internal sealed record Texture(string Path, string Revision);
    internal sealed record Catalog(int Version, string[] Folders, Texture[] Textures, Dictionary<string, string> Redirects);
    internal sealed record Request(int Version, Guid AssetId, string Operation, string Path, string Revision, string? Kind, string? Sha256, Guid? ResultId);
    internal sealed record Response(string Status, string? Revision, string? Sha256, string? Message);
    internal sealed record Job(string Directory, Request Request, Response? Response)
    {
        public string Export => Path.Combine(Directory, "unreal.png");
        public string Payload => Path.Combine(Directory, "local.png");
        public bool HasExport => Response?.Sha256 is { } hash && File.Exists(Export) && UnrealSyncBridge.Hash(Export) == hash;

        public void Remove()
        {
            File.Delete(Path.Combine(Directory, "request.json"));
            File.Delete(Path.Combine(Directory, "response.json"));
            File.Delete(Export);
            File.Delete(Payload);
            System.IO.Directory.Delete(Directory);
        }
    }

    public bool Online => DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(_root, "editor.json")) < TimeSpan.FromSeconds(9);
    public long CatalogStamp => File.GetLastWriteTimeUtc(Path.Combine(_root, "catalog.json")).Ticks;
    public Catalog? ReadCatalog() => Read<Catalog>(Path.Combine(_root, "catalog.json")) is { Version: 2 } value ? value : null;

    public Job[] Jobs()
    {
        string requests = Path.Combine(_root, "requests");
        if (!Directory.Exists(requests)) return [];
        var jobs = new List<Job>();
        foreach (string folder in Directory.EnumerateDirectories(requests))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            var request = Read<Request>(Path.Combine(folder, "request.json"));
            if (request is { Version: 2 }) jobs.Add(new Job(folder, request, Read<Response>(Path.Combine(folder, "response.json"))));
        }
        return jobs.ToArray();
    }

    public void Queue(Guid assetId, string operation, string objectPath, string revision, string? kind = null, string? png = null, Guid? resultId = null)
    {
        string folder = Path.Combine(_root, "requests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string? hash = null;
        if (png is not null)
        {
            string payload = Path.Combine(folder, "local.png");
            File.Copy(png, payload);
            hash = UnrealSyncBridge.Hash(payload);
        }
        string manifest = Path.Combine(folder, "request.json");
        File.WriteAllText(manifest + ".tmp", JsonSerializer.Serialize(new Request(2, assetId, operation, objectPath, revision, kind, hash, resultId), Json));
        File.Move(manifest + ".tmp", manifest);
    }

    private static T? Read<T>(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (IOException) { return default; }
        catch (JsonException) { return default; }
    }
}
