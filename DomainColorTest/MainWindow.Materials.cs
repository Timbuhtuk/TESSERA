using System.IO;

namespace DomainColorTest;

public partial class MainWindow
{
    private IReadOnlyList<EditorMaterial> GetEditorMaterials()
    {
        var materials = new List<EditorMaterial>();
        foreach (var source in _sources.Reverse())
        {
            materials.Add(new EditorMaterial(source.Label, source.Label, _library.SourcePath(source), source.OriginalPath,
                source.Thumbnail, false));
        }
        foreach (var source in _sources.Reverse())
        {
            foreach (var generation in source.Generations.Reverse())
            {
                string fileLabel = Path.GetFileNameWithoutExtension(source.Label) + "_result_" + generation.Id.ToString("N")[..8] + ".png";
                materials.Add(new EditorMaterial(source.Label + " · " + generation.Label, fileLabel,
                    _library.ResultPath(source, generation), null, generation.Thumbnail, true));
            }
        }
        return materials;
    }
}
