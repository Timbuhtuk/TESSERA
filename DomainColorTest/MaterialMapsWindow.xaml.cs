using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MaterialMapsWindow : Window
{
    public MaterialMapDocument? Document { get; private set; }
    public bool IsBusy { get; private set; }
    public string? ProjectPath { get; private set; }
    public Action<string>? ValidateExportPath { get; set; }
    private bool _ready, _allowClose, _closed;
    private string _name = "material";
    private BitmapSource? _source;
    private MaterialMapSnapshot? _strokeBefore;
    private MaterialMapKind _strokeKind;
    private (int X, int Y)? _lastPoint;
    private Vector3 _light = Vector3.Normalize(new(-.5f, .5f, 1));
    private readonly DispatcherTimer _generationTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _paintTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly DispatcherTimer _lightTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private bool _paintPending, _lightPending;
    private IEnumerable<MaterialMapView> MapViews => new[] { heightView, normalView, aoView };

    public MaterialMapsWindow()
    {
        InitializeComponent();
        Wire(heightView, MaterialMapKind.Height); Wire(normalView, MaterialMapKind.Normal); Wire(aoView, MaterialMapKind.AO);
        directionPicker.Changed += _ => UpdateDirection(); UpdateDirection();
        aoRadiusInput.ValueChanged += (_, _) => ScheduleGeneration();
        _generationTimer.Tick += async (_, _) => { _generationTimer.Stop(); await ApplyGenerationAsync(); };
        _paintTimer.Tick += (_, _) =>
        {
            if (!_paintPending || Document is null) return;
            _paintPending = false; RefreshMap(_strokeKind); if (_strokeKind != MaterialMapKind.Height) RefreshLighting();
        };
        _lightTimer.Tick += (_, _) => { if (_lightPending && !IsBusy) { _lightPending = false; RefreshLighting(); } };
        litView.LightMoved += (x, y) => { _light = Vector3.Normalize(new((float)x, (float)y, .8f)); _lightPending = true; };
        Loaded += (_, _) => { if (Owner is not null) Owner.Closing += OwnerClosing; };
        Closed += (_, _) =>
        {
            _closed = true; _generationTimer.Stop(); _paintTimer.Stop(); _lightTimer.Stop();
            if (Owner is not null) Owner.Closing -= OwnerClosing;
        };
        SizeChanged += (_, _) => settingsColumn.Width = new GridLength(ActualWidth < 1100 ? 205 : 255);
        Localize(this);
        SetBusy(true);
    }

    private static void Localize(DependencyObject element)
    {
        if (element is TextBlock text) text.Text = Localization.Text(text.Text);
        if (element is ContentControl { Content: string content } control) control.Content = Localization.Text(content);
        if (element is FrameworkElement { ToolTip: string tip } framework) framework.ToolTip = Localization.Text(tip);
        if (element is FrameworkElement node)
        {
            string name = System.Windows.Automation.AutomationProperties.GetName(node);
            if (name.Length > 0) System.Windows.Automation.AutomationProperties.SetName(node, Localization.Text(name));
        }
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) Localize(child);
    }

    public async Task SetImageAsync(Bitmap image, string name)
    {
        _name = Path.GetFileNameWithoutExtension(name);
        _source = PixelPreview.ToBitmapSource(image);
        if (_source.Format != PixelFormats.Bgra32) _source = new FormatConvertedBitmap(_source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(_source.PixelWidth * _source.PixelHeight * 4)];
        _source.CopyPixels(pixels, _source.PixelWidth * 4, 0);
        int width = _source.PixelWidth, height = _source.PixelHeight;
        statusLabel.Text = Localization.Text("Генерация Height, Normal и AO…");
        try
        {
            Document = await Task.Run(() => new MaterialMapDocument(width, height, pixels));
            ProjectPath = null; BindDocument();
        }
        finally { SetBusy(false); }
    }

    private void BindDocument()
    {
        if (Document is null) return;
        _ready = false;
        _source = ToImage(Document.Source, Document.Width, Document.Height);
        normalStrengthInput.Value = Document.NormalStrength; aoStrengthInput.Value = Document.AoStrength;
        aoRadiusInput.Value = Document.AoRadius; conventionInput.SelectedIndex = Document.DirectX ? 0 : 1;
        sourceLabel.Text = $"{_name} · {Document.Width} × {Document.Height}";
        foreach (var view in MapViews) view.Overlay = _source;
        _ready = true; _paintTimer.Start(); _lightTimer.Start();
        RefreshAll();
        statusLabel.Text = Localization.Text("Рисуйте на любой карте. Height обновляет Normal и AO, сохраняя их ручные слои.");
    }

    private void Wire(MaterialMapView view, MaterialMapKind kind)
    {
        view.PaintRequested += (x, y, begin) => Paint(kind, x, y, begin);
        view.StrokeEnded += async () => await FinishStrokeAsync();
        view.ZoomRequested += delta => zoomInput.SelectedIndex = Math.Clamp(zoomInput.SelectedIndex + delta, 0, zoomInput.Items.Count - 1);
    }

    private void Paint(MaterialMapKind kind, int x, int y, bool begin)
    {
        if (!_ready || IsBusy || Document is null) return;
        if (brushInput.SelectedIndex == 2)
        {
            int q = y * Document.Width + x;
            if (kind == MaterialMapKind.Normal) directionPicker.SetNormal(Document.NormalAt(q));
            else valueInput.Value = kind == MaterialMapKind.Height ? Document.Heights[q] : Document.AoAt(q);
            statusLabel.Text = Localization.Format("{0} · пиксель ({1}, {2})", kind, x, y); return;
        }
        if (begin)
        {
            _strokeBefore = Document.Capture(); _strokeKind = kind; _lastPoint = (x, y);
            foreach (var border in new[] { heightBorder, normalBorder, aoBorder }) border.SetResourceReference(Border.BorderBrushProperty, "Ts.Border");
            (kind == MaterialMapKind.Height ? heightBorder : kind == MaterialMapKind.Normal ? normalBorder : aoBorder).SetResourceReference(Border.BorderBrushProperty, "Ts.Text");
        }
        if (_strokeBefore is null || kind != _strokeKind) return;
        var from = _lastPoint ?? (x, y);
        Document.PaintLine(kind, from.Item1, from.Item2, x, y, brushSizeInput.Value, (float)valueInput.Value,
            directionPicker.Normal, (float)brushStrengthInput.Value, (float)softnessInput.Value, brushInput.SelectedIndex == 1);
        _lastPoint = (x, y); _paintPending = true;
    }

    public async Task FinishStrokeAsync()
    {
        if (_strokeBefore is not { } before || Document is null) return;
        _strokeBefore = null; _lastPoint = null; _paintPending = false;
        if (!Document.Commit(before)) return;
        if (_strokeKind == MaterialMapKind.Height) await RecalculateAsync();
        else RefreshAll();
    }

    private async Task RecalculateAsync()
    {
        if (Document is null || IsBusy) return;
        SetBusy(true); statusLabel.Text = Localization.Text("Пересчёт Normal и AO · ручные слои сохраняются…");
        try { await Task.Run(Document.Recalculate); RefreshAll(); statusLabel.Text = Localization.Text("Карты обновлены · ручные слои сохранены."); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private void GenerationChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ScheduleGeneration();
    private void ConventionChanged(object sender, SelectionChangedEventArgs e) => ScheduleGeneration();
    private void ScheduleGeneration()
    {
        if (!_ready) return;
        _generationTimer.Stop(); _generationTimer.Start();
    }
    public async Task ApplyGenerationAsync()
    {
        _generationTimer.Stop();
        if (Document is null || !_ready) return;
        if (IsBusy || _strokeBefore is not null) { _generationTimer.Start(); return; }
        if (ReadGenerationSettings()) await RecalculateAsync();
    }
    private bool ReadGenerationSettings()
    {
        if (Document is null) return false;
        var before = Document.Capture();
        Document.NormalStrength = (float)normalStrengthInput.Value; Document.AoStrength = (float)aoStrengthInput.Value;
        Document.AoRadius = aoRadiusInput.Value; Document.DirectX = conventionInput.SelectedIndex == 0;
        return Document.Commit(before);
    }
    private void FlushGeneration()
    {
        if (!_generationTimer.IsEnabled || Document is null || IsBusy) return;
        _generationTimer.Stop();
        // A save/export/close may arrive before the debounced slider update.
        if (ReadGenerationSettings()) { Document.Recalculate(); RefreshAll(); }
    }

    private void RefreshAll()
    {
        if (Document is null || _closed) return;
        foreach (var kind in Enum.GetValues<MaterialMapKind>()) RefreshMap(kind);
        ApplyView(); RefreshLighting(); UpdateHistory();
    }
    private void RefreshMap(MaterialMapKind kind)
    {
        if (Document is null) return;
        var view = kind == MaterialMapKind.Height ? heightView : kind == MaterialMapKind.Normal ? normalView : aoView;
        view.Image = ToImage(Document.MapPixels(kind), Document.Width, Document.Height);
    }
    private void RefreshLighting()
    {
        if (Document is null || _closed) return;
        double factor = Math.Min(1, 512.0 / Math.Max(Document.Width, Document.Height));
        int width = Math.Max(1, (int)Math.Round(Document.Width * factor)), height = Math.Max(1, (int)Math.Round(Document.Height * factor));
        litView.Image = ToImage(Document.LitPixels(width, height, _light, useNormalInput.IsChecked == true,
            useAoInput.IsChecked == true, compareInput.IsChecked == true), width, height);
    }
    private static BitmapSource ToImage(byte[] pixels, int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, checked(width * 4));
        image.Freeze(); return image;
    }
    private void ViewChanged(object sender, RoutedEventArgs e) { if (_ready) ApplyView(); }
    private void ApplyView()
    {
        foreach (var view in MapViews)
        {
            view.Zoom = zoomInput.SelectedIndex <= 0 ? 0 : Math.Pow(2, zoomInput.SelectedIndex - 1);
            view.ShowGrid = gridInput.IsChecked == true; view.OverlayOpacity = overlayInput.Value; view.Refresh();
        }
    }
    private void LightingChanged(object sender, RoutedEventArgs e) { if (_ready) RefreshLighting(); }
    private void UpdateDirection() => directionLabel.Text = $"X {directionPicker.Normal.X:0.00} · Y {directionPicker.Normal.Y:0.00} · Z {directionPicker.Normal.Z:0.00}";
    private void UpdateHistory()
    {
        undoButton.IsEnabled = Document?.CanUndo == true; redoButton.IsEnabled = Document?.CanRedo == true;
        Title = $"{_name}{(Document?.HasChanges == true ? " *" : "")} · Height / Normal / AO — TESSERA";
    }
    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        settingsPanel.IsEnabled = viewSettings.IsEnabled = openProjectButton.IsEnabled = saveProjectButton.IsEnabled = exportButton.IsEnabled = !busy;
        foreach (var view in MapViews) view.IsEnabled = !busy;
        if (!busy) UpdateHistory();
    }
    private void UndoClicked(object sender, RoutedEventArgs e) => History(false);
    private void RedoClicked(object sender, RoutedEventArgs e) => History(true);
    private void History(bool redo)
    {
        if (IsBusy || Document is null || _strokeBefore is not null) return;
        FlushGeneration();
        if (redo ? Document.Redo() : Document.Undo()) BindDocument();
    }
    private void ResetNormalClicked(object sender, RoutedEventArgs e) => Reset(MaterialMapKind.Normal);
    private void ResetAoClicked(object sender, RoutedEventArgs e) => Reset(MaterialMapKind.AO);
    public void Reset(MaterialMapKind kind)
    {
        if (IsBusy || Document is null || _strokeBefore is not null) return;
        FlushGeneration();
        Document.ResetManual(kind); RefreshAll();
    }

    public void SaveProject(string path)
    {
        if (Document is null || IsBusy) return;
        FlushGeneration();
        ValidateExportPath?.Invoke(path);
        MaterialMapFile.Save(Document, path); ProjectPath = Path.GetFullPath(path); UpdateHistory();
        statusLabel.Text = Localization.Text("Проект карт сохранён: ") + ProjectPath;
    }
    private bool SaveProjectDialog()
    {
        if (IsBusy || Document is null) return false;
        var dialog = new SaveFileDialog { Filter = "Tessera material maps|*.tmaps", DefaultExt = ".tmaps", AddExtension = true,
            FileName = ProjectPath ?? _name + ".tmaps", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return false;
        SaveProject(dialog.FileName); return true;
    }
    private void SaveProjectClicked(object sender, RoutedEventArgs e) => Safely(() => SaveProjectDialog());
    private async void OpenProjectClicked(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !CanLeave()) return;
        var dialog = new OpenFileDialog { Filter = "Tessera material maps|*.tmaps" };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var document = await Task.Run(() => MaterialMapFile.Load(dialog.FileName));
            Document = document; ProjectPath = dialog.FileName; _name = Path.GetFileNameWithoutExtension(dialog.FileName); _allowClose = false;
            BindDocument();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    public void ExportMap(MaterialMapKind kind, string path)
    {
        if (Document is null || IsBusy) return;
        FlushGeneration();
        ValidateExportPath?.Invoke(path);
        string fullPath = Path.GetFullPath(path);
        if (string.Equals(fullPath, ProjectPath, StringComparison.OrdinalIgnoreCase)) throw new IOException(Localization.Text("Выберите отдельный файл для экспорта карты."));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(ToImage(Document.MapPixels(kind), Document.Width, Document.Height)));
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = File.Create(temporary)) encoder.Save(stream); File.Move(temporary, fullPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void ExportMapClicked(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (IsBusy || Document is null) return;
        var kind = Enum.Parse<MaterialMapKind>((string)((Button)sender).Tag);
        var dialog = PngDialog(_name + "_" + kind.ToString().ToLowerInvariant() + ".png");
        if (dialog.ShowDialog(this) == true) { ExportMap(kind, dialog.FileName); statusLabel.Text = Localization.Text("Карта сохранена: ") + dialog.FileName; }
    });
    private void ExportClicked(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (IsBusy || Document is null) return;
        var dialog = PngDialog(_name + ".png"); dialog.OverwritePrompt = false;
        if (dialog.ShowDialog(this) != true) return;
        string prefix = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, Path.GetFileNameWithoutExtension(dialog.FileName));
        var paths = Enum.GetValues<MaterialMapKind>().Select(kind => (Kind: kind, Path: prefix + "_" + kind.ToString().ToLowerInvariant() + ".png")).ToArray();
        foreach (var item in paths) ValidateExportPath?.Invoke(item.Path);
        string[] existing = paths.Where(item => File.Exists(item.Path)).Select(item => Path.GetFileName(item.Path)).ToArray();
        if (existing.Length > 0 && TesseraDialog.Show(this, Localization.Text("Заменить существующие карты?\n") + string.Join("\n", existing), "Tessera",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var item in paths) ExportMap(item.Kind, item.Path);
        statusLabel.Text = Localization.Text("Сохранены Height, Normal и AO: ") + Path.GetDirectoryName(dialog.FileName);
    });
    private static SaveFileDialog PngDialog(string name) => new() { Filter = "PNG|*.png", FileName = name, DefaultExt = ".png", AddExtension = true, OverwritePrompt = true };
    private void Safely(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) => statusLabel.Text = Localization.Text("Ошибка карт материала: ") + ex.Message;
    public bool CanLeave()
    {
        foreach (var view in MapViews) view.FinishStroke();
        if (IsBusy) return false;
        FlushGeneration();
        if (Document?.HasChanges != true) return true;
        var answer = TesseraDialog.Show(this, Localization.Text("Сохранить проект карт с ручными слоями перед закрытием?"), "Tessera",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && SaveProjectDialog();
    }
    private void OwnerClosing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || _allowClose) return;
        try
        {
            if (!CanLeave()) e.Cancel = true;
            else
            {
                _allowClose = true;
                Dispatcher.BeginInvoke(new Action(() => _allowClose = false));
            }
        }
        catch (Exception ex) { ShowError(ex); e.Cancel = true; }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        try { e.Cancel = !CanLeave(); }
        catch (Exception ex) { ShowError(ex); e.Cancel = true; }
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key != Key.S && Keyboard.FocusedElement is TextBoxBase) return;
        if (e.Key == Key.Z) History(false);
        else if (e.Key == Key.Y) History(true);
        else if (e.Key == Key.S) Safely(() => { Focus(); if (ProjectPath is null) SaveProjectDialog(); else SaveProject(ProjectPath); });
        else return;
        e.Handled = true;
    }
}
