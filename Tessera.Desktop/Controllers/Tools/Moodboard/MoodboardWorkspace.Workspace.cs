using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tessera.Desktop;

public partial class MoodboardWorkspace
{
    public Task ExportDialogAsync() => ExportPngAsync();
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
    internal void ReleaseDocument()
    {
        _dragged = null;
        _dragStart.Clear();
        WorkspaceSavePath = null;
        ShowStart();
    }
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
