using System.Windows;
using System.Windows.Controls;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private void InitializeAseprite()
    {
        asepriteWorkspace.BackRequested += (_, _) => ShowHome();
        asepriteWorkspace.StatusChanged += message => statusLabel.Text = message;
        asepriteWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
        Closed += (_, _) => asepriteWorkspace.Dispose();
    }

    internal void OpenAsepriteWorkspace(object sender, RoutedEventArgs e) => ShowAsepriteWorkspace();

    private async Task OpenAsepriteImportAsync()
    {
        if (WorkspaceBusy) return;
        ShowAsepriteWorkspace();
        await asepriteWorkspace.OpenAsync();
    }

    private void ShowAsepriteWorkspace()
    {
        if (Editor._processing || iconWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(LibraryPage.homeScroll, false); SetVisible(Editor.workspaceHost, false);  SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, true);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, false);
        HideWorkspaceBrowser();
    }

    private bool IsAsepriteDropTarget(DependencyObject? target, IDataObject data)
    {
        if (asepriteWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ImageEditorPage.ParentOf(current))
            if (ReferenceEquals(current, LibraryPage.asepriteBanner)) return true;
        return data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(AsepriteWorkspace.SupportsFile);
    }

    private bool CanDropAseprite(IDataObject data) => !Editor._processing && !iconWorkspace.IsBusy && asepriteWorkspace.CanAcceptFiles &&
        data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(AsepriteWorkspace.SupportsFile);

    private async Task DropAsepriteAsync(DragEventArgs e)
    {
        if (!CanDropAseprite(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowAsepriteWorkspace();
        await asepriteWorkspace.AddFilesAsync(DropPaths(e));
    }
}
