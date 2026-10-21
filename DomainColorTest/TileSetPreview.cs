using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtDownscale;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

public sealed class TileSetPreview : FrameworkElement
{
    public BitmapSource? Source { get; private set; }
    public TileSetLayout? Layout { get; private set; }
    private Rect _imageRect;
    private double _scale;

    public TileSetPreview()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        MouseMove += Hover;
        _ = new PreviewThemeSubscription(this, InvalidateVisual);
    }

    public void SetImage(BitmapSource? image) { Source = image; InvalidateVisual(); }
    public void SetLayout(TileSetLayout? layout) { Layout = layout; InvalidateVisual(); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(PreviewBackgroundBrushes.Resource(this, "Ts.Canvas"), null, new Rect(RenderSize));
        if (Source is null || ActualWidth < 1 || ActualHeight < 1) return;
        _scale = Math.Min(Math.Max(1, ActualWidth - 20) / Source.PixelWidth, Math.Max(1, ActualHeight - 20) / Source.PixelHeight);
        if (_scale >= 1) _scale = Math.Floor(_scale);
        double width = Source.PixelWidth * _scale, height = Source.PixelHeight * _scale;
        _imageRect = new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        drawing.DrawRectangle(PreviewBackgroundBrushes.Create(this, ThemeManager.Current == TesseraTheme.Light ? 3 : 0), null, _imageRect);
        drawing.DrawImage(Source, _imageRect);
        if (Layout is null) return;
        var outer = new Pen(Brushes.Black, 2);
        var inner = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1);
        foreach (var region in TileSetSlicer.GetRegions(Source.PixelWidth, Source.PixelHeight, Layout))
        {
            var rect = new Rect(_imageRect.X + region.X * _scale, _imageRect.Y + region.Y * _scale, region.Width * _scale, region.Height * _scale);
            drawing.DrawRectangle(null, outer, rect); drawing.DrawRectangle(null, inner, rect);
        }
    }

    private void Hover(object sender, MouseEventArgs e)
    {
        if (Source is null || Layout is null || _scale <= 0) return;
        var point = e.GetPosition(this);
        int x = (int)Math.Floor((point.X - _imageRect.X) / _scale) - Layout.MarginX;
        int y = (int)Math.Floor((point.Y - _imageRect.Y) / _scale) - Layout.MarginY;
        int stepX = Layout.TileWidth + Layout.SpacingX, stepY = Layout.TileHeight + Layout.SpacingY;
        if (!_imageRect.Contains(point) || x < 0 || y < 0 || x / stepX >= Layout.Columns || y / stepY >= Layout.Rows ||
            x % stepX >= Layout.TileWidth || y % stepY >= Layout.TileHeight) ToolTip = null;
        else ToolTip = Localization.Format("Тайл {0} · строка {1}, столбец {2}", y / stepY * Layout.Columns + x / stepX + 1, y / stepY + 1, x / stepX + 1);
    }
}
