using System.Windows;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private void InitializeBackground()
    {
        backgroundWorkspace.ValidateExportPath = EnsureExportPathAllowed;
        backgroundWorkspace.BackRequested += (_, _) => ShowHome();
        backgroundWorkspace.MaterialRequested += UseEditorMaterialForBackground;
        backgroundWorkspace.StatusChanged += message => statusLabel.Text = message;
        backgroundWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
        Closed += (_, _) => backgroundWorkspace.Dispose();
    }

    internal void UpdateBusyIndicator()
    {
        RefreshOperationProgressVisibility();
        UpdateNavigationState();
    }

    internal void OpenBackgroundWorkspace(object sender, RoutedEventArgs e) => ShowBackgroundWorkspace();

    private void ShowBackgroundWorkspace()
    {
        if (Editor._processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(LibraryPage.homeScroll, false);
        SetVisible(Editor.workspaceHost, false);

        SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(backgroundWorkspace, true);
        SetVisible(moodboardWorkspace, false);
        backgroundWorkspace.SetEditorMaterials(GetEditorMaterials());
        HideWorkspaceBrowser();
        statusLabel.Text = Localization.Text("Done");
    }

    private void UseEditorMaterialForBackground(EditorMaterial material)
    {
        if (Editor._processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        try
        {
            using var image = ImageLibrary.ReadBitmap(material.SnapshotPath);
            backgroundWorkspace.SetImage(image, material.FileLabel, material.OriginalPath ?? material.SnapshotPath);
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("CouldNotOpenMaterialFormat", ex.Message); }
    }

    private bool IsBackgroundDropTarget(DependencyObject? target)
    {
        if (backgroundWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ImageEditorPage.ParentOf(current))
            if (ReferenceEquals(current, LibraryPage.backgroundBanner)) return true;
        return false;
    }

    private bool CanDropBackground(IDataObject data) => !Editor._processing && !backgroundWorkspace.IsBusy &&
        (Editor.History.TryGetDraggedGeneration(data, out _, out _) ||
         data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile));

    private async Task DropBackgroundAsync(DragEventArgs e)
    {
        if (!CanDropBackground(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowBackgroundWorkspace();
        try
        {
            if (Editor.History.TryGetDraggedGeneration(e.Data, out var source, out var generation))
            {
                using var image = ImageLibrary.ReadBitmap(_library.ResultPath(source, generation));
                backgroundWorkspace.SetImage(image, System.IO.Path.GetFileNameWithoutExtension(source.Label) + "_result.png");
            }
            else
            {
                string? path = DropPaths(e).FirstOrDefault(IconWorkspace.SupportsFile);
                if (path is not null) await backgroundWorkspace.LoadFileAsync(path);
            }
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("CouldNotOpenImageFormat", ex.Message); }
    }
}
