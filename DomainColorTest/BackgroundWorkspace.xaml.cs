using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace DomainColorTest;

public partial class BackgroundWorkspace : UserControl, IDisposable
{
    private Bitmap? _source;
    private Bitmap? _result;
    private string? _inputPath;
    private bool _busy;
    private bool _disposed;
    private int _revision;
    private CancellationTokenSource? _previewCancellation;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    public bool IsBusy => _busy;
    public bool IsRendering { get; private set; }
    public bool HasResult => _result is not null;
    public event EventHandler? BackRequested;
    public event Action<EditorMaterial>? MaterialRequested;

    public event EventHandler? BusyChanged;
    public event Action<string>? StatusChanged;

    public BackgroundWorkspace()
    {
        InitializeComponent();
        backgroundBackButton.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        backgroundOpenButton.Click += async (_, _) => await OpenAsync();
        backgroundSaveButton.Click += async (_, _) => await SaveDialogAsync();
        backgroundEditorChoices.MaterialSelected += material => MaterialRequested?.Invoke(material);

        backgroundModeInput.SelectionChanged += (_, _) => SchedulePreview();
        backgroundColorInput.SelectionChanged += (_, _) => SchedulePreview();
        backgroundToleranceInput.ValueChanged += (_, _) => SchedulePreview();
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshPreviewAsync(); };
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    public void SetEditorMaterials(IEnumerable<EditorMaterial> materials)
        => backgroundEditorChoices.SetMaterials(materials);

    public async Task OpenAsync()
    {
        if (_busy || _disposed) return;
        var dialog = new OpenFileDialog { Title = "Изображение для удаления фона", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await LoadFileAsync(dialog.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (_busy || _disposed) return;
        SetBusy(true);
        try
        {
            using var image = await Task.Run(() => IconWorkspace.ReadImage(path));
            if (_disposed) return;
            SetImage(image, Path.GetFileName(path), path);
            StatusChanged?.Invoke("Изображение открыто. Настройте удаление фона и сохраните PNG.");
        }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось открыть изображение: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    public void SetImage(Bitmap source, string label, string? inputPath = null)
    {
        if (_disposed) return;
        var copy = (Bitmap)source.Clone();
        _previewCancellation?.Cancel();
        ++_revision;
        backgroundSourcePreview.Image = backgroundResultPreview.Image = null;
        _source?.Dispose();
        _result?.Dispose();
        _source = copy;
        _result = null;
        _inputPath = inputPath is null ? null : Path.GetFullPath(inputPath);
        backgroundFileLabel.Text = label;
        backgroundFileLabel.ToolTip = label;
        backgroundSourcePreview.Image = _source;
        SchedulePreview();
    }

    public (BackgroundRemovalMode Mode, int Tolerance, Color? Color) GetOptions() =>
        (backgroundModeInput.SelectedIndex == 1 ? BackgroundRemovalMode.EdgeConnected : BackgroundRemovalMode.GlobalColor,
        (int)backgroundToleranceInput.Value,
        backgroundColorInput.SelectedIndex switch { 1 => Color.White, 2 => Color.Black, _ => (Color?)null });

    private void SchedulePreview()
    {
        if (_disposed) return;
        ++_revision;
        _previewCancellation?.Cancel();
        backgroundResultPreview.Image = null;
        _result?.Dispose();
        _result = null;
        IsRendering = _source is not null;
        UpdateSaveState();
        _previewTimer.Stop();
        if (_source is not null) _previewTimer.Start();
    }

    public async Task RefreshPreviewAsync()
    {
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        int revision = ++_revision;
        if (_disposed || _source is null) { IsRendering = false; UpdateSaveState(); return; }
        var options = GetOptions();
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        using var source = (Bitmap)_source.Clone();
        IsRendering = true;
        UpdateSaveState();
        try
        {
            var result = await Task.Run(() => BackgroundRemover.Remove(source, options.Mode, options.Tolerance, options.Color, cancellation.Token));
            if (_disposed || revision != _revision) { result.Dispose(); return; }
            backgroundResultPreview.Image = null;
            _result?.Dispose();
            _result = result;
            backgroundResultPreview.Image = result;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (revision == _revision) StatusChanged?.Invoke($"Не удалось удалить фон: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            if (revision == _revision) { IsRendering = false; UpdateSaveState(); }
        }
    }

    private async Task SaveDialogAsync()
    {
        if (_busy || _result is null) return;
        var dialog = new SaveFileDialog { Title = "Сохранить без фона", Filter = "PNG с прозрачностью|*.png", DefaultExt = ".png",
            AddExtension = true, OverwritePrompt = true, FileName = Path.GetFileNameWithoutExtension(backgroundFileLabel.Text) + "_transparent.png" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await SaveAsync(dialog.FileName, true); }
        catch (Exception ex) { StatusChanged?.Invoke($"Не удалось сохранить PNG: {ex.Message}"); }
    }

    public async Task SaveAsync(string path, bool overwrite = false)
    {
        if (_busy || _disposed || _result is null || IsRendering) throw new InvalidOperationException("Дождитесь результата обработки.");
        string output = Path.GetFullPath(path);
        if (string.Equals(output, _inputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя перезаписать исходное изображение.");
        if (!overwrite && File.Exists(output)) throw new IOException("Файл уже существует.");
        using var image = (Bitmap)_result.Clone();
        SetBusy(true);
        StatusChanged?.Invoke("Сохранение PNG…");
        try
        {
            await Task.Run(() =>
            {
                string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) image.Save(stream, ImageFormat.Png);
                    File.Move(temporary, output, overwrite);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
            StatusChanged?.Invoke($"Сохранено: {output}");
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        backgroundControls.IsEnabled = backgroundBackButton.IsEnabled = !busy;
        UpdateSaveState();
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSaveState() => backgroundSaveButton.IsEnabled = !_busy && !IsRendering && _result is not null;

    private void UpdateLayoutForWidth()
    {
        bool compact = ActualWidth < 800;
        backgroundContent.Margin = new Thickness(compact ? 18 : 32, 16, compact ? 18 : 32, 32);
        backgroundPreviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        backgroundPreviewGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 16);
        backgroundPreviewGrid.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : 1, compact ? GridUnitType.Pixel : GridUnitType.Star);
        Grid.SetColumn(backgroundResultPanel, compact ? 0 : 2);
        Grid.SetRow(backgroundResultPanel, compact ? 1 : 0);
        backgroundResultPanel.Margin = compact ? new Thickness(0, 22, 0, 0) : new Thickness();
    }

    public void Dispose()
    {
        _disposed = true;
        ++_revision;
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        backgroundSourcePreview.Image = backgroundResultPreview.Image = null;
        _source?.Dispose(); _source = null;
        _result?.Dispose(); _result = null;
    }
}
