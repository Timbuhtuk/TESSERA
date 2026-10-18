using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeMoodboard()
    {
        moodboardWorkspace.BackRequested += (_, _) => ShowHome();
        moodboardWorkspace.StatusChanged += message => statusLabel.Text = message;
        moodboardWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
    }

    private void OpenMoodboardWorkspace(object sender, RoutedEventArgs e) => ShowMoodboardWorkspace();

    private void ShowMoodboardWorkspace()
    {
        if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(homeScroll, false);
        SetVisible(workspaceHost, false);
        SetVisible(editorToolbar, false);
        SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, true);
        HideWorkspaceBrowser();
        statusLabel.Text = moodboardWorkspace.HasBoard ? "Доска готова к работе" : "Создайте полотно для доски";
    }

    private bool IsMoodboardDropTarget(DependencyObject? target)
    {
        for (var current = target; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, moodboardBanner) || ReferenceEquals(current, moodboardWorkspace)) return true;
        return false;
    }

    private bool CanDropMoodboard(IDataObject data) => !_processing && !moodboardWorkspace.IsBusy &&
        data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile);

    private async Task DropMoodboardAsync(DragEventArgs e)
    {
        if (!CanDropMoodboard(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowMoodboardWorkspace();
        await moodboardWorkspace.AddFilesAsync(DropPaths(e));
    }
}
