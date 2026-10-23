using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DomainColorTest;

/// <summary>Pixel-exact paint surface with scrolling, fit/explicit zoom and source overlay.</summary>
public sealed class MaterialMapView : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly PaintSurface _surface = new();
    private BitmapSource? _image;
    private double _zoom;
    private bool _drawing;
    public bool DrawingEnabled { get; set; } = true;
    public bool ShowGrid { get; set; }
    public double OverlayOpacity { get; set; }
    public BitmapSource? Overlay { get; set; }
    public event Action<int, int, bool>? PaintRequested;
    public event Action? StrokeEnded;
    public event Action<double, double>? LightMoved;
    public event Action<int>? ZoomRequested;
    public double Zoom { get => _zoom; set { _zoom = value; RefreshScale(); } }
    public BitmapSource? Image { get => _image; set { _image = value; RefreshScale(); } }

    public MaterialMapView()
    {
        Focusable = true; ClipToBounds = true;
        _scroll = new ScrollViewer { Content = _surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Background = Brushes.Transparent };
        Children.Add(_scroll);
        _scroll.SizeChanged += (_, _) => RefreshScale();
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            if (!DrawingEnabled || Hit(e.GetPosition(_surface)) is not { } hit) return;
            Focus(); _drawing = true; _surface.CaptureMouse();
            PaintRequested?.Invoke(hit.X, hit.Y, true); e.Handled = true;
        };
        _surface.MouseMove += (_, e) =>
        {
            if (_drawing && e.LeftButton == MouseButtonState.Pressed && Hit(e.GetPosition(_surface)) is { } hit)
                PaintRequested?.Invoke(hit.X, hit.Y, false);
            if (!DrawingEnabled && _image is not null)
            {
                var point = e.GetPosition(_surface);
                LightMoved?.Invoke(Math.Clamp(point.X / _surface.Width * 2 - 1, -1, 1), Math.Clamp(1 - point.Y / _surface.Height * 2, -1, 1));
            }
        };
        _surface.MouseLeftButtonUp += (_, _) => FinishStroke();
        _surface.LostMouseCapture += (_, _) => FinishStroke();
        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || ZoomRequested is null) return;
            ZoomRequested.Invoke(Math.Sign(e.Delta)); e.Handled = true;
        };
    }

    public void FinishStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
        StrokeEnded?.Invoke();
    }
    public (int X, int Y)? Hit(Point point)
    {
        if (_image is null || point.X < 0 || point.Y < 0 || point.X >= _surface.Width || point.Y >= _surface.Height) return null;
        return (Math.Min(_image.PixelWidth - 1, (int)(point.X / _surface.Scale)), Math.Min(_image.PixelHeight - 1, (int)(point.Y / _surface.Scale)));
    }
    public void Refresh() { _surface.Grid = ShowGrid; _surface.Overlay = Overlay; _surface.OpacityOverlay = OverlayOpacity; _surface.InvalidateVisual(); }
    private void RefreshScale()
    {
        if (_image is null) return;
        double fit = Math.Min(Math.Max(32, _scroll.ActualWidth - 20) / _image.PixelWidth, Math.Max(32, _scroll.ActualHeight - 20) / _image.PixelHeight);
        _surface.Scale = _zoom > 0 ? _zoom : fit >= 1 ? Math.Max(1, Math.Floor(fit)) : fit;
        _surface.Source = _image;
        _surface.Width = _image.PixelWidth * _surface.Scale; _surface.Height = _image.PixelHeight * _surface.Scale;
        Refresh();
    }
    private sealed class PaintSurface : FrameworkElement
    {
        public BitmapSource? Source, Overlay;
        public double Scale = 1, OpacityOverlay;
        public bool Grid;
        public PaintSurface() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        protected override void OnRender(DrawingContext drawing)
        {
            if (Source is null) return;
            var bounds = new Rect(0, 0, Width, Height);
            drawing.DrawRectangle(PreviewBackgroundBrushes.Create(this, 0), null, bounds);
            drawing.DrawImage(Source, bounds);
            if (Overlay is not null && OpacityOverlay > 0)
            { drawing.PushOpacity(OpacityOverlay); drawing.DrawImage(Overlay, bounds); drawing.Pop(); }
            if (!Grid || Scale < 6) return;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(55, 180, 180, 180)), .5);
            // Only visible grid lines; a large atlas must not allocate millions of primitives.
            Rect clip = new Rect(0, 0, Width, Height);
            if (Parent is FrameworkElement parent)
            {
                try { clip.Intersect(TransformToAncestor(parent).Inverse!.TransformBounds(new Rect(0, 0, parent.ActualWidth, parent.ActualHeight))); }
                catch (InvalidOperationException) { }
            }
            for (double x = Math.Max(0, Math.Floor(clip.Left / Scale) * Scale); x <= clip.Right; x += Scale)
                drawing.DrawLine(pen, new Point(x, clip.Top), new Point(x, clip.Bottom));
            for (double y = Math.Max(0, Math.Floor(clip.Top / Scale) * Scale); y <= clip.Bottom; y += Scale)
                drawing.DrawLine(pen, new Point(clip.Left, y), new Point(clip.Right, y));
        }
    }
}

public sealed class NormalDirectionPicker : FrameworkElement
{
    private readonly BitmapSource _hemisphere;
    public Vector3 Normal { get; private set; } = Vector3.UnitZ;
    public event Action<Vector3>? Changed;
    public NormalDirectionPicker()
    {
        Width = Height = 120; Cursor = Cursors.Cross; Focusable = true;
        byte[] pixels = new byte[120 * 120 * 4];
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 120; x++)
            {
                float nx = (x - 59.5f) / 59.5f, ny = (59.5f - y) / 59.5f;
                float radius = nx * nx + ny * ny;
                if (radius > 1) continue;
                int q = (y * 120 + x) * 4;
                pixels[q] = (byte)(127.5f + 127.5f * MathF.Sqrt(1 - radius));
                pixels[q + 1] = (byte)(127.5f + 127.5f * ny); pixels[q + 2] = (byte)(127.5f + 127.5f * nx); pixels[q + 3] = 255;
            }
        _hemisphere = BitmapSource.Create(120, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 480); _hemisphere.Freeze();
        MouseLeftButtonDown += (_, e) => { Focus(); CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        KeyDown += (_, e) =>
        {
            var delta = e.Key switch { Key.Left => new Vector2(-.05f, 0), Key.Right => new Vector2(.05f, 0), Key.Up => new Vector2(0, .05f), Key.Down => new Vector2(0, -.05f), _ => Vector2.Zero };
            if (e.Key == Key.Home) SetNormal(Vector3.UnitZ);
            else if (delta != Vector2.Zero) SetXY(Normal.X + delta.X, Normal.Y + delta.Y);
            else return;
            e.Handled = true;
        };
    }
    public void SetNormal(Vector3 normal) { Normal = Vector3.Normalize(normal); InvalidateVisual(); Changed?.Invoke(Normal); }
    private void Pick(Point point) => SetXY((float)(point.X / ActualWidth * 2 - 1), (float)(1 - point.Y / ActualHeight * 2));
    private void SetXY(float x, float y)
    {
        float length = MathF.Sqrt(x * x + y * y);
        if (length > .999f) { x *= .999f / length; y *= .999f / length; }
        SetNormal(new(x, y, MathF.Sqrt(Math.Max(.000001f, 1 - x * x - y * y))));
    }
    protected override void OnRender(DrawingContext drawing)
    {
        drawing.DrawImage(_hemisphere, new Rect(0, 0, ActualWidth, ActualHeight));
        var center = new Point((Normal.X + 1) * ActualWidth / 2, (1 - Normal.Y) * ActualHeight / 2);
        drawing.DrawEllipse(null, new Pen(Brushes.Black, 3), center, 5, 5);
        drawing.DrawEllipse(null, new Pen(Brushes.White, 1), center, 5, 5);
    }
}
