using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public sealed record TilePointer(int X, int Y, int Column, int Row);

public sealed class TilePreview : FrameworkElement
{
    internal const int Copies = 3;
    public BitmapSource? Source { get; private set; }
    public TileSeamAnalysis? Analysis { get; private set; }
    public double PixelScale { get; private set; } = 1;
    public event Action<TilePointer, bool>? PaintRequested;
    public event Action? StrokeEnded;
    public bool EditingEnabled { get; set; }
    private BitmapSource[] _tiles = [];
    private TileHeatmapOverlay[] _heatmaps = [];
    private bool _customNeighbors;
    private int? _dropSlot;
    private bool _drawing;
    private bool _heat = true;
    public bool HeatmapVisible
    {
        get => _heat;
        set { if (_heat == value) return; _heat = value; InvalidateVisual(); }
    }
    private Brush _background = Brushes.Transparent;
    private int _backgroundIndex;

    public TilePreview()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        Focusable = true;
        MouseMove += (_, e) =>
        {
            if (_drawing && e.LeftButton == MouseButtonState.Pressed && Hit(e.GetPosition(this)) is { } point)
                PaintRequested?.Invoke(point, false);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (Hit(e.GetPosition(this)) is not { } point) return;
            Focus();
            if (!EditingEnabled) return;
            _drawing = true;
            CaptureMouse();
            PaintRequested?.Invoke(point, true);
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, e) => { if (_drawing) { EndStroke(); e.Handled = true; } };
        LostMouseCapture += (_, _) => EndStroke();
        _ = new PreviewThemeSubscription(this, RefreshBackground);
    }

    internal void SetTiles(BitmapSource source, BitmapSource[] tiles, bool customNeighbors, int background)
    {
        Source = source;
        _tiles = tiles;
        _customNeighbors = customNeighbors;
        _backgroundIndex = background;
        var edges = new Dictionary<BitmapSource, TileHeatmapOverlay.Edges>();
        var joins = new Dictionary<(BitmapSource, BitmapSource, BitmapSource), (TileSeamAnalysis Analysis, TileHeatmapOverlay Overlay)>();
        var seams = new TileSeamAnalysis[tiles.Length];
        _heatmaps = new TileHeatmapOverlay[tiles.Length];
        for (int y = 0; y < Copies; y++)
            for (int x = 0; x < Copies; x++)
            {
                int slot = y * Copies + x;
                var tile = tiles[slot];
                var right = x + 1 < Copies ? tiles[slot + 1] : tile;
                var bottom = y + 1 < Copies ? tiles[slot + Copies] : tile;
                if (!joins.TryGetValue((tile, right, bottom), out var join))
                {
                    var current = ReadEdges(tile);
                    var analysis = TileSeamAnalyzer.AnalyzeEdges(current.Right, ReadEdges(right).Left, current.Bottom, ReadEdges(bottom).Top);
                    join = (analysis, new TileHeatmapOverlay(analysis, 5));
                    joins.Add((tile, right, bottom), join);
                }
                seams[slot] = join.Analysis;
                _heatmaps[slot] = join.Overlay;
            }
        int center = tiles.Length / 2;
        Analysis = new(
            seams[center - 1].Horizontal.Zip(seams[center].Horizontal, (left, right) => (left + right) / 2).ToArray(),
            seams[center - Copies].Vertical.Zip(seams[center].Vertical, (top, bottom) => (top + bottom) / 2).ToArray());
        RefreshBackground();

        TileHeatmapOverlay.Edges ReadEdges(BitmapSource tile)
        {
            if (!edges.TryGetValue(tile, out var result)) edges[tile] = result = TileHeatmapOverlay.ReadEdges(tile);
            return result;
        }
    }

    internal void SetScale(double scale)
    {
        if (Source is null) return;
        PixelScale = scale;
        Width = Source.PixelWidth * Copies * scale;
        Height = Source.PixelHeight * Copies * scale;
        InvalidateVisual();
    }

    internal BitmapSource TileAt(int slot) => _tiles[slot];

    internal void ShowDropSlot(int? slot)
    {
        if (_dropSlot == slot) return;
        _dropSlot = slot;
        InvalidateVisual();
    }

    public void Clear()
    {
        Source = null; Analysis = null; _tiles = []; _heatmaps = []; _dropSlot = null;
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
        int column = Math.Min(Copies - 1, (int)(point.X / (Source.PixelWidth * PixelScale)));
        int row = Math.Min(Copies - 1, (int)(point.Y / (Source.PixelHeight * PixelScale)));
        int x = Math.Clamp((int)(point.X / PixelScale) - column * Source.PixelWidth, 0, Source.PixelWidth - 1);
        int y = Math.Clamp((int)(point.Y / PixelScale) - row * Source.PixelHeight, 0, Source.PixelHeight - 1);
        return new(x, y, column, row);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Source is null) return;
        drawing.DrawRectangle(_background, null, new Rect(0, 0, Width, Height));
        double width = Source.PixelWidth * PixelScale, height = Source.PixelHeight * PixelScale;
        for (int y = 0; y < Copies; y++)
            for (int x = 0; x < Copies; x++)
            {
                drawing.DrawImage(_tiles[y * Copies + x], new Rect(x * width, y * height, width, height));
            }
        if (_heat)
            for (int y = 0; y < Copies; y++)
                for (int x = 0; x < Copies; x++)
                {
                    int slot = y * Copies + x;
                    var current = _heatmaps[slot];
                    var left = x > 0 ? _heatmaps[slot - 1] : _customNeighbors ? null : current;
                    var top = y > 0 ? _heatmaps[slot - Copies] : _customNeighbors ? null : current;
                    current.DrawEdges(drawing, new Rect(x * width, y * height, width, height), Source, left, top,
                        x + 1 < Copies || !_customNeighbors, y + 1 < Copies || !_customNeighbors, opacity: .7, stripe: 1);
                }
        if (_dropSlot is { } target)
        {
            var bounds = new Rect(target % Copies * width, target / Copies * height, width, height);
            drawing.DrawRectangle(null, new Pen(Brushes.Black, 4), bounds);
            drawing.DrawRectangle(null, new Pen(PreviewBackgroundBrushes.Resource(this, "Ts.Accent"), 2), bounds);
        }
    }

    private void RefreshBackground()
    {
        _background = PreviewBackgroundBrushes.Create(this, _backgroundIndex);
        InvalidateVisual();
    }

}
