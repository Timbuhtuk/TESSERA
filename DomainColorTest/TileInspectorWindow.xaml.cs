using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class TileInspectorWindow : Window
{
    private bool _ready, _closed, _settingCollection, _projectDirty, _allowClose, _busy, _reloading;
    private int _loadVersion;
    private string? _projectPath;
    private readonly ObservableCollection<TileAsset> _assets = [];
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _protectedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Key> _heatmapKeys = [];
    private bool HeatmapEnabled => heatInput.IsChecked == true || _heatmapKeys.Count > 0;
    private readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private sealed record CompanionChoice(TileAsset? Asset, TileAsset? SelfAsset = null)
    {
        public string Label => Asset?.Label ?? Localization.Text("Сам с собой");
        public TileAsset? DisplayAsset => Asset ?? SelfAsset;
        public string Caption => Asset is null ? "↻ A" : $"{Asset.Width}×{Asset.Height}";
        public override string ToString() => Label;
    }
    private bool? _compactLayout;
    private TileAsset? CompanionAsset => (companionInput.SelectedItem as CompanionChoice)?.Asset;
    public IReadOnlyList<TileAsset> Assets => _assets;
    public TileAsset? ActiveAsset => assetList.SelectedItem as TileAsset;
    public TileSeamAnalysis? Analysis => preview.Analysis;
    public TileSeamAnalysis? ReverseAnalysis => preview.ReverseAnalysis;
    public string? SourcePath => ActiveAsset?.SourcePath;
    public Action<string>? ValidateExportPath { get; set; }
    public IReadOnlyCollection<string> SourcePaths => _protectedSourcePaths;

    public TileInspectorWindow()
    {
        InitializeComponent();
        assetList.ItemsSource = _assets;
        _assets.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (TileAsset asset in e.NewItems)
            {
                if (asset.SourcePath is not null) _protectedSourcePaths.Add(Path.GetFullPath(asset.SourcePath));
                if (asset.AtlasSourcePath is not null) _protectedSourcePaths.Add(Path.GetFullPath(asset.AtlasSourcePath));
            }
        };
        _ready = true;
        preview.PixelHovered += text => pixelLabel.Text = text.Length == 0
            ? Localization.Text("Наведите курсор на тайл, чтобы увидеть Δ для строки и столбца.") : text;
        InitializePainting();
        previewScroll.PreviewMouseWheel += (_, e) =>
        {
            if (ChangePreviewZoom(e.Delta, e.GetPosition(previewScroll), Keyboard.Modifiers)) e.Handled = true;
        };
        _reloadTimer.Tick += async (_, _) => { _reloadTimer.Stop(); await ReloadChangedFilesAsync(); };
        Closing += ConfirmWindowClosing;
        Loaded += (_, _) => { if (Owner is not null) Owner.Closing += ConfirmWindowClosing; UpdateCompactLayout(); };
        SizeChanged += (_, _) => UpdateCompactLayout();
        Closed += (_, _) =>
        {
            _closed = true; _loadVersion++;
            _reloadTimer.Stop();
            foreach (var watcher in _watchers) watcher.Dispose();
            if (Owner is not null) Owner.Closing -= ConfirmWindowClosing;
        };
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (_closed) break;
            await LoadFileAsync(path);
        }
    }

    public async Task LoadFileAsync(string path, bool automatic = false, bool discardEdits = false)
    {
        if (_busy || _strokeAsset is not null) return;
        if (!automatic && string.Equals(Path.GetExtension(path), ".ttiles", StringComparison.OrdinalIgnoreCase))
        {
            if (!ConfirmReplaceCollection()) return;
            try { LoadCollectionFromFile(path); }
            catch (Exception ex) { statusLabel.Text = Localization.Text("Не удалось открыть коллекцию: ") + ex.Message; }
            return;
        }
        int version = ++_loadVersion;
        statusLabel.Text = automatic ? Localization.Text("Файл изменён · обновление…") : Localization.Text("Загрузка и анализ краёв…");
        try
        {
            string fullPath = Path.GetFullPath(path);
            var existing = _assets.FirstOrDefault(asset => string.Equals(asset.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing?.HasLocalEdits == true && !discardEdits)
            {
                if (!automatic) assetList.SelectedItem = existing;
                statusLabel.Text = Localization.Text("В тайле есть локальные правки. Перечитайте файл вручную, чтобы заменить их.");
                return;
            }
            long assetVersion = existing?.Version ?? 0;
            var image = await DecodeAsync(fullPath, automatic);
            if (_closed || version != _loadVersion || (existing is not null && existing.Version != assetVersion)) return;
            TileAsset asset;
            if (existing is not null && existing.Width == image.PixelWidth && existing.Height == image.PixelHeight)
            {
                existing.Reload(image); asset = existing;
            }
            else
            {
                asset = new TileAsset(image) { Id = existing?.Id ?? Guid.NewGuid(), Name = Path.GetFileName(fullPath), SourcePath = fullPath };
                if (existing is not null) _assets[_assets.IndexOf(existing)] = asset;
                else _assets.Add(asset);
            }
            assetList.SelectedItem = asset;
            SetProjectDirty(); RefreshActiveAsset(); ConfigureWatchers();
            statusLabel.Text = $"{(automatic ? "Обновлено из файла" : "Изображение загружено")} · {asset.Name}";
        }
        catch (Exception ex)
        {
            if (!_closed && version == _loadVersion)
                statusLabel.Text = Localization.Format("Не удалось прочитать файл: {0}", ex.Message) + (Analysis is not null ? Localization.Text(" · Показана предыдущая версия.") : "");
        }
    }

    private static async Task<BitmapSource> DecodeAsync(string path, bool retry)
    {
        return await Task.Run(async () =>
        {
            for (int q = 0; ; q++)
            {
                try { return TileAsset.DecodeFile(path); }
                catch (Exception ex) when (retry && q < 3 && ex is IOException or FileFormatException or NotSupportedException)
                { await Task.Delay(200 * (q + 1)); }
            }
        });
    }

    public void SelectAsset(Guid id)
    {
        FinishStroke();
        assetList.SelectedItem = _assets.FirstOrDefault(asset => asset.Id == id);
    }

    public void SelectCompanion(Guid? id)
    {
        if (id is not null && !companionInput.Items.Cast<CompanionChoice>().Any(choice => choice.Asset?.Id == id))
            throw new ArgumentException(Localization.Text("Соседний тайл должен иметь тот же размер, что и активный."));
        companionInput.SelectedItem = companionInput.Items.Cast<CompanionChoice>().First(choice => choice.Asset?.Id == id);
    }

    private void RefreshActiveAsset()
    {
        if (!_ready || _settingCollection) return;
        Guid? neighbor = CompanionAsset?.Id;
        _settingCollection = true;
        var choices = new List<CompanionChoice> { new(null, ActiveAsset) };
        if (ActiveAsset is { } active)
            choices.AddRange(_assets.Where(asset => asset != active && asset.Width == active.Width && asset.Height == active.Height)
                .Select(asset => new CompanionChoice(asset)));
        companionInput.ItemsSource = choices;
        companionInput.SelectedItem = choices.FirstOrDefault(choice => choice.Asset?.Id == neighbor) ?? choices[0];
        _settingCollection = false;
        UpdateAnalysis(); UpdateEditControls();
    }

    private void UpdateAnalysis()
    {
        var active = ActiveAsset;
        if (active is null)
        {
            preview.Clear(); onboarding.Visibility = Visibility.Visible;
            fileLabel.Text = Localization.Text("Добавьте тайлы в коллекцию");
            xSummary.Text = Localization.Text("X · нет изображения"); ySummary.Text = Localization.Text("Y · нет изображения");
            reloadButton.IsEnabled = exportButton.IsEnabled = false;
            return;
        }
        var companion = CompanionAsset;
        var analysis = TileAsset.Compare(active, companion ?? active);
        var reverse = companion is null ? analysis : TileAsset.Compare(companion, active);
        preview.SetPair(active.Image, companion?.Image, analysis, reverse);
        onboarding.Visibility = Visibility.Collapsed;
        fileLabel.Text = $"A: {active.Label}"; fileLabel.ToolTip = active.SourcePath ?? active.AtlasSourcePath;
        pairLabel.Text = companion is null ? Localization.Text("Сам с собой") : Localization.Format("A: {0}\nB: {1}\nЧередование A/B; Δ среднее/макс.", active.Name, companion.Name);
        reloadButton.IsEnabled = active.SourcePath is not null && !_busy;
        exportButton.IsEnabled = !_busy;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!_ready || preview.Source is null || Analysis is null) return;
        _zoomRequestRevision++;
        bool repeatX = repeatXInput.IsChecked == true, repeatY = repeatYInput.IsChecked == true;
        int copies = copiesInput.SelectedIndex == 1 ? 5 : copiesInput.SelectedIndex == 2 ? 7 : 3;
        double scale = zoomInput.SelectedIndex <= 0 ? Math.Min(
            Math.Max(1, previewScroll.ActualWidth - 20) / (preview.Source.PixelWidth * (repeatX ? copies : 1)),
            Math.Max(1, previewScroll.ActualHeight - 20) / (preview.Source.PixelHeight * (repeatY ? copies : 1)))
            : TileZoomFactors[zoomInput.SelectedIndex];
        if (zoomInput.SelectedIndex == 0 && scale >= 1) scale = Math.Max(1, Math.Floor(scale));
        double threshold = thresholdInput.Value;
        thresholdLabel.Text = Localization.Format("Порог различия Δ: {0:F1}", threshold);
        opacityLabel.Text = Localization.Format("Непрозрачность: {0:F0}%", opacityInput.Value);
        preview.Configure(repeatX, repeatY, copies, scale, HeatmapEnabled, guidesInput.IsChecked == true,
            threshold, opacityInput.Value / 100, stripeInput.Value, backgroundInput.SelectedIndex);
        if (CompanionAsset is null)
        {
            xSummary.Text = Summary(Localization.Text("X · левый ↔ правый"), Analysis.Horizontal, repeatX, threshold);
            ySummary.Text = Summary(Localization.Text("Y · верхний ↔ нижний"), Analysis.Vertical, repeatY, threshold);
        }
        else
        {
            xSummary.Text = PairSummary("X", Analysis.Horizontal, ReverseAnalysis!.Horizontal, repeatX, threshold);
            ySummary.Text = PairSummary("Y", Analysis.Vertical, ReverseAnalysis!.Vertical, repeatY, threshold);
        }
    }

    private static string Summary(string axis, double[] values, bool enabled, double threshold)
    {
        if (!enabled) return Localization.Format("{0} · выключено", axis);
        var summary = TileSeamAnalysis.Summarize(values, threshold);
        string verdict = summary.Maximum < .000001 ? Localization.Text("края совпадают") : summary.AboveThreshold == 0 ? Localization.Text("в пределах порога") : Localization.Text("есть различия");
        return Localization.Format("{0} · {1}\nΔ среднее {2:F2} · макс. {3:F2} · выше порога {4}/{5} ({6:F1}%)", axis, verdict, summary.Mean, summary.Maximum, summary.AboveThreshold, summary.Count, 100.0 * summary.AboveThreshold / summary.Count);
    }

    private static string PairSummary(string axis, double[] forward, double[] reverse, bool enabled, double threshold)
    {
        if (!enabled) return Localization.Format("{0} · выключено", axis);
        string Describe(string direction, double[] values)
        {
            var s = TileSeamAnalysis.Summarize(values, threshold);
            return Localization.Format("{0}: Δ {1:F2}/{2:F2} · >порога {3}/{4} ({5:F1}%)", direction, s.Mean, s.Maximum, s.AboveThreshold, s.Count, 100.0 * s.AboveThreshold / s.Count);
        }
        return $"{axis} · {Describe("A→B", forward)}\n     {Describe("B→A", reverse)}";
    }

    private void ConfigureWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        if (watchInput.IsChecked != true) return;
        foreach (string directory in _assets.Where(asset => asset.SourcePath is not null).Select(asset => Path.GetDirectoryName(asset.SourcePath!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                watcher.Changed += FileChanged; watcher.Created += FileChanged; watcher.Deleted += FileChanged;
                watcher.Renamed += (_, e) => { QueueReload(e.OldFullPath); QueueReload(e.FullPath); };
                watcher.Error += (_, _) => QueueReload(null);
                watcher.EnableRaisingEvents = true; _watchers.Add(watcher);
            }
            catch (Exception ex) { statusLabel.Text = Localization.Format("Автообновление недоступно: {0}", ex.Message); }
        }
    }

    private void FileChanged(object sender, FileSystemEventArgs e) => QueueReload(e.FullPath);
    private void QueueReload(string? path)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closed || watchInput.IsChecked != true) return;
            foreach (var asset in _assets.Where(asset => asset.SourcePath is not null &&
                (path is null || string.Equals(asset.SourcePath, path, StringComparison.OrdinalIgnoreCase)))) _changedPaths.Add(asset.SourcePath!);
            if (_changedPaths.Count == 0) return;
            _reloadTimer.Stop(); _reloadTimer.Start();
        });
    }

    private async Task ReloadChangedFilesAsync()
    {
        if (_busy || _strokeAsset is not null || _reloading) { _reloadTimer.Start(); return; }
        _reloading = true;
        try
        {
            string[] paths = _changedPaths.ToArray(); _changedPaths.Clear();
            foreach (string path in paths)
            {
                var asset = _assets.FirstOrDefault(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
                if (asset is null) continue;
                if (asset.HasLocalEdits) { statusLabel.Text = Localization.Format("{0}: файл изменён снаружи; локальные правки сохранены. Для замены перечитайте вручную.", asset.Name); continue; }
                long version = asset.Version;
                try
                {
                    var image = await DecodeAsync(path, true);
                    if (_closed || watchInput.IsChecked != true || !_assets.Contains(asset) || asset.Version != version || asset.HasLocalEdits || _strokeAsset == asset) continue;
                    if (image.PixelWidth == asset.Width && image.PixelHeight == asset.Height) asset.Reload(image);
                    else
                    {
                        bool selected = ActiveAsset == asset;
                        var replacement = new TileAsset(image) { Id = asset.Id, Name = asset.Name, SourcePath = path };
                        _assets[_assets.IndexOf(asset)] = replacement;
                        if (selected) assetList.SelectedItem = replacement;
                    }
                    SetProjectDirty(); RefreshActiveAsset();
                    statusLabel.Text = Localization.Format("Обновлено из файла · {0}", asset.Name);
                }
                catch (Exception ex) { if (!_closed) statusLabel.Text = Localization.Format("{0}: {1} · Показана предыдущая версия.", asset.Name, ex.Message); }
            }
        }
        finally { _reloading = false; }
    }

    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = Localization.Text("Добавить тайлы"), Multiselect = true, Filter = Localization.Text("Изображения|*.png;*.bmp;*.gif;*.jpg;*.jpeg;*.tif;*.tiff|Все файлы|*.*") };
        if (dialog.ShowDialog(this) == true) await AddFilesAsync(dialog.FileNames);
    }
    private async void ReloadClicked(object sender, RoutedEventArgs e)
    {
        if (ActiveAsset is not { SourcePath: { } path } asset || _busy) return;
        if (asset.HasLocalEdits && TesseraDialog.Show(this, Localization.Text("Заменить локальные правки активного тайла содержимым исходного файла?"), Localization.Text("Перечитать тайл"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await LoadFileAsync(path, discardEdits: true);
    }
    private void FileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && paths.All(File.Exists) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void FileDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await AddFilesAsync(paths);
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            _heatmapKeys.Add(key);
            preview.HeatmapVisible = HeatmapEnabled;
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O) OpenClicked(sender, e);
        else if (e.Key == Key.S) SaveCollectionClicked(sender, e);
        else if (e.Key is Key.Z or Key.Y)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Z) UndoClicked(sender, e); else RedoClicked(sender, e);
        }
        else return;
        e.Handled = true;
    }
    private void WindowKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt)) return;
        _heatmapKeys.Remove(key);
        preview.HeatmapVisible = HeatmapEnabled;
        e.Handled = true;
    }
    private void WindowDeactivated(object? sender, EventArgs e)
    {
        _heatmapKeys.Clear();
        preview.HeatmapVisible = HeatmapEnabled;
    }
    private void AssetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingCollection) return;
        FinishStroke(); _historyAsset = ActiveAsset; RefreshActiveAsset(); SetProjectDirty();
        if (ActiveAsset is not null) assetList.ScrollIntoView(ActiveAsset);
    }
    private void CompanionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _settingCollection) return;
        FinishStroke(); UpdateAnalysis(); SetProjectDirty();
    }
    private void WatchChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _reloadTimer.Stop(); _changedPaths.Clear(); ConfigureWatchers();
    }
    private void OptionsChanged(object sender, RoutedEventArgs e) { RefreshPreview(); if (_ready) SetProjectDirty(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshPreview(); if (_ready) SetProjectDirty(); }
    private void ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { RefreshPreview(); if (_ready) SetProjectDirty(); }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) { if (_ready && zoomInput.SelectedIndex == 0) RefreshPreview(); }

    private void UpdateCompactLayout()
    {
        if (!_ready) return;
        bool compact = ActualWidth < 940 || ActualHeight < 700;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        if (compact) inspectorTitle.FontSize = 20;
        else inspectorTitle.ClearValue(TextBlock.FontSizeProperty);
        editTools.IsExpanded = !compact;
        editScroll.MaxHeight = compact ? 80 : double.PositiveInfinity;
        summaryScroll.MaxHeight = compact ? 100 : 125;
    }
}
