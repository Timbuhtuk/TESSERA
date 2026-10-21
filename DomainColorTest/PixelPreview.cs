using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public sealed class PixelColorPickedEventArgs(System.Drawing.Color color, int x, int y) : EventArgs
{
    public System.Drawing.Color Color { get; } = color;
    public int Rgb => Color.ToArgb() & 0xFFFFFF;
    public int X { get; } = x;
    public int Y { get; } = y;
}

/// <summary>Native WPF preview: integer device-pixel zoom, alpha and scrolling.</summary>
public sealed class PixelPreview : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly Surface _surface = new();
    private readonly TextBlock _empty;
    private Bitmap? _image;
    private double _zoom;
    private int _background;
    private double _relativeX, _relativeY;
    private bool _applyingScroll;
    private int _scrollRevision;
    private bool _isColorPicking, _continuousFit;
    public event EventHandler? ScrollPositionChanged;
    public event EventHandler? ImageChanged;
    public event EventHandler<PixelColorPickedEventArgs>? ColorPicked;
    public (double X, double Y) ScrollPosition => (_relativeX, _relativeY);
    public double EffectiveZoom => _surface.Source is { } source ? _surface.ImageWidth * VisualTreeHelper.GetDpi(this).DpiScaleX / source.PixelWidth : 1;
    public Bitmap? Image
    {
        get => _image;
        set
        {
            _surface.Source = value is null ? null : ToBitmapSource(value);
            _surface.HighlightRgb = null;
            _surface.HighlightMask = null;
            _image = value;
            _empty.Visibility = value is null ? Visibility.Visible : Visibility.Collapsed;
            UpdateScale();
            ImageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string EmptyText { get => _empty.Text; set => _empty.Text = value; }
    public double Zoom { get => _zoom; set { _zoom = value; UpdateScale(); } }
    public int PreviewBackground { get => _background; set { _background = value; UpdateBackground(); } }
    public bool ContinuousFit { get => _continuousFit; set { _continuousFit = value; UpdateScale(); } }
    /// <summary>Source-pixel rectangle to outline; overlays never modify the bitmap.</summary>
    public Rect? SelectedRegion { get => _surface.SelectedRegion; set { _surface.SelectedRegion = value; _surface.InvalidateVisual(); } }
    public bool IsColorPicking
    {
        get => _isColorPicking;
        set { _isColorPicking = value; Cursor = value ? Cursors.Cross : null; }
    }
    public int? HighlightRgb
    {
        get => _surface.HighlightRgb;
        set
        {
            int? rgb = value.HasValue ? value.Value & 0xFFFFFF : null;
            if (_surface.HighlightRgb == rgb) return;
            _surface.HighlightRgb = rgb;
            UpdateHighlightMask();
        }
    }

    public PixelPreview()
    {
        ClipToBounds = true;
        _scroll = new ScrollViewer { Content = _surface, Background = Brushes.Transparent, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        Children.Add(_scroll);
        _empty = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16), IsHitTestVisible = false };
        Children.Add(_empty);
        SizeChanged += (_, _) => UpdateScale();
        _ = new PreviewThemeSubscription(this, RefreshTheme);
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            for (var current = e.OriginalSource as DependencyObject; current is not null && !ReferenceEquals(current, this); current = VisualTreeHelper.GetParent(current))
                if (current is ScrollBar) return;
            if (!IsColorPicking || _image is null || !TryGetImagePixel(e.GetPosition(this), out int x, out int y)) return;
            var color = _image.GetPixel(x, y);
            if (color.A == 0) return;
            e.Handled = true;
            ColorPicked?.Invoke(this, new PixelColorPickedEventArgs(color, x, y));
        };
        _scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0 || e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0)
            {
                UpdateScale();
                ScrollToRelativePosition(_relativeX, _relativeY);
                return;
            }
            if (_applyingScroll || (e.HorizontalChange == 0 && e.VerticalChange == 0)) return;
            if (_scroll.ScrollableWidth > 0) _relativeX = _scroll.HorizontalOffset / _scroll.ScrollableWidth;
            if (_scroll.ScrollableHeight > 0) _relativeY = _scroll.VerticalOffset / _scroll.ScrollableHeight;
            ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
        };
        UpdateBackground();
    }

    public bool TryGetImagePixel(Point viewportPoint, out int x, out int y)
    {
        x = y = 0;
        if (_surface.Source is not { } source) return false;
        if (!Inside(viewportPoint, ActualWidth, ActualHeight)) return false;
        // Captured pointers can leave the control while the scrolled image still extends beneath them.
        // The content presenter excludes scrollbars and any template chrome from the visible viewport.
        if (_scroll.Template?.FindName("PART_ScrollContentPresenter", _scroll) is not ScrollContentPresenter viewport ||
            !Inside(TranslatePoint(viewportPoint, viewport), viewport.ActualWidth, viewport.ActualHeight)) return false;
        Point point = TranslatePoint(viewportPoint, _surface);
        Rect bounds = _surface.ImageBounds;
        if (!bounds.Contains(point) || bounds.Width <= 0 || bounds.Height <= 0) return false;
        x = Math.Clamp((int)((point.X - bounds.X) * source.PixelWidth / bounds.Width), 0, source.PixelWidth - 1);
        y = Math.Clamp((int)((point.Y - bounds.Y) * source.PixelHeight / bounds.Height), 0, source.PixelHeight - 1);
        return true;

        static bool Inside(Point point, double width, double height)
            => double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X >= 0 && point.Y >= 0 && point.X < width && point.Y < height;
    }

    private void UpdateHighlightMask()
    {
        _surface.HighlightMask = null;
        if (_surface.Source is { } source && _surface.HighlightRgb is int rgb)
        {
            int stride = checked(source.PixelWidth * 4);
            var pixels = new byte[checked(stride * source.PixelHeight)];
            source.CopyPixels(pixels, stride, 0);
            Color accent = ThemeBrush("Ts.Accent").Color, canvas = ThemeBrush("Ts.Canvas").Color;
            int Difference(Color color) => Math.Abs(color.R - (rgb >> 16 & 255)) + Math.Abs(color.G - (rgb >> 8 & 255)) + Math.Abs(color.B - (rgb & 255));
            Color highlight = Difference(accent) >= Difference(canvas) ? accent : canvas;
            for (int q = 0; q < pixels.Length; q += 4)
            {
                bool matches = pixels[q + 3] > 0 && (pixels[q + 2] << 16 | pixels[q + 1] << 8 | pixels[q]) == rgb;
                pixels[q] = highlight.B; pixels[q + 1] = highlight.G; pixels[q + 2] = highlight.R;
                pixels[q + 3] = matches ? (byte)180 : (byte)0;
            }
            var mask = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            mask.Freeze(); _surface.HighlightMask = mask;
        }
        _surface.InvalidateVisual();
    }

    public void ScrollToRelativePosition(double x, double y)
    {
        _relativeX = double.IsFinite(x) ? Math.Clamp(x, 0, 1) : 0;
        _relativeY = double.IsFinite(y) ? Math.Clamp(y, 0, 1) : 0;
        _applyingScroll = true;
        int revision = ++_scrollRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (revision != _scrollRevision) return;
            _scroll.ScrollToHorizontalOffset(_relativeX * _scroll.ScrollableWidth);
            _scroll.ScrollToVerticalOffset(_relativeY * _scroll.ScrollableHeight);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (revision == _scrollRevision) _applyingScroll = false;
            }));
        }));
    }

    public Point ImagePointAt(Point viewportPoint)
    {
        double left = Math.Max(0, (_surface.ActualWidth - _surface.ImageWidth) / 2);
        double top = Math.Max(0, (_surface.ActualHeight - _surface.ImageHeight) / 2);
        return new Point(
            Math.Clamp((_scroll.HorizontalOffset + viewportPoint.X - left) / Math.Max(1, _surface.ImageWidth), 0, 1),
            Math.Clamp((_scroll.VerticalOffset + viewportPoint.Y - top) / Math.Max(1, _surface.ImageHeight), 0, 1));
    }

    public void PlaceImagePointAt(Point imagePoint, Point viewportPoint)
    {
        UpdateLayout();
        double left = Math.Max(0, (_surface.ActualWidth - _surface.ImageWidth) / 2);
        double top = Math.Max(0, (_surface.ActualHeight - _surface.ImageHeight) / 2);
        ScrollToRelativePosition(
            _scroll.ScrollableWidth > 0 ? (left + imagePoint.X * _surface.ImageWidth - viewportPoint.X) / _scroll.ScrollableWidth : _relativeX,
            _scroll.ScrollableHeight > 0 ? (top + imagePoint.Y * _surface.ImageHeight - viewportPoint.Y) / _scroll.ScrollableHeight : _relativeY);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateScale();
    }

    private void UpdateScale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        bool fit = _zoom <= 0;
        _scroll.HorizontalScrollBarVisibility = _scroll.VerticalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        double availableWidth = Math.Max(1, _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : ActualWidth);
        double availableHeight = Math.Max(1, _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : ActualHeight);
        if (_surface.Source is { } source)
        {
            double scale = fit ? Math.Min(Math.Max(1, availableWidth * dpi.DpiScaleX - 16) / source.PixelWidth, Math.Max(1, availableHeight * dpi.DpiScaleY - 16) / source.PixelHeight) : _zoom;
            if (fit && !_continuousFit && scale >= 1) scale = Math.Floor(scale);
            _surface.ImageWidth = (_continuousFit ? source.PixelWidth * scale : Math.Max(1, Math.Round(source.PixelWidth * scale))) / dpi.DpiScaleX;
            _surface.ImageHeight = (_continuousFit ? source.PixelHeight * scale : Math.Max(1, Math.Round(source.PixelHeight * scale))) / dpi.DpiScaleY;
        }
        else _surface.ImageWidth = _surface.ImageHeight = 0;
        // Let the scroll presenter stretch small images without feeding its viewport back into the extent.
        _surface.MinWidth = _surface.ImageWidth;
        _surface.MinHeight = _surface.ImageHeight;
        _surface.InvalidateVisual();
    }

    private void UpdateBackground()
    {
        bool lightBackground = _background is 2 or 3;
        Background = PreviewBackgroundBrushes.Create(this, _background);
        _empty.Foreground = PreviewBackgroundBrushes.Solid(this, !lightBackground);
        _surface.RegionPen = new Pen(ThemeBrush("Ts.Accent"), 2);
        _surface.RegionContrastPen = new Pen(ThemeBrush("Ts.Canvas"), 4);
        _surface.InvalidateVisual();
    }

    private SolidColorBrush ThemeBrush(string key) => PreviewBackgroundBrushes.Resource(this, key);

    private void RefreshTheme()
    {
        UpdateBackground();
        if (_surface.HighlightRgb.HasValue) UpdateHighlightMask();
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        // Copy BGRA directly; HBITMAP conversion can lose alpha and requires GDI handle ownership.
        using var converted = bitmap.Clone(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bits = converted.LockBits(new System.Drawing.Rectangle(0, 0, converted.Width, converted.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int stride = checked(converted.Width * 4);
            byte[] pixels = new byte[checked(stride * converted.Height)];
            for (int y = 0; y < converted.Height; y++) Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * stride, stride);
            var source = BitmapSource.Create(converted.Width, converted.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            return source;
        }
        finally { converted.UnlockBits(bits); }
    }

    private sealed class Surface : FrameworkElement
    {
        public BitmapSource? Source { get; set; }
        public double ImageWidth { get; set; }
        public double ImageHeight { get; set; }
        public Rect? SelectedRegion { get; set; }
        public int? HighlightRgb { get; set; }
        public BitmapSource? HighlightMask { get; set; }
        public Pen? RegionPen { get; set; }
        public Pen? RegionContrastPen { get; set; }
        public Rect ImageBounds
        {
            get
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                double x = Math.Round(Math.Max(0, (ActualWidth - ImageWidth) / 2) * dpi.DpiScaleX) / dpi.DpiScaleX;
                double y = Math.Round(Math.Max(0, (ActualHeight - ImageHeight) / 2) * dpi.DpiScaleY) / dpi.DpiScaleY;
                return new Rect(x, y, ImageWidth, ImageHeight);
            }
        }
        public Surface() { RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); SnapsToDevicePixels = true; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Source is null) return;
            Rect image = ImageBounds;
            drawingContext.DrawImage(Source, image);
            if (HighlightMask is not null) drawingContext.DrawImage(HighlightMask, image);
            if (SelectedRegion is { } region)
            {
                Rect bounds = new(image.X + region.X * image.Width / Source.PixelWidth,
                    image.Y + region.Y * image.Height / Source.PixelHeight,
                    region.Width * image.Width / Source.PixelWidth, region.Height * image.Height / Source.PixelHeight);
                bounds.Intersect(image);
                if (!bounds.IsEmpty)
                {
                    bounds.Inflate(-Math.Min(2, bounds.Width / 4), -Math.Min(2, bounds.Height / 4));
                    drawingContext.DrawRectangle(null, RegionContrastPen, bounds);
                    drawingContext.DrawRectangle(null, RegionPen, bounds);
                }
            }
        }
    }
}
