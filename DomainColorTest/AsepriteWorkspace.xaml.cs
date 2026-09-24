using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelArtAseprite;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class AsepriteWorkspace : UserControl, IDisposable
{
    public sealed class SheetEntry(string inputPath, string directory) : INotifyPropertyChanged
    {
        public string InputPath { get; } = inputPath;
        public string Label => Path.GetFileName(InputPath);
        internal string Directory { get; } = directory;
        internal string Snapshot => Path.Combine(Directory, "source", Label);
        public AsepriteExportResult? Result { get; internal set; }
        public BitmapSource? Thumbnail { get; internal set; }
        public string State { get; internal set; } = "В очереди";
        public string Detail { get; internal set; } = inputPath;
        internal bool Pending { get; set; } = true;
        public bool Ready => !Pending && Result is not null;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public sealed record SaveResult(string Input, string? ImagePath, string? Error);
    private readonly ObservableCollection<SheetEntry> _entries = [];
    private readonly Queue<SheetEntry> _pending = new();
    private readonly string _session = Path.Combine(Path.GetTempPath(), "Tessera", "Aseprite", Guid.NewGuid().ToString("N"));
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private CancellationTokenSource? _cancellation;
    private Task _worker = Task.CompletedTask;
    private Bitmap? _previewImage;
    private bool _converting, _saving, _disposed, _settingsPending;
    public bool IsBusy => _converting || _saving || _settingsPending;
    public bool CanAcceptFiles => !_saving && !_disposed;
    public event EventHandler? BackRequested;
    public event EventHandler? BusyChanged;
    public event Action<string>? StatusChanged;

    public AsepriteWorkspace()
    {
        InitializeComponent();
        sheetStrip.ItemsSource = _entries;
        sheetOpenButton.Click += async (_, _) => await OpenAsync();
        sheetBackButton.Click += (_, _) => { if (!IsBusy) BackRequested?.Invoke(this, EventArgs.Empty); };
        sheetSaveButton.Click += async (_, _) => await SaveDialogAsync(false);
        sheetSaveAllButton.Click += async (_, _) => await SaveDialogAsync(true);
        sheetClearButton.Click += (_, _) => Clear();
        sheetCancelButton.Click += (_, _) => _cancellation?.Cancel();
        sheetStrip.SelectionChanged += (_, _) => UpdateSelection();
        sheetLayout.SelectionChanged += (_, _) => ScheduleRebuild();
        sheetColumns.ValueChanged += (_, _) => ScheduleRebuild();
        sheetPadding.ValueChanged += (_, _) => ScheduleRebuild();
        sheetZoom.SelectionChanged += (_, _) => sheetPreview.Zoom = new[] { 0, .5, 1, 2, 4, 8 }[Math.Clamp(sheetZoom.SelectedIndex, 0, 5)];
        sheetBackground.SelectionChanged += (_, _) => sheetPreview.PreviewBackground = sheetBackground.SelectedIndex;
        _settingsTimer.Tick += async (_, _) => { _settingsTimer.Stop(); await RebuildAsync(); };
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    private void UpdateLayoutForWidth()
    {
        bool compact = ActualWidth < 900;
        sheetWorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 260, compact ? GridUnitType.Star : GridUnitType.Pixel);
        sheetWorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 1, compact ? GridUnitType.Pixel : GridUnitType.Star);
        sheetWorkspaceGrid.RowDefinitions[0].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        sheetWorkspaceGrid.RowDefinitions[0].MaxHeight = compact ? 220 : double.PositiveInfinity;
        sheetSettingsPanel.MaxHeight = compact ? 220 : double.PositiveInfinity;
        sheetSettingsPanel.MaxHeight = compact ? 220 : double.PositiveInfinity;
        sheetWorkspaceGrid.RowDefinitions[1].Height = new GridLength(compact ? 1 : 0, compact ? GridUnitType.Star : GridUnitType.Pixel);
        Grid.SetColumn(sheetSettingsPanel, 0);
        Grid.SetRow(sheetSettingsPanel, 0);
        Grid.SetColumn(sheetContentPanel, compact ? 0 : 1);
        Grid.SetRow(sheetContentPanel, compact ? 1 : 0);
        sheetSettingsPanel.BorderThickness = compact ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
    }
    public static bool SupportsFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ase" or ".aseprite";

    public async Task OpenAsync()
    {
        if (!CanAcceptFiles) return;
        var dialog = new OpenFileDialog { Filter = "Aseprite|*.aseprite;*.ase", Title = "Открыть анимации", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await AddFilesAsync(dialog.FileNames);
    }

    public Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (!CanAcceptFiles) return Task.CompletedTask;
        foreach (string path in paths)
        {
            if (!SupportsFile(path)) continue;
            string fullPath = Path.GetFullPath(path);
            var existing = _entries.FirstOrDefault(e => string.Equals(e.InputPath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { sheetStrip.SelectedItem = existing; continue; }
            var entry = new SheetEntry(fullPath, Path.Combine(_session, Guid.NewGuid().ToString("N")));
            _entries.Add(entry);
            if (sheetStrip.SelectedItem is null) sheetStrip.SelectedItem = entry;
            _pending.Enqueue(entry);
        }
        if (_settingsPending) return RebuildAsync();
        if (!_converting && _pending.Count > 0) _worker = ProcessQueueAsync();
        UpdateControls();
        return _worker;
    }

    private AsepriteExportOptions Options() => new()
    {
        Layout = (SpriteSheetLayout)Math.Clamp(sheetLayout.SelectedIndex, 0, 2),
        Columns = sheetColumns.Value, Padding = sheetPadding.Value
    };

    private void ScheduleRebuild()
    {
        sheetColumnsPanel.Visibility = sheetLayout.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_disposed || _converting || _saving || _entries.Count == 0) return;
        _settingsPending = true;
        _settingsTimer.Stop(); _settingsTimer.Start();
        UpdateControls();
    }

    public Task RebuildAsync()
    {
        _settingsTimer.Stop();
        if (_converting || _saving || _disposed) return _worker;
        _settingsPending = false;
        _pending.Clear();
        foreach (var entry in _entries) { entry.Pending = true; entry.State = "В очереди"; entry.Refresh(); _pending.Enqueue(entry); }
        _worker = ProcessQueueAsync();
        return _worker;
    }

    private async Task ProcessQueueAsync()
    {
        _converting = true;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var options = Options();
        UpdateControls();
        int converted = 0, failed = 0;
        try
        {
            while (_pending.TryDequeue(out var entry))
            {
                entry.Pending = true; entry.State = "Конвертация…"; entry.Refresh();
                StatusChanged?.Invoke($"Конвертация: {entry.Label}. Осталось: {_pending.Count}");
                string output = Path.Combine(entry.Directory, Guid.NewGuid().ToString("N"));
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var result = await Task.Run(() =>
                    {
                        EnsureSnapshot(entry, cancellation.Token);
                        var convertedResult = AsepriteConverter.Convert(entry.Snapshot, output, options, cancellationToken: cancellation.Token);
                        return (Result: convertedResult, Thumbnail: LoadThumbnail(convertedResult.ImagePath, convertedResult.Width, convertedResult.Height));
                    });
                    if (_disposed) return;
                    string? previous = entry.Result is null ? null : Path.GetDirectoryName(entry.Result.ImagePath);
                    entry.Result = result.Result; entry.Thumbnail = result.Thumbnail; entry.Pending = false;
                    entry.State = $"{result.Result.FrameCount} кадров · {result.Result.Width} × {result.Result.Height}";
                    entry.Detail = $"{entry.Label} · {entry.State} · {result.Result.TotalDurationMs} мс";
                    if (result.Result.Warnings.Count > 0) entry.Detail += "\n" + string.Join("; ", result.Result.Warnings);
                    entry.Refresh(); converted++;
                    sheetStrip.SelectedItem = entry; UpdateSelection();
                    if (previous is not null) DeleteCache(previous);
                }
                catch (OperationCanceledException)
                {
                    entry.Pending = false; entry.Result = null; entry.State = "Отменено"; entry.Detail = entry.Label + ": конвертация отменена."; entry.Refresh();
                    DeleteCache(output);
                    throw;
                }
                catch (Exception ex)
                {
                    entry.Pending = false; entry.Result = null; entry.State = "Ошибка"; entry.Detail = ex.Message; entry.Refresh();
                    DeleteCache(output); failed++;
                }
                UpdateControls();
            }
            StatusChanged?.Invoke($"Готово: {converted}. Ошибок: {failed}. Сохранение — PNG и JSON для каждой анимации.");
        }
        catch (OperationCanceledException)
        {
            while (_pending.TryDequeue(out var entry)) { entry.Pending = false; entry.Result = null; entry.State = "Отменено"; entry.Detail = entry.Label + ": конвертация отменена."; entry.Refresh(); }
            StatusChanged?.Invoke($"Отменено. Готовых результатов: {_entries.Count(e => e.Ready)}.");
        }
        finally
        {
            _converting = false; _cancellation = null;
            if (_disposed) DeleteCache(_session);
            else { UpdateSelection(); UpdateControls(); }
        }
    }

    private static void EnsureSnapshot(SheetEntry entry, CancellationToken token)
    {
        if (File.Exists(entry.Snapshot)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(entry.Snapshot)!);
        string temporary = entry.Snapshot + ".tmp";
        try
        {
            using (var input = File.OpenRead(entry.InputPath))
            using (var output = File.Create(temporary))
            {
                int total = 0, read; var buffer = new byte[65536];
                while ((read = input.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    total = checked(total + read);
                    if (total > 64 * 1024 * 1024) throw new InvalidDataException("Файл превышает допустимый размер 64 МБ.");
                    output.Write(buffer, 0, read);
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, entry.Snapshot);
        }
        finally { File.Delete(temporary); }
    }

    private static BitmapSource LoadThumbnail(string path, int width, int height)
    {
        using var input = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; if (width >= height) bitmap.DecodePixelWidth = 160; else bitmap.DecodePixelHeight = 96; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    private void UpdateSelection()
    {
        sheetPreview.Image = null;
        _previewImage?.Dispose(); _previewImage = null;
        var entry = sheetStrip.SelectedItem as SheetEntry;
        sheetDetail.Text = entry?.Detail ?? ""; sheetDetail.ToolTip = entry?.Detail;
        sheetPreview.EmptyText = entry is null ? "Перетащите сюда файлы .aseprite или .ase\nСпрайтлисты будут подготовлены автоматически" : entry.Ready ? "" : entry.State;
        if (entry?.Ready == true)
        {
            try { _previewImage = ImageLibrary.ReadBitmap(entry.Result!.ImagePath); sheetPreview.Image = _previewImage; }
            catch (Exception ex) { sheetDetail.Text = $"Не удалось показать предпросмотр: {ex.Message}"; }
        }
        UpdateControls();
    }

    private void UpdateControls()
    {
        sheetOpenButton.IsEnabled = CanAcceptFiles;
        sheetBackButton.IsEnabled = !IsBusy;
        sheetOptions.IsEnabled = !_converting && !_saving;
        sheetSaveButton.IsEnabled = !IsBusy && sheetStrip.SelectedItem is SheetEntry { Ready: true };
        sheetSaveAllButton.IsEnabled = !IsBusy && _entries.Any(e => e.Ready);
        sheetClearButton.IsEnabled = !IsBusy && _entries.Count > 0;
        sheetCancelButton.Visibility = _converting || _saving ? Visibility.Visible : Visibility.Collapsed;
        sheetCount.Text = $"Результаты ({_entries.Count})";
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveDialogAsync(bool all)
    {
        if (IsBusy) return;
        var dialog = new OpenFolderDialog { Title = all ? "Сохранить все спрайтлисты и JSON" : "Сохранить спрайтлист и JSON" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await SaveAsync(dialog.FolderName, all);
    }

    public async Task<IReadOnlyList<SaveResult>> SaveAsync(string directory, bool all)
    {
        if (IsBusy || _disposed) return Array.Empty<SaveResult>();
        var entries = all ? _entries.Where(e => e.Ready).ToArray() : sheetStrip.SelectedItem is SheetEntry { Ready: true } entry ? new[] { entry } : [];
        var results = new List<SaveResult>();
        _saving = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        UpdateControls();
        try
        {
            foreach (var item in entries)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                StatusChanged?.Invoke($"Сохранение: {item.Label}");
                try
                {
                    string path = await Task.Run(() => ExportReady(item, directory, cancellation.Token));
                    results.Add(new SaveResult(item.InputPath, path, null));
                    item.State = "Сохранён"; item.Detail = path; item.Refresh();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { results.Add(new SaveResult(item.InputPath, null, ex.Message)); item.State = "Ошибка сохранения"; item.Detail = ex.Message; item.Refresh(); }
            }
            StatusChanged?.Invoke($"Сохранено: {results.Count(r => r.Error is null)}. Ошибок: {results.Count(r => r.Error is not null)}. Папка: {directory}");
        }
        catch (OperationCanceledException) { StatusChanged?.Invoke($"Сохранение отменено. Сохранено: {results.Count(r => r.Error is null)}."); }
        finally { _saving = false; _cancellation = null; if (_disposed) DeleteCache(_session); else { UpdateSelection(); UpdateControls(); } }
        return results;
    }

    private static string ExportReady(SheetEntry entry, string directory, CancellationToken token)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        string stem = Path.GetFileNameWithoutExtension(entry.Label), name = stem;
        int suffix = 2;
        while (Exists(name + ".png") || Exists(name + ".json")) name = $"{stem} ({suffix++})";
        string png = Path.Combine(directory, name + ".png"), json = Path.Combine(directory, name + ".json");
        string stagedPng = Path.Combine(directory, $".tessera-{Guid.NewGuid():N}.tmp"), stagedJson = stagedPng + ".json";
        bool publishedPng = false, complete = false;
        try
        {
            token.ThrowIfCancellationRequested();
            File.Copy(entry.Result!.ImagePath, stagedPng, false);
            var metadata = JsonNode.Parse(File.ReadAllText(entry.Result.JsonPath))!;
            metadata["image"] = name + ".png";
            File.WriteAllText(stagedJson, metadata.ToJsonString(AsepriteConverter.JsonOptions));
            token.ThrowIfCancellationRequested();
            File.Move(stagedPng, png, false); publishedPng = true;
            token.ThrowIfCancellationRequested();
            File.Move(stagedJson, json, false); complete = true;
            return png;
        }
        finally
        {
            if (publishedPng && !complete) File.Delete(png);
            File.Delete(stagedPng); File.Delete(stagedJson);
        }
        bool Exists(string file) => File.Exists(Path.Combine(directory, file)) || Directory.Exists(Path.Combine(directory, file));
    }

    private void RemoveSheet(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsBusy || sender is not FrameworkElement { DataContext: SheetEntry entry }) return;
        int index = _entries.IndexOf(entry);
        _entries.Remove(entry);
        if (_entries.Count > 0 && sheetStrip.SelectedItem is null) sheetStrip.SelectedIndex = Math.Min(index, _entries.Count - 1);
        DeleteCache(entry.Directory); UpdateSelection();
    }

    public void Clear()
    {
        if (IsBusy) return;
        _entries.Clear(); UpdateSelection(); DeleteCache(_session);
    }

    private void DeleteCache(string path)
    {
        string full = Path.GetFullPath(path), root = Path.GetFullPath(_session);
        if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cache path.");
        try
        {
            if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0) Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _disposed = true; _settingsTimer.Stop(); _cancellation?.Cancel();
        sheetPreview.Image = null; _previewImage?.Dispose(); _previewImage = null;
        if (!_converting && !_saving) DeleteCache(_session);
    }
}
