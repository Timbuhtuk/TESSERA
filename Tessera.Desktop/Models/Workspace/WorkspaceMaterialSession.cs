using System.IO;
using System.Text.Json.Serialization;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public sealed class WorkspaceMaterialSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid? GenerationId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Archived { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool AlternateDocument { get; set; }
    public Guid HeightId { get; set; } = Guid.NewGuid();
    public Guid NormalId { get; set; } = Guid.NewGuid();
    public Guid AoId { get; set; } = Guid.NewGuid();
    public Guid MapId(EMaterialMapKind kind) => kind switch { EMaterialMapKind.Height => HeightId, EMaterialMapKind.Normal => NormalId, _ => AoId };
}

public sealed partial class WorkspaceCatalog
{
    public string MaterialSessionPath(WorkspaceMaterialSession session)
        => MaterialSessionPath(session, session.AlternateDocument);

    private string MaterialSessionPath(WorkspaceMaterialSession session, bool alternate)
        => Path.Combine(Root, "material-maps", session.Id.ToString("N"), alternate ? "layers-next.tmaps" : "layers.tmaps");

    internal WorkspaceMaterialSession? FindMaterialSession(Guid sourceId) =>
        State.MaterialSessions.FirstOrDefault(session => session.SourceId == sourceId && !session.Archived);

    internal HashSet<Guid> MaterialMapAssetIds() => State.MaterialSessions
        .SelectMany(session => new[] { session.AoId, session.NormalId, session.HeightId }).ToHashSet();

    // TESSERA_LEGACY Archive duplicate material sessions from the former per-result workflow while preserving their .tmaps files.
    private void ArchiveDuplicateMaterialSessions()
    {
        foreach (var group in State.MaterialSessions.Where(session => !session.Archived).GroupBy(session => session.SourceId))
        {
            if (group.Count() < 2) continue;
            var newest = group.MaxBy(session => File.GetLastWriteTimeUtc(MaterialSessionPath(session)));
            foreach (var session in group)
                session.Archived = session != newest;
        }
    }

    internal void SaveMaterialDocument(WorkspaceMaterialSession session, MaterialMapDocument document, Guid? generationId, bool rebuild = false)
    {
        bool added = !State.MaterialSessions.Contains(session);
        bool previousSlot = session.AlternateDocument;
        var previousGeneration = session.GenerationId;
        // Write a replacement to the other slot before publishing its basis and file together.
        bool slot = rebuild ? !previousSlot : previousSlot;
        string path = MaterialSessionPath(session, slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        MaterialMapFile.Save(document, path, markSaved: false);
        if (!added && !rebuild && previousGeneration == generationId) return;

        session.GenerationId = generationId;
        session.AlternateDocument = slot;
        if (added) State.MaterialSessions.Add(session);
        try { Save(); }
        catch
        {
            if (added) State.MaterialSessions.Remove(session);
            session.GenerationId = previousGeneration;
            session.AlternateDocument = previousSlot;
            throw;
        }
    }
}
