using System.Windows;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private void InitializeMoodboard()
    {
        moodboardWorkspace.ValidateExportPath = EnsureExportPathAllowed;
        moodboardWorkspace.StatusChanged += Notify;
        moodboardWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
    }

    internal void OpenMoodboardWorkspace(object sender, RoutedEventArgs e) => ShowMoodboardWorkspace();

    private void ShowMoodboardWorkspace()
    {
        if (Editor._processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(LibraryPage.homeScroll, false);
        SetVisible(Editor.workspaceHost, false);

        SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, true);
        HideWorkspaceBrowser();
        statusLabel.Text = moodboardWorkspace.HasBoard ? Localization.Text("BoardReady") : Localization.Text("CreateACanvasForYourBoard");
    }

    private bool IsMoodboardDropTarget(DependencyObject? target)
    {
        for (var current = target; current is not null; current = ImageEditorPage.ParentOf(current))
            if (ReferenceEquals(current, LibraryPage.moodboardBanner) || ReferenceEquals(current, moodboardWorkspace)) return true;
        return false;
    }

    private bool CanDropMoodboard(IDataObject data) => !Editor._processing && !moodboardWorkspace.IsBusy &&
        data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile);

    private async Task DropMoodboardAsync(DragEventArgs e)
    {
        if (!CanDropMoodboard(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowMoodboardWorkspace();
        await moodboardWorkspace.AddFilesAsync(DropPaths(e));
    }
}
