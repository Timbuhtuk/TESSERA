using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Microsoft.Win32;
using System.Drawing.Imaging;

namespace DomainColorTest;

public partial class MoodboardWorkspace : UserControl
{
    private const int MaxBoardSize = 16384;
    private const int PreviewPixelLimit = 1536;
    private static readonly double[] ZoomFactors = [0, .0625, .125, .25, .5, 1, 2, 4, 8, 16, 32, 64];

    private sealed class Layer
    {
        public string Name { get; init; } = "";
        public byte[] ImageData { get; init; } = [];
        public BitmapSource Image { get; init; } = null!;
        public double Width { get; init; }
        public double Height { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
        public Grid? Element { get; set; }
        public Border? Outline { get; set; }
        public Ellipse? Knob { get; set; }
    }

    private readonly record struct LayerPose(double X, double Y, double Angle, double Scale);

    private sealed class BoardFile
    {
        public int Version { get; set; } = 1;
        public int Width { get; set; }
        public int Height { get; set; }
        public List<LayerFile> Layers { get; set; } = [];
    }

    private sealed class LayerFile
    {
        public string Name { get; set; } = "";
        public byte[] Png { get; set; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
    }

    private sealed class BoardManifest
    {
        public int Version { get; set; } = 2;
        public int Width { get; set; }
        public int Height { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public List<ManifestLayer> Layers { get; set; } = [];
    }

    private sealed class ManifestLayer
    {
        public string Name { get; set; } = "";
        public string ImageEntry { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; } = 1;
    }

    private readonly List<Layer> _layers = [];
    private readonly HashSet<Layer> _selection = [];
    private readonly Dictionary<Layer, LayerPose> _dragStart = [];
    private Layer? _selected;
    private Layer? _dragged;
    private Point _pointerStart;
    private Point _selectionPivot;
    private bool _rotating;
    private double _rotationStartAngle;
    private bool _updatingControls;
    private bool _syncingLayerList;
    private bool _dirty;
    private Color _backgroundColor = Colors.White;
    private double _boardEffectiveZoom = 1;
    private int _zoomRequestRevision;
    private string? _projectPath;

    public event EventHandler? BackRequested;
    public event EventHandler? BusyChanged;
    public event Action<string>? StatusChanged;
    public bool HasBoard => editorPanel.Visibility == Visibility.Visible;
    public bool IsBusy { get; private set; }
    public Color BackgroundColor => _backgroundColor;

    public MoodboardWorkspace()
    {
        InitializeComponent();
        backButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        createButton.Click += (_, _) => CreateBoard();
        newButton.Click += (_, _) => { if (ConfirmReplace()) ShowStart(); };
        addButton.Click += async (_, _) => await OpenImagesAsync();
        saveProjectButton.Click += async (_, _) => await SaveProjectAsync();
        openProjectButton.Click += async (_, _) => await OpenProjectAsync();
        openStartButton.Click += async (_, _) => await OpenProjectAsync();
        exportButton.Click += async (_, _) => await ExportPngAsync();
        backgroundColorButton.Click += (_, _) => ChooseBackgroundColor();
        boardZoomInput.SelectionChanged += (_, _) => UpdateBoardScale();
        boardArea.PreviewMouseWheel += (_, e) =>
        {
            if (ChangeBoardZoom(e.Delta, e.GetPosition(boardScroll), e.GetPosition(boardSurface), Keyboard.Modifiers)) e.Handled = true;
        };
        boardScroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) UpdateBoardScale();
        };
        upButton.Click += (_, _) => MoveLayer(1);
        downButton.Click += (_, _) => MoveLayer(-1);
        toFrontButton.Click += (_, _) => MoveSelectionToEdge(true);
        toBackButton.Click += (_, _) => MoveSelectionToEdge(false);
        deleteButton.Click += (_, _) => DeleteSelected();
        layerList.SelectionChanged += (_, e) =>
        {
            if (_syncingLayerList) return;
            var selected = layerList.SelectedItems.OfType<Layer>().ToArray();
            var active = e.AddedItems.OfType<Layer>().LastOrDefault() ??
                (_selected is not null && selected.Contains(_selected) ? _selected : selected.LastOrDefault());
            SetSelection(selected, active, fromList: true);
        };
        rotationSlider.ValueChanged += (_, _) =>
        {
            if (_updatingControls || _selected is null) return;
            RotateSelection(rotationSlider.Value - _selected.Angle);
            UpdateSelectionControls();
            _dirty = true;
        };
        scaleSlider.ValueChanged += (_, _) =>
        {
            if (_updatingControls || _selected is null) return;
            ScaleSelection(scaleSlider.Value / 100 / _selected.Scale);
            UpdateSelectionControls();
            _dirty = true;
        };
        boardSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, boardSurface)) SetSelection([]);
        };
        boardArea.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(IconWorkspace.SupportsFile)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        boardArea.Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await AddFilesAsync(paths);
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && HasBoard && _selection.Count > 0) { DeleteSelected(); e.Handled = true; }
        };
        SizeChanged += (_, _) => { UpdateMoodboardLayout(); UpdateBoardScale(); };
        UpdateSelectionControls();
    }

    public bool CanLeave() => !IsBusy && (!_dirty || TesseraDialog.Show(Window.GetWindow(this),
        "Доска изменена. Продолжить без сохранения?", "Несохранённая доска",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        IsEnabled = !busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ConfirmReplace() => CanLeave();

    private void ShowStart()
    {
        _layers.Clear();
        _selection.Clear();
        _selected = null;
        _projectPath = null;
        _dirty = false;
        RebuildLayers();
        startPanel.Visibility = Visibility.Visible;
        editorPanel.Visibility = Visibility.Collapsed;
    }

    private void CreateBoard()
    {
        widthInput.Commit(); heightInput.Commit();
        ResetBoard(widthInput.Value, heightInput.Value);
        StatusChanged?.Invoke("Полотно создано. Добавьте изображения.");
    }

    private void ResetBoard(int width, int height)
    {
        _layers.Clear();
        _selection.Clear();
        _selected = null;
        _projectPath = null;
        _dirty = false;
        boardSurface.Width = width;
        boardSurface.Height = height;
        _zoomRequestRevision++;
        boardZoomInput.SelectedIndex = 0;
        boardSizeLabel.Text = $"Полотно: {width} × {height} px";
        startPanel.Visibility = Visibility.Collapsed;
        editorPanel.Visibility = Visibility.Visible;
        RebuildLayers();
        SetBackgroundColor(Colors.White);
        _dirty = false;
        UpdateBoardScale();
    }

    private void ChooseBackgroundColor()
    {
        var initial = System.Drawing.Color.FromArgb(_backgroundColor.R, _backgroundColor.G, _backgroundColor.B);
        var picker = new ColorReplaceWindow(initial) { Owner = Window.GetWindow(this) };
        picker.ShowDialog();
        if (picker.SelectedColor is { } selected)
            SetBackgroundColor(Color.FromRgb(selected.R, selected.G, selected.B));
    }

    public void SetBackgroundColor(Color color)
    {
        color = Color.FromRgb(color.R, color.G, color.B);
        if (_backgroundColor != color && HasBoard) _dirty = true;
        _backgroundColor = color;
        boardSurface.Background = new SolidColorBrush(color);
        boardFrame.Background = boardSurface.Background;
        backgroundSwatch.Background = boardSurface.Background;
        backgroundHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void UpdateBoardScale()
    {
        double viewportWidth = boardScroll.ViewportWidth > 0 ? boardScroll.ViewportWidth : boardArea.ActualWidth;
        double viewportHeight = boardScroll.ViewportHeight > 0 ? boardScroll.ViewportHeight : boardArea.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        int index = Math.Clamp(boardZoomInput.SelectedIndex, 0, ZoomFactors.Length - 1);
        bool fit = index == 0;
        boardScroll.HorizontalScrollBarVisibility = boardScroll.VerticalScrollBarVisibility =
            fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        double boardWidth = boardSurface.Width + 2, boardHeight = boardSurface.Height + 2;
        double zoom = fit ? Math.Min((viewportWidth - 48) / boardWidth, (viewportHeight - 48) / boardHeight) : ZoomFactors[index];
        if (fit && zoom >= 1) zoom = Math.Floor(zoom);
        _boardEffectiveZoom = Math.Max(.001, zoom);
        double displayWidth = boardWidth * _boardEffectiveZoom, displayHeight = boardHeight * _boardEffectiveZoom;
        boardViewbox.Width = displayWidth;
        boardViewbox.Height = displayHeight;
        boardZoomHost.Width = Math.Max(viewportWidth, displayWidth + 48);
        boardZoomHost.Height = Math.Max(viewportHeight, displayHeight + 48);
    }

    private bool ChangeBoardZoom(int delta, Point pointer, Point boardPoint, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0 || delta == 0) return false;
        var anchor = new Point(Math.Clamp(boardPoint.X, 0, boardSurface.Width), Math.Clamp(boardPoint.Y, 0, boardSurface.Height));
        double factor = boardZoomInput.SelectedIndex > 0 ? ZoomFactors[boardZoomInput.SelectedIndex] : _boardEffectiveZoom;
        int steps = Math.Max(1, Math.Abs(delta / 120));
        for (int step = 0; step < steps; step++)
            factor = delta > 0
                ? ZoomFactors.Skip(1).Where(z => z > factor + .000001).DefaultIfEmpty(factor).Min()
                : ZoomFactors.Skip(1).Where(z => z < factor - .000001).DefaultIfEmpty(factor).Max();
        int index = Array.IndexOf(ZoomFactors, factor);
        if (index < 1 || boardZoomInput.SelectedIndex == index) return true;
        boardZoomInput.SelectedIndex = index;
        int revision = ++_zoomRequestRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (revision != _zoomRequestRevision || !HasBoard) return;
            boardScroll.UpdateLayout();
            Point position = boardSurface.TranslatePoint(anchor, boardZoomHost);
            boardScroll.ScrollToHorizontalOffset(position.X - pointer.X);
            boardScroll.ScrollToVerticalOffset(position.Y - pointer.Y);
        }));
        return true;
    }

    private async Task OpenImagesAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Title = "Добавить изображения на доску", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await AddFilesAsync(dialog.FileNames);
    }

    public Task AddFilesFromDialogAsync() => OpenImagesAsync();

    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        if (!HasBoard) CreateBoard();
        var prepared = PrepareImages(paths);
        AddPreparedImages(prepared);
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        if (!HasBoard) CreateBoard();
        string[] fileNames = paths.ToArray();
        SetBusy(true);
        StatusChanged?.Invoke("Добавление изображений…");
        try { AddPreparedImages(await Task.Run(() => PrepareImages(fileNames))); }
        finally { SetBusy(false); }
    }

    private static (List<(string Name, byte[] Data, BitmapSource Preview, int Width, int Height)> Images, List<string> Errors) PrepareImages(IEnumerable<string> paths)
    {
        var images = new List<(string, byte[], BitmapSource, int, int)>();
        var errors = new List<string>();
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IconWorkspace.SupportsFile(path)) continue;
            try
            {
                byte[] bytes = ReadImageData(path, out int sourceWidth, out int sourceHeight);
                BitmapSource preview = DecodePreview(bytes, PreviewPixelLimit);
                images.Add((Path.GetFileName(path), bytes, preview, sourceWidth, sourceHeight));
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        return (images, errors);
    }

    private void AddPreparedImages((List<(string Name, byte[] Data, BitmapSource Preview, int Width, int Height)> Images, List<string> Errors) prepared)
    {
        int added = 0;
        Layer? lastAdded = null;
        foreach (var item in prepared.Images)
        {
            try
            {
                double fit = Math.Min(1, Math.Min(boardSurface.Width * .48 / item.Width, boardSurface.Height * .48 / item.Height));
                var layer = new Layer
                {
                    Name = item.Name, Image = item.Preview, ImageData = item.Data,
                    Width = item.Width * fit, Height = item.Height * fit,
                    X = (boardSurface.Width - item.Width * fit) / 2 + added * 24,
                    Y = (boardSurface.Height - item.Height * fit) / 2 + added * 24
                };
                _layers.Add(layer);
                CreateLayerElement(layer);
                added++;
                lastAdded = layer;
            }
            catch (Exception ex) { prepared.Errors.Add($"{item.Name}: {ex.Message}"); }
        }
        if (added > 0)
        {
            _dirty = true;
            RefreshLayerList();
            SelectLayer(lastAdded);
            emptyHint.Visibility = Visibility.Collapsed;
        }
        StatusChanged?.Invoke(prepared.Errors.Count > 0 ? $"Добавлено: {added}. Ошибки: {string.Join("; ", prepared.Errors)}" : $"Добавлено слоёв: {added}.");
    }

    private static byte[] ReadImageData(string path, out int width, out int height)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var metadata = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
        width = metadata.Width; height = metadata.Height;
        int orientation = metadata.PropertyIdList.Contains(0x112) && metadata.GetPropertyItem(0x112)?.Value is { Length: >= 2 } value
            ? BitConverter.ToUInt16(value, 0) : 1;
        if (orientation == 1) return bytes;

        using var corrected = IconWorkspace.ReadImage(path);
        using var normalized = new MemoryStream();
        corrected.Save(normalized, ImageFormat.Png);
        width = corrected.Width; height = corrected.Height;
        return normalized.ToArray();
    }

    private static BitmapSource DecodePreview(byte[] bytes, int pixelWidth)
    {
        using var metadataStream = new MemoryStream(bytes, writable: false);
        using var metadata = System.Drawing.Image.FromStream(metadataStream, useEmbeddedColorManagement: false, validateImageData: false);
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (metadata.Width > pixelWidth || metadata.Height > pixelWidth)
        {
            if (metadata.Width >= metadata.Height) image.DecodePixelWidth = pixelWidth;
            else image.DecodePixelHeight = pixelWidth;
        }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void RebuildLayers()
    {
        boardSurface.Children.Clear();
        foreach (var layer in _layers) CreateLayerElement(layer);
        RefreshLayerList();
        emptyHint.Visibility = _layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshLayerList()
    {
        _syncingLayerList = true;
        try
        {
            layerList.ItemsSource = null;
            layerList.ItemsSource = _layers.AsEnumerable().Reverse().ToArray();
        }
        finally { _syncingLayerList = false; }
        SyncLayerListSelection();
        UpdateSelectionVisuals();
    }

    private void UpdateMoodboardLayout()
    {
        bool compact = ActualWidth < 850;
        editorContent.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(260);
        editorContent.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        editorContent.RowDefinitions[0].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        editorContent.RowDefinitions[1].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(boardArea, compact ? 0 : 1);
        Grid.SetRow(boardArea, compact ? 1 : 0);
        layerList.Height = compact ? 90 : 240;
        layersPanel.MaxHeight = compact ? 280 : double.PositiveInfinity;
        layersPanel.Padding = new Thickness(compact ? 10 : 14);
        layersPanel.BorderThickness = compact ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
    }

    private void CreateLayerElement(Layer layer)
    {
        var grid = new Grid { Cursor = Cursors.SizeAll, RenderTransformOrigin = new Point(.5, .5), ClipToBounds = false };
        var outline = new Border { BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), Background = Brushes.Transparent };
        outline.Child = new Image { Source = layer.Image, Stretch = Stretch.Fill, IsHitTestVisible = false };
        grid.Children.Add(outline);
        var knob = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -25, 0, 0), Cursor = Cursors.Hand };
        knob.MouseLeftButtonDown += (_, e) => BeginDrag(layer, grid, e, true);
        grid.Children.Add(knob);
        grid.MouseLeftButtonDown += (_, e) => BeginDrag(layer, grid, e, false);
        grid.MouseMove += (_, e) => MoveDrag(e);
        grid.MouseLeftButtonUp += (_, _) => EndDrag();
        grid.LostMouseCapture += (_, _) => { _dragged = null; _dragStart.Clear(); };
        layer.Element = grid;
        layer.Outline = outline;
        layer.Knob = knob;
        UpdateLayer(layer);
        Panel.SetZIndex(grid, _layers.IndexOf(layer));
        boardSurface.Children.Add(grid);
    }

    private void UpdateLayer(Layer layer)
    {
        if (layer.Element is null) return;
        layer.Element.Width = layer.Width * layer.Scale;
        layer.Element.Height = layer.Height * layer.Scale;
        UpdateLayerRotation(layer);
    }

    private static void UpdateLayerPosition(Layer layer)
    {
        if (layer.Element is null) return;
        Canvas.SetLeft(layer.Element, layer.X);
        Canvas.SetTop(layer.Element, layer.Y);
    }

    private static void UpdateLayerRotation(Layer layer)
    {
        if (layer.Element is null) return;
        if (layer.Element.RenderTransform is RotateTransform rotation) rotation.Angle = layer.Angle;
        else layer.Element.RenderTransform = new RotateTransform(layer.Angle);
        UpdateLayerPosition(layer);
    }

    private void BeginDrag(Layer layer, Grid element, MouseButtonEventArgs e, bool rotate)
    {
        SelectLayer(layer, Keyboard.Modifiers);
        if (!_selection.Contains(layer)) { e.Handled = true; return; }
        _dragged = layer;
        _rotating = rotate;
        _pointerStart = e.GetPosition(boardSurface);
        _dragStart.Clear();
        foreach (var item in _selection) _dragStart[item] = CapturePose(item);
        if (rotate)
        {
            _selectionPivot = SelectionCenter(_dragStart);
            _rotationStartAngle = PointerAngle(_selectionPivot, _pointerStart);
        }
        element.CaptureMouse();
        e.Handled = true;
    }

    private static double PointerAngle(Point center, Point pointer) =>
        Math.Atan2(pointer.Y - center.Y, pointer.X - center.X) * 180 / Math.PI + 90;

    private void MoveDrag(MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(boardSurface);
        if (_rotating)
        {
            double delta = NormalizeAngle(PointerAngle(_selectionPivot, pointer) - _rotationStartAngle);
            ApplyRotation(_dragStart, _selectionPivot, delta);
        }
        else
        {
            TranslateSelection(pointer.X - _pointerStart.X, pointer.Y - _pointerStart.Y);
            _pointerStart = pointer;
        }
        UpdateSelectionControls();
        _dirty = true;
        e.Handled = true;
    }

    private void EndDrag()
    {
        _dragged?.Element?.ReleaseMouseCapture();
        _dragged = null;
        _dragStart.Clear();
    }

    private static LayerPose CapturePose(Layer layer) => new(layer.X, layer.Y, layer.Angle, layer.Scale);

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle > 180) angle -= 360;
        if (angle < -180) angle += 360;
        return angle;
    }

    private static Point SelectionCenter(IReadOnlyDictionary<Layer, LayerPose> poses)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (var (layer, pose) in poses)
        {
            double halfWidth = layer.Width * pose.Scale / 2, halfHeight = layer.Height * pose.Scale / 2;
            double angle = pose.Angle * Math.PI / 180;
            double extentX = Math.Abs(Math.Cos(angle)) * halfWidth + Math.Abs(Math.Sin(angle)) * halfHeight;
            double extentY = Math.Abs(Math.Sin(angle)) * halfWidth + Math.Abs(Math.Cos(angle)) * halfHeight;
            double centerX = pose.X + halfWidth, centerY = pose.Y + halfHeight;
            left = Math.Min(left, centerX - extentX); right = Math.Max(right, centerX + extentX);
            top = Math.Min(top, centerY - extentY); bottom = Math.Max(bottom, centerY + extentY);
        }
        return new Point((left + right) / 2, (top + bottom) / 2);
    }

    private static Point RotatePoint(Point point, Point center, double angle)
    {
        double radians = angle * Math.PI / 180;
        double dx = point.X - center.X, dy = point.Y - center.Y;
        return new Point(center.X + dx * Math.Cos(radians) - dy * Math.Sin(radians),
            center.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians));
    }

    private void ApplyRotation(IReadOnlyDictionary<Layer, LayerPose> poses, Point center, double delta)
    {
        foreach (var (layer, pose) in poses)
        {
            double halfWidth = layer.Width * pose.Scale / 2, halfHeight = layer.Height * pose.Scale / 2;
            Point moved = RotatePoint(new Point(pose.X + halfWidth, pose.Y + halfHeight), center, delta);
            layer.X = moved.X - halfWidth; layer.Y = moved.Y - halfHeight;
            layer.Angle = NormalizeAngle(pose.Angle + delta);
            UpdateLayerRotation(layer);
        }
    }

    private void RotateSelection(double delta)
    {
        if (_selection.Count == 0 || delta == 0) return;
        var poses = _selection.ToDictionary(layer => layer, CapturePose);
        ApplyRotation(poses, SelectionCenter(poses), delta);
    }

    private void ScaleSelection(double factor)
    {
        if (_selection.Count == 0 || factor == 1) return;
        factor = Math.Clamp(factor, _selection.Max(layer => .1 / layer.Scale), _selection.Min(layer => 3 / layer.Scale));
        var poses = _selection.ToDictionary(layer => layer, CapturePose);
        Point center = SelectionCenter(poses);
        foreach (var (layer, pose) in poses)
        {
            Point previous = new(pose.X + layer.Width * pose.Scale / 2, pose.Y + layer.Height * pose.Scale / 2);
            Point moved = new(center.X + (previous.X - center.X) * factor, center.Y + (previous.Y - center.Y) * factor);
            layer.Scale = pose.Scale * factor;
            layer.X = moved.X - layer.Width * layer.Scale / 2;
            layer.Y = moved.Y - layer.Height * layer.Scale / 2;
            UpdateLayer(layer);
        }
    }

    private void TranslateSelection(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        foreach (var layer in _selection)
        {
            layer.X += dx; layer.Y += dy;
            UpdateLayerPosition(layer);
        }
        if (_selection.Count > 0) _dirty = true;
    }

    private void SelectLayer(Layer? layer, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (layer is null) { SetSelection([]); return; }
        if ((modifiers & ModifierKeys.Control) != 0 && _selection.Contains(layer))
            SetSelection(_selection.Where(item => !ReferenceEquals(item, layer)), _selected);
        else if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            SetSelection(_selection.Append(layer), layer);
        else if (_selection.Contains(layer)) SetSelection(_selection, layer);
        else SetSelection([layer], layer);
    }

    private void SetSelection(IEnumerable<Layer> selected, Layer? active = null, bool fromList = false)
    {
        var items = selected.Where(_layers.Contains).ToArray();
        _selection.Clear();
        foreach (var item in items) _selection.Add(item);
        _selected = active is not null && _selection.Contains(active) ? active : _layers.LastOrDefault(_selection.Contains);
        if (!fromList) SyncLayerListSelection();
        UpdateSelectionVisuals();
    }

    private void SyncLayerListSelection()
    {
        _syncingLayerList = true;
        try
        {
            layerList.SelectedItems.Clear();
            foreach (var item in layerList.Items.OfType<Layer>())
                if (_selection.Contains(item)) layerList.SelectedItems.Add(item);
        }
        finally { _syncingLayerList = false; }
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var item in _layers)
        {
            if (item.Outline is not null) item.Outline.BorderBrush = _selection.Contains(item) ? Brushes.White : Brushes.Transparent;
            if (item.Knob is not null) item.Knob.Visibility = ReferenceEquals(item, _selected) ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateSelectionControls();
    }

    private void UpdateSelectionControls()
    {
        _updatingControls = true;
        bool hasSelection = _selected is not null;
        bool canMoveUp = false, canMoveDown = false;
        for (int q = 0; q < _layers.Count; q++)
        {
            if (!_selection.Contains(_layers[q])) continue;
            if (q < _layers.Count - 1 && !_selection.Contains(_layers[q + 1])) canMoveUp = true;
            if (q > 0 && !_selection.Contains(_layers[q - 1])) canMoveDown = true;
        }
        upButton.IsEnabled = toFrontButton.IsEnabled = canMoveUp;
        downButton.IsEnabled = toBackButton.IsEnabled = canMoveDown;
        deleteButton.IsEnabled = rotationSlider.IsEnabled = scaleSlider.IsEnabled = hasSelection;
        rotationSlider.Value = hasSelection ? Math.Clamp(_selected!.Angle, -180, 180) : 0;
        scaleSlider.Value = hasSelection ? Math.Clamp(_selected!.Scale * 100, 10, 300) : 100;
        string count = _selection.Count > 1 ? $" · {_selection.Count} слоёв" : "";
        rotationLabel.Text = $"{rotationSlider.Value:0}°{count}";
        scaleLabel.Text = $"{scaleSlider.Value:0}%{count}";
        _updatingControls = false;
    }

    private void MoveLayer(int step)
    {
        bool changed = false;
        if (step > 0)
        {
            for (int q = _layers.Count - 2; q >= 0; q--)
            {
                if (!_selection.Contains(_layers[q]) || _selection.Contains(_layers[q + 1])) continue;
                (_layers[q], _layers[q + 1]) = (_layers[q + 1], _layers[q]);
                changed = true;
            }
        }
        else
        {
            for (int q = 1; q < _layers.Count; q++)
            {
                if (!_selection.Contains(_layers[q]) || _selection.Contains(_layers[q - 1])) continue;
                (_layers[q], _layers[q - 1]) = (_layers[q - 1], _layers[q]);
                changed = true;
            }
        }
        if (!changed) return;
        _dirty = true;
        UpdateZOrder();
        RefreshLayerList();
    }

    private void MoveSelectionToEdge(bool front)
    {
        if (_selection.Count == 0) return;
        var selected = _layers.Where(_selection.Contains).ToArray();
        var others = _layers.Where(layer => !_selection.Contains(layer)).ToArray();
        var ordered = front ? others.Concat(selected).ToArray() : selected.Concat(others).ToArray();
        if (_layers.SequenceEqual(ordered)) return;
        _layers.Clear();
        _layers.AddRange(ordered);
        _dirty = true;
        UpdateZOrder();
        RefreshLayerList();
    }

    private void UpdateZOrder()
    {
        for (int q = 0; q < _layers.Count; q++)
            if (_layers[q].Element is { } element) Panel.SetZIndex(element, q);
    }

    private void DeleteSelected()
    {
        if (_selection.Count == 0) return;
        foreach (var layer in _selection)
            if (layer.Element is { } element) boardSurface.Children.Remove(element);
        _layers.RemoveAll(_selection.Contains);
        _selection.Clear();
        _selected = null;
        _dirty = true;
        RefreshLayerList();
        emptyHint.Visibility = _layers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SaveProjectAsync()
    {
        if (IsBusy) return;
        var dialog = new SaveFileDialog { Title = "Сохранить доску", Filter = "Доска Tessera|*.tboard", DefaultExt = ".tboard", AddExtension = true,
            FileName = _projectPath is null ? "Доска.tboard" : Path.GetFileName(_projectPath) };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await SaveToFileAsync(dialog.FileName); }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось сохранить доску: {ex.Message}"); }
    }

    public async Task SaveToFileAsync(string path)
    {
        if (!HasBoard || IsBusy) throw new InvalidOperationException("Доска сейчас недоступна.");
        var board = CaptureManifest(out var images);
        SetBusy(true);
        StatusChanged?.Invoke("Сохранение доски…");
        try
        {
            await Task.Run(() => WriteProject(path, board, images));
            MarkSaved(path);
        }
        finally { SetBusy(false); }
    }

    public void SaveToFile(string path)
    {
        if (!HasBoard) throw new InvalidOperationException("Сначала создайте полотно.");
        var board = CaptureManifest(out var images);
        WriteProject(path, board, images);
        MarkSaved(path);
    }

    private BoardManifest CaptureManifest(out byte[][] images)
    {
        var board = new BoardManifest { Width = (int)boardSurface.Width, Height = (int)boardSurface.Height,
            BackgroundColor = $"#{_backgroundColor.R:X2}{_backgroundColor.G:X2}{_backgroundColor.B:X2}" };
        images = new byte[_layers.Count][];
        for (int q = 0; q < _layers.Count; q++)
        {
            var item = _layers[q];
            string entryName = $"images/{q:D4}.bin";
            images[q] = item.ImageData;
            board.Layers.Add(new ManifestLayer { Name = item.Name, ImageEntry = entryName,
                Width = item.Width, Height = item.Height, X = item.X, Y = item.Y, Angle = item.Angle, Scale = item.Scale });
        }
        return board;
    }

    private static void WriteProject(string path, BoardManifest board, byte[][] images)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                for (int q = 0; q < board.Layers.Count; q++)
                {
                    var entry = archive.CreateEntry(board.Layers[q].ImageEntry, CompressionLevel.NoCompression);
                    using (var image = entry.Open()) image.Write(images[q]);
                }
                using var manifest = archive.CreateEntry("board.json", CompressionLevel.Fastest).Open();
                JsonSerializer.Serialize(manifest, board);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void MarkSaved(string path)
    {
        _projectPath = path;
        _dirty = false;
        StatusChanged?.Invoke($"Доска сохранена: {path}");
    }

    private async Task OpenProjectAsync()
    {
        if (IsBusy) return;
        if (!ConfirmReplace()) return;
        var dialog = new OpenFileDialog { Title = "Открыть доску", Filter = "Доска Tessera|*.tboard" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await LoadFromFileAsync(dialog.FileName); }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось открыть доску: {ex.Message}"); }
    }

    public async Task LoadFromFileAsync(string path)
    {
        if (IsBusy) throw new InvalidOperationException("Дождитесь завершения операции.");
        SetBusy(true);
        StatusChanged?.Invoke("Открытие доски…");
        try
        {
            var loaded = await Task.Run(() => ReadProject(path));
            ApplyLoadedBoard(loaded.Width, loaded.Height, loaded.Background, loaded.Layers, path);
        }
        finally { SetBusy(false); }
    }

    public void LoadFromFile(string path)
    {
        var loaded = ReadProject(path);
        ApplyLoadedBoard(loaded.Width, loaded.Height, loaded.Background, loaded.Layers, path);
    }

    private static (int Width, int Height, Color Background, List<Layer> Layers) ReadProject(string path)
    {
        using var file = File.OpenRead(path);
        bool zipped = file.ReadByte() == 'P' && file.ReadByte() == 'K';
        file.Position = 0;
        int width, height;
        Color background = Colors.White;
        var layers = new List<Layer>();
        if (zipped)
        {
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            using var manifestStream = archive.GetEntry("board.json")?.Open() ?? throw new InvalidDataException("Нет описания доски.");
            var board = JsonSerializer.Deserialize<BoardManifest>(manifestStream) ?? throw new InvalidDataException("Пустой файл доски.");
            ValidateBoard(board.Version, board.Width, board.Height, board.Layers?.Count ?? -1, 2);
            background = ParseBackgroundColor(board.BackgroundColor);
            var items = board.Layers ?? throw new InvalidDataException("Нет слоёв доски.");
            width = board.Width; height = board.Height;
            foreach (var item in items)
            {
                ValidateLayer(item.Width, item.Height, item.X, item.Y, item.Angle, item.Scale);
                if (!item.ImageEntry.StartsWith("images/", StringComparison.Ordinal) || item.ImageEntry.Contains("..", StringComparison.Ordinal))
                    throw new InvalidDataException("Неверная запись изображения.");
                var entry = archive.GetEntry(item.ImageEntry) ?? throw new InvalidDataException("Нет изображения слоя.");
                if (entry.Length is <= 0 or > 536870912) throw new InvalidDataException("Недопустимый размер изображения слоя.");
                byte[] imageData = new byte[(int)entry.Length];
                using (var image = entry.Open()) image.ReadExactly(imageData);
                layers.Add(new Layer { Name = item.Name, ImageData = imageData, Image = DecodePreview(imageData, PreviewPixelLimit),
                    Width = item.Width, Height = item.Height, X = item.X, Y = item.Y, Angle = item.Angle, Scale = item.Scale });
            }
        }
        else
        {
            var board = JsonSerializer.Deserialize<BoardFile>(file) ?? throw new InvalidDataException("Пустой файл доски.");
            ValidateBoard(board.Version, board.Width, board.Height, board.Layers?.Count ?? -1, 1);
            var items = board.Layers ?? throw new InvalidDataException("Нет слоёв доски.");
            width = board.Width; height = board.Height;
            foreach (var item in items)
            {
                ValidateLayer(item.Width, item.Height, item.X, item.Y, item.Angle, item.Scale);
                if (item.Png is null || item.Png.Length == 0) throw new InvalidDataException("Пустое изображение слоя.");
                layers.Add(new Layer { Name = item.Name, ImageData = item.Png, Image = DecodePreview(item.Png, PreviewPixelLimit),
                    Width = item.Width, Height = item.Height, X = item.X, Y = item.Y, Angle = item.Angle, Scale = item.Scale });
            }
        }
        return (width, height, background, layers);
    }

    private static Color ParseBackgroundColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Colors.White;
        if (hex.Length != 7 || hex[0] != '#' ||
            !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new InvalidDataException("Неверный цвет фона доски.");
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private void ApplyLoadedBoard(int width, int height, Color background, List<Layer> layers, string path)
    {
        ResetBoard(width, height);
        SetBackgroundColor(background);
        _layers.AddRange(layers);
        _projectPath = path;
        _dirty = false;
        RebuildLayers();
        StatusChanged?.Invoke($"Доска открыта: {path}");
    }

    private static void ValidateBoard(int version, int width, int height, int layerCount, int expectedVersion)
    {
        if (version != expectedVersion || width is < 128 or > MaxBoardSize || height is < 128 or > MaxBoardSize || layerCount is < 0 or > 500)
            throw new InvalidDataException("Неподдерживаемый формат или размер доски.");
    }

    private static void ValidateLayer(double width, double height, double x, double y, double angle, double scale)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(angle) || !double.IsFinite(scale) || width <= 0 || height <= 0 || scale is < .1 or > 3)
            throw new InvalidDataException("Повреждённый слой доски.");
    }

    private async Task ExportPngAsync()
    {
        if (IsBusy) return;
        var dialog = new SaveFileDialog { Title = "Экспорт доски в PNG", Filter = "PNG|*.png", DefaultExt = ".png", AddExtension = true, FileName = "Доска.png" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await ExportToPngAsync(dialog.FileName); }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось экспортировать PNG: {ex.Message}"); }
    }

    public async Task ExportToPngAsync(string path)
    {
        if (!HasBoard || IsBusy) throw new InvalidOperationException("Доска сейчас недоступна.");
        var layers = CaptureRenderLayers();
        int width = (int)boardSurface.Width, height = (int)boardSurface.Height;
        SetBusy(true);
        StatusChanged?.Invoke("Экспорт PNG…");
        try
        {
            Color background = _backgroundColor;
            await Task.Run(() => MoodboardPngExporter.Export(path, width, height, background, layers));
            StatusChanged?.Invoke($"PNG сохранён: {path}");
        }
        finally { SetBusy(false); }
    }

    public void ExportToPng(string path)
    {
        if (!HasBoard) throw new InvalidOperationException("Сначала создайте полотно.");
        int width = (int)boardSurface.Width, height = (int)boardSurface.Height;
        MoodboardPngExporter.Export(path, width, height, _backgroundColor, CaptureRenderLayers());
        StatusChanged?.Invoke($"PNG сохранён: {path}");
    }

    private MoodboardRenderLayer[] CaptureRenderLayers() => _layers.Select(item =>
        new MoodboardRenderLayer(item.ImageData, item.Width, item.Height, item.X, item.Y, item.Angle, item.Scale)).ToArray();
}
