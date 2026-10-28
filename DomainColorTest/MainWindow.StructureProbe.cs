using System.Windows;
using System.Windows.Threading;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly DispatcherTimer _structurePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _structurePreviewRevision;
    private bool _structurePreviewBusy;
    private Bitmap? _structurePreviewBitmap, _structurePreviewBasis;
    private bool StructureToolOpen => ActiveLivePreviewTool == "structure-probe";
    private bool StructurePreviewVisible => _structurePreviewBitmap is not null &&
        ReferenceEquals(resultPreview.Image, _structurePreviewBitmap);

    private void InitializeStructurePreview()
    {
        _structurePreviewTimer.Tick += async (_, _) =>
        {
            _structurePreviewTimer.Stop();
            await RenderStructurePreview(false);
        };
        foreach (var input in new[] { structureSimilarityInput, structureComponentInput, structureAgreementInput,
            structureDistanceInput, structureConfidenceInput, structureDetailDistanceInput })
            input.ValueChanged += (_, _) => ScheduleStructurePreview();
        foreach (var check in new[] { structureProtectAlphaInput, structureProtectLinesInput, structureMapPreviewInput })
        {
            check.Checked += (_, _) => ScheduleStructurePreview();
            check.Unchecked += (_, _) => ScheduleStructurePreview();
        }
        structurePreviewButton.Click += async (_, _) => await RenderStructurePreview(true);
        structureApplyButton.Click += async (_, _) => await ApplyStructureProbe(false);
        structureMapButton.Click += async (_, _) => await ApplyStructureProbe(true);
        Closed += (_, _) => ClearStructurePreview();
    }

    private StructureProbeOptions BuildStructureOptions() => new()
    {
        SimilarColorDistance = structureSimilarityInput.Value,
        MaximumComponentSize = structureComponentInput.Value,
        MinimumNeighborAgreement = structureAgreementInput.Value / 100.0,
        MaximumReplacementDistance = structureDistanceInput.Value,
        MinimumConfidence = structureConfidenceInput.Value / 100.0,
        DistinctDetailDistance = structureDetailDistanceInput.Value,
        ProtectTransparentBoundary = structureProtectAlphaInput.IsChecked == true,
        ProtectLines = structureProtectLinesInput.IsChecked == true
    };

    private void ScheduleStructurePreview()
    {
        _structurePreviewRevision++;
        _structurePreviewTimer.Stop();
        if (!StructureToolOpen || _processing || _restoringHistory) { ClearStructurePreview(); return; }
        if (!IsLoaded || LivePreviewBasis is not { } basis) return;
        if (!ReferenceEquals(basis, _structurePreviewBasis))
        {
            ClearStructurePreview();
            _structurePreviewBasis = basis;
        }
        structureApplyButton.IsEnabled = structureMapButton.IsEnabled = structurePreviewButton.IsEnabled = true;
        if ((long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (StructurePreviewVisible) ClearStructurePreview();
            structurePreviewStatus.Text = Localization.Text("Большое изображение: нажмите «Обновить предпросмотр».");
            return;
        }
        structurePreviewStatus.Text = Localization.Text("Расчёт структуры…");
        _structurePreviewTimer.Start();
    }

    private void ClearStructurePreview()
    {
        _structurePreviewRevision++;
        _structurePreviewTimer.Stop();
        if (StructurePreviewVisible)
        {
            resultPreview.Image = _downscaledResult;
            resultCaption.Text = Localization.StoredText(_activeSource?.SelectedGeneration?.Caption ?? "РЕЗУЛЬТАТ");
            resultPreview.ToolTip = Localization.Text("Ctrl + колёсико — масштаб. Перетащите результат в исходники, чтобы обработать его дальше.");
        }
        _structurePreviewBitmap?.Dispose();
        _structurePreviewBitmap = null;
        _structurePreviewBasis = null;
        livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Collapsed;
        UpdateNavigationState();
    }

    private async Task RenderStructurePreview(bool manual)
    {
        if (_structurePreviewBusy || _processing || !StructureToolOpen || LivePreviewBasis is not { } basis) return;
        if (!manual && (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;
        int revision = _structurePreviewRevision;
        var options = BuildStructureOptions();
        bool showMap = structureMapPreviewInput.IsChecked == true;
        using var source = (Bitmap)basis.Clone();
        _structurePreviewBusy = true;
        StructureProbeResult? analysis = null;
        try
        {
            structurePreviewStatus.Text = Localization.Text("Расчёт структуры…");
            analysis = await Task.Run(() => StructureProbe.Process(source, options));
            if (revision != _structurePreviewRevision || _processing || !StructureToolOpen || !IsLoaded) return;
            resultPreview.Image = null;
            _structurePreviewBitmap?.Dispose();
            _structurePreviewBitmap = showMap ? analysis.Heatmap : analysis.Image;
            (showMap ? analysis.Image : analysis.Heatmap).Dispose();
            resultPreview.Image = _structurePreviewBitmap;
            livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Visible;
            resultCaption.Text = Localization.Format("ПРЕДПРОСМОТР СТРУКТУРЫ · {0} × {1}", basis.Width, basis.Height);
            resultPreview.ToolTip = Localization.Text("Предпросмотр. Создайте результат, чтобы сохранить изображение в истории.");
            structurePreviewStatus.Text = Localization.Format("Компонентов: {0}; кандидатов: {1}; замен: {2}.",
                analysis.ComponentCount, analysis.CandidatePixels, analysis.ChangedPixels);
            UpdateNavigationState();
            analysis = null;
        }
        catch (Exception ex)
        {
            if (revision == _structurePreviewRevision)
                structurePreviewStatus.Text = Localization.Format("Предпросмотр недоступен: {0}", ex.Message);
        }
        finally
        {
            if (analysis is not null) { analysis.Image.Dispose(); analysis.Heatmap.Dispose(); }
            _structurePreviewBusy = false;
            if (revision != _structurePreviewRevision) ScheduleStructurePreview();
        }
    }

    private async Task ApplyStructureProbe(bool map)
    {
        if (!StructureToolOpen || _sourceImage is null || _activeSource is null || _processing ||
            LivePreviewBasis is not { } basis) return;
        var options = BuildStructureOptions();
        var settings = CaptureSettings();
        var document = _activeSource;
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _structurePreviewRevision++;
        _structurePreviewTimer.Stop();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Проба структуры…");
        try
        {
            var generation = await Task.Run(() =>
            {
                var analysis = StructureProbe.Process(source, options);
                using (analysis.Image)
                using (analysis.Heatmap)
                    return _library.SaveGeneration(document, map ? analysis.Heatmap : analysis.Image, null, new GenerationEntry
                    {
                        Settings = settings,
                        Operation = map ? "Карта структуры" : "Проба структуры",
                        OperationId = map ? "structure-probe.map" : "structure-probe.apply",
                        ParentGenerationId = parentId,
                        PreservesTransparency = true,
                        Caption = map ? $"КАРТА СТРУКТУРЫ · {basis.Width} × {basis.Height}" :
                            $"ПРОБА СТРУКТУРЫ · {basis.Width} × {basis.Height}",
                        Log = Localization.Format("Компонентов: {0}; кандидатов: {1}; замен: {2}. Сходство LAB: {3}; размер области: {4}; согласие: {5}%; дистанция замены: {6}; уверенность: {7}%; защита прозрачности: {8}; защита линий: {9}; защита контрастных деталей от LAB {10}.",
                            analysis.ComponentCount, analysis.CandidatePixels, analysis.ChangedPixels,
                            options.SimilarColorDistance, options.MaximumComponentSize,
                            options.MinimumNeighborAgreement * 100, options.MaximumReplacementDistance,
                            options.MinimumConfidence * 100, options.ProtectTransparentBoundary, options.ProtectLines,
                            options.DistinctDetailDistance)
                    });
            });
            document.Generations.Add(generation);
            if (_workspaceReady) CloseDockedWorkspaceTool();
            else if (_toolWindows.TryGetValue("structure-probe", out var tool)) tool.Close();
            SelectGeneration(generation);
            statusLabel.Text = Localization.Text(map ? "Карта структуры сохранена как результат." : "Проба структуры сохранена как результат.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Не удалось выполнить пробу структуры.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Проба структуры"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
