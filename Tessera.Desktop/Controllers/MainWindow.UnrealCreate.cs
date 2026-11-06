using System.IO;
using System.Text.RegularExpressions;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private bool _unrealCreating;

    private async Task CreateUnrealTextureAsync()
    {
        if (_workspaceCatalog is null || WorkspaceBusy || _unrealCreating) return;
        if (!ImageEditorVisible || _activeSource is null)
            throw new InvalidOperationException(Localization.Text("Откройте текстуру в редакторе Tessera."));
        var projects = _workspaceCatalog.State.Projects.Where(p => p.IsUnreal).ToArray();
        if (projects.Length == 0) throw new InvalidOperationException(Localization.Text("Сначала подключите проект Unreal в библиотеке."));
        var source = _activeSource;
        Guid? result = ProcessingBasis == 1 ? source.SelectedGeneration?.Id : null;
        string png = WorkspaceBasisPath();
        var binding = _workspaceActive?.Unreal;
        Guid? projectId = binding?.ProjectId ?? _workspaceCatalog.State.ProjectId;
        string folder = binding is not null ? binding.Path[..binding.Path.LastIndexOf('/')] :
            _workspaceCatalog.State.Folders.FirstOrDefault(f => f.Id == _workspaceCatalog.State.FolderId)?.UnrealPath ?? "/Game";
        string name = Regex.Replace(Path.GetFileNameWithoutExtension(source.Label), @"[^\p{L}\p{Nd}_]", "_");
        if (!name.StartsWith("T_", StringComparison.OrdinalIgnoreCase)) name = "T_" + name;
        var dialog = new UnrealTextureDialog(projects, _workspaceCatalog.State.Folders, projectId, folder, name,
            (project, path) => ValidateNewUnrealTexture(project, path));
        if (IsVisible) dialog.Owner = this;
        _unrealCreating = true;
        WorkspaceAsset? created = null;
        try
        {
            if (dialog.ShowDialog() == true)
                created = CreateLinkedUnrealTexture(dialog.Project, dialog.ObjectPath, png, source.Id, result);
        }
        finally { _unrealCreating = false; }
        if (created is null) return;
        await OpenWorkspaceAssetAsync(created);
        statusLabel.Text = Localization.Format("Текстура ожидает создания в Unreal: {0}", created.Unreal!.Path);
    }

    private string? ValidateNewUnrealTexture(WorkspaceProject project, string path, Guid? exceptAsset = null)
    {
        if (!WorkspaceCatalog.ValidObjectPath(path) || path.Any(char.IsWhiteSpace))
            return Localization.Text("Укажите папку /Game/… и имя текстуры без пробелов и расширения.");
        if (_workspaceCatalog!.State.Assets.Any(a => a.Id != exceptAsset && a.Unreal is { } link &&
            link.ProjectId == project.Id && (!link.Missing || link.CreatePending) && WorkspaceCatalog.UnrealPathComparer.Equals(link.Path, path)) ||
            Mailbox(project).Jobs().Any(j => j.Request.AssetId != exceptAsset && j.Request.Operation == "create" &&
                WorkspaceCatalog.UnrealPathComparer.Equals(j.Request.Path, path) &&
                (j.Response is null || j.Response.Status == "saved")))
            return Localization.Text("Это имя уже занято в Unreal.");
        return null;
    }

    private WorkspaceAsset CreateLinkedUnrealTexture(WorkspaceProject project, string path, string png, Guid sourceId, Guid? resultId)
    {
        if (ValidateNewUnrealTexture(project, path) is { } error) throw new InvalidOperationException(error);
        string name = path[(path.LastIndexOf('.') + 1)..];
        var source = new SourceEntry { DisplayName = name + ".png", OriginSourceId = sourceId, OriginGenerationId = resultId };
        _library.StoreMutableImage(source, TileAsset.DecodeFile(png));
        var asset = new WorkspaceAsset
        {
            Id = source.Id, Name = name, Width = source.Width, Height = source.Height, Thumbnail = source.Thumbnail,
            Unreal = new UnrealTextureLink { ProjectId = project.Id, Path = path, Missing = true, CreatePending = true }
        };
        _workspaceCatalog!.State.Assets.Add(asset);
        _sources.Add(source);
        RefreshPendingUnrealTexture(project);
        var placement = _workspaceCatalog.State.Links.Single(l => l.AssetId == asset.Id && l.ProjectId == project.Id);
        _workspaceBrowser!.Navigate("project", project.Id, placement.FolderId);
        SaveUnrealTexture(asset, _library.SourcePath(source));
        return asset;
    }

    private void RefreshPendingUnrealTexture(WorkspaceProject project)
    {
        var mailbox = Mailbox(project);
        var catalog = mailbox.ReadCatalog() ?? new UnrealProjectMailbox.Catalog(2, [],
            _workspaceCatalog!.State.Assets.Where(a => a.Unreal?.ProjectId == project.Id && !a.Unreal.Missing)
                .Select(a => new UnrealProjectMailbox.Texture(a.Unreal!.Path, a.Unreal.Revision)).ToArray(), []);
        _workspaceCatalog!.MirrorUnreal(project, catalog);
        _workspaceBrowser!.RebuildTree();
        _workspaceBrowser.Refresh();
    }

    private bool ChooseNewUnrealDestination(WorkspaceAsset asset, WorkspaceProject project)
    {
        var link = asset.Unreal!;
        var dialog = new UnrealTextureDialog([project], _workspaceCatalog!.State.Folders, project.Id,
            link.Path[..link.Path.LastIndexOf('/')], link.Path[(link.Path.LastIndexOf('.') + 1)..],
            (target, path) => WorkspaceCatalog.UnrealPathComparer.Equals(path, link.Path)
                ? Localization.Text("Это имя уже занято в Unreal.") : ValidateNewUnrealTexture(target, path, asset.Id));
        if (IsVisible) dialog.Owner = this;
        if (dialog.ShowDialog() != true) return false;
        link.Path = dialog.ObjectPath;
        asset.Name = link.Path[(link.Path.LastIndexOf('.') + 1)..];
        RefreshPendingUnrealTexture(project);
        return true;
    }
}
