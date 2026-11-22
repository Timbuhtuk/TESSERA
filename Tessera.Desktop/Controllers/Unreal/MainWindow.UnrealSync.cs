using System.IO;
using System.Windows;
using Microsoft.Win32;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private UnrealSyncBridge _unrealSync = null!;

    private WorkspaceAsset? SelectedLibraryImage => _workspaceBrowser is
        { Visibility: Visibility.Visible, Selection: [{ Kind: EWorkspaceAssetKind.Image, IsLegacyTileArchive: false } asset] } ? asset : null;

    private Task SelectUnrealProjectAsync()
    {
        var dialog = new OpenFileDialog { Title = Localization.Text("ChooseAnUnrealEngineProject"), Filter = "Unreal Engine project|*.uproject" };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        _unrealSync.SelectProject(dialog.FileName);
        if (_workspaceCatalog is not null) ConnectUnrealProject(dialog.FileName);
        else statusLabel.Text = Localization.Text("UnrealProjectConnectedPrefix") + _unrealSync.ProjectPath;
        return Task.CompletedTask;
    }

    private Task InstallUnrealPluginAsync()
    {
        if (_workspaceCatalog?.State.ProjectId is { } projectId && UnrealProject(projectId)?.UnrealProjectPath is { } projectPath)
        {
            InstallProjectPlugin(projectPath);
            return Task.CompletedTask;
        }
        EnsureUnrealProject();
        InstallProjectPlugin(_unrealSync.ProjectPath!);
        return Task.CompletedTask;
    }

    private async Task<(SourceEntry Source, GenerationEntry? Result)?> UnrealTextureBasisAsync(WorkspaceAsset? asset)
    {
        if (asset is not null)
        {
            if (!await EnsureUnrealImageAsync(asset)) return null;
            var source = _sources.FirstOrDefault(item => item.Id == asset.Id)
                ?? throw new InvalidOperationException(Localization.Text("AssetNotFound"));
            var result = asset.Basis == 1 && asset.SelectedResult is { } id
                ? _library.FindGeneration(source, id) : null;
            return (source, result);
        }
        if (_workspaceReady && LibraryVisible || Editor._activeSource is not { } active)
            throw new InvalidOperationException(Localization.Text("SelectAnImageInTheLibrary"));
        return (active, Editor.ProcessingBasis == 1 ? active.SelectedGeneration : null);
    }

    private async Task SendTextureToUnrealAsync()
    {
        EnsureUnrealProject();
        if (WorkspaceBusy) return;
        if (await UnrealTextureBasisAsync(SelectedLibraryImage) is not { } basis) return;
        string path = basis.Result is { } generation ? _library.ResultPath(basis.Source, generation) : _library.SourcePath(basis.Source);
        string destination = _unrealSync.SendPng(path, basis.Source.Id, basis.Source.Label);
        statusLabel.Text = Localization.Text("TextureQueuedForUnrealImportPrefix") + destination;
    }

    private async Task SendMapsToUnrealAsync()
    {
        EnsureUnrealProject();
        if (WorkspaceBusy) return;
        var selected = SelectedLibraryImage;
        var source = selected is null ? Editor._activeSource : _sources.FirstOrDefault(item => item.Id == selected.Id);
        var sourceId = selected?.Id ?? source?.Id;
        if (sourceId is null || (_workspaceReady && LibraryVisible && selected is null))
            throw new InvalidOperationException(Localization.Text("SelectAnImageInTheLibrary"));
        string label = source?.Label ?? selected!.Name;
        var session = _workspaceCatalog?.FindMaterialSession(sourceId.Value);
        var generation = _workspaceCatalog is null && Editor.ProcessingBasis == 1 ? source?.SelectedGeneration?.Id : null;
        _materialMapWindows.TryGetValue((sourceId.Value, generation), out var window);
        MaterialMapDocument? document = null;
        if (window is not null)
        {
            await window.SaveWorkspaceAsync();
            document = window.Document;
        }
        if (document is null && session is not null)
        {
            string savedPath = _workspaceCatalog!.MaterialSessionPath(session);
            if (File.Exists(savedPath)) document = await Task.Run(() => MaterialMapFile.Load(savedPath));
        }
        if (document is null) throw new InvalidOperationException(Localization.Text("OpenMaterialMapsSelectedImageFirst"));
        foreach (var kind in Enum.GetValues<EMaterialMapKind>())
        {
            string path = Path.Combine(Path.GetTempPath(), "tessera-map-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                if (window is not null) window.ExportMap(kind, path);
                else MaterialMapsWindow.WriteMapPng(document, kind, path);
                _unrealSync.SendPng(path, sourceId.Value, label, kind.ToString().ToLowerInvariant());
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        statusLabel.Text = Localization.Text("HeightNormalAOMapsAreQueued");
    }

    private async Task ReceiveFromUnrealAsync()
    {
        EnsureUnrealProject();
        if (WorkspaceBusy) throw new InvalidOperationException(Localization.Text("WaitForTheCurrentOperationTo"));
        var pending = _unrealSync.PendingFromUnreal();
        int received = 0;
        foreach (var item in pending)
        {
            await AddImagesAsync([item.Path]);
            if (_sources.Any(source => string.Equals(source.OriginalPath, item.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(source.Fingerprint, item.Transfer.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                _unrealSync.MarkReceived(item.Transfer);
                received++;
            }
        }
        statusLabel.Text = Localization.Format("TexturesReceivedFromUnrealFormat", received);
    }

    private void EnsureUnrealProject()
    {
        if (_unrealSync.ProjectPath is null) throw new InvalidOperationException(Localization.Text("ChooseUnrealEngineProjectFileUnreal"));
    }
}
