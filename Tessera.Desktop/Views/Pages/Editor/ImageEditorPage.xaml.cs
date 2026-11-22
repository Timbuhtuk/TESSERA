using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class ImageEditorPage : Page
{
    internal MainWindow Host { get; }
    internal EditorHistory History { get; }
    internal EditorPreview Preview { get; }
    internal EditorTools Tools { get; }
    internal EditorToolDock Dock { get; }
    internal EditorInfoTool Info { get; }

    public int PreviewZoom { get => Preview.PreviewZoom; set => Preview.PreviewZoom = value; }
    public int PreviewBackground { get => Preview.PreviewBackground; set => Preview.PreviewBackground = value; }
    public bool AlternativePreview { get => Preview.AlternativePreview; set => Preview.AlternativePreview = value; }

    internal ImageEditorPage(MainWindow host, string libraryDirectory)
    {
        Host = host;
        saveDialog.Title = Localization.Text("SaveResult");
        History = new EditorHistory(this, libraryDirectory);
        Preview = new EditorPreview(this);
        Tools = new EditorTools(this);
        Dock = new EditorToolDock(this);
        Info = new EditorInfoTool(this);
        InitializeComponent();
    }

    internal void InitializeTools(string libraryDirectory)
    {
        Tools.InitializeCustomPalettes(libraryDirectory);
        History.Initialize();
        Tools.InitializeSizeFactor();
        Tools.InitializeChannelTool();
        Tools.InitializeFinalistTool();
        Tools.InitializeConnectedPalette();
        Preview.InitializeGridLens();
        Preview.InitializeTileHeatmapLens();
        InitializeAtlas();
        Dock.InitializeEditorDock();
    }

    internal void InitializeInputs()
    {
        Preview.InitializePreviewMode();
        Host.PreviewKeyDown += Preview.LensKeyDown;
        Tools.InitializeInputs();
    }

    internal void InitializeLiveTools()
    {
        Preview.InitializeViewport();
        History.InitializeDrag();
        Tools.InitializeGridReduction();
        Preview.InitializeLiveToolPreviews();
        Preview.InitializeContourPreview();
        Tools.InitializeContourTool();
    }

    internal void InitializePresentation()
    {
        Info.InitializeInfo();
        Tools.InitializePalettePreviews();
        Tools.InitializeFilters();
        Preview.UpdatePreviewSettings();
        Tools.UpdateDependentControls();
    }

    private void RemoveLibraryItem(object sender, RoutedEventArgs e) => History.RemoveLibraryItem(sender, e);
    private void ToggleSourceLibrary(object sender, RoutedEventArgs e) => History.ToggleSourceLibrary(sender, e);
    private void TrayFolderClicked(object sender, RoutedEventArgs e) => History.TrayFolderClicked(sender, e);

    internal void Release()
    {
        Dock.CloseEditorTools();
        ReleaseImageEditor();
        Atlas.Release();
    }

    internal string? _selectedFilePath;

    internal Bitmap? _sourceImage;

    internal Bitmap? _downscaledResult;

    internal bool _resultPreservesTransparency;

    internal bool _resultIsAlignment;

    internal bool _processing;

    internal bool _resultStale;

    internal readonly SaveFileDialog saveDialog = new() { DefaultExt = ".png", AddExtension = true, OverwritePrompt = true };

    internal void SetProcessingState(bool processing)
    {
        bool wasProcessing = _processing;
        if (processing && !wasProcessing) Host._mainProgress = Host.BeginOperationProgress(Host);
        if (!processing && wasProcessing) { Host._mainProgress?.Dispose(); Host._mainProgress = null; }
        _processing = processing;
        Tools.UpdateCompactButton();
        settingsPanel.IsEnabled = !processing;
        Dock.SetControlsEnabled(!processing, History.IsMacroRunning);
        sourceStrip.IsEnabled = generationStrip.IsEnabled = !processing;
        Host.LibraryPage.homeContent.IsEnabled = !processing;
        processButton.IsEnabled = !processing && _sourceImage is not null;
        scaleOnlyButton.IsEnabled = colorOnlyButton.IsEnabled = neighborColorsButton.IsEnabled =
            !processing && _sourceImage is not null;
        trimTransparentButton.IsEnabled = !processing && _sourceImage is not null;
        contourApplyButton.IsEnabled = contourPreviewButton.IsEnabled = !processing && Preview.LivePreviewBasis is not null;
        if (processing) Preview.ClearContourPreview();
        if (!processing) Preview.ScheduleLiveToolPreviews();
        if (_downscaledResult is null && ProcessingBasis == 1) ProcessingBasis = 0;
        alignButton.IsEnabled = !processing && _sourceImage is not null;
        Tools.SetFinalistEnabled(!processing && _sourceImage is not null);
        Cursor = processing ? Cursors.Wait : null;
        Host.UpdateBusyIndicator();
        if (!processing) Info.RefreshInfo();
        Tools.RefreshFilterState();
        Host.UpdateNavigationState();
        if (!processing) Host.SynchronizeWorkspace();
    }

    internal async void SaveImage(object? sender, EventArgs e)
    {
        if (_downscaledResult is null) return;
        string suffix = _resultIsAlignment ? "aligned" : _activeSource?.SelectedGeneration?.IsGridReduction == true ? "pixels" : _activeSource?.SelectedGeneration?.Operation == Localization.Text("UpscaleLabel") ? "upscaled" : "pixelized";
        saveDialog.FileName = $"{Path.GetFileNameWithoutExtension(_activeSource?.Label ?? _selectedFilePath) ?? "result"}_{suffix}.png";
        saveDialog.Filter = _resultPreservesTransparency ? Localization.Text("PNGWithTransparencyFileFilter")
            : "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp";
        saveDialog.FilterIndex = 1;
        if (saveDialog.ShowDialog(Host) != true) return;
        try { await Host.SaveResultWithProgressAsync(saveDialog.FileName); }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            TesseraDialog.Show(Host, ex.Message, Localization.Text("SaveError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveResult(string path)
    {
        if (_downscaledResult is null) throw new InvalidOperationException(Localization.Text("ProcessTheImageFirst"));
        string outputPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || (_resultPreservesTransparency && extension != ".png"))
        {
            throw new InvalidOperationException(_resultPreservesTransparency
                ? Localization.Text("SaveThisResultPNGPreserveTransparency")
                : Localization.Text("ChoosePNGJPEGOrBMPFormat"));
        }
        Host.EnsureExportPathAllowed(outputPath);
        string temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var format = extension switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };
            _downscaledResult.Save(temporaryPath, format);
            File.Move(temporaryPath, outputPath, overwrite: true);
            Host.statusLabel.Text = Localization.Format("SavedFormat", outputPath) + (_resultStale ? Localization.Text("ResultFromPreviousSettings") : "");
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
