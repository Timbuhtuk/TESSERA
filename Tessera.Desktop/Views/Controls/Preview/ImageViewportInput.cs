using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tessera.Desktop;

internal sealed class ImageViewportInput
{
    internal const string GestureHint = "ImageViewportGestures";
    private readonly ScrollViewer _scroll;
    private bool _dragging;
    private Point _lastPointer;
    private Vector _dragOffset;
    private Vector? _wheelOffset;

    public ImageViewportInput(ScrollViewer scroll)
    {
        _scroll = scroll;
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (ScrollWheel(e.Delta, Keyboard.Modifiers)) e.Handled = true;
        };
        scroll.PreviewMouseDown += MouseDown;
        scroll.PreviewMouseMove += MouseMove;
        scroll.PreviewMouseUp += MouseUp;
        scroll.LostMouseCapture += (_, _) => { if (!_scroll.IsMouseCaptured) EndDrag(); };
        scroll.Unloaded += (_, _) => EndDrag();
        scroll.QueryCursor += (_, e) =>
        {
            if (!_dragging) return;
            e.Cursor = Cursors.SizeAll;
            e.Handled = true;
        };
    }

    internal static bool IsZoomGesture(ModifierKeys modifiers) => modifiers == ModifierKeys.None;

    internal bool ScrollWheel(int delta, ModifierKeys modifiers)
    {
        if (modifiers is not (ModifierKeys.Control or ModifierKeys.Alt) || delta == 0) return false;
        if (_dragging) return true;
        if (!_wheelOffset.HasValue)
            _scroll.Dispatcher.InvokeAsync(() => _wheelOffset = null, DispatcherPriority.Loaded);
        var offset = _wheelOffset ?? new Vector(_scroll.HorizontalOffset, _scroll.VerticalOffset);
        if (modifiers == ModifierKeys.Alt)
        {
            offset.X = Math.Clamp(offset.X - delta, 0, _scroll.ScrollableWidth);
            _scroll.ScrollToHorizontalOffset(offset.X);
        }
        else
        {
            offset.Y = Math.Clamp(offset.Y - delta, 0, _scroll.ScrollableHeight);
            _scroll.ScrollToVerticalOffset(offset.Y);
        }
        _wheelOffset = offset;
        return true;
    }

    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || Mouse.Captured is not null ||
            (_scroll.ScrollableWidth <= 0 && _scroll.ScrollableHeight <= 0)) return;
        for (var node = e.OriginalSource as DependencyObject; node is Visual && !ReferenceEquals(node, _scroll); node = VisualTreeHelper.GetParent(node))
            if (node is ScrollBar) return;

        if (!_scroll.CaptureMouse()) return;
        _lastPointer = e.GetPosition(_scroll);
        _dragOffset = _wheelOffset ?? new Vector(_scroll.HorizontalOffset, _scroll.VerticalOffset);
        _dragging = true;
        Mouse.UpdateCursor();
        e.Handled = true;
    }

    private void MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.MiddleButton != MouseButtonState.Pressed) { EndDrag(); return; }

        DragTo(e.GetPosition(_scroll));
        e.Handled = true;
    }

    internal void DragTo(Point pointer)
    {
        if (!_dragging) return;
        _dragOffset = new Vector(
            Math.Clamp(_dragOffset.X + _lastPointer.X - pointer.X, 0, _scroll.ScrollableWidth),
            Math.Clamp(_dragOffset.Y + _lastPointer.Y - pointer.Y, 0, _scroll.ScrollableHeight));
        _lastPointer = pointer;
        _scroll.ScrollToHorizontalOffset(_dragOffset.X);
        _scroll.ScrollToVerticalOffset(_dragOffset.Y);
    }

    private void MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || e.ChangedButton != MouseButton.Middle) return;
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_scroll.IsMouseCaptured) _scroll.ReleaseMouseCapture();
        Mouse.UpdateCursor();
    }
}
