using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessera.Desktop;

public sealed class PaletteSwatches : FrameworkElement
{
    private IReadOnlyList<int> _colors = [];
    private const double CellWidth = 24;
    private const double CellHeight = 20;
    private const double Gap = 2;

    public void SetColors(IReadOnlyList<int> colors)
    {
        _colors = colors;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = Columns(availableSize.Width);
        int rows = (_colors.Count + columns - 1) / columns;
        return new Size(double.IsInfinity(availableSize.Width) ? columns * (CellWidth + Gap) : availableSize.Width,
            rows * (CellHeight + Gap));
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        int columns = Columns(ActualWidth);
        var border = new Pen(TryFindResource("Ts.Canvas") as Brush ?? Brushes.Black, 1);
        var scroll = FindScrollViewer();
        double offset = scroll?.VerticalOffset ?? 0;
        double height = scroll is { ViewportHeight: > 0 } ? scroll.ViewportHeight : ActualHeight;
        int first = (int)(offset / (CellHeight + Gap)) * columns;
        int end = Math.Min(_colors.Count, (int)Math.Ceiling((offset + height) / (CellHeight + Gap)) * columns);
        for (int q = first; q < end; q++)
        {
            int rgb = _colors[q];
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            context.DrawRectangle(brush, border, new Rect((q % columns) * (CellWidth + Gap),
                (q / columns) * (CellHeight + Gap), CellWidth, CellHeight));
        }
    }

    private ScrollViewer? FindScrollViewer()
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll) return scroll;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        int columns = Columns(ActualWidth);
        int column = (int)(point.X / (CellWidth + Gap));
        int row = (int)(point.Y / (CellHeight + Gap));
        int index = row * columns + column;
        ToolTip = column >= 0 && column < columns && index >= 0 && index < _colors.Count &&
                  point.X % (CellWidth + Gap) < CellWidth && point.Y % (CellHeight + Gap) < CellHeight
            ? $"#{_colors[index]:X6}" : null;
    }

    private static int Columns(double width) => double.IsInfinity(width) ? 12 : Math.Max(1, (int)((width + Gap) / (CellWidth + Gap)));
}
