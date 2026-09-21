using System.Windows;

namespace DomainColorTest;

public partial class MainWindow
{
    private void InitializeBackground()
    {
        backgroundWorkspace.BackRequested += (_, _) => ShowHome();
        backgroundWorkspace.MaterialRequested += UseEditorMaterialForBackground;
        backgroundWorkspace.StatusChanged += message => statusLabel.Text = message;
        backgroundWorkspace.BusyChanged += (_, _) => UpdateBusyIndicator();
        Closed += (_, _) => backgroundWorkspace.Dispose();
    }

    private void UpdateBusyIndicator() => SetVisible(busyIndicator,
        _processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy);

    private void OpenBackgroundWorkspace(object sender, RoutedEventArgs e) => ShowBackgroundWorkspace();

    private void ShowBackgroundWorkspace()
    {
        if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        SetVisible(homeScroll, false);
        SetVisible(workspaceHost, false);
        SetVisible(editorToolbar, false);
        SetVisible(iconWorkspace, false);
        SetVisible(asepriteWorkspace, false);
        SetVisible(backgroundWorkspace, true);
        backgroundWorkspace.SetEditorMaterials(GetEditorMaterials());
        statusLabel.Text = "Готово";
    }

    private void UseEditorMaterialForBackground(EditorMaterial material)
    {
        if (_processing || iconWorkspace.IsBusy || asepriteWorkspace.IsBusy || backgroundWorkspace.IsBusy) return;
        try
        {
            using var image = ImageLibrary.ReadBitmap(material.SnapshotPath);
            backgroundWorkspace.SetImage(image, material.FileLabel, material.OriginalPath ?? material.SnapshotPath);
        }
        catch (Exception ex) { statusLabel.Text = $"Не удалось открыть материал: {ex.Message}"; }
    }

    private bool IsBackgroundDropTarget(DependencyObject? target)
    {
        if (backgroundWorkspace.Visibility == Visibility.Visible) return true;
        for (var current = target; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, backgroundBanner)) return true;
        return false;
    }

    private bool CanDropBackground(IDataObject data) => !_processing && !backgroundWorkspace.IsBusy &&
        (TryGetDraggedGeneration(data, out _, out _) ||
         data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile));

    private async Task DropBackgroundAsync(DragEventArgs e)
    {
        if (!CanDropBackground(e.Data)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        ShowBackgroundWorkspace();
        try
        {
            if (TryGetDraggedGeneration(e.Data, out var source, out var generation))
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
        catch (Exception ex) { statusLabel.Text = $"Не удалось открыть изображение: {ex.Message}"; }
    }
}
