using System.Windows;
using System.Windows.Controls;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeAseprite()
    {
        asepriteWorkspace.BackRequested += (_, _) => ShowHome();
        asepriteWorkspace.StatusChanged += message => statusLabel.Text = message;
        asepriteWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
        Closed += (_, _) => asepriteWorkspace.Dispose();
    }

    private void OpenAsepriteWorkspace(object sender, RoutedEventArgs e) => ShowAsepriteWorkspace();

    private void ShowAsepriteWorkspace()
    {
        if (_processing || iconWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(homeScroll, false); SetVisible(workspaceHost, false); SetVisible(editorToolbar, false); SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, true);
        SetVisible(backgroundWorkspace, false);
    }

    private bool IsAsepriteDropTarget(DependencyObject? target, IDataObject data)
    {
        if (asepriteWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, asepriteBanner)) return true;
        return data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(AsepriteWorkspace.SupportsFile);
    }

    private bool CanDropAseprite(IDataObject data) => !_processing && !iconWorkspace.IsBusy && asepriteWorkspace.CanAcceptFiles &&
        data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(AsepriteWorkspace.SupportsFile);

    private async Task DropAsepriteAsync(DragEventArgs e)
    {
        if (!CanDropAseprite(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowAsepriteWorkspace();
        await asepriteWorkspace.AddFilesAsync(DropPaths(e));
    }
}
