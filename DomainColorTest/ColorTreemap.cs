using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace DomainColorTest;

public sealed record ColorArea(DrawingColor? Color, long Pixels);

public sealed class ColorTreemap : FrameworkElement
{
    private readonly List<(ColorArea Area, Rect Bounds)> _cells = [];
    private readonly Dictionary<int, Brush> _brushes = [];
    private IReadOnlyList<ColorArea> _areas = [];
    private long _totalPixels;
    private int _selectedIndex = -1;

    public ColorTreemap() => Focusable = true;

    public IReadOnlyList<ColorArea> Areas => _areas;
    public event Action<DrawingColor>? ColorSelected;

    public void SetAreas(IReadOnlyList<ColorArea> areas)
    {
        _areas = areas;
        _totalPixels = areas.Sum(area => area.Pixels);
        _selectedIndex = Enumerable.Range(0, areas.Count).FirstOrDefault(q => areas[q].Color is not null, -1);
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
        int index = Enumerable.Range(0, _areas.Count).FirstOrDefault(q => _areas[q].Color?.ToArgb() == color.ToArgb(), -1);
        if (index < 0) return;
        _selectedIndex = index;
        InvalidateVisual();
        ColorSelected?.Invoke(color);
    }

    public bool SelectColorAt(Point point)
    {
        var cell = _cells.FirstOrDefault(item => item.Bounds.Contains(point));
        if (cell.Area?.Color is not { } color) return false;
        SelectColor(color);
        return true;
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
        if (IsKeyboardFocusWithin && _selectedIndex >= 0 && _selectedIndex < _cells.Count)
            drawingContext.DrawRectangle(null, new Pen(TryFindResource("Ts.TextSecondary") as Brush ?? Brushes.White, 2), _cells[_selectedIndex].Bounds);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = _cells.FirstOrDefault(item => item.Bounds.Contains(e.GetPosition(this)));
        if (cell.Area is null) { ToolTip = null; Cursor = null; return; }
        double percent = _totalPixels == 0 ? 0 : cell.Area.Pixels / (double)_totalPixels;
        ToolTip = cell.Area.Color is { } color
            ? Localization.Format("{0} · {1:N0} пикселей ({2:P1})", PaletteColorEntry.FormatHex(color), cell.Area.Pixels, percent)
            : Localization.Format("Остальные цвета · {0:N0} пикселей ({1:P1})", cell.Area.Pixels, percent);
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
        Focus();
        e.Handled = SelectColorAt(e.GetPosition(this));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int[] selectable = Enumerable.Range(0, _areas.Count).Where(q => _areas[q].Color is not null).ToArray();
        if (selectable.Length == 0) return;
        int current = Math.Max(0, Array.IndexOf(selectable, _selectedIndex));
        switch (e.Key)
        {
            case Key.Left: case Key.Up: current = Math.Max(0, current - 1); break;
            case Key.Right: case Key.Down: current = Math.Min(selectable.Length - 1, current + 1); break;
            case Key.Home: current = 0; break;
            case Key.End: current = selectable.Length - 1; break;
            case Key.Enter: case Key.Space:
                SelectColor(_areas[selectable[current]].Color!.Value);
                e.Handled = true;
                return;
            default: return;
        }
        _selectedIndex = selectable[current];
        InvalidateVisual();
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
