using System.Windows;
using Microsoft.Win32;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private WorkspaceAsset? SelectedLibraryImage => _workspaceBrowser is
        { Visibility: Visibility.Visible, Selection: [{ Kind: EWorkspaceAssetKind.Image } asset] } ? asset : null;

    private Task SelectUnrealProjectAsync()
    {
        var dialog = new OpenFileDialog { Title = Localization.Text("ChooseAnUnrealEngineProject"), Filter = "Unreal Engine project|*.uproject" };
        if (_workspaceCatalog is null || dialog.ShowDialog(this) != true) return Task.CompletedTask;
        ConnectUnrealProject(dialog.FileName);
        return Task.CompletedTask;
    }

    private Task InstallUnrealPluginAsync()
    {
        if (_workspaceCatalog?.State.ProjectId is { } projectId && UnrealProject(projectId)?.UnrealProjectPath is { } projectPath)
        {
            InstallProjectPlugin(projectPath);
            return Task.CompletedTask;
        }
        throw new InvalidOperationException(Localization.Text("SelectUnrealProjectFirst"));
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
}
