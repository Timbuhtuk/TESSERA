using System.Windows;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private void InitializeIcons()
    {
        iconWorkspace.ValidateExportPath = EnsureExportPathAllowed;
        iconWorkspace.BackRequested += (_, _) => ShowHome();
        iconWorkspace.MaterialRequested += UseEditorMaterialForIcon;
        iconWorkspace.StatusChanged += Notify;
        iconWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
        Closed += (_, _) => iconWorkspace.Dispose();
    }

    internal void OpenIconWorkspace(object sender, RoutedEventArgs e) => ShowIconWorkspace();

    private void ShowIconWorkspace()
    {
        if (Editor._processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(asepriteWorkspace, false);
        SetVisible(backgroundWorkspace, false);
        SetVisible(moodboardWorkspace, false);
        SetVisible(LibraryPage.homeScroll, false);
        SetVisible(Editor.workspaceHost, false);

        SetVisible(iconWorkspace, true);
        iconWorkspace.SetEditorMaterials(GetEditorMaterials());
        HideWorkspaceBrowser();
        Notify(Localization.Text("Done"), NotificationKind.Success);
    }

    private void UseEditorMaterialForIcon(EditorMaterial material)
    {
        if (Editor._processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        try
        {
            using var image = ImageLibrary.ReadBitmap(material.SnapshotPath);
            iconWorkspace.SetImage(image, material.FileLabel, material.OriginalPath ?? material.SnapshotPath);
        }
        catch (Exception ex) { Notify(Localization.Format("CouldNotOpenMaterialFormat", ex.Message), NotificationKind.Error); }
    }

    private bool IsIconDropTarget(DependencyObject? target)
    {
        if (iconWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ImageEditorPage.ParentOf(current))
            if (ReferenceEquals(current, LibraryPage.iconBanner)) return true;
        return false;
    }

    private bool CanDropIcon(IDataObject data) => !Editor._processing && !iconWorkspace.IsBusy &&
        (Editor.History.TryGetDraggedGeneration(data, out _, out _) ||
         data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile));

    private async Task DropIconAsync(DragEventArgs e)
    {
        if (!CanDropIcon(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowIconWorkspace();
        try
        {
            if (Editor.History.TryGetDraggedGeneration(e.Data, out var source, out var generation))
            {
                using var image = ImageLibrary.ReadBitmap(_library.ResultPath(source, generation));
                iconWorkspace.SetImage(image, System.IO.Path.GetFileNameWithoutExtension(source.Label) + "_result.png");
            }
            else
            {
                string? path = DropPaths(e).FirstOrDefault(IconWorkspace.SupportsFile);
                if (path is not null) await iconWorkspace.LoadFileAsync(path);
            }
        }
        catch (Exception ex) { Notify(Localization.Format("CouldNotOpenImageForICOFormat", ex.Message), NotificationKind.Error); }
    }
}
