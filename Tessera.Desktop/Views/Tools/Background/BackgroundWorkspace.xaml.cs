using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace Tessera.Desktop;

public partial class BackgroundWorkspace : UserControl, IDisposable
{
    private BackgroundSettings? _renderedRecipe;
    public bool HasManualErasure => _erasedCount > 0;
    public ProcessingStep? AutomaticRecipeStep => _renderedRecipe is null ? null : new ProcessingStep { OperationId = ProcessingOperations.RemoveBackground, Background = _renderedRecipe };
    private Bitmap? _source;
    private Bitmap? _result;
    private string? _inputPath;
    private readonly HashSet<string> _sourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;
    private bool _disposed;
    private int _revision;
    private CancellationTokenSource? _previewCancellation;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    public bool IsBusy => _busy;
    public bool IsRendering { get; private set; }
    public bool HasResult => _result is not null;
    public Action<string>? ValidateExportPath { get; set; }
    public IReadOnlyCollection<string> SourcePaths => _sourcePaths;
    public event EventHandler? BackRequested;
    public event Action<EditorMaterial>? MaterialRequested;

    public event EventHandler? BusyChanged;
    public Func<string, bool, OperationProgressSession>? ProgressFactory { get; set; }
    private OperationProgressSession BeginProgress(string label, bool preview = false)
        => ProgressFactory?.Invoke(label, preview) ?? new OperationProgressSession(Dispatcher);

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
        InitializeEditing();
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshPreviewAsync(); };
        SizeChanged += (_, _) => UpdateLayoutForWidth();
    }

    public void SetEditorMaterials(IEnumerable<EditorMaterial> materials)
        => backgroundEditorChoices.SetMaterials(materials);

    public async Task OpenAsync()
    {
        if (_busy || _disposed) return;
        var dialog = new OpenFileDialog { Title = Localization.Text("BackgroundRemovalSource"), Filter = Localization.Text("ImagesFileFilterLabel2") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await LoadFileAsync(dialog.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (_busy || _disposed) return;
        SetBusy(true);
        using var operation = BeginProgress(Localization.Text("OpeningImageAction"));
        try
        {
            using var image = await Task.Run(() => IconWorkspace.ReadImage(path));
            if (_disposed) return;
            SetImage(image, Path.GetFileName(path), path);
            StatusChanged?.Invoke(Localization.Text("BackgroundSourceOpenedHint"));
            operation.Complete();
        }
        catch (Exception ex) { StatusChanged?.Invoke(Localization.Format("CouldNotOpenImageFormat", ex.Message)); }
        finally { SetBusy(false); }
    }

    public void SetImage(Bitmap source, string label, string? inputPath = null)
    {
        if (_disposed) return;
        var copy = (Bitmap)source.Clone();
        FinishEraseStroke();
        ResetManualEdits();
        backgroundPickColorToggle.IsChecked = false;
        _previewCancellation?.Cancel();
        ++_revision;
        backgroundSourcePreview.Image = backgroundResultPreview.Image = null;
        _source?.Dispose();
        _result?.Dispose();
        _processedResult?.Dispose();
        _source = copy;
        _result = null;
        _processedResult = null;
        _inputPath = inputPath is null ? null : Path.GetFullPath(inputPath);
        if (_inputPath is not null) _sourcePaths.Add(_inputPath);
        backgroundFileLabel.Text = label;
        backgroundFileLabel.ToolTip = label;
        backgroundSourcePreview.Image = _source;
        SchedulePreview();
    }

    public (EBackgroundRemovalMode Mode, int Tolerance, Color? Color) GetOptions() =>
        (backgroundModeInput.SelectedIndex == 1 ? EBackgroundRemovalMode.EdgeConnected : EBackgroundRemovalMode.GlobalColor,
        (int)backgroundToleranceInput.Value,
        backgroundColorInput.SelectedIndex switch { 1 => Color.White, 2 => Color.Black, 3 => _customBackgroundColor, _ => (Color?)null });

    private void SchedulePreview()
    {
        if (_disposed) return;
        FinishEraseStroke();
        ++_revision;
        _previewCancellation?.Cancel();
        backgroundResultPreview.Image = null;
        _result?.Dispose();
        _result = null;
        _processedResult?.Dispose();
        _processedResult = null;
        IsRendering = _source is not null;
        UpdateBackgroundColorDisplay();
        UpdateSaveState();
        _previewTimer.Stop();
        if (_source is not null) _previewTimer.Start();
    }

    public async Task RefreshPreviewAsync()
    {
        FinishEraseStroke();
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        int revision = ++_revision;
        if (_disposed || _source is null) { IsRendering = false; UpdateSaveState(); return; }
        var options = GetOptions();
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        using var operation = BeginProgress(Localization.Text("RemovingBackground"), true);
        using var source = (Bitmap)_source.Clone();
        IsRendering = true;
        UpdateSaveState();
        try
        {
            var result = await Task.Run(() => BackgroundRemover.Remove(source, options.Mode, options.Tolerance, options.Color, cancellation.Token, operation.Work));
            if (_disposed || revision != _revision) { result.Dispose(); return; }
            _processedResult?.Dispose();
            _processedResult = result;
            _renderedRecipe = new BackgroundSettings { Mode = options.Mode, Tolerance = options.Tolerance, ColorArgb = options.Color?.ToArgb() };
            RecomposeManualResult();
            operation.Complete();
            if (IsVisible) StatusChanged?.Invoke(Localization.Text("BackgroundRemovalIsReady"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (revision == _revision) StatusChanged?.Invoke(Localization.Format("CouldNotRemoveBackgroundFormat", ex.Message));
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
        var dialog = new SaveFileDialog
        {
            Title = Localization.Text("SaveWithoutBackground"),
            Filter = Localization.Text("PNGWithTransparencyFileFilterLabel"),
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileNameWithoutExtension(backgroundFileLabel.Text) + "_transparent.png"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await SaveAsync(dialog.FileName, true); }
        catch (Exception ex) { StatusChanged?.Invoke(Localization.Format("CouldNotSavePNGFormat", ex.Message)); }
    }

    public async Task SaveAsync(string path, bool overwrite = false)
    {
        if (_busy || _disposed || _result is null || IsRendering) throw new InvalidOperationException(Localization.Text("WaitForAProcessingResult"));
        string output = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Localization.Text("SaveTheBackgroundRemovalResultAs"));
        ValidateExportPath?.Invoke(output);
        if (_sourcePaths.Contains(output))
            throw new InvalidOperationException(Localization.Text("CannotOverwriteTheSourceImage"));
        if (!overwrite && File.Exists(output)) throw new IOException(Localization.Text("TheFileAlreadyExists"));
        using var image = (Bitmap)_result.Clone();
        SetBusy(true);
        using var operation = BeginProgress(Localization.Text("SavingPNGAction"));
        StatusChanged?.Invoke(Localization.Text("SavingPNGAction"));
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
            operation.Complete();
            StatusChanged?.Invoke(Localization.Format("SavedFormat", output));
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

    private void UpdateSaveState()
    {
        backgroundSaveButton.IsEnabled = !_busy && !IsRendering && _result is not null;
        if (_workspaceApplyButton is not null) _workspaceApplyButton.IsEnabled = backgroundSaveButton.IsEnabled;
        UpdateEditingState();
    }

    private void UpdateLayoutForWidth()
    {
        bool compact = ActualWidth < 900;
        bool stackPreviews = ActualWidth < 700;
        bool compactHeader = ActualWidth < 700;

        DockPanel.SetDock(backgroundHeaderActions, compactHeader ? Dock.Bottom : Dock.Right);
        backgroundHeaderActions.Margin = compactHeader ? new Thickness(0, 8, 0, 0) : new Thickness();

        backgroundContent.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 260, compact ? GridUnitType.Star : GridUnitType.Pixel);
        backgroundContent.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 1, compact ? GridUnitType.Pixel : GridUnitType.Star);
        backgroundContent.RowDefinitions[0].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        backgroundContent.RowDefinitions[0].MaxHeight = compact ? 220 : double.PositiveInfinity;
        backgroundSettingsPanel.MaxHeight = compact ? 220 : double.PositiveInfinity;
        backgroundContent.RowDefinitions[1].Height = new GridLength(compact ? 1 : 0, compact ? GridUnitType.Star : GridUnitType.Pixel);

        Grid.SetColumn(backgroundSettingsPanel, 0);
        Grid.SetRow(backgroundSettingsPanel, 0);
        Grid.SetColumn(backgroundPreviewScroll, compact ? 0 : 1);
        Grid.SetRow(backgroundPreviewScroll, compact ? 1 : 0);
        backgroundSettingsPanel.BorderThickness = compact ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);

        backgroundPreviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        backgroundPreviewGrid.ColumnDefinitions[1].Width = new GridLength(stackPreviews ? 0 : 12);
        backgroundPreviewGrid.ColumnDefinitions[2].Width = new GridLength(stackPreviews ? 0 : 1, stackPreviews ? GridUnitType.Pixel : GridUnitType.Star);
        Grid.SetColumn(backgroundResultPanel, stackPreviews ? 0 : 2);
        Grid.SetRow(backgroundResultPanel, stackPreviews ? 1 : 0);
        backgroundResultPanel.Margin = stackPreviews ? new Thickness(0, 12, 0, 0) : new Thickness();
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseImage();
    }

    internal void ReleaseImage()
    {
        ++_revision;
        _previewTimer.Stop();
        _previewCancellation?.Cancel();
        IsRendering = false;
        FinishEraseStroke();
        ResetManualEdits();
        backgroundSourcePreview.Image = backgroundResultPreview.Image = null;
        _source?.Dispose(); _source = null;
        _result?.Dispose(); _result = null;
        _processedResult?.Dispose(); _processedResult = null;
        _inputPath = null;
        _workspaceApply = null;
        backgroundOpenButton.Visibility = Visibility.Visible;
        _renderedRecipe = null;
        backgroundEditorChoices.SetMaterials([]);
        if (_workspaceApplyButton is not null) _workspaceApplyButton.Visibility = Visibility.Collapsed;
        UpdateSaveState();
    }
}
