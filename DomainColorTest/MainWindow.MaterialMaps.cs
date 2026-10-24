using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly Dictionary<string, MaterialMapsWindow> _materialMapWindows = [];

    private async Task OpenMaterialMapsAsync()
    {
        if (!ImageEditorVisible || WorkspaceBusy || _activeSource is null) return;
        bool result = ProcessingBasis == 1 && _downscaledResult is not null;
        Guid sourceId = _activeSource.Id;
        Guid? generationId = result ? _activeSource.SelectedGeneration?.Id : null;
        var session = !result ? _workspaceCatalog?.State.MaterialSessions.FirstOrDefault(s => s.Contains(sourceId)) : null;
        session ??= _workspaceCatalog?.State.MaterialSessions.FirstOrDefault(s => s.SourceId == sourceId && s.GenerationId == generationId);
        string key = session?.Id.ToString("N") ?? sourceId.ToString("N") + ":" + (generationId?.ToString("N") ?? "source");
        if (_materialMapWindows.TryGetValue(key, out var existing))
        {
            if (session is not null && EnsureMaterialMapPlacement(session, MaterialMapPlacement(session)))
                SynchronizeWorkspace();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate(); return;
        }
        using var image = (Bitmap)(result ? _downscaledResult! : _sourceImage!).Clone();
        var window = new MaterialMapsWindow { Owner = this, ValidateExportPath = EnsureExportPathAllowed };
        if (_workspaceCatalog is not null)
        {
            if (session is null)
            {
                session = new WorkspaceMaterialSession { SourceId = sourceId, GenerationId = generationId };
                _workspaceCatalog.State.MaterialSessions.Add(session);
                key = session.Id.ToString("N");
            }
            var storedSession = session;
            var links = MaterialMapPlacement(session);
            window.EnableWorkspace(document => StoreMaterialMaps(storedSession, document, links));
        }
        _materialMapWindows.Add(key, window);
        window.Closed += (_, _) => _materialMapWindows.Remove(key);
        window.Show();
        try
        {
            string? path = session is not null ? _workspaceCatalog!.MaterialSessionPath(session) : null;
            if (path is not null && File.Exists(path)) await window.LoadDocumentAsync(path, _sources.FirstOrDefault(s => s.Id == session!.SourceId)?.Label ?? _activeSource.Label);
            else await window.SetImageAsync(image, _activeSource.Label);
            window.SaveWorkspace(true);
        }
        catch { if (window.Document is null) window.Close(); throw; }
    }

    private void StoreMaterialMaps(WorkspaceMaterialSession session, MaterialMapDocument document, WorkspaceLink[] placement)
    {
        var catalog = _workspaceCatalog!;
        string path = catalog.MaterialSessionPath(session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        MaterialMapFile.Save(document, path, markSaved: false);
        string name = Path.GetFileNameWithoutExtension(_sources.FirstOrDefault(s => s.Id == session.SourceId)?.Label ?? "material");
        if (session.GenerationId is not null) name += "_result_" + session.GenerationId.Value.ToString("N")[..8];
        _workspaceSyncing = true;
        try
        {
            foreach (var kind in Enum.GetValues<MaterialMapKind>())
            {
                Guid id = session.MapId(kind);
                var source = _sources.FirstOrDefault(s => s.Id == id);
                bool added = source is null;
                source ??= new SourceEntry { Id = id, DisplayName = name + "_" + kind.ToString().ToLowerInvariant() + ".png", OriginSourceId = session.SourceId, OriginGenerationId = session.GenerationId };
                var bitmap = BitmapSource.Create(document.Width, document.Height, 96, 96, PixelFormats.Bgra32, null, document.MapPixels(kind), document.Width * 4);
                bitmap.Freeze(); _library.StoreMaterialMap(source, bitmap);
                if (added) _sources.Add(source);
                catalog.SyncImages([source]);
                var asset = catalog.State.Assets.Single(a => a.Id == id);
                asset.ModifiedAt = DateTimeOffset.Now; asset.Transparent = HasTransparency(bitmap);
            }
            EnsureMaterialMapPlacement(session, placement);
            catalog.Save();
        }
        finally { _workspaceSyncing = false; }
        SynchronizeWorkspace();
        // Reload an open map's base without changing its result history or selected processing basis.
        if (_activeSource is { } active && session.Contains(active.Id))
        {
            _sourceImage?.Dispose(); _sourceImage = ImageLibrary.ReadBitmap(_library.SourcePath(active));
            UpdateSourcePreview();
        }
    }

    private WorkspaceLink[] MaterialMapPlacement(WorkspaceMaterialSession session)
        => _workspaceCatalog!.State.Links.Where(l => l.AssetId == session.SourceId &&
            (_workspaceSourceProject is null || l.ProjectId == _workspaceSourceProject))
            .Select(l => new WorkspaceLink { ProjectId = l.ProjectId, FolderId = l.FolderId }).ToArray();

    private bool EnsureMaterialMapPlacement(WorkspaceMaterialSession session, WorkspaceLink[] placement)
    {
        var catalog = _workspaceCatalog!;
        bool changed = false;
        foreach (var kind in Enum.GetValues<MaterialMapKind>())
        {
            Guid id = session.MapId(kind);
            if (!catalog.State.Assets.Any(a => a.Id == id)) continue;
            foreach (var link in placement.Where(l => catalog.State.Projects.Any(p => p.Id == l.ProjectId)))
            {
                if (catalog.State.Links.Any(l => l.AssetId == id && l.ProjectId == link.ProjectId)) continue;
                Guid? folder = catalog.State.Folders.Any(f => f.Id == link.FolderId && f.ProjectId == link.ProjectId) ? link.FolderId : null;
                catalog.Link(id, link.ProjectId, folder);
                changed = true;
            }
        }
        return changed;
    }
}
