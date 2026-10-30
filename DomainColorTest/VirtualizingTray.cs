using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DomainColorTest;

public sealed class TrayTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container)
        => (DataTemplate)((FrameworkElement)container).FindResource(item switch
        {
            TrayFolderHeader => "TrayFolderTemplate",
            HistorySourceRoot => "HistorySourceTemplate",
            GenerationEntry => "GenerationCardTemplate",
            _ => "TrayCardTemplate"
        });
}

public sealed class HistorySourceRoot
{
    public HistorySourceRoot(SourceEntry source) => Source = source;
    public SourceEntry Source { get; }
    public string Label => Source.Label;
    public string Description => Source.Description;
    public object? Thumbnail => Source.Thumbnail;
}

/// <summary>Pixel-scrolled, two-dimensional filmstrip. Only intersecting cards get containers.</summary>
public sealed class VirtualizingTray : ListBox
{
    internal Rect[] CardBounds { get; private set; } = [];
    internal IReadOnlyList<(Point From, Point To)> Connections { get; private set; } = [];
    internal SortedList<double, List<int>> Rows { get; } = [];
    internal TrayPanel? Panel { get; set; }
    private double? _insertionY;
    internal double? InsertionY { get => _insertionY; set { _insertionY = value; Panel?.InvalidateVisual(); } }
    public Point ScrollPosition => new(Panel?.HorizontalOffset ?? 0, Panel?.VerticalOffset ?? 0);
    public event EventHandler? ScrollPositionChanged;
    internal void OnScrollChanged() => ScrollPositionChanged?.Invoke(this, EventArgs.Empty);

    public void SetLayout(IReadOnlyList<Rect> bounds, IReadOnlyList<(Point From, Point To)>? connections = null)
    {
        CardBounds = bounds.ToArray(); Connections = connections ?? [];
        Rows.Clear();
        for (int q = 0; q < bounds.Count; q++)
        {
            double y = bounds[q].Y;
            if (!Rows.TryGetValue(y, out var row)) Rows.Add(y, row = []);
            row.Add(q);
        }
        Panel?.InvalidateMeasure(); Panel?.InvalidateVisual();
    }

    internal IEnumerable<int> VisibleIndices(Rect viewport)
    {
        int lo = 0, hi = Rows.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (Rows.Keys[mid] < viewport.Top - 78) lo = mid + 1; else hi = mid; }
        for (int q = lo; q < Rows.Count && Rows.Keys[q] <= viewport.Bottom; q++)
            foreach (int index in Rows.Values[q])
                if (index < Items.Count && CardBounds[index].IntersectsWith(viewport)) yield return index;
    }

    public void RestoreScroll(Point point)
    {
        Panel?.SetHorizontalOffset(point.X); Panel?.SetVerticalOffset(point.Y);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Panel is null) { base.OnPreviewMouseWheel(e); return; }
        bool horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Panel.ExtentHeight <= Panel.ViewportHeight + 1;
        int steps = (int)Math.Max(1, Math.Abs((long)e.Delta) / 120);
        for (int q = 0; q < steps; q++) Panel.ScrollByCard(e.Delta < 0, horizontal);
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
        {
            int current = SelectedIndex;
            if (CardBounds.Length == Items.Count && CardBounds.Length > 0)
            {
                int next = -1;
                if (e.Key is Key.Home or Key.End)
                {
                    var candidates = Enumerable.Range(0, Items.Count).Where(q => Items[q] is not TrayFolderHeader);
                    next = e.Key == Key.Home ? candidates.FirstOrDefault(-1) : candidates.LastOrDefault(-1);
                }
                else
                {
                    var from = current >= 0 ? CardBounds[current] : CardBounds[0];
                    double best = double.MaxValue;
                    for (int q = 0; q < Items.Count; q++)
                    {
                        if (q == current || Items[q] is TrayFolderHeader) continue;
                        var target = CardBounds[q]; double dx = target.X - from.X, dy = target.Y - from.Y;
                        bool horizontal = e.Key is Key.Left or Key.Right;
                        double primary = horizontal ? dx : dy, cross = horizontal ? dy : dx;
                        if ((e.Key is Key.Left or Key.Up ? -primary : primary) <= 0) continue;
                        double score = Math.Abs(primary) + Math.Abs(cross) * 4;
                        if (score < best) { best = score; next = q; }
                    }
                }
                if (next >= 0) { SelectedIndex = next; ScrollIntoView(Items[next]); }
                e.Handled = true; return;
            }
        }
        base.OnPreviewKeyDown(e);
    }
}

public sealed class TrayFolderHeader
{
    public bool IsHeader => true;
    public Guid? FolderId { get; init; }
    public string Label { get; init; } = "";
    public bool IsEmpty { get; init; }
    public Action? Activate { get; init; }
}

public sealed class TrayPanel : VirtualizingPanel, IScrollInfo
{
    private VirtualizingTray? Owner => ItemsControl.GetItemsOwner(this) as VirtualizingTray;
    private Size _extent, _viewport;
    private Point _offset;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Owner is not { } owner) return new Size();
        owner.Panel = this;
        _viewport = new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 78 : availableSize.Height);
        _extent = new Size(owner.CardBounds.Select(r => r.Right + 4).DefaultIfEmpty(0).Max(),
            owner.CardBounds.Select(r => r.Bottom + 4).DefaultIfEmpty(0).Max());
        _offset.X = Math.Clamp(_offset.X, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        _offset.Y = Math.Clamp(_offset.Y, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();
        var visible = owner.VisibleIndices(new Rect(_offset, _viewport)).Order().ToArray();
        var wanted = visible.ToHashSet();
        // Accessing InternalChildren initializes the generator on the first measure.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        for (int q = InternalChildren.Count - 1; q >= 0; q--)
        {
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(q, 0));
            if (wanted.Contains(index)) continue;
            generator.Remove(new GeneratorPosition(q, 0), 1); RemoveInternalChildRange(q, 1);
        }
        foreach (int index in visible)
        {
            var position = generator.GeneratorPositionFromIndex(index);
            int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                var child = (UIElement)generator.GenerateNext(out bool created);
                if (created)
                {
                    InsertInternalChild(childIndex, child); generator.PrepareItemContainer(child);
                }
                child.Measure(owner.CardBounds[index].Size);
            }
        }
        InvalidateVisual();
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Owner is not { } owner) return finalSize;
        for (int q = 0; q < InternalChildren.Count; q++)
        {
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(q, 0));
            if (index < 0 || index >= owner.CardBounds.Length) continue;
            var bounds = owner.CardBounds[index]; bounds.Offset(-_offset.X, -_offset.Y);
            InternalChildren[q].Arrange(bounds);
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        ItemContainerGenerator.RemoveAll();
        RemoveInternalChildRange(0, InternalChildren.Count);
        InvalidateMeasure();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Owner is not { } owner) return;
        var pen = new Pen(TryFindResource("Ts.TextSecondary") as Brush ?? Brushes.Gray, 1);
        dc.PushClip(new RectangleGeometry(new Rect(new Point(), RenderSize)));
        foreach (var (from, to) in owner.Connections)
        {
            if (Math.Max(from.Y, to.Y) < VerticalOffset || Math.Min(from.Y, to.Y) > VerticalOffset + ViewportHeight) continue;
            if (Math.Max(from.X, to.X) < HorizontalOffset || Math.Min(from.X, to.X) > HorizontalOffset + ViewportWidth) continue;
            var a = new Point(from.X - HorizontalOffset, from.Y - VerticalOffset);
            var b = new Point(to.X - HorizontalOffset, to.Y - VerticalOffset);
            double x = (a.X + b.X) / 2;
            dc.DrawLine(pen, a, new Point(x, a.Y)); dc.DrawLine(pen, new Point(x, a.Y), new Point(x, b.Y)); dc.DrawLine(pen, new Point(x, b.Y), b);
        }
        if (owner.InsertionY is { } insertion)
            dc.DrawLine(new Pen(TryFindResource("Ts.Accent") as Brush ?? Brushes.White, 2),
                new Point(0, insertion - VerticalOffset), new Point(RenderSize.Width, insertion - VerticalOffset));
        dc.Pop();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (Owner is not { } owner || index < 0 || index >= owner.CardBounds.Length) return;
        Bring(owner.CardBounds[index]);
    }

    private void Bring(Rect rect)
    {
        if (rect.Left < HorizontalOffset) SetHorizontalOffset(rect.Left);
        else if (rect.Right > HorizontalOffset + ViewportWidth) SetHorizontalOffset(rect.Right - ViewportWidth);
        if (rect.Top < VerticalOffset) SetVerticalOffset(rect.Top);
        else if (rect.Bottom > VerticalOffset + ViewportHeight) SetVerticalOffset(rect.Bottom - ViewportHeight);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (Owner is { } owner && ItemsControl.ContainerFromElement(owner, visual) is ListBoxItem container)
            BringIndexIntoView(owner.ItemContainerGenerator.IndexFromContainer(container));
        return rectangle;
    }
    public void SetHorizontalOffset(double offset) => SetOffset(new Point(Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth)), _offset.Y));
    public void SetVerticalOffset(double offset) => SetOffset(new Point(_offset.X, Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight))));
    private void SetOffset(Point offset)
    {
        if (_offset == offset) return;
        _offset = offset; InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo(); Owner?.OnScrollChanged();
    }
    internal void ScrollByCard(bool forward, bool horizontal)
    {
        if (Owner is not { } owner || owner.CardBounds.Length == 0) return;
        double maximum = Math.Max(0, horizontal ? ExtentWidth - ViewportWidth : ExtentHeight - ViewportHeight);
        if (maximum < 1) return;
        double current = horizontal ? HorizontalOffset : VerticalOffset;
        double origin = owner.CardBounds.Min(bounds => horizontal ? bounds.Left : bounds.Top);
        var anchors = owner.CardBounds.Select(bounds => Math.Clamp((horizontal ? bounds.Left : bounds.Top) - origin, 0, maximum))
            .Append(0).Append(maximum).Distinct().Order().ToArray();
        double target = forward ? anchors.FirstOrDefault(value => value > current + .5, maximum)
            : anchors.LastOrDefault(value => value < current - .5, 0);
        if (horizontal) SetHorizontalOffset(target); else SetVerticalOffset(target);
    }
    public void LineLeft() => ScrollByCard(false, true);
    public void LineRight() => ScrollByCard(true, true);
    public void LineUp() => ScrollByCard(false, false);
    public void LineDown() => ScrollByCard(true, false);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();
    public void MouseWheelUp() => ScrollByCard(false, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || ExtentHeight <= ViewportHeight + 1);
    public void MouseWheelDown() => ScrollByCard(true, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || ExtentHeight <= ViewportHeight + 1);
}
