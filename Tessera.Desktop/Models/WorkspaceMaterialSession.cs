using System.IO;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public sealed class WorkspaceMaterialSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid? GenerationId { get; set; }
    public Guid HeightId { get; set; } = Guid.NewGuid();
    public Guid NormalId { get; set; } = Guid.NewGuid();
    public Guid AoId { get; set; } = Guid.NewGuid();
    public Guid MapId(EMaterialMapKind kind) => kind switch { EMaterialMapKind.Height => HeightId, EMaterialMapKind.Normal => NormalId, _ => AoId };
    public bool Contains(Guid id) => id == HeightId || id == NormalId || id == AoId;
}

public sealed partial class WorkspaceCatalog
{
    public string MaterialSessionPath(WorkspaceMaterialSession session)
        => Path.Combine(Root, "material-maps", session.Id.ToString("N"), "layers.tmaps");
}
