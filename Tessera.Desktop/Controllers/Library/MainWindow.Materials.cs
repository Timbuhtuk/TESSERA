using System.IO;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<EditorMaterial> GetEditorMaterials()
    {
        var materials = new List<EditorMaterial>();
        var mapIds = _workspaceCatalog?.MaterialMapAssetIds();
        var sources = (_workspaceReady ? _workspaceSources : _sources).Reverse()
            .Where(source => mapIds?.Contains(source.Id) != true).ToArray();
        foreach (var source in sources)
        {
            materials.Add(new EditorMaterial(source.Label, source.Label, _library.SourcePath(source), source.OriginalPath,
                false) { ThumbnailFile = source.ThumbnailFile });
        }
        foreach (var source in sources)
        {
            IEnumerable<GenerationEntry> history = source.HasLoadedHistory ? source.Generations : _library.ReadHistory(source);
            foreach (var generation in history.Reverse())
            {
                string fileLabel = Path.GetFileNameWithoutExtension(source.Label) + "_result_" + generation.Id.ToString("N")[..8] + ".png";
                materials.Add(new EditorMaterial(source.Label + " · " + generation.Label, fileLabel,
                    _library.ResultPath(source, generation), null, true) { ThumbnailFile = generation.ThumbnailFile });
            }
        }
        return materials;
    }
}
