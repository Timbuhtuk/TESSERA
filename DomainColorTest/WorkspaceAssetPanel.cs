using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DomainColorTest;

/// <summary>Fixed-size asset grid that creates containers only for visible rows.</summary>
public sealed class WorkspaceAssetPanel : VirtualizingPanel, IScrollInfo
{
    private Size _extent, _viewport;
    private double _verticalOffset;
    private int _columns = 1;

    public double CellWidth { get; set; }
    public double CellHeight { get; set; } = 66;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _verticalOffset;
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new Size();
        double width = double.IsInfinity(availableSize.Width) ? Math.Max(1, ActualWidth) : Math.Max(1, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? CellHeight * 3 : Math.Max(1, availableSize.Height);
        _viewport = new Size(width, height);
        _columns = CellWidth > 0 ? Math.Max(1, (int)(width / CellWidth)) : 1;
        int rows = (owner.Items.Count + _columns - 1) / _columns;
        _extent = new Size(width, rows * CellHeight);
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();

        int first = Math.Max(0, (int)(_verticalOffset / CellHeight) * _columns);
        int last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((_verticalOffset + height) / CellHeight) + 1) * _columns - 1);
        var visible = first <= last ? Enumerable.Range(first, last - first + 1).ToArray() : [];
        var wanted = visible.ToHashSet();
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        for (int q = children.Count - 1; q >= 0; q--)
        {
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(q, 0));
            if (wanted.Contains(index)) continue;
            generator.Remove(new GeneratorPosition(q, 0), 1);
            RemoveInternalChildRange(q, 1);
        }
        double cellWidth = CellWidth > 0 ? CellWidth : width;
        foreach (int index in visible)
        {
            var position = generator.GeneratorPositionFromIndex(index);
            int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                var child = (UIElement)generator.GenerateNext(out bool created);
                if (created)
                {
                    InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(cellWidth, CellHeight));
            }
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cellWidth = CellWidth > 0 ? CellWidth : finalSize.Width;
        for (int q = 0; q < InternalChildren.Count; q++)
        {
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(q, 0));
            if (index < 0) continue;
            double x = index % _columns * cellWidth;
            double y = index / _columns * CellHeight - _verticalOffset;
            InternalChildren[q].Arrange(new Rect(x, y, cellWidth, CellHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        ItemContainerGenerator.RemoveAll();
        RemoveInternalChildRange(0, InternalChildren.Count);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0) return;
        double top = index / _columns * CellHeight;
        if (top < VerticalOffset) SetVerticalOffset(top);
        else if (top + CellHeight > VerticalOffset + ViewportHeight)
            SetVerticalOffset(top + CellHeight - ViewportHeight);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is not null && ItemsControl.ContainerFromElement(owner, visual) is ListBoxItem item)
            BringIndexIntoView(owner.ItemContainerGenerator.IndexFromContainer(item));
        return rectangle;
    }

    public void SetHorizontalOffset(double offset) { }
    public void SetVerticalOffset(double offset)
    {
        double next = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(next - _verticalOffset) < 0.5) return;
        _verticalOffset = next;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - CellHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + CellHeight);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void MouseWheelUp() => LineUp();
    public void MouseWheelDown() => LineDown();
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
