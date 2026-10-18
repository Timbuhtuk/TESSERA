using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class BackgroundWorkspace
{
    private Button? _workspaceApplyButton;
    private Func<Bitmap, Task>? _workspaceApply;
    public void EnableWorkspaceApply(Func<Bitmap, Task> apply)
    {
        _workspaceApply = apply;
        backgroundOpenButton.Visibility = Visibility.Collapsed;
        backgroundEditorChoices.Visibility = Visibility.Collapsed;
        if (_workspaceApplyButton is null)
        {
            _workspaceApplyButton = new Button { Content = "В историю ассета", Margin = new Thickness(6, 0, 0, 0) };
            ((StackPanel)backgroundSaveButton.Parent).Children.Add(_workspaceApplyButton);
            _workspaceApplyButton.Click += async (_, _) =>
            {
                if (_result is null || IsRendering || _busy) return;
                try { await _workspaceApply!((Bitmap)_result.Clone()); }
                catch (Exception ex) { TesseraDialog.Show(Window.GetWindow(this), ex.Message, "Tessera"); }
            };
        }
        UpdateSaveState();
    }
}

public partial class MoodboardWorkspace
{
    private string? _workspaceSavePath;
    public string? WorkspaceSavePath
    {
        get => _workspaceSavePath;
        set { _workspaceSavePath = value; openProjectButton.Visibility = value is null ? Visibility.Visible : Visibility.Collapsed; }
    }
    public event Action<string>? ProjectSaved;
    public int WorkspaceWidth => (int)boardSurface.Width;
    public int WorkspaceHeight => (int)boardSurface.Height;
    public void CreateWorkspaceBoard(int width, int height) => ResetBoard(width, height);
    public Task SaveWorkspaceAsync() => WorkspaceSavePath is { } path ? SaveToFileAsync(path) : SaveProjectAsync();
    public BitmapSource WorkspaceThumbnail()
    {
        double scale = Math.Min(180 / boardSurface.Width, 128 / boardSurface.Height);
        int width = Math.Max(1, (int)(boardSurface.Width * scale)), height = Math.Max(1, (int)(boardSurface.Height * scale));
        boardSurface.Measure(new Size(boardSurface.Width, boardSurface.Height)); boardSurface.Arrange(new Rect(0, 0, boardSurface.Width, boardSurface.Height));
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen()) draw.DrawRectangle(new VisualBrush(boardSurface), null, new Rect(0, 0, width, height));
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
}

public partial class AsepriteWorkspace
{
    public Func<IReadOnlyList<string>, Task>? WorkspaceImport { get; set; }
    public string? CurrentSheetPath => (sheetStrip.SelectedItem as SheetEntry)?.Result?.ImagePath;
    public void ApplyWorkspaceSettings(WorkspaceAsset asset)
    {
        sheetLayout.SelectedIndex = Math.Clamp(asset.SheetLayout, 0, 2); sheetColumns.Value = asset.SheetColumns; sheetPadding.Value = asset.SheetPadding;
    }
    public void CaptureWorkspaceSettings(WorkspaceAsset asset)
    {
        asset.SheetLayout = sheetLayout.SelectedIndex; asset.SheetColumns = sheetColumns.Value; asset.SheetPadding = sheetPadding.Value;
    }
}

public partial class TileInspectorWindow
{
    public string? WorkspaceSavePath { get; set; }
    public event Action? WorkspaceSaved;
    public void EnableWorkspaceApply(Action<TileAsset> apply)
    {
        var original = ActiveAsset;
        var button = new Button { Content = "В историю ассета", Margin = new Thickness(6, 0, 0, 6), ToolTip = "Сохранить правки исходного тайла новым результатом в библиотеке" };
        workspaceTileToolbar.Children.Add(button);
        button.Click += (_, _) =>
        {
            if (_busy || original is null || original != ActiveAsset) return;
            try { FinishStroke(); apply(original); } catch (Exception ex) { TesseraDialog.Show(this, ex.Message, "Tessera"); }
        };
        LayoutUpdated += (_, _) => button.IsEnabled = !_busy && original is not null && original == ActiveAsset;
    }
    public void MarkWorkspaceApplied() { _projectDirty = false; UpdateProjectTitle(); }
}

public partial class IconWorkspace
{
    public void UseWorkspaceExport()
    {
        iconOpenButton.Visibility = Visibility.Collapsed;
    }
}
