using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly DispatcherTimer _connectedPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _connectedPreviewRevision;
    private bool _connectedPreviewBusy;
    private Bitmap? _connectedPreviewBitmap, _connectedPreviewBasis;
    private bool ConnectedPaletteOpen => ActiveLivePreviewTool == "connected-palette";
    private bool ConnectedPreviewVisible => _connectedPreviewBitmap is not null &&
        ReferenceEquals(resultPreview.Image, _connectedPreviewBitmap);

    private void InitializeConnectedPalette()
    {
        connectedTilesInput.ToolTip = Localization.Text("Учитывает концентрацию цвета в локальных областях заданного размера. Отключите для оценки по всему изображению.");
        _connectedPreviewTimer.Tick += async (_, _) =>
        {
            _connectedPreviewTimer.Stop();
            await RenderConnectedPreview(false);
        };
        foreach (var input in new[] { connectedColorsInput, connectedConnectivityInput, connectedTileSizeInput,
            connectedLocalInput, connectedDetailInput, connectedContextInput })
            input.ValueChanged += (_, _) => ScheduleConnectedPreview();
        connectedSpaceInput.SelectionChanged += (_, _) => ScheduleConnectedPreview();
        connectedTilesInput.Checked += (_, _) => ChangeTiles();
        connectedTilesInput.Unchecked += (_, _) => ChangeTiles();
        connectedPreviewButton.Click += async (_, _) => await RenderConnectedPreview(true);
        connectedApplyButton.Click += async (_, _) => await ApplyConnectedPalette();
        Closed += (_, _) => ClearConnectedPreview();

        void ChangeTiles()
        {
            connectedTileSizeInput.IsEnabled = connectedLocalInput.IsEnabled = connectedTilesInput.IsChecked == true;
            ScheduleConnectedPreview();
        }
    }

    private ConnectivityPaletteOptions BuildConnectedOptions() => new()
    {
        TargetColors = connectedColorsInput.Value,
        ConnectivityWeight = connectedConnectivityInput.Value / 100.0,
        TileSize = connectedTilesInput.IsChecked == true ? connectedTileSizeInput.Value : 0,
        LocalWeight = connectedLocalInput.Value / 100.0,
        DetailWeight = connectedDetailInput.Value / 100.0,
        ContextWeight = connectedContextInput.Value / 100.0,
        DistanceSpace = connectedSpaceInput.SelectedIndex == 1 ? PaletteDistanceSpace.CieLab : PaletteDistanceSpace.Oklab
    };

    private void ScheduleConnectedPreview()
    {
        _connectedPreviewRevision++;
        _connectedPreviewTimer.Stop();
        if (!ConnectedPaletteOpen || _processing || _restoringHistory) { ClearConnectedPreview(); return; }
        if (!IsLoaded || LivePreviewBasis is not { } basis) return;
        if (!ReferenceEquals(basis, _connectedPreviewBasis))
        {
            ClearConnectedPreview();
            _connectedPreviewBasis = basis;
        }
        connectedApplyButton.IsEnabled = connectedPreviewButton.IsEnabled = true;
        if ((long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (ConnectedPreviewVisible) ClearConnectedPreview();
            connectedPreviewStatus.Text = Localization.Text("Большое изображение: нажмите «Обновить предпросмотр».");
            return;
        }
        connectedPreviewStatus.Text = Localization.Text("Расчёт палитры…");
        _connectedPreviewTimer.Start();
    }

    private void ClearConnectedPreview()
    {
        _connectedPreviewRevision++;
        _connectedPreviewTimer.Stop();
        if (ConnectedPreviewVisible)
        {
            resultPreview.Image = _downscaledResult;
            resultCaption.Text = Localization.StoredText(_activeSource?.SelectedGeneration?.Caption ?? "РЕЗУЛЬТАТ");
            resultPreview.ToolTip = Localization.Text("Ctrl + колёсико — масштаб. Перетащите результат в исходники, чтобы обработать его дальше.");
        }
        _connectedPreviewBitmap?.Dispose();
        _connectedPreviewBitmap = null;
        _connectedPreviewBasis = null;
        livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Collapsed;
        UpdateNavigationState();
    }

    private async Task RenderConnectedPreview(bool manual)
    {
        if (_connectedPreviewBusy || _processing || !ConnectedPaletteOpen || LivePreviewBasis is not { } basis) return;
        if (!manual && (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;
        int revision = _connectedPreviewRevision;
        var options = BuildConnectedOptions();
        using var source = (Bitmap)basis.Clone();
        _connectedPreviewBusy = true;
        ConnectivityPaletteResult? report = null;
        try
        {
            connectedPreviewStatus.Text = Localization.Text("Расчёт палитры…");
            report = await Task.Run(() => ConnectivityPaletteReducer.Process(source, options));
            if (revision != _connectedPreviewRevision || _processing || !ConnectedPaletteOpen || !IsLoaded) return;
            resultPreview.Image = null;
            _connectedPreviewBitmap?.Dispose();
            _connectedPreviewBitmap = report.Image;
            resultPreview.Image = _connectedPreviewBitmap;
            livePreviewFrame.Visibility = livePreviewBadge.Visibility = Visibility.Visible;
            resultCaption.Text = Localization.Format("ПРЕДПРОСМОТР ПАЛИТРЫ · {0} × {1}", basis.Width, basis.Height);
            resultPreview.ToolTip = Localization.Text("Предпросмотр. Создайте результат, чтобы сохранить изображение в истории.");
            connectedPreviewStatus.Text = Localization.Format("Цветов: {0} → {1}; изменено {2} пикселей.",
                report.ColorsBefore, report.ColorsAfter, report.ChangedPixels);
            UpdateNavigationState();
            report = null;
        }
        catch (Exception ex)
        {
            if (revision == _connectedPreviewRevision)
                connectedPreviewStatus.Text = Localization.Format("Предпросмотр недоступен: {0}", ex.Message);
        }
        finally
        {
            report?.Image.Dispose();
            _connectedPreviewBusy = false;
            if (revision != _connectedPreviewRevision) ScheduleConnectedPreview();
        }
    }

    private async Task ApplyConnectedPalette()
    {
        if (!ConnectedPaletteOpen || _sourceImage is null || _activeSource is null || _processing ||
            LivePreviewBasis is not { } basis) return;
        var options = BuildConnectedOptions();
        var settings = CaptureSettings();
        var document = _activeSource;
        bool useResult = ProcessingBasis == 1 && _downscaledResult is not null;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _connectedPreviewRevision++;
        _connectedPreviewTimer.Stop();
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Сокращение связной палитры…");
        try
        {
            var generation = await Task.Run(() =>
            {
                var report = ConnectivityPaletteReducer.Process(source, options);
                using (report.Image)
                    return _library.SaveGeneration(document, report.Image, null, new GenerationEntry
                    {
                        Settings = settings,
                        Operation = "Связная палитра",
                        OperationId = ProcessingOperations.ConnectedPalette,
                        AppliedRecipe = HistoryRecipes.Single(new ProcessingStep
                        {
                            OperationId = ProcessingOperations.ConnectedPalette,
                            ConnectedPalette = options
                        }),
                        ParentGenerationId = parentId,
                        PreservesTransparency = true,
                        Caption = $"СВЯЗНАЯ ПАЛИТРА · {basis.Width} × {basis.Height}",
                        Log = Localization.Format("Цветов: {0} → {1}; компонент: {2}; изменено пикселей: {3}. Цель: {4}; связность: {5}%; локальная область: {6}; локальный вес: {7}%; различие: {8}%; контекст: {9}%; пространство: {10}.",
                            report.ColorsBefore, report.ColorsAfter, report.ComponentsBefore, report.ChangedPixels,
                            options.TargetColors, options.ConnectivityWeight * 100, options.TileSize,
                            options.LocalWeight * 100, options.DetailWeight * 100, options.ContextWeight * 100,
                            options.DistanceSpace)
                    });
            });
            document.Generations.Add(generation);
            if (_workspaceReady) CloseDockedWorkspaceTool();
            else if (_toolWindows.TryGetValue("connected-palette", out var tool)) tool.Close();
            SelectGeneration(generation);
            statusLabel.Text = Localization.Text("Связная палитра сохранена как результат.");
        }
        catch (Exception ex)
        {
            statusLabel.Text = Localization.Text("Не удалось сократить палитру.");
            TesseraDialog.Show(this, ex.Message, Localization.Text("Связная палитра"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetProcessingState(false); }
    }
}
