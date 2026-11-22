using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using IOException = System.IO.IOException;
using InvalidDataException = System.IO.InvalidDataException;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tessera.Core.Imaging;
using Tessera.Models;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace Tessera.Desktop;

public partial class TileAtlasPage : Page
{
    internal const string ToolKey = "atlas";

    private ImageEditorPage Editor { get; }
    private MainWindow Host => Editor.Host;
    private bool _atlasHeatmapInverted;

    internal TileAtlasPage(ImageEditorPage editor)
    {
        Editor = editor;
        InitializeComponent();
    }

    internal GroupBox SettingsGroup => _atlasToolGroup;
    internal PixelPreview Preview => atlasImagePreview;
    internal bool IsActive => _atlasToolActive;
    internal bool CanInvertHeatmap => _atlasToolActive && _atlasCell is not null;
    internal bool HeatmapHasFocus => _atlasToolActive && _atlasHeatmap.IsKeyboardFocused;

    internal void SetHeatmapInverted(bool inverted)
    {
        if (_atlasHeatmapInverted == inverted) return;
        _atlasHeatmapInverted = inverted;
        UpdateAtlasHeatmap();
    }

    internal void SetHeatmapKeyHint(string key)
        => _atlasHeatmap.ToolTip = Localization.Format("HoldTemporarilyInvertSeamHeatmapVisibilityFormat", key);

    internal bool TryUndoOrRedo(Key key)
    {
        if (!_atlasToolActive || _atlasCell is not { } cell || key is not (Key.Z or Key.Y) ||
            !atlasRepeatPreview.IsKeyboardFocusWithin) return false;
        FinishAtlasStroke();
        if (key == Key.Z) cell.Undo(); else cell.Redo();
        UpdateAtlasDraft();
        return true;
    }

    internal void Release() => _atlasDraftStore.Dispose();


    private enum AtlasPaintTool { View, Pencil, Eraser, Eyedropper, FloodFill, TileFill }

    private readonly GroupBox _atlasToolGroup = new();

    private readonly StackPanel _atlasGridSettings = new();

    private readonly IntegerInput _atlasTileWidth = new() { Maximum = int.MaxValue, Value = 32 };

    private readonly IntegerInput _atlasTileHeight = new() { Maximum = int.MaxValue, Value = 32 };

    private readonly IntegerInput _atlasMarginX = new() { Minimum = 0, Value = 0 };

    private readonly IntegerInput _atlasMarginY = new() { Minimum = 0, Value = 0 };

    private readonly IntegerInput _atlasGapX = new() { Minimum = 0, Value = 0 };

    private readonly IntegerInput _atlasGapY = new() { Minimum = 0, Value = 0 };

    private readonly TextBlock _atlasLayoutStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };

    private readonly TextBlock _atlasSeamStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };

    private readonly CheckBox _atlasHeatmap = new() { IsChecked = true };

    private Color _atlasPaintColor = Colors.White;

    private Button _atlasUndo = null!, _atlasRedo = null!, _atlasSmooth = null!, _atlasApply = null!, _atlasDiscard = null!;

    private readonly Dictionary<(Guid Source, Guid? Parent), AtlasDraft> _atlasDrafts = [];

    private readonly AtlasDraftStore _atlasDraftStore = new();

    private (Guid Source, Guid? Parent) _atlasBasis;

    private TileSetLayout? _atlasLayout;

    private IReadOnlyList<TileSetRegion> _atlasRegions = [];

    private TileSetRegion? _atlasRegion;

    private PixelEditBuffer? _atlasCell;

    private BitmapSource? _atlasImage;

    private TileEditSnapshot? _atlasStroke;

    private TilePointer? _atlasStrokeStart, _atlasLastPoint;

    private Color _atlasStrokeColor;

    private bool _atlasStrokeErase, _atlasToolActive, _settingAtlasLayout;

    internal void InitializeSettings()
    {
        _atlasToolGroup.Header = Localization.Text("TileAtlas");
        var panel = new StackPanel();
        _atlasToolGroup.Content = panel;
        panel.Children.Add(new Expander
        {
            Header = Localization.Text("AtlasGrid"),
            Content = _atlasGridSettings, Style = (Style)FindResource("ToolExpander")
        });
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("TileWidthPx", _atlasTileWidth));
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("TileHeightPx", _atlasTileHeight));
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("XMarginPx", _atlasMarginX));
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("YMarginPx", _atlasMarginY));
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("XSpacingPxLabel", _atlasGapX));
        _atlasGridSettings.Children.Add(EditorPreview.GridLensRow("YSpacingPxLabel", _atlasGapY));
        var detect = new Button { Content = Localization.Text("DetectGridLabel"), Margin = new Thickness(0, 4, 0, 0) };
        detect.Click += DetectAtlasLayout;
        _atlasGridSettings.Children.Add(detect);
        panel.Children.Add(_atlasLayoutStatus);
        _atlasHeatmap.Content = Localization.Text("SeamHeatmap");
        _atlasHeatmap.Style = (Style)FindResource("Ts.SwitchBox");
        _atlasHeatmap.Checked += (_, _) => UpdateAtlasHeatmap();
        _atlasHeatmap.Unchecked += (_, _) => UpdateAtlasHeatmap();
        panel.Children.Add(_atlasHeatmap);
        panel.Children.Add(_atlasSeamStatus);
        InitializeAtlasPaintRail();
        var history = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 2) };
        _atlasUndo = AddButton(history, "UndoEdit", () => { _atlasCell?.Undo(); UpdateAtlasDraft(); });
        _atlasRedo = AddButton(history, "RedoEdit", () => { _atlasCell?.Redo(); UpdateAtlasDraft(); });
        foreach (var button in new[] { _atlasUndo, _atlasRedo })
        {
            button.Padding = new Thickness(4, 7, 4, 7);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        panel.Children.Add(history);
        _atlasSmooth = AddButton(panel, "SmoothTileEdges", SmoothAtlasEdges);
        _atlasDiscard = AddButton(panel, "DiscardAtlasEdits", DiscardAtlasDraft);
        _atlasApply = AddButton(panel, "SaveAtlasToHistory", ApplyAtlasDraft);
        _atlasApply.Style = (Style)FindResource("PrimaryButton");

        foreach (var input in new[] { _atlasTileWidth, _atlasTileHeight, _atlasMarginX, _atlasMarginY, _atlasGapX, _atlasGapY })
            input.ValueChanged += (_, _) => { if (!_settingAtlasLayout) UpdateAtlasLayout(); };
        atlasImagePreview.EmptyText = Localization.Text("OpenAnAtlasImage");
        atlasImagePreview.Focusable = true;
        AutomationProperties.SetName(atlasImagePreview, Localization.Text("AtlasGrid"));
        AutomationProperties.SetName(atlasRepeatScroll, Localization.Text("SelectedTileRepeat"));
        atlasImagePreview.PreviewKeyDown += MoveAtlasCell;
        atlasRepeatPreview.PaintRequested += PaintAtlasCell;
        atlasRepeatPreview.StrokeEnded += FinishAtlasStroke;
        InitializeAtlasPreviewInput();
        UpdateAtlasControls();

        Button AddButton(Panel parent, string title, Action action)
        {
            var button = new Button { Content = Localization.Text(title), Margin = new Thickness(0, 3, 0, 0) };
            button.Click += (_, _) => action();
            parent.Children.Add(button);
            return button;
        }
    }

    private void InitializeAtlasPaintRail()
    {
        var tools = new (string Title, string Shortcut, string Icon)[]
        {
            ("View", "V", "M3,2 L20,13 12,14 9,21 3,2 Z"),
            ("Pencil", "B", "M3,17 L16,4 Q17,3 18,4 L21,7 Q22,8 21,9 L8,22 2,23 Z M13,7 L18,12 M3,17 L8,22"),
            ("Eraser", "E", "M3,15 L13,5 Q14,4 15,5 L22,12 Q23,13 22,14 L15,21 8,21 Z M9,9 L19,19 M14,21 H23"),
            ("Eyedropper", "I", "M13,8 L19,2 Q21,1 23,3 Q24,5 22,7 L16,13 M10,6 L18,14 M11,8 L5,14 4,18 2,21 5,19 9,18 15,12"),
            ("FloodFill", "G", "M5,4 L16,15 M11,5 L20,14 11,23 2,14 Z M3,14 H19 M22,16 Q18,21 21,23 Q24,23 24,21 Z"),
            ("FillTile", "Shift+G", "M2,3 H22 V22 H2 Z M2,12 H22 M6,17 H18 M6,19 H18")
        };
        AutomationProperties.SetName(atlasPaintTool, Localization.Text("Tool"));
        foreach (var (title, shortcut, icon) in tools)
        {
            string label = Localization.Text(title);
            var path = new Path { Data = Geometry.Parse(icon), Width = 18, Height = 18, Stretch = Stretch.Uniform,
                StrokeThickness = 1.65, StrokeLineJoin = PenLineJoin.Round };
            path.SetResourceReference(Shape.StrokeProperty, "Ts.Text");
            var item = new ListBoxItem { Content = path, Tag = shortcut, ToolTip = $"{label} · {shortcut}" };
            AutomationProperties.SetName(item, label);
            AutomationProperties.SetAcceleratorKey(item, shortcut);
            atlasPaintTool.Items.Add(item);
        }
        atlasPaintTool.SelectedIndex = (int)AtlasPaintTool.View;
        atlasPaintTool.SelectionChanged += (_, _) =>
        {
            FinishAtlasStroke();
            UpdateAtlasControls();
        };
        AutomationProperties.SetName(atlasColorButton, Localization.Text("ColorAction"));
        atlasColorButton.Click += ChooseAtlasPaintColor;
        AutomationProperties.SetName(atlasRepeatResetButton, Localization.Text("Reset33Layout"));
        atlasRepeatResetButton.ToolTip = Localization.Text("FillAllCellsWithTheSelected");
        atlasColorChecker.Background = PreviewBackgroundBrushes.Create(atlasColorChecker, 0);
        _ = new PreviewThemeSubscription(atlasColorChecker, () => atlasColorChecker.Background = PreviewBackgroundBrushes.Create(atlasColorChecker, 0));
        SetAtlasPaintColor(_atlasPaintColor);
    }

    internal bool SelectPaintTool(Key key, ModifierKeys modifiers, object? focusedElement)
    {
        if (!_atlasToolActive || Host.WorkspaceBusy || _atlasCell is null ||
            focusedElement is TextBoxBase or PasswordBox or MenuItem or Menu) return false;
        string? shortcut = modifiers switch
        {
            ModifierKeys.None => key.ToString(),
            ModifierKeys.Shift => "Shift+" + key,
            _ => null
        };
        if (shortcut is null) return false;
        foreach (ListBoxItem item in atlasPaintTool.Items)
        {
            if (!Equals(item.Tag, shortcut)) continue;
            atlasPaintTool.SelectedItem = item;
            return true;
        }
        return false;
    }

    private void ChooseAtlasPaintColor(object sender, RoutedEventArgs e)
    {
        if (Host.WorkspaceBusy || !_atlasToolActive || _atlasCell is null) return;
        FinishAtlasStroke();
        var color = _atlasPaintColor;
        var palette = Palettes.GetPalette(EPaletteKind.DB32)
            .Select(rgb => new PaletteColorEntry(DrawingColor.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)))
            .ToArray();
        var picker = new ColorReplaceWindow(DrawingColor.FromArgb(color.A, color.R, color.G, color.B), Localization.Text("BrushColor"), palette) { Owner = Host };
        picker.ShowDialog();
        if (picker.SelectedColor is { } selected)
            SetAtlasPaintColor(Color.FromArgb(selected.A, selected.R, selected.G, selected.B));
    }

    private void SetAtlasPaintColor(Color color)
    {
        _atlasPaintColor = color;
        atlasColorSwatch.Background = new SolidColorBrush(color);
        atlasColorButton.ToolTip = $"{Localization.Text("ColorAction")} · {color}";
    }

    internal void SetActive(bool active)
    {
        if (_atlasToolActive == active) return;
        if (!active)
        {
            if (_atlasHeatmapInverted) Editor.Preview.ResetComparisonKey();
            FinishAtlasStroke();
        }
        _atlasToolActive = active;
    }

    internal void UpdateView(bool visible)
    {
        MainWindow.SetVisible(atlasPreviewHost, visible);
        var image = visible ? Editor.WorkspaceBasis() : null;
        var basis = image is not null && Editor.ActiveSource is { } source
            ? (source.Id, Editor.ProcessingBasis == 1 ? source.SelectedGeneration?.Id : null) : default;
        if (ReferenceEquals(atlasImagePreview.Image, image) && basis == _atlasBasis) { UpdateAtlasControls(); return; }

        FinishAtlasStroke();
        if (basis != _atlasBasis && _atlasDrafts.TryGetValue(_atlasBasis, out var previous))
        {
            try
            {
                _atlasDraftStore.Save(_atlasBasis, previous);
                _atlasDrafts.Remove(_atlasBasis);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Host.statusLabel.Text = ex.Message; }
        }
        _atlasBasis = basis;
        atlasImagePreview.Image = image;
        _atlasImage = image is null ? null : atlasImagePreview.BitmapSource;
        if (image is not null)
        {
            if (!_atlasDrafts.ContainsKey(basis))
            {
                try
                {
                    if (_atlasDraftStore.Load(basis) is { } restored)
                    {
                        _atlasDrafts[basis] = restored;
                        _atlasDraftStore.Delete(basis);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { Host.statusLabel.Text = ex.Message; }
            }
            if (_atlasDrafts.TryGetValue(basis, out var draft)) SetAtlasLayoutInputs(draft.Layout);
            else if (!TryAtlasLayout(image.Width, image.Height, out _, out _))
            {
                int width = image.Width % 32 == 0 ? 32 : image.Width;
                int height = image.Height % 32 == 0 ? 32 : image.Height;
                if ((long)(image.Width / width) * (image.Height / height) > TileSetLayout.MaxTiles)
                    (width, height) = (image.Width, image.Height);
                SetAtlasLayoutInputs(TileSetLayout.FromTileSize(image.Width, image.Height, width, height));
            }
        }
        UpdateAtlasLayout();
    }

    private bool TryAtlasLayout(int width, int height, out TileSetLayout? layout, out string error)
    {
        try
        {
            layout = TileSetLayout.FromTileSize(width, height, _atlasTileWidth.Value, _atlasTileHeight.Value,
                _atlasMarginX.Value, _atlasMarginY.Value, _atlasGapX.Value, _atlasGapY.Value);
            error = "";
            return true;
        }
        catch (TileSetLayoutException ex)
        {
            layout = null;
            error = ex.Error switch
            {
                ETileSetLayoutError.GridDoesNotCoverImage => Localization.Text("AtlasGridDoesNotCoverImage"),
                ETileSetLayoutError.PartialTiles => Localization.Text("AtlasGridRequiresWholeTiles"),
                ETileSetLayoutError.TileCountOutOfRange => Localization.Format("AtlasTileCountOutOfRange", TileSetLayout.MaxTiles),
                ETileSetLayoutError.InvalidDimensions => Localization.Text("AtlasGridDimensionsOutOfRange"),
                _ => ex.Message
            };
            return false;
        }
    }

    private void SetAtlasLayoutInputs(TileSetLayout layout)
    {
        _settingAtlasLayout = true;
        _atlasTileWidth.Value = layout.TileWidth;
        _atlasTileHeight.Value = layout.TileHeight;
        _atlasMarginX.Value = layout.MarginX;
        _atlasMarginY.Value = layout.MarginY;
        _atlasGapX.Value = layout.SpacingX;
        _atlasGapY.Value = layout.SpacingY;
        _settingAtlasLayout = false;
    }

    private void UpdateAtlasLayout()
    {
        if (_atlasImage is not { } image)
        {
            _atlasLayout = null;
            _atlasRegions = [];
        }
        else if (TryAtlasLayout(image.PixelWidth, image.PixelHeight, out _atlasLayout, out string error))
        {
            var layout = _atlasLayout!;
            _atlasRegions = TileSetSlicer.GetRegions(image.PixelWidth, image.PixelHeight, layout);
            _atlasLayoutStatus.Text = Localization.Format("CellsFormat", layout.Columns, layout.Rows);
        }
        else
        {
            _atlasRegions = [];
            _atlasLayoutStatus.Text = error;
        }
        var selected = _atlasRegion is { } region && _atlasRegions.Contains(region) ? region :
            _atlasRegions.Count > 0 ? _atlasRegions[0] : (TileSetRegion?)null;
        ShowAtlasCell(selected);
    }

    private async void DetectAtlasLayout(object sender, RoutedEventArgs e)
    {
        if (Host.WorkspaceBusy || _atlasImage is not { } image) return;
        Editor.SetProcessingState(true);
        try
        {
            int mx = _atlasMarginX.Value, my = _atlasMarginY.Value, gx = _atlasGapX.Value, gy = _atlasGapY.Value;
            var suggestion = await Task.Run(() =>
            {
                int stride = checked(image.PixelWidth * 4);
                var pixels = new byte[checked(stride * image.PixelHeight)];
                image.CopyPixels(pixels, stride, 0);
                return TileSetSlicer.Suggest(pixels, image.PixelWidth, image.PixelHeight, mx, my, gx, gy).FirstOrDefault();
            });
            if (!ReferenceEquals(image, _atlasImage)) return;
            if (suggestion is null) { _atlasLayoutStatus.Text = Localization.Text("NoSuitableGridFound"); return; }
            SetAtlasLayoutInputs(suggestion.Layout);
            UpdateAtlasLayout();
            Host.CompleteProcessingProgress();
        }
        catch (Exception ex) { _atlasLayoutStatus.Text = ex.Message; }
        finally { Editor.SetProcessingState(false); }
    }

    private TileSetRegion? AtlasRegionAt(Point point)
    {
        if (_atlasLayout is not { } layout || !atlasImagePreview.TryGetImagePixel(point, out int x, out int y)) return null;
        int dx = x - layout.MarginX, dy = y - layout.MarginY;
        if (dx < 0 || dy < 0) return null;
        int column = (int)(dx / ((long)layout.TileWidth + layout.SpacingX));
        int row = (int)(dy / ((long)layout.TileHeight + layout.SpacingY));
        if (column >= layout.Columns || row >= layout.Rows) return null;
        var region = _atlasRegions[row * layout.Columns + column];
        return x < region.X + region.Width && y < region.Y + region.Height ? region : null;
    }

    private void MoveAtlasCell(object sender, KeyEventArgs e)
    {
        if (Host.WorkspaceBusy || Keyboard.Modifiers != ModifierKeys.None || _atlasLayout is not { } layout || _atlasRegion is not { } region)
            return;
        var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        int x = Math.Clamp(region.Column + dx, 0, layout.Columns - 1), y = Math.Clamp(region.Row + dy, 0, layout.Rows - 1);
        var next = _atlasRegions[y * layout.Columns + x];
        e.Handled = true;
        if (next != region) ShowAtlasCell(next);
    }

    private void ShowAtlasCell(TileSetRegion? region)
    {
        FinishAtlasStroke();
        _atlasRegion = region;
        _atlasCell = null;
        atlasImagePreview.SelectedRegion = null;
        if (region is { } cell && _atlasImage is { } image)
        {
            _atlasCell = _atlasDrafts.GetValueOrDefault(_atlasBasis)?.Edits.GetValueOrDefault(cell) ??
                new PixelEditBuffer(new CroppedBitmap(image, new Int32Rect(cell.X, cell.Y, cell.Width, cell.Height)));
            atlasImagePreview.SelectedRegion = new Rect(cell.X, cell.Y, cell.Width, cell.Height);
        }
        RefreshAtlasEdits();
    }

    private void PaintAtlasCell(TilePointer point, bool begin)
    {
        if (Host.WorkspaceBusy || _atlasCell is not { } cell) return;
        if (atlasPaintTool.SelectedIndex <= 0) return;
        var tool = (AtlasPaintTool)atlasPaintTool.SelectedIndex;
        var tile = atlasRepeatPreview.TileAt(point.Row * TilePreview.Copies + point.Column);
        if (tool == AtlasPaintTool.Eyedropper)
        {
            if (begin)
            {
                var pixel = new byte[4];
                tile.CopyPixels(new Int32Rect(point.X, point.Y, 1, 1), pixel, 4, 0);
                SetAtlasPaintColor(Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]));
            }
            return;
        }
        if (!ReferenceEquals(tile, cell.Image)) return;
        if (begin)
        {
            FinishAtlasStroke();
            _atlasStrokeErase = tool == AtlasPaintTool.Eraser;
            _atlasStrokeColor = _atlasStrokeErase ? Colors.Transparent : _atlasPaintColor;
            if (tool is AtlasPaintTool.FloodFill or AtlasPaintTool.TileFill)
            {
                if (cell.Fill(point.X, point.Y, _atlasStrokeColor, entireTile: tool == AtlasPaintTool.TileFill))
                    UpdateAtlasDraft();
                return;
            }
            _atlasStroke = cell.Capture();
            _atlasStrokeStart = _atlasLastPoint = point;
        }
        if (_atlasStroke is null || _atlasStrokeStart is not { } start || start.Row != point.Row || start.Column != point.Column) return;
        cell.PaintLine(_atlasLastPoint!.X, _atlasLastPoint.Y, point.X, point.Y, _atlasStrokeColor, 1, _atlasStroke, _atlasStrokeErase);
        _atlasLastPoint = point;
        UpdateAtlasRepeat();
    }

    private void FinishAtlasStroke()
    {
        if (_atlasStroke is not { } before) return;
        _atlasStroke = null;
        _atlasStrokeStart = _atlasLastPoint = null;
        if (_atlasCell?.FinishEdit(before) == true) UpdateAtlasDraft();
    }

    private void UpdateAtlasDraft()
    {
        if (_atlasCell is not { } cell || _atlasRegion is not { } region || _atlasLayout is not { } layout) return;
        if (cell.HasLocalEdits)
        {
            if (!_atlasDrafts.TryGetValue(_atlasBasis, out var draft)) _atlasDrafts[_atlasBasis] = draft = new(layout, []);
            draft.Edits[region] = cell;
        }
        else if (_atlasDrafts.TryGetValue(_atlasBasis, out var draft))
        {
            draft.Edits.Remove(region);
            if (draft.Edits.Count == 0) _atlasDrafts.Remove(_atlasBasis);
        }
        RefreshAtlasEdits();
    }

    private void RefreshAtlasEdits()
    {
        var images = _atlasDrafts.GetValueOrDefault(_atlasBasis)?.Edits.ToDictionary(pair => pair.Key, pair => (BitmapSource)pair.Value.Image) ?? [];
        atlasImagePreview.SetAtlasOverlay(_atlasRegions, images);
        UpdateAtlasRepeat();
        UpdateAtlasControls();
    }

    internal void UpdateAtlasRepeat()
    {
        if (_atlasCell is not { } cell || _atlasRegion is not { } region ||
            _atlasImage is not { } image || _atlasLayout is not { } layout)
        {
            atlasRepeatPreview.Clear();
            _atlasSeamStatus.Text = "";
            return;
        }
        SetAtlasRepeatTiles(cell, region, image, layout);
        var analysis = atlasRepeatPreview.Analysis!;
        _atlasSeamStatus.Text = Localization.Format("RowColumnXYFormat",
            region.Row + 1, region.Column + 1, analysis.Horizontal.Average(), analysis.Vertical.Average());
    }

    private void UpdateAtlasHeatmap() =>
        atlasRepeatPreview.HeatmapVisible = (_atlasHeatmap.IsChecked == true) != _atlasHeatmapInverted;

    private void UpdateAtlasControls()
    {
        if (_atlasApply is null) return;
        bool enabled = _atlasToolActive && !Host.WorkspaceBusy;
        bool dirty = _atlasDrafts.ContainsKey(_atlasBasis);
        _atlasGridSettings.IsEnabled = enabled && !dirty;
        _atlasApply.IsEnabled = _atlasDiscard.IsEnabled = enabled && dirty;
        _atlasUndo.IsEnabled = enabled && _atlasCell?.CanUndo == true;
        _atlasRedo.IsEnabled = enabled && _atlasCell?.CanRedo == true;
        _atlasSmooth.IsEnabled = enabled && _atlasCell is not null;
        atlasPaintTool.IsEnabled = atlasColorButton.IsEnabled = enabled && _atlasCell is not null;
        atlasRepeatResetButton.IsEnabled = enabled && _atlasRegion is { } anchor && _atlasLayout is { } layout &&
            _atlasNeighbors.ContainsKey((_atlasBasis.Source, layout, anchor));
        atlasRepeatPreview.EditingEnabled = atlasPaintTool.IsEnabled && atlasPaintTool.SelectedIndex > 0;
        atlasRepeatPreview.Cursor = atlasRepeatPreview.EditingEnabled ? Cursors.Cross : Cursors.Arrow;
    }

    private async void SmoothAtlasEdges()
    {
        if (Host.WorkspaceBusy || _atlasCell is not { } cell) return;
        FinishAtlasStroke();
        Editor.SetProcessingState(true);
        try
        {
            var pixels = await Task.Run(() => TileAssetProcessing.Smooth(cell.Pixels, cell.Width, cell.Height, 1, true, 1));
            if (ReferenceEquals(_atlasCell, cell) && cell.ReplacePixels(pixels))
                UpdateAtlasDraft();
            Host.CompleteProcessingProgress();
        }
        catch (Exception ex) { Host.WorkspaceError(ex); }
        finally { Editor.SetProcessingState(false); }
    }

    private void DiscardAtlasDraft()
    {
        FinishAtlasStroke();
        _atlasDrafts.Remove(_atlasBasis);
        _atlasDraftStore.Delete(_atlasBasis);
        ShowAtlasCell(_atlasRegion);
    }

    internal bool CanCloseAtlas()
    {
        FinishAtlasStroke();
        return (_atlasDrafts.Count == 0 && !_atlasDraftStore.HasDrafts) || TesseraDialog.Show(Host,
            Localization.Text("SomeAtlasesHaveUnsavedChangesConfirmation"),
            Localization.Text("TileAtlas"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ApplyAtlasDraft()
    {
        FinishAtlasStroke();
        var basis = _atlasBasis;
        if (Host.WorkspaceBusy || Editor.ActiveSource is not { } source || source.Id != basis.Source || _atlasImage is not { } image ||
            !_atlasDrafts.TryGetValue(basis, out var draft)) return;
        try
        {
            var composed = new WriteableBitmap(image);
            foreach (var (region, cell) in draft.Edits)
                composed.WritePixels(new Int32Rect(region.X, region.Y, region.Width, region.Height), cell.Pixels, region.Width * 4, 0);
            using var result = new Bitmap(image.PixelWidth, image.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bits = result.LockBits(new System.Drawing.Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[checked(result.Width * 4)];
                for (int y = 0; y < result.Height; y++)
                {
                    composed.CopyPixels(new Int32Rect(0, y, result.Width, 1), row, row.Length, 0);
                    Marshal.Copy(row, 0, IntPtr.Add(bits.Scan0, y * bits.Stride), row.Length);
                }
            }
            finally { result.UnlockBits(bits); }
            if (!Editor.CommitAtlasEdit(source, result, basis.Parent)) return;
            _atlasDrafts.Remove(basis);
            _atlasDraftStore.Delete(basis);
            UpdateAtlasControls();
            Host.SynchronizeWorkspace();
        }
        catch (Exception ex) { Host.WorkspaceError(ex); }
    }
}
