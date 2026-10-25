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
    private Bitmap? _colorPreviewBitmap, _smoothingPreviewBitmap;
    private Bitmap? _approvedLargeColorBasis, _approvedLargeSmoothingBasis;

    private void InitializeLiveToolPreviews()
    {
        trimTransparentButton.Content = Localization.Text("Обрезать прозрачные края");
        trimTransparentButton.ToolTip = Localization.Text("Обрезать изображение по крайним пикселям с альфой больше нуля.");
        foreach (var check in new[] { colorLivePreviewInput, smoothingLivePreviewInput })
        {
            check.Content = Localization.Text("Живой предпросмотр");
            check.ToolTip = Localization.Text("Показывать результат настройки без сохранения в историю.");
        }
        colorLivePreview.EmptyText = Localization.Text("Предпросмотр цвета");
        smoothingLivePreview.EmptyText = Localization.Text("Предпросмотр очистки цветов");
        _livePreviewTimer.Tick += async (_, _) =>
        {
            _livePreviewTimer.Stop();
            await RenderLiveToolPreviews();
        };
        foreach (var check in new[] { colorLivePreviewInput, smoothingLivePreviewInput })
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
        resultPreview.ImageChanged += (_, _) => ScheduleLiveToolPreviews();
        Closed += (_, _) =>
        {
            _livePreviewTimer.Stop();
            colorLivePreview.Image = smoothingLivePreview.Image = null;
            _colorPreviewBitmap?.Dispose(); _smoothingPreviewBitmap?.Dispose();
        };
    }

    private Bitmap? LivePreviewBasis => ProcessingBasis == 1 && _downscaledResult is not null
        ? _downscaledResult : _sourceImage;

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
            var answer = TesseraDialog.Show(owner,
                Localization.Format("Изображение {0} × {1} превышает 65 536 пикселей (площадь 256 × 256). Живой предпросмотр может замедлять изменение настроек. Включить?", basis.Width, basis.Height),
                Localization.Text("Большое изображение"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) { check.IsChecked = false; return; }
            if (ReferenceEquals(check, colorLivePreviewInput)) _approvedLargeColorBasis = basis;
            else _approvedLargeSmoothingBasis = basis;
        }
        var preview = ReferenceEquals(check, colorLivePreviewInput) ? colorLivePreview : smoothingLivePreview;
        preview.Visibility = check.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (check.IsChecked != true)
        {
            if (ReferenceEquals(check, colorLivePreviewInput)) _approvedLargeColorBasis = null;
            else _approvedLargeSmoothingBasis = null;
            ClearLivePreview(ReferenceEquals(check, colorLivePreviewInput));
        }
        ScheduleLiveToolPreviews();
    }

    private void ClearLivePreview(bool color)
    {
        var preview = color ? colorLivePreview : smoothingLivePreview;
        preview.Image = null;
        if (color) { _colorPreviewBitmap?.Dispose(); _colorPreviewBitmap = null; }
        else { _smoothingPreviewBitmap?.Dispose(); _smoothingPreviewBitmap = null; }
    }

    private void ScheduleLiveToolPreviews()
    {
        _livePreviewRevision++;
        _livePreviewTimer.Stop();
        var basis = LivePreviewBasis;
        if (basis is not null && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (colorLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeColorBasis))
                colorLivePreviewInput.IsChecked = false;
            if (smoothingLivePreviewInput.IsChecked == true && !ReferenceEquals(basis, _approvedLargeSmoothingBasis))
                smoothingLivePreviewInput.IsChecked = false;
        }
        if (!IsLoaded || _processing || LivePreviewBasis is null ||
            colorLivePreviewInput.IsChecked != true && smoothingLivePreviewInput.IsChecked != true) return;
        _livePreviewTimer.Start();
    }

    private async Task RenderLiveToolPreviews()
    {
        if (_livePreviewBusy || _processing || LivePreviewBasis is not { } basis) return;
        bool color = colorLivePreviewInput.IsChecked == true && colorToolGroup.IsVisible;
        bool smooth = smoothingLivePreviewInput.IsChecked == true && smoothingToolGroup.IsVisible;
        if (!color && !smooth) return;

        int revision = _livePreviewRevision;
        var options = BuildOptionsFromUi();
        using var source = (Bitmap)basis.Clone();
        _livePreviewBusy = true;
        Bitmap? colorResult = null, smoothingResult = null;
        try
        {
            (colorResult, smoothingResult) = await Task.Run(() =>
            {
                Bitmap? first = null, second = null;
                try
                {
                    if (color) first = IndependentImageProcessor.ApplyColors(source, options);
                    if (smooth) second = IndependentImageProcessor.ApplyNeighborColors(source, options);
                    return (first, second);
                }
                catch { first?.Dispose(); second?.Dispose(); throw; }
            });
            if (revision != _livePreviewRevision || _processing) return;
            if (colorResult is not null)
            {
                ClearLivePreview(true);
                _colorPreviewBitmap = colorResult;
                colorLivePreview.Image = colorResult;
                colorResult = null;
            }
            if (smoothingResult is not null)
            {
                ClearLivePreview(false);
                _smoothingPreviewBitmap = smoothingResult;
                smoothingLivePreview.Image = smoothingResult;
                smoothingResult = null;
            }
        }
        catch (Exception ex)
        {
            if (revision == _livePreviewRevision) statusLabel.Text = Localization.Format("Предпросмотр недоступен: {0}", ex.Message);
        }
        finally
        {
            colorResult?.Dispose(); smoothingResult?.Dispose();
            _livePreviewBusy = false;
            if (revision != _livePreviewRevision) ScheduleLiveToolPreviews();
        }
    }
}
