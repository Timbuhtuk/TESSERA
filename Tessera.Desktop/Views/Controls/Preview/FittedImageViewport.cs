using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Tessera.Desktop;

internal sealed class FittedImageViewport
{
    private readonly ScrollViewer _scroll;
    private readonly FrameworkElement _surface;
    private readonly Func<BitmapSource?> _image;
    private readonly double _inset;
    private readonly bool _wholePixelFit;
    private double _zoom;

    public FittedImageViewport(ScrollViewer scroll, FrameworkElement surface, Func<BitmapSource?> image,
        double inset = 0, bool wholePixelFit = false)
    {
        _scroll = scroll;
        _surface = surface;
        _image = image;
        _inset = inset;
        _wholePixelFit = wholePixelFit;
        scroll.ToolTip = Localization.Text(ImageViewportInput.GestureHint);
        _ = new ImageViewportInput(scroll);
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (!ImageViewportInput.IsZoomGesture(Keyboard.Modifiers) || e.Delta == 0 || _image() is not { } source) return;
            Zoom(source, e.GetPosition(_scroll), e.Delta);
            e.Handled = true;
        };
    }

    public void Reset()
    {
        _zoom = 0;
        _surface.Width = _surface.Height = double.NaN;
        _scroll.HorizontalScrollBarVisibility = _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.ScrollToHome();
    }

    private void Zoom(BitmapSource source, Point pointer, int delta)
    {
        var margin = _surface.Margin;
        double fit = Math.Min(
            Math.Max(1, _scroll.ActualWidth - margin.Left - margin.Right - 2 * _inset) / source.PixelWidth,
            Math.Max(1, _scroll.ActualHeight - margin.Top - margin.Bottom - 2 * _inset) / source.PixelHeight);
        if (_wholePixelFit && fit >= 1) fit = Math.Floor(fit);
        double scale = _zoom > 0 ? _zoom : fit;
        double next = Math.Min(Math.Max(64, fit), scale * Math.Pow(2, Math.Clamp(delta / 120d, -6, 6)));
        if (next <= fit) { Reset(); return; }
        if (next == scale) return;

        var position = _scroll.TranslatePoint(pointer, _surface);
        double x = Math.Clamp((position.X - (_surface.ActualWidth - source.PixelWidth * scale) / 2) / scale, 0, source.PixelWidth);
        double y = Math.Clamp((position.Y - (_surface.ActualHeight - source.PixelHeight * scale) / 2) / scale, 0, source.PixelHeight);
        _zoom = next;
        _scroll.HorizontalScrollBarVisibility = _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _surface.Width = source.PixelWidth * next + 2 * _inset;
        _surface.Height = source.PixelHeight * next + 2 * _inset;
        _scroll.UpdateLayout();
        var projected = _surface.TranslatePoint(new Point(x * next + _inset, y * next + _inset), _scroll);
        _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset + projected.X - pointer.X);
        _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + projected.Y - pointer.Y);
    }
}
