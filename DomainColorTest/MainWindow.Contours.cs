using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly DispatcherTimer _contourPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _contourPreviewRevision;
    private bool _contourPreviewBusy;
    private bool _contourToolOpen => ActiveLivePreviewTool == "contours";
    private Bitmap? _contourPreviewBitmap, _contourPreviewBasis;

    private int ContourWindowSize => 3 + 2 * Math.Clamp((int)Math.Round((contourWindowInput.Value - 3) / 2), 0, 6);
    private bool ContourPreviewVisible => _contourPreviewBitmap is not null &&
        ReferenceEquals(resultPreview.Image, _contourPreviewBitmap);

    private void InitializeContourPreview()
    {
        _contourPreviewTimer.Tick += async (_, _) =>
        {
            _contourPreviewTimer.Stop();
            await RenderContourPreview(false);
        };
        contourWindowInput.ValueChanged += (_, _) =>
        {
            int window = ContourWindowSize;
            contourWindowLabel.Text = Localization.Format("Область анализа · {0} × {0}", window);
            ScheduleContourPreview();
        };
        foreach (var input in new[] { contourSimilarityInput, contourPixelGradientInput,
            contourNeighborGradientInput, contourNeighborCountInput, contourAgreementInput, contourOppositionInput })
            input.ValueChanged += (_, _) => ScheduleContourPreview();
        UpdateContourWindowLabel();
        contourDetailGuardInput.Checked += (_, _) => ScheduleContourPreview();
        contourDetailGuardInput.Unchecked += (_, _) => ScheduleContourPreview();
        contourPreviewButton.Click += async (_, _) => await RenderContourPreview(true);
        contourApplyButton.Click += ApplyContourHeatmap;
        Closed += (_, _) => ClearContourPreview();
    }

    private void UpdateContourWindowLabel()
        => contourWindowLabel.Text = Localization.Format("Область анализа · {0} × {0}", ContourWindowSize);

    private ContourCleanOptions BuildContourOptions() => new()
    {
        WindowSize = ContourWindowSize,
        Strictness = ContourCleanStrictness.Strong,
        ProtectDetails = contourDetailGuardInput.IsChecked == true,
        SimilarColorDistance = contourSimilarityInput.Value,
        MinimumPixelGradient = contourPixelGradientInput.Value,
        MinimumNeighborGradient = contourNeighborGradientInput.Value,
        MinimumSimilarNeighbors = contourNeighborCountInput.Value,
        MinimumAgreement = contourAgreementInput.Value / 100.0,
        MinimumOpposition = contourOppositionInput.Value / 100.0
    };

    private void CloseContourTool()
    {
        if (_workspaceReady) CloseDockedWorkspaceTool();
        else if (_toolWindows.TryGetValue("contours", out var tool)) tool.Close();
    }

    private void ScheduleContourPreview()
    {
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
        if (!_contourToolOpen || _processing || _restoringHistory) { ClearContourPreview(); return; }
        if (!IsLoaded || _processing || _restoringHistory || !_contourToolOpen) return;
        var basis = LivePreviewBasis;
        if (!ReferenceEquals(basis, _contourPreviewBasis))
        {
            ClearContourPreview();
            _contourPreviewBasis = basis;
        }
        contourApplyButton.IsEnabled = contourPreviewButton.IsEnabled = basis is not null;
        if (basis is null)
        {
            contourPreviewStatus.Text = Localization.Text("Откройте изображение");
            return;
        }
        if ((long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (ContourPreviewVisible)
            {
                ClearContourPreview();
                _contourPreviewBasis = basis;
            }
            contourPreviewStatus.Text = Localization.Text("Большое изображение: нажмите «Обновить предпросмотр».");
            return;
        }
        contourPreviewStatus.Text = Localization.Text("Расчёт карты…");
        _contourPreviewTimer.Start();
    }

    private void ClearContourPreview()
    {
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
        if (ContourPreviewVisible)
        {
            resultPreview.Image = _downscaledResult;
            resultCaption.Text = Localization.StoredText(_activeSource?.SelectedGeneration?.Caption ?? "РЕЗУЛЬТАТ");
            resultPreview.ToolTip = Localization.Text("Ctrl + колёсико — масштаб. Перетащите результат в исходники, чтобы обработать его дальше.");
        }
        _contourPreviewBitmap?.Dispose();
        _contourPreviewBitmap = null;
        _contourPreviewBasis = null;
        livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Collapsed;
        UpdateNavigationState();
    }

    private async Task RenderContourPreview(bool manual)
    {
        if (_contourPreviewBusy || _processing || !_contourToolOpen || LivePreviewBasis is not { } basis) return;
        if (!manual && (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;

        int revision = _contourPreviewRevision;
        var options = BuildContourOptions();
        using var source = (Bitmap)basis.Clone();
        _contourPreviewBusy = true;
        ContourAnalysisResult? analysis = null;
        try
        {
            contourPreviewStatus.Text = Localization.Text("Расчёт карты…");
            analysis = await Task.Run(() => ContourNoiseCleaner.Analyze(source, options));
            if (revision != _contourPreviewRevision || _processing || !_contourToolOpen || !IsLoaded) return;
            resultPreview.Image = null;
            _contourPreviewBitmap?.Dispose();
            _contourPreviewBitmap = analysis.Heatmap;
            resultPreview.Image = analysis.Heatmap;
            livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Visible;
            resultPreview.ToolTip = Localization.Text("Предпросмотр карты. Чтобы сохранить её, нажмите «Создать результат-карту».");
            resultCaption.Text = Localization.Format("ПРЕДПРОСМОТР КАРТЫ · {0} × {1}", analysis.Heatmap.Width, analysis.Heatmap.Height);
            contourPreviewStatus.Text = Localization.Format("Кандидатов: {0}. Для сохранения создайте результат.", analysis.CandidateCount);
            UpdateNavigationState();
            analysis = null;
        }
        catch (Exception ex)
        {
            if (revision == _contourPreviewRevision) contourPreviewStatus.Text = Localization.Format("Предпросмотр недоступен: {0}", ex.Message);
        }
        finally
        {
            analysis?.Heatmap.Dispose();
            _contourPreviewBusy = false;
            if (revision != _contourPreviewRevision) ScheduleContourPreview();
        }
    }

    private async void ApplyContourHeatmap(object? sender, RoutedEventArgs e)
    {
        if (!_contourToolOpen || _sourceImage is null || _activeSource is null || _processing ||
            LivePreviewBasis is not { } basis) return;
        var options = BuildContourOptions();
        var settings = CaptureSettings();
        var document = _activeSource;
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Создание карты кандидатов…");
        try
        {
            var generation = await Task.Run(() =>
            {
                var analysis = ContourNoiseCleaner.Analyze(source, options);
                using (analysis.Heatmap)
                    return _library.SaveGeneration(document, analysis.Heatmap, null, new GenerationEntry
                    {
                        Settings = settings,
                        Operation = "Карта контуров",
                        OperationId = "contours.heatmap",
                        ParentGenerationId = parentId,
                        PreservesTransparency = true,
                        Caption = $"КАРТА КОНТУРОВ · {analysis.Heatmap.Width} × {analysis.Heatmap.Height}",
                        Log = Localization.Format("Кандидатов: {0}. Окно {1} × {1}. Допуск цветов: {2}; градиенты пикселя/соседей: {3}/{4}; похожих соседей: {5}; согласие: {6}%; противоположность: {7}%; защита деталей: {8}.", analysis.CandidateCount,
                            options.WindowSize, options.SimilarColorDistance, options.MinimumPixelGradient,
                            options.MinimumNeighborGradient, options.MinimumSimilarNeighbors,
                            options.MinimumAgreement * 100, options.MinimumOpposition * 100,
                            Localization.Text(options.ProtectDetails ? "Да" : "Нет"))
                    });
            });
            document.Generations.Add(generation);
            CloseContourTool();
            SelectGeneration(generation);
            statusLabel.Text = Localization.Text("Карта кандидатов сохранена в истории как результат.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Не удалось создать карту кандидатов.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Ошибка карты контуров"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
