using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Tessera.Models;

namespace Tessera.Desktop;

public partial class TileAtlasPage
{
    private const string AtlasTileDragFormat = "Tessera.AtlasTile";

    private sealed record AtlasTileDrag((Guid Source, Guid? Parent) Basis, TileSetLayout Layout, TileSetRegion Region);

    private readonly Dictionary<(Guid Source, TileSetLayout Layout, TileSetRegion Anchor), Dictionary<int, TileSetRegion>> _atlasNeighbors = [];

    private AtlasTileDrag? _atlasDragCandidate;

    private Point _atlasDragStart;

    private double _atlasRepeatZoom = 1;

    internal void ResizePreview(bool wide)
    {
        atlasPreviewHost.RowDefinitions[1].Height = new GridLength(wide ? 0 : 6);
        atlasPreviewHost.RowDefinitions[2].Height = new GridLength(wide ? 0 : 1, GridUnitType.Star);
        atlasPreviewHost.ColumnDefinitions[1].Width = new GridLength(wide ? 6 : 0);
        atlasPreviewHost.ColumnDefinitions[2].Width = new GridLength(wide ? 1 : 0, GridUnitType.Star);
        Grid.SetRow(atlasRepeatPane, wide ? 0 : 2);
        Grid.SetColumn(atlasRepeatPane, wide ? 2 : 0);
        DockPanel.SetDock(atlasPaintRail, wide ? Dock.Left : Dock.Top);
        atlasPaintRail.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
    }

    private void InitializeAtlasPreviewInput()
    {
        atlasImagePreview.PreviewMouseLeftButtonDown += BeginAtlasTilePointer;
        atlasImagePreview.PreviewMouseMove += DragAtlasTile;
        atlasImagePreview.PreviewMouseLeftButtonUp += EndAtlasTilePointer;
        atlasImagePreview.LostMouseCapture += (_, _) => _atlasDragCandidate = null;
        atlasRepeatPreview.DragLeave += (_, _) => atlasRepeatPreview.ShowDropSlot(null);
        _ = new ImageViewportInput(atlasRepeatScroll);
        atlasRepeatScroll.ToolTip = Localization.Text(ImageViewportInput.GestureHint) + " " +
            Localization.Text("DragTileAtlasIntoCellReplace");
        atlasRepeatScroll.SizeChanged += (_, _) => UpdateAtlasRepeatScale();
        atlasRepeatScroll.PreviewMouseWheel += (_, e) =>
        {
            if (ZoomAtlasRepeat(e.Delta, e.GetPosition(atlasRepeatScroll), Keyboard.Modifiers)) e.Handled = true;
        };
    }

    private void UpdateAtlasRepeatScale()
    {
        if (atlasRepeatPreview.Source is not { } tile) return;
        const int copies = TilePreview.Copies;
        double fit = Math.Min((atlasRepeatScroll.ActualWidth - 4) / (tile.PixelWidth * (double)copies),
            (atlasRepeatScroll.ActualHeight - 4) / (tile.PixelHeight * (double)copies));
        atlasRepeatPreview.SetScale(Math.Max(0.01, fit) * _atlasRepeatZoom);
    }

    private bool ZoomAtlasRepeat(int delta, Point pointer, ModifierKeys modifiers)
    {
        if (atlasRepeatPreview.Source is not { } tile || delta == 0 || !ImageViewportInput.IsZoomGesture(modifiers)) return false;
        double next = Math.Clamp(_atlasRepeatZoom * Math.Pow(1.25, delta / 120d), .25, 32);
        if (next == _atlasRepeatZoom) return true;

        atlasRepeatScroll.UpdateLayout();
        Point position = atlasRepeatScroll.TranslatePoint(pointer, atlasRepeatPreview);
        double scale = atlasRepeatPreview.PixelScale;
        double x = Math.Clamp(position.X / scale, 0, tile.PixelWidth * (double)TilePreview.Copies);
        double y = Math.Clamp(position.Y / scale, 0, tile.PixelHeight * (double)TilePreview.Copies);
        _atlasRepeatZoom = next;
        UpdateAtlasRepeatScale();
        atlasRepeatScroll.UpdateLayout();
        scale = atlasRepeatPreview.PixelScale;
        Point projected = atlasRepeatPreview.TranslatePoint(new Point(x * scale, y * scale), atlasRepeatScroll);
        atlasRepeatScroll.ScrollToHorizontalOffset(atlasRepeatScroll.HorizontalOffset + projected.X - pointer.X);
        atlasRepeatScroll.ScrollToVerticalOffset(atlasRepeatScroll.VerticalOffset + projected.Y - pointer.Y);
        return true;
    }

    private void BeginAtlasTilePointer(object sender, MouseButtonEventArgs e)
    {
        var pointer = e.GetPosition(atlasImagePreview);
        if (Host.WorkspaceBusy || _atlasLayout is not { } layout || AtlasRegionAt(pointer) is not { } region ||
            !atlasImagePreview.CaptureMouse()) return;
        _atlasDragCandidate = new(_atlasBasis, layout, region);
        _atlasDragStart = pointer;
        atlasImagePreview.Focus();
        e.Handled = true;
    }

    private void EndAtlasTilePointer(object sender, MouseButtonEventArgs e)
    {
        if (_atlasDragCandidate is not { } candidate) return;
        _atlasDragCandidate = null;
        if (atlasImagePreview.IsMouseCaptured) atlasImagePreview.ReleaseMouseCapture();
        if (!Host.WorkspaceBusy && candidate.Basis == _atlasBasis && candidate.Layout == _atlasLayout &&
            candidate.Region == AtlasRegionAt(e.GetPosition(atlasImagePreview)) && candidate.Region != _atlasRegion)
            ShowAtlasCell(candidate.Region);
        e.Handled = true;
    }

    private void DragAtlasTile(object sender, MouseEventArgs e)
    {
        if (_atlasDragCandidate is not { } candidate || e.LeftButton != MouseButtonState.Pressed || Host.WorkspaceBusy) return;
        Vector move = e.GetPosition(atlasImagePreview) - _atlasDragStart;
        if (Math.Abs(move.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(move.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _atlasDragCandidate = null;
        if (atlasImagePreview.IsMouseCaptured) atlasImagePreview.ReleaseMouseCapture();
        e.Handled = true;
        try { DragDrop.DoDragDrop(atlasImagePreview, new DataObject(AtlasTileDragFormat, candidate), DragDropEffects.Copy); }
        finally { atlasRepeatPreview.ShowDropSlot(null); }
    }

    internal bool HandleAtlasTileDrop(DragEventArgs e, bool commit)
    {
        if (!e.Data.GetDataPresent(AtlasTileDragFormat, false)) return false;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (Host.WorkspaceBusy || !_atlasToolActive || !atlasRepeatPreview.IsVisible || _atlasRegion is null ||
            (e.AllowedEffects & DragDropEffects.Copy) == 0 ||
            !ReferenceEquals(ImageEditorPage.Within<TilePreview>(e.OriginalSource as DependencyObject), atlasRepeatPreview) ||
            e.Data.GetData(AtlasTileDragFormat, false) is not AtlasTileDrag tile ||
            tile.Basis != _atlasBasis || tile.Layout != _atlasLayout || !_atlasRegions.Contains(tile.Region) ||
            atlasRepeatPreview.Hit(e.GetPosition(atlasRepeatPreview)) is not { } target)
        {
            atlasRepeatPreview.ShowDropSlot(null);
            return true;
        }
        int slot = target.Row * TilePreview.Copies + target.Column;
        e.Effects = DragDropEffects.Copy;
        atlasRepeatPreview.ShowDropSlot(commit ? null : slot);
        if (commit) SetAtlasNeighbor(slot, tile.Region);
        return true;
    }

    private void SetAtlasNeighbor(int slot, TileSetRegion tile)
    {
        if (_atlasRegion is not { } anchor || _atlasLayout is not { } layout) return;
        var key = (_atlasBasis.Source, layout, anchor);
        if (!_atlasNeighbors.TryGetValue(key, out var slots))
        {
            if (tile == anchor) return;
            _atlasNeighbors[key] = slots = [];
        }
        if (tile == anchor)
        {
            slots.Remove(slot);
            if (slots.Count == 0) _atlasNeighbors.Remove(key);
        }
        else slots[slot] = tile;
        UpdateAtlasRepeat();
        UpdateAtlasControls();
    }

    private void ResetAtlasRepeat(object sender, RoutedEventArgs e)
    {
        if (!_atlasToolActive || Host.WorkspaceBusy || _atlasRegion is not { } anchor || _atlasLayout is not { } layout ||
            !_atlasNeighbors.Remove((_atlasBasis.Source, layout, anchor))) return;
        atlasRepeatPreview.ShowDropSlot(null);
        UpdateAtlasRepeat();
        UpdateAtlasControls();
    }

    private void SetAtlasRepeatTiles(PixelEditBuffer cell, TileSetRegion anchor, BitmapSource image, TileSetLayout layout)
    {
        var tiles = Enumerable.Repeat<BitmapSource>(cell.Image, TilePreview.Copies * TilePreview.Copies).ToArray();
        var slots = _atlasNeighbors.GetValueOrDefault((_atlasBasis.Source, layout, anchor));
        if (slots is not null)
        {
            var edits = _atlasDrafts.GetValueOrDefault(_atlasBasis)?.Edits;
            var sources = new Dictionary<TileSetRegion, BitmapSource>();
            foreach (var (slot, region) in slots)
            {
                if (!sources.TryGetValue(region, out var tile))
                    sources[region] = tile = (BitmapSource?)edits?.GetValueOrDefault(region)?.Image ??
                        new CroppedBitmap(image, new Int32Rect(region.X, region.Y, region.Width, region.Height));
                tiles[slot] = tile;
            }
        }
        atlasRepeatPreview.SetTiles(cell.Image, tiles, slots is not null, Editor.Preview.PreviewBackground);
        UpdateAtlasRepeatScale();
    }
}
