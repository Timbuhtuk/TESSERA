using System.IO;
using System.Windows;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private void MoveUnrealTexture(WorkspaceAsset asset)
    {
        var link = RequireUnrealMutation(asset);
        var project = UnrealProject(link.ProjectId)!;
        var mailbox = Mailbox(project);
        if (!_unrealResolving.Add(asset.Id)) return;
        try
        {
            string path = link.Path;
            var dialog = new UnrealTextureDialog([project], _workspaceCatalog!.State.Folders, project.Id,
                path[..path.LastIndexOf('/')], path[(path.LastIndexOf('.') + 1)..],
                (_, destination) => WorkspaceCatalog.UnrealPathComparer.Equals(destination, path)
                    ? Localization.Text("UnrealChooseDifferentNameOrFolder")
                    : ValidateNewUnrealTexture(project, destination, asset.Id),
                Localization.Text("UnrealMoveTextureTitle"), Localization.Text("Move"));
            if (IsVisible) dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            string destination = dialog.ObjectPath;
            if (WorkspaceCatalog.UnrealPathComparer.Equals(destination, path))
                throw new InvalidOperationException(Localization.Text("UnrealChooseDifferentNameOrFolder"));
            if (ValidateNewUnrealTexture(project, destination, asset.Id) is { } error)
                throw new InvalidOperationException(error);
            string? revision = MutationRevision(asset, mailbox);
            if (revision is null) return;
            mailbox.Queue(asset.Id, "move", path, revision, destination: destination);
            RemoveFailedUnrealMutations(asset, mailbox);
            statusLabel.Text = Localization.Text("UnrealMovePending");
        }
        finally { _unrealResolving.Remove(asset.Id); }
    }

    private void DeleteUnrealTexture(WorkspaceAsset asset)
    {
        var link = RequireUnrealMutation(asset);
        var project = UnrealProject(link.ProjectId)!;
        var mailbox = Mailbox(project);
        string message = Localization.Format("UnrealDeleteTextureConfirmation", link.Path) + "\n" +
            Localization.Text("UnrealDeleteTextureWarning");
        if (TesseraDialogs.Show(this, message, Localization.Text("DeleteFromUnreal"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        string? revision = MutationRevision(asset, mailbox);
        if (revision is null) return;
        mailbox.Queue(asset.Id, "delete", link.Path, revision);
        RemoveFailedUnrealMutations(asset, mailbox);
        statusLabel.Text = Localization.Text("UnrealDeletePending");
    }

    private UnrealTextureLink RequireUnrealMutation(WorkspaceAsset asset)
    {
        if (asset.Unreal is not { Missing: false, CreatePending: false } link || UnrealProject(link.ProjectId) is not { } project)
            throw new InvalidOperationException(Localization.Text("TheLinkedUnrealAssetIsUnavailable"));
        if (InstalledUnrealPluginVersion(project.UnrealProjectPath!) < UnrealSyncBridge.PluginVersion)
            throw new InvalidOperationException(Localization.Text("UpdateTesseraSyncHint"));
        if (Mailbox(project).Jobs().Any(j => j.Request.AssetId == asset.Id && j.Request.Operation != "read" &&
            j.Response is null or { Status: "saved" or "moved" or "deleted" }))
            throw new InvalidOperationException(Localization.Text("UnrealTextureOperationAlreadyQueued"));
        return link;
    }

    private string? MutationRevision(WorkspaceAsset asset, UnrealProjectMailbox mailbox)
    {
        var conflict = mailbox.Jobs().FirstOrDefault(j => j.Request.AssetId == asset.Id && j.Response?.Status == "conflict");
        if (conflict is null) return asset.Unreal!.Revision;
        if (conflict.Response?.Revision is not { Length: > 0 } revision)
            throw new IOException(Localization.Text("TheUnrealResponseFailedVerification"));
        if (TesseraDialogs.Show(this, Localization.Text("UnrealTextureChangedConfirmation"),
            "Unreal Engine", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return null;
        return revision;
    }

    private static void RemoveFailedUnrealMutations(WorkspaceAsset asset, UnrealProjectMailbox mailbox)
    {
        foreach (var job in mailbox.Jobs().Where(j => j.Request.AssetId == asset.Id && j.Request.Operation != "read" &&
            j.Response is { Status: not ("saved" or "moved" or "deleted") })) job.Remove();
    }
}
