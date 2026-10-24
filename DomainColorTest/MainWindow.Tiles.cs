using System.IO;
using System.Windows;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private TileInspectorWindow? _tileInspector;
    private readonly HashSet<string> _closedTileSourcePaths = new(StringComparer.OrdinalIgnoreCase);

    private async void OpenTileInspector(object sender, RoutedEventArgs e)
    {
        var inspector = GetTileInspector();
        if (!inspector.RefreshWorkspaceMaterials()) return;
        if (_activeSource is { } source && GetTileWorkspaceContext().Materials.Any(item => item.AssetId == source.Id))
            await inspector.AddWorkspaceMaterialAsync(source.Id, ProcessingBasis == 1 && source.SelectedGeneration is not null);
        ShowTileInspector(inspector);
    }

    private TileInspectorWindow GetTileInspector()
    {
        if (_tileInspector is not null) return _tileInspector;
        var inspector = new TileInspectorWindow { Owner = this, ValidateExportPath = EnsureExportPathAllowed };
        _tileInspector = inspector;
        inspector.SetWorkspaceMaterials(GetTileWorkspaceContext);
        inspector.EnableWorkspaceApply(tile => SaveTileWorkspaceResult(tile), tile => FindTileWorkspaceOrigin(tile) is not null);
        inspector.WorkspaceSaved += () =>
        {
            if (_workspaceCatalog is null || inspector.WorkspaceSavePath is not { } path) return;
            var document = _workspaceCatalog.State.Assets.FirstOrDefault(asset => asset.Kind == WorkspaceAssetKind.Tiles &&
                string.Equals(_workspaceCatalog.ContentPath(asset), path, StringComparison.OrdinalIgnoreCase));
            if (document is null) return;
            document.ModifiedAt = DateTimeOffset.Now; UpdateDocumentPreview(document); _workspaceCatalog.Save();
        };
        inspector.Closed += (_, _) =>
        {
            foreach (string source in inspector.SourcePaths) _closedTileSourcePaths.Add(source);
            _tileInspector = null; SynchronizeWorkspace();
        };
        return inspector;
    }

    private static void ShowTileInspector(TileInspectorWindow inspector)
    {
        if (inspector.WindowState == WindowState.Minimized) inspector.WindowState = WindowState.Normal;
        if (!inspector.IsVisible) inspector.Show();
        inspector.Activate();
    }

    private TileWorkspaceContext GetTileWorkspaceContext()
    {
        var state = _workspaceCatalog?.State;
        bool browser = _workspaceBrowser?.Visibility == Visibility.Visible;
        Guid? project = state is null ? null : !browser && _workspaceActive?.Kind == WorkspaceAssetKind.Image
            ? _workspaceSourceProject : state.View == "project" ? state.ProjectId : null;
        bool unassigned = state is not null && (!browser && _workspaceActive?.Kind == WorkspaceAssetKind.Image
            ? _workspaceSourcesUnassigned : state.View == "unassigned");
        var members = state?.Links.Where(link => project is null || link.ProjectId == project).Select(link => link.AssetId).ToHashSet() ?? [];
        var sources = _sources.Where(source => project is not null ? members.Contains(source.Id) : !unassigned || !members.Contains(source.Id));
        var materials = new List<TileWorkspaceMaterial>();
        foreach (var source in sources.DistinctBy(source => source.Id))
        {
            var asset = state?.Assets.FirstOrDefault(item => item.Id == source.Id);
            var result = source.SelectedGeneration;
            if (result is not null && !source.Generations.Contains(result)) result = null;
            var link = project is null ? null : state!.Links.FirstOrDefault(item => item.ProjectId == project && item.AssetId == source.Id);
            string location = link is null ? "" : _workspaceCatalog!.Location(link);
            materials.Add(new(source.Id, asset?.Name ?? source.Label, location, _library.SourcePath(source),
                result is null ? null : _library.ResultPath(source, result), result?.Id, result?.Thumbnail ?? source.Thumbnail));
        }
        string label = project is { } id ? Localization.Format("Проект: {0}", state!.Projects.FirstOrDefault(item => item.Id == id)?.Name ?? Localization.Text("Проект"))
            : unassigned ? Localization.Text("Ассеты без проекта") : Localization.Text("Ассеты библиотеки");
        return new(project?.ToString("N") ?? (unassigned ? "unassigned" : "library"), label, materials);
    }

    private (SourceEntry Source, GenerationEntry? Generation)? FindTileWorkspaceOrigin(TileAsset tile)
    {
        if (tile.SourcePath is null) return null;
        var context = GetTileWorkspaceContext();
        foreach (var material in context.Materials)
        {
            var source = _sources.FirstOrDefault(item => item.Id == material.AssetId);
            if (source is null) continue;
            if (string.Equals(material.SourcePath, tile.SourcePath, StringComparison.OrdinalIgnoreCase)) return (source, null);
            foreach (var generation in source.Generations)
                if (string.Equals(_library.ResultPath(source, generation), tile.SourcePath, StringComparison.OrdinalIgnoreCase)) return (source, generation);
        }
        return null;
    }

    private void SaveTileWorkspaceResult(TileAsset tile)
    {
        var origin = FindTileWorkspaceOrigin(tile) ?? throw new InvalidOperationException(Localization.Text("Исходный ассет больше не доступен в текущем проекте."));
        using var memory = new MemoryStream(); tile.WritePng(memory); memory.Position = 0;
        using var image = new Bitmap(memory); using var result = new Bitmap(image);
        string operation = Localization.Text("Исправление тайла");
        var entry = _library.SaveGeneration(origin.Source, result, null, new GenerationEntry
        {
            Operation = operation, Caption = operation,
            OperationId = "manual.tile", RecipeUnavailableReason = Localization.Text("Цепочка содержит ручное исправление тайла. Этот шаг нельзя воспроизвести макросом."), ParentGenerationId = origin.Generation?.Id,
            PreservesTransparency = true, Settings = origin.Generation?.Settings ?? origin.Source.DraftSettings ?? new()
        });
        origin.Source.Generations.Add(entry); origin.Source.SelectedGeneration = entry;
        if (origin.Source == _activeSource) { SelectGeneration(entry); ProcessingBasis = 1; }
        SynchronizeWorkspace();
    }
}
