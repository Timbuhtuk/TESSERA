using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace Tessera.Desktop;

public sealed record ColorArea(DrawingColor? Color, long Pixels);

public sealed class ColorTreemap : FrameworkElement
{
    private readonly List<(ColorArea Area, Rect Bounds)> _cells = [];
    private readonly Dictionary<int, Brush> _brushes = [];
    private IReadOnlyList<ColorArea> _areas = [];
    private long _totalPixels;

    public IReadOnlyList<ColorArea> Areas => _areas;
    public event Action<DrawingColor>? ColorSelected;

    public void SetAreas(IReadOnlyList<ColorArea> areas)
    {
        _areas = areas;
        _totalPixels = areas.Sum(area => area.Pixels);
        _brushes.Clear();
        foreach (var area in areas)
        {
            if (area.Color is not { } color || _brushes.ContainsKey(color.ToArgb())) continue;
            var brush = new SolidColorBrush(MediaColor.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            _brushes.Add(color.ToArgb(), brush);
        }
        ArrangeCells();
        InvalidateVisual();
    }

    public void SelectColor(DrawingColor color)
    {
        if (_areas.Any(area => area.Color?.ToArgb() == color.ToArgb())) ColorSelected?.Invoke(color);
    }

    public Rect? BoundsFor(DrawingColor color)
    {
        foreach (var cell in _cells)
            if (cell.Area.Color?.ToArgb() == color.ToArgb()) return cell.Bounds;
        return null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ArrangeCells();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var background = TryFindResource("Ts.Raised") as Brush ?? Brushes.Transparent;
        var other = TryFindResource("Ts.TextMuted") as Brush ?? Brushes.Gray;
        var outline = new Pen(TryFindResource("Ts.Canvas") as Brush ?? Brushes.Black, 1);
        drawingContext.DrawRectangle(background, null, new Rect(RenderSize));
        foreach (var cell in _cells)
        {
            Brush brush = cell.Area.Color is { } color ? _brushes[color.ToArgb()] : other;
            drawingContext.DrawRectangle(brush, cell.Bounds.Width >= 3 && cell.Bounds.Height >= 3 ? outline : null, cell.Bounds);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = _cells.FirstOrDefault(item => item.Bounds.Contains(e.GetPosition(this)));
        if (cell.Area is null) { ToolTip = null; Cursor = null; return; }
        double percent = _totalPixels == 0 ? 0 : cell.Area.Pixels / (double)_totalPixels;
        ToolTip = cell.Area.Color is { } color
            ? Localization.Format("PixelsFormat", PaletteColorEntry.FormatHex(color), cell.Area.Pixels, percent)
            : Localization.Format("OtherColorsPixelsFormat", cell.Area.Pixels, percent);
        Cursor = cell.Area.Color is null ? null : Cursors.Hand;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ToolTip = null;
        Cursor = null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var cell = _cells.FirstOrDefault(item => item.Bounds.Contains(e.GetPosition(this)));
        if (cell.Area?.Color is not { } color) return;
        SelectColor(color);
        e.Handled = true;
    }

    private void ArrangeCells()
    {
        _cells.Clear();
        if (_areas.Count == 0 || _totalPixels == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        Layout(0, _areas.Count, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void Layout(int start, int end, Rect bounds)
    {
        if (end - start == 1) { _cells.Add((_areas[start], bounds)); return; }
        long total = 0;
        for (int q = start; q < end; q++) total += _areas[q].Pixels;
        long left = 0;
        int split = start + 1;
        long closest = long.MaxValue;
        for (int q = start; q < end - 1; q++)
        {
            left += _areas[q].Pixels;
            long difference = Math.Abs(total - 2 * left);
            if (difference < closest) { closest = difference; split = q + 1; }
        }
        left = 0;
        for (int q = start; q < split; q++) left += _areas[q].Pixels;
        double fraction = left / (double)total;
        if (bounds.Width >= bounds.Height)
        {
            double width = bounds.Width * fraction;
            Layout(start, split, new Rect(bounds.X, bounds.Y, width, bounds.Height));
            Layout(split, end, new Rect(bounds.X + width, bounds.Y, bounds.Width - width, bounds.Height));
        }
        else
        {
            double height = bounds.Height * fraction;
            Layout(start, split, new Rect(bounds.X, bounds.Y, bounds.Width, height));
            Layout(split, end, new Rect(bounds.X, bounds.Y + height, bounds.Width, bounds.Height - height));
        }
    }
}
