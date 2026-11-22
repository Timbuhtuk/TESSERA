using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    internal const long LivePreviewPixelLimit = 256L * 256;

    private readonly DispatcherTimer _livePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private int _livePreviewRevision;

    private bool _livePreviewBusy;

    private Bitmap? _livePreviewBitmap;

    private Bitmap? _approvedLargeColorBasis, _approvedLargeSmoothingBasis, _approvedLargeSharpenBasis, _approvedLargeAsciiBasis;

    private bool _largePreviewDecisionPending;

    private string? _activeLivePreviewTool;

    internal void ClearLargeImageApprovals()
        => _approvedLargeColorBasis = _approvedLargeSmoothingBasis = _approvedLargeSharpenBasis = _approvedLargeAsciiBasis = null;

    internal string? ActiveLivePreviewTool => _activeLivePreviewTool;

    internal void InitializeLiveToolPreviews()
    {
        _editor.trimTransparentButton.Content = Localization.Text("TrimTransparentEdges");
        _editor.trimTransparentButton.ToolTip = Localization.Text("TrimOutermostPixelsAlphaAboveZero");
        _editor.livePreviewBadgeText.Text = Localization.Text("Preview");
        foreach (var check in new[] { _editor.colorLivePreviewInput, _editor.smoothingLivePreviewInput, _editor.sharpenLivePreviewInput, _editor.asciiLivePreviewInput })
        {
            check.Content = Localization.Text("LivePreview");
            check.ToolTip = Localization.Text("ShowChangesWithoutSavingResultHistory");
        }
        _livePreviewTimer.Tick += async (_, _) =>
        {
            _livePreviewTimer.Stop();
            await RenderLiveToolPreviews();
        };
        foreach (var check in new[] { _editor.colorLivePreviewInput, _editor.smoothingLivePreviewInput, _editor.sharpenLivePreviewInput, _editor.asciiLivePreviewInput })
        {
            check.Checked += (_, _) => ChangeLivePreview(check);
            check.Unchecked += (_, _) => ChangeLivePreview(check);
        }
        foreach (var slider in new[] { _editor.localBrightnessInput, _editor.localContrastInput, _editor.localEdgeInput })
            slider.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var input in new[] { _editor.quantizationColorsInput, _editor.paletteStepInput, _editor.localColorPassesInput })
            input.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        _editor.asciiColumnsInput.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        _editor.asciiCharactersInput.TextChanged += (_, _) => ScheduleLiveToolPreviews();
        _editor.asciiRampPresetInput.SelectionChanged += (_, _) => ScheduleLiveToolPreviews();
        _editor.asciiColorModeInput.SelectionChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var combo in new[] { _editor.quantizationInput, _editor.paletteInput })
            combo.SelectionChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var toggle in new ToggleButton[] { _editor.quantizationColorModeInput, _editor.paletteColorModeInput, _editor.colorWeightsInput,
                     _editor.ditheringInput, _editor.localDetailGuardInput, _editor.asciiKeepSourceSizeInput, _editor.asciiInvertSourceInput,
                     _editor.asciiInvertRenderInput, _editor.asciiDitherInput })
        {
            toggle.Checked += (_, _) => ScheduleLiveToolPreviews();
            toggle.Unchecked += (_, _) => ScheduleLiveToolPreviews();
        }
        _editor.sourcePreview.ImageChanged += (_, _) => ScheduleLiveToolPreviews();
        Host.Closed += (_, _) =>
        {
            _livePreviewRevision++;
            _livePreviewTimer.Stop();
            ClearDisplayedLivePreview();
        };
    }

    internal Bitmap? LivePreviewBasis => _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null
        ? _editor._downscaledResult : _editor._sourceImage;

    internal void SetActiveLivePreviewTool(string? key)
    {
        string? next = key is "color" or "smoothing" or "sharpen" or "ascii" or "contours" ? key : null;
        if (_activeLivePreviewTool == next) { ScheduleLiveToolPreviews(); return; }
        _activeLivePreviewTool = next;
        ClearDisplayedLivePreview();
        ScheduleLiveToolPreviews();
    }

    internal void AutoEnableLivePreview(string key)
    {
        var basis = LivePreviewBasis;
        if (basis is null || (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;
        var check = key == "color" ? _editor.colorLivePreviewInput : _editor.smoothingLivePreviewInput;
        if (check.IsChecked != true) check.IsChecked = true;
        else ScheduleLiveToolPreviews();
    }

    private void ChangeLivePreview(CheckBox check)
    {
        var basis = LivePreviewBasis;
        if (check.IsChecked == true && basis is not null && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            var owner = Window.GetWindow(check) ?? Host;
            _largePreviewDecisionPending = true;
            MessageBoxResult answer;
            try
            {
                answer = TesseraDialogs.Show(owner,
                    Localization.Format("ImageExceeds65536Pixels256Format", basis.Width, basis.Height),
                    Localization.Text("LargeImage"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            }
            finally { _largePreviewDecisionPending = false; }
            if (answer != MessageBoxResult.Yes) { check.IsChecked = false; return; }
            if (ReferenceEquals(check, _editor.colorLivePreviewInput)) _approvedLargeColorBasis = basis;
            else if (ReferenceEquals(check, _editor.smoothingLivePreviewInput)) _approvedLargeSmoothingBasis = basis;
            else if (ReferenceEquals(check, _editor.sharpenLivePreviewInput)) _approvedLargeSharpenBasis = basis;
            else _approvedLargeAsciiBasis = basis;
        }
        if (check.IsChecked != true)
        {
            if (ReferenceEquals(check, _editor.colorLivePreviewInput)) _approvedLargeColorBasis = null;
            else if (ReferenceEquals(check, _editor.smoothingLivePreviewInput)) _approvedLargeSmoothingBasis = null;
            else if (ReferenceEquals(check, _editor.sharpenLivePreviewInput)) _approvedLargeSharpenBasis = null;
            else _approvedLargeAsciiBasis = null;
            string key = ReferenceEquals(check, _editor.colorLivePreviewInput) ? "color" :
                ReferenceEquals(check, _editor.smoothingLivePreviewInput) ? "smoothing" :
                ReferenceEquals(check, _editor.sharpenLivePreviewInput) ? "sharpen" : "ascii";
            if (_activeLivePreviewTool == key) ClearDisplayedLivePreview();
        }
        ScheduleLiveToolPreviews();
    }

    internal void ClearDisplayedLivePreview()
    {
        ClearContourPreview();
        if (ReferenceEquals(_editor.resultPreview.Image, _livePreviewBitmap) && _livePreviewBitmap is not null)
            _editor.resultPreview.Image = _editor._downscaledResult;
        _editor.livePreviewFrame.Visibility = _editor.livePreviewBadge.Visibility = Visibility.Collapsed;
        _livePreviewBitmap?.Dispose();
        _livePreviewBitmap = null;
    }

    internal void ScheduleLiveToolPreviews()
    {
        _livePreviewRevision++;
        _livePreviewTimer.Stop();
        if (_activeLivePreviewTool == "contours") { ScheduleContourPreview(); return; }
        if (_largePreviewDecisionPending) { ClearDisplayedLivePreview(); return; }
        var basis = LivePreviewBasis;
        if (basis is not null && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (_editor.colorLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeColorBasis))
                _editor.colorLivePreviewInput.IsChecked = false;
            if (_editor.smoothingLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeSmoothingBasis))
                _editor.smoothingLivePreviewInput.IsChecked = false;
            if (_editor.sharpenLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeSharpenBasis))
                _editor.sharpenLivePreviewInput.IsChecked = false;
            if (_editor.asciiLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeAsciiBasis))
                _editor.asciiLivePreviewInput.IsChecked = false;
        }
        bool enabled = _activeLivePreviewTool switch
        {
            "color" => _editor.colorLivePreviewInput.IsChecked == true,
            "smoothing" => _editor.smoothingLivePreviewInput.IsChecked == true,
            "sharpen" => _editor.sharpenLivePreviewInput.IsChecked == true,
            "ascii" => _editor.asciiLivePreviewInput.IsChecked == true,
            _ => false
        };
        if (!_editor.IsLoaded || _editor._processing || basis is null || !enabled)
        {
            ClearDisplayedLivePreview();
            return;
        }
        _livePreviewTimer.Start();
    }

    private async Task RenderLiveToolPreviews()
    {
        if (_livePreviewBusy || _editor._processing || LivePreviewBasis is not { } basis) return;
        bool color = _activeLivePreviewTool == "color" && _editor.colorLivePreviewInput.IsChecked == true && _editor.colorToolGroup.IsVisible;
        bool smooth = _activeLivePreviewTool == "smoothing" && _editor.smoothingLivePreviewInput.IsChecked == true && _editor.smoothingToolGroup.IsVisible;
        bool sharpen = _activeLivePreviewTool == "sharpen" && _editor.sharpenLivePreviewInput.IsChecked == true && _editor.sharpenToolGroup.IsVisible;
        bool ascii = _activeLivePreviewTool == "ascii" && _editor.asciiLivePreviewInput.IsChecked == true && _editor.asciiToolGroup.IsVisible;
        if (!color && !smooth && !sharpen && !ascii) { ClearDisplayedLivePreview(); return; }

        int revision = _livePreviewRevision;
        var options = color || smooth ? _editor.Tools.BuildOptionsFromUi() : null;
        var connected = color ? _editor.Tools.BuildConnectedPaletteOptions() : null;
        int strength = _editor.sharpenStrengthInput.Value, threshold = _editor.sharpenThresholdInput.Value;
        string characters = _editor.asciiCharactersInput.Text;
        bool keepSourceSize = _editor.asciiKeepSourceSizeInput.IsChecked == true;
        bool invertSource = _editor.asciiInvertSourceInput.IsChecked == true, invertRender = _editor.asciiInvertRenderInput.IsChecked == true;
        bool dither = _editor.asciiDitherInput.IsChecked == true;
        int columns = _editor.asciiColumnsInput.Value;
        var (foregroundArgb, backgroundArgb) = _editor.Tools.AsciiColors;
        AsciiColorMode colorMode = (AsciiColorMode)_editor.asciiColorModeInput.SelectedIndex;
        using var source = (Bitmap)basis.Clone();
        _livePreviewBusy = true;
        using var operation = Host.BeginOperationProgress(Host, preview: true);
        Bitmap? result = null;
        try
        {
            result = await Task.Run(() => ascii
                ? ImageFilters.RenderAscii(source, keepSourceSize, characters, invertSource, invertRender, colorMode,
                    foregroundArgb, backgroundArgb, columns, dither, operation.Work, ImageFilters.DefaultAsciiFontSize)
                : sharpen ? ImageFilters.Sharpen(source, strength, threshold, progress: operation.Work) : color
                ? PaletteProcessor.Apply(source, options!, connected, operation.Work)
                : IndependentImageProcessor.ApplyNeighborColors(source, options!, operation.Work));
            if (revision != _livePreviewRevision || _editor._processing) return;
            ClearDisplayedLivePreview();
            _livePreviewBitmap = result;
            _editor.resultPreview.Image = result;
            _editor.livePreviewFrame.Visibility = _editor.livePreviewBadge.Visibility = Visibility.Visible;
            result = null;
            operation.Complete();
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            if (revision == _livePreviewRevision) Host.Notify(Localization.Format("PreviewUnavailableFormat", ex.Message), NotificationKind.Warning);
        }
        finally
        {
            result?.Dispose();
            _livePreviewBusy = false;
            if (revision != _livePreviewRevision) ScheduleLiveToolPreviews();
        }
    }
}
