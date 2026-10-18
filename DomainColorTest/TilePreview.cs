using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelArtDownscale;

namespace DomainColorTest;

public sealed record TilePointer(bool Companion, int X, int Y, int Column, int Row);

public sealed class TilePreview : FrameworkElement
{
    public BitmapSource? Source { get; private set; }
    public TileSeamAnalysis? Analysis { get; private set; }
    public int Columns { get; private set; } = 3;
    public int Rows { get; private set; } = 3;
    public double PixelScale { get; private set; } = 1;
    public event Action<string>? PixelHovered;
    public event Action<TilePointer, bool>? PaintRequested;
    public event Action? StrokeEnded;
    public bool EditingEnabled { get; set; }
    public BitmapSource? Companion { get; private set; }
    public TileSeamAnalysis? ReverseAnalysis { get; private set; }
    private BitmapSource? _horizontalHeat, _verticalHeat;
    private BitmapSource? _reverseHorizontalHeat, _reverseVerticalHeat;
    private bool _drawing;
    private bool _repeatX = true, _repeatY = true, _heat = true, _guides;
    public bool HeatmapVisible
    {
        get => _heat;
        set { if (_heat == value) return; _heat = value; InvalidateVisual(); }
    }
    private double _opacity = .7, _stripe = 1;
    private Brush _background = Brushes.Transparent;
    private static readonly Brush Checker = MakeChecker();

    public TilePreview()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        Focusable = true;
        MouseMove += Hover;
        MouseLeave += (_, _) => PixelHovered?.Invoke("");
        MouseLeftButtonDown += (_, e) =>
        {
            if (!EditingEnabled || Hit(e.GetPosition(this)) is not { } point) return;
            Focus();
            _drawing = true;
            CaptureMouse();
            PaintRequested?.Invoke(point, true);
            ReportPixel(point);
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, e) => { if (_drawing) { EndStroke(); e.Handled = true; } };
        LostMouseCapture += (_, _) => EndStroke();
    }

    public void SetSource(BitmapSource source, TileSeamAnalysis analysis)
    {
        Source = source;
        Analysis = analysis;
        Companion = null; ReverseAnalysis = analysis;
    }

    public void SetPair(BitmapSource source, BitmapSource? companion, TileSeamAnalysis analysis, TileSeamAnalysis reverse)
    {
        Source = source; Companion = companion;
        Analysis = analysis; ReverseAnalysis = reverse;
    }

    public void Clear()
    {
        Source = Companion = null; Analysis = ReverseAnalysis = null;
        Width = Height = 0; InvalidateVisual();
    }

    private void EndStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        StrokeEnded?.Invoke();
    }

    public TilePointer? Hit(Point point)
    {
        if (Source is null || point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height) return null;
        int column = (int)(point.X / (Source.PixelWidth * PixelScale));
        int row = (int)(point.Y / (Source.PixelHeight * PixelScale));
        int x = (int)(point.X / PixelScale) % Source.PixelWidth;
        int y = (int)(point.Y / PixelScale) % Source.PixelHeight;
        return new(Companion is not null && (column + row) % 2 == 1, x, y, column, row);
    }

    public void Configure(bool repeatX, bool repeatY, int copies, double scale, bool heat, bool guides,
        double threshold, double opacity, double stripe, int background)
    {
        _repeatX = repeatX; _repeatY = repeatY; _heat = heat; _guides = guides;
        Columns = repeatX ? copies : 1; Rows = repeatY ? copies : 1;
        PixelScale = scale; _opacity = opacity; _stripe = stripe;
        _background = background == 1 ? Brushes.White : background == 2 ? new SolidColorBrush(Color.FromRgb(24, 24, 24)) : Checker;
        if (Source is null || Analysis is null) return;
        Width = Source.PixelWidth * Columns * scale;
        Height = Source.PixelHeight * Rows * scale;
        _horizontalHeat = HeatStrip(Analysis.Horizontal, true, threshold);
        _verticalHeat = HeatStrip(Analysis.Vertical, false, threshold);
        _reverseHorizontalHeat = HeatStrip((ReverseAnalysis ?? Analysis).Horizontal, true, threshold);
        _reverseVerticalHeat = HeatStrip((ReverseAnalysis ?? Analysis).Vertical, false, threshold);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Source is null) return;
        drawing.DrawRectangle(_background, null, new Rect(0, 0, Width, Height));
        double width = Source.PixelWidth * PixelScale, height = Source.PixelHeight * PixelScale;
        var guide = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
            {
                double left = x * width, top = y * height;
                bool second = Companion is not null && (x + y) % 2 == 1;
                drawing.DrawImage(second ? Companion : Source, new Rect(left, top, width, height));
                if (_heat)
                {
                    drawing.PushOpacity(_opacity);
                    if (_repeatX && _horizontalHeat is not null)
                    {
                        double stripe = Math.Min(_stripe, Source.PixelWidth / 2.0) * PixelScale;
                        drawing.DrawImage(second ? _horizontalHeat : _reverseHorizontalHeat, new Rect(left, top, stripe, height));
                        drawing.DrawImage(second ? _reverseHorizontalHeat : _horizontalHeat, new Rect(left + width - stripe, top, stripe, height));
                    }
                    if (_repeatY && _verticalHeat is not null)
                    {
                        double stripe = Math.Min(_stripe, Source.PixelHeight / 2.0) * PixelScale;
                        drawing.DrawImage(second ? _verticalHeat : _reverseVerticalHeat, new Rect(left, top, width, stripe));
                        drawing.DrawImage(second ? _reverseVerticalHeat : _verticalHeat, new Rect(left, top + height - stripe, width, stripe));
                    }
                    drawing.Pop();
                }
                if (_guides) drawing.DrawRectangle(null, guide, new Rect(left, top, width, height));
            }
    }

    private void Hover(object sender, MouseEventArgs e)
    {
        if (Analysis is null || Hit(e.GetPosition(this)) is not { } point) return;
        if (_drawing && e.LeftButton == MouseButtonState.Pressed) PaintRequested?.Invoke(point, false);
        ReportPixel(point);
    }

    private void ReportPixel(TilePointer point)
    {
        if (Analysis is null) return;
        var outgoing = point.Companion ? ReverseAnalysis ?? Analysis : Analysis;
        var incoming = point.Companion ? Analysis : ReverseAnalysis ?? Analysis;
        string text = Localization.Format("{0} · пиксель ({1}, {2})", (point.Companion ? "B" : "A"), point.X, point.Y);
        if (_repeatX) text += $" · X ← {incoming.Horizontal[point.Y]:F2} / → {outgoing.Horizontal[point.Y]:F2}";
        if (_repeatY) text += $" · Y ↑ {incoming.Vertical[point.X]:F2} / ↓ {outgoing.Vertical[point.X]:F2}";
        PixelHovered?.Invoke(text);
    }

    private static BitmapSource HeatStrip(double[] values, bool vertical, double threshold)
    {
        byte[] pixels = new byte[values.Length * 4];
        for (int q = 0; q < values.Length; q++)
        {
            // Green <= threshold; yellow at 2x; red at 4x. A zero threshold still has a finite ramp.
            double amount = Math.Clamp((values[q] - threshold) / Math.Max(1, threshold * 3), 0, 1);
            Color color = amount < .5
                ? Color.FromRgb((byte)(48 + amount * 2 * 207), (byte)(210 + amount * 2 * 5), 80)
                : Color.FromRgb(255, (byte)(215 - (amount - .5) * 2 * 165), 50);
            pixels[q * 4] = color.B; pixels[q * 4 + 1] = color.G;
            pixels[q * 4 + 2] = color.R; pixels[q * 4 + 3] = 255;
        }
        int width = vertical ? 1 : values.Length, height = vertical ? values.Length : 1;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush MakeChecker()
    {
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(62, 62, 62)), null, new Rect(0, 0, 16, 16));
            var dark = new SolidColorBrush(Color.FromRgb(46, 46, 46));
            drawing.DrawRectangle(dark, null, new Rect(0, 0, 8, 8));
            drawing.DrawRectangle(dark, null, new Rect(8, 8, 8, 8));
        }
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16) };
        brush.Freeze();
        return brush;
    }
}
