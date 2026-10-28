using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private const long LivePreviewPixelLimit = 256L * 256;
    private readonly DispatcherTimer _livePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _livePreviewRevision;
    private bool _livePreviewBusy;
    private Bitmap? _livePreviewBitmap;
    private Bitmap? _approvedLargeColorBasis, _approvedLargeSmoothingBasis, _approvedLargeSharpenBasis;
    private bool _largePreviewDecisionPending;
    private string? _activeLivePreviewTool;
    private string? ActiveLivePreviewTool => _activeLivePreviewTool;

    private void InitializeLiveToolPreviews()
    {
        trimTransparentButton.Content = Localization.Text("Обрезать прозрачные края");
        trimTransparentButton.ToolTip = Localization.Text("Обрезать изображение по крайним пикселям с альфой больше нуля.");
        livePreviewBadgeText.Text = Localization.Text("ПРЕДПРОСМОТР");
        foreach (var check in new[] { colorLivePreviewInput, smoothingLivePreviewInput, sharpenLivePreviewInput })
        {
            check.Content = Localization.Text("Живой предпросмотр");
            check.ToolTip = Localization.Text("Показывать результат настройки без сохранения в историю.");
        }
        _livePreviewTimer.Tick += async (_, _) =>
        {
            _livePreviewTimer.Stop();
            await RenderLiveToolPreviews();
        };
        foreach (var check in new[] { colorLivePreviewInput, smoothingLivePreviewInput, sharpenLivePreviewInput })
        {
            check.Checked += (_, _) => ChangeLivePreview(check);
            check.Unchecked += (_, _) => ChangeLivePreview(check);
        }
        foreach (var slider in new[] { localBrightnessInput, localContrastInput, localEdgeInput })
            slider.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var input in new[] { quantizationColorsInput, paletteStepInput, localColorPassesInput })
            input.ValueChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var combo in new[] { quantizationInput, paletteInput })
            combo.SelectionChanged += (_, _) => ScheduleLiveToolPreviews();
        foreach (var toggle in new ToggleButton[] { quantizationColorModeInput, paletteColorModeInput, colorWeightsInput,
                     ditheringInput, localDetailGuardInput })
        {
            toggle.Checked += (_, _) => ScheduleLiveToolPreviews();
            toggle.Unchecked += (_, _) => ScheduleLiveToolPreviews();
        }
        sourcePreview.ImageChanged += (_, _) => ScheduleLiveToolPreviews();
        Closed += (_, _) =>
        {
            _livePreviewRevision++;
            _livePreviewTimer.Stop();
            ClearDisplayedLivePreview();
        };
    }

    private Bitmap? LivePreviewBasis => ProcessingBasis == 1 && _downscaledResult is not null
        ? _downscaledResult : _sourceImage;

    private void SetActiveLivePreviewTool(string? key)
    {
        string? next = key is "color" or "smoothing" or "filters" or "contours" or "structure-probe" ? key : null;
        if (_activeLivePreviewTool == next) { ScheduleLiveToolPreviews(); return; }
        _activeLivePreviewTool = next;
        ClearDisplayedLivePreview();
        ScheduleLiveToolPreviews();
    }

    private void AutoEnableLivePreview(string key)
    {
        var basis = LivePreviewBasis;
        if (basis is null || (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;
        var check = key == "color" ? colorLivePreviewInput : smoothingLivePreviewInput;
        if (check.IsChecked != true) check.IsChecked = true;
        else ScheduleLiveToolPreviews();
    }

    private void ChangeLivePreview(CheckBox check)
    {
        var basis = LivePreviewBasis;
        if (check.IsChecked == true && basis is not null && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            var owner = Window.GetWindow(check) ?? this;
            _largePreviewDecisionPending = true;
            MessageBoxResult answer;
            try
            {
                answer = TesseraDialog.Show(owner,
                    Localization.Format("Изображение {0} × {1} превышает 65 536 пикселей (площадь 256 × 256). Живой предпросмотр может замедлять изменение настроек. Включить?", basis.Width, basis.Height),
                    Localization.Text("Большое изображение"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            }
            finally { _largePreviewDecisionPending = false; }
            if (answer != MessageBoxResult.Yes) { check.IsChecked = false; return; }
            if (ReferenceEquals(check, colorLivePreviewInput)) _approvedLargeColorBasis = basis;
            else if (ReferenceEquals(check, smoothingLivePreviewInput)) _approvedLargeSmoothingBasis = basis;
            else _approvedLargeSharpenBasis = basis;
        }
        if (check.IsChecked != true)
        {
            if (ReferenceEquals(check, colorLivePreviewInput)) _approvedLargeColorBasis = null;
            else if (ReferenceEquals(check, smoothingLivePreviewInput)) _approvedLargeSmoothingBasis = null;
            else _approvedLargeSharpenBasis = null;
            string key = ReferenceEquals(check, colorLivePreviewInput) ? "color" :
                ReferenceEquals(check, smoothingLivePreviewInput) ? "smoothing" : "filters";
            if (_activeLivePreviewTool == key) ClearDisplayedLivePreview();
        }
        ScheduleLiveToolPreviews();
    }

    private bool LiveToolPreviewVisible => _livePreviewBitmap is not null && ReferenceEquals(resultPreview.Image, _livePreviewBitmap);
    private bool PalettePreviewVisible => _activeLivePreviewTool == "color" && LiveToolPreviewVisible;

    private void ClearDisplayedLivePreview()
    {
        ClearContourPreview();
        ClearStructurePreview();
        if (ReferenceEquals(resultPreview.Image, _livePreviewBitmap) && _livePreviewBitmap is not null)
            resultPreview.Image = _downscaledResult;
        livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Collapsed;
        _livePreviewBitmap?.Dispose();
        _livePreviewBitmap = null;
        UpdateNavigationState();
    }

    private void ScheduleLiveToolPreviews()
    {
        _livePreviewRevision++;
        _livePreviewTimer.Stop();
        if (_activeLivePreviewTool == "contours") { ScheduleContourPreview(); return; }
        if (_activeLivePreviewTool == "structure-probe") { ScheduleStructurePreview(); return; }
        if (_largePreviewDecisionPending) { ClearDisplayedLivePreview(); return; }
        var basis = LivePreviewBasis;
        if (basis is not null && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (colorLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeColorBasis))
                colorLivePreviewInput.IsChecked = false;
            if (smoothingLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeSmoothingBasis))
                smoothingLivePreviewInput.IsChecked = false;
            if (sharpenLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeSharpenBasis))
                sharpenLivePreviewInput.IsChecked = false;
        }
        bool enabled = _activeLivePreviewTool switch
        {
            "color" => colorLivePreviewInput.IsChecked == true,
            "smoothing" => smoothingLivePreviewInput.IsChecked == true,
            "filters" => sharpenLivePreviewInput.IsChecked == true,
            _ => false
        };
        if (!IsLoaded || _processing || basis is null || !enabled)
        {
            ClearDisplayedLivePreview();
            return;
        }
        _livePreviewTimer.Start();
    }

    private async Task RenderLiveToolPreviews()
    {
        if (_livePreviewBusy || _processing || LivePreviewBasis is not { } basis) return;
        bool color = _activeLivePreviewTool == "color" && colorLivePreviewInput.IsChecked == true && colorToolGroup.IsVisible;
        bool smooth = _activeLivePreviewTool == "smoothing" && smoothingLivePreviewInput.IsChecked == true && smoothingToolGroup.IsVisible;
        bool sharpen = _activeLivePreviewTool == "filters" && sharpenLivePreviewInput.IsChecked == true && filtersToolGroup.IsVisible;
        if (!color && !smooth && !sharpen) { ClearDisplayedLivePreview(); return; }

        int revision = _livePreviewRevision;
        var options = color || smooth ? BuildOptionsFromUi() : null;
        var connected = color ? BuildConnectedPaletteOptions() : null;
        int strength = sharpenStrengthInput.Value, threshold = sharpenThresholdInput.Value;
        using var source = (Bitmap)basis.Clone();
        _livePreviewBusy = true;
        Bitmap? result = null;
        try
        {
            result = await Task.Run(() => sharpen ? ImageFilters.Sharpen(source, strength, threshold) : color
                ? PaletteProcessor.Apply(source, options!, connected)
                : IndependentImageProcessor.ApplyNeighborColors(source, options!));
            if (revision != _livePreviewRevision || _processing) return;
            ClearDisplayedLivePreview();
            _livePreviewBitmap = result;
            resultPreview.Image = result;
            livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Visible;
            result = null;
            UpdateNavigationState();
        }
        catch (Exception ex)
        {
            if (revision == _livePreviewRevision) statusLabel.Text = Localization.Format("Предпросмотр недоступен: {0}", ex.Message);
        }
        finally
        {
            result?.Dispose();
            _livePreviewBusy = false;
            if (revision != _livePreviewRevision) ScheduleLiveToolPreviews();
        }
    }
}
