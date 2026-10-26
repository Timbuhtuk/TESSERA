using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PixelArtDownscale;
using PixelArtAlignment;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly ImageLibrary _library;
    private readonly ObservableCollection<SourceEntry> _sources = [];
    private SourceEntry? _activeSource;
    private bool _restoringHistory;

    private void InitializeLibrary()
    {
        sourceStrip.ItemsSource = _traySources;
        RefreshSourceTray();
        sourceStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _processing || sourceStrip.SelectedItem is not SourceEntry source) return;
            try { SelectSource(source); }
            catch (Exception ex)
            {
                statusLabel.Text = Localization.Format("Не удалось открыть сохранённый исходник: {0}", ex.Message);
                _restoringHistory = true; sourceStrip.SelectedItem = _activeSource; _restoringHistory = false;
            }
        };
        generationStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _processing || generationStrip.SelectedItem is not GenerationEntry generation) return;
            try { SelectGeneration(generation); }
            catch (Exception ex)
            {
                statusLabel.Text = Localization.Format("Не удалось открыть результат: {0}", ex.Message);
                _restoringHistory = true; generationStrip.SelectedItem = _activeSource?.SelectedGeneration; _restoringHistory = false;
            }
        };
        foreach (var preview in new[] { sourcePreview, resultPreview })
            preview.ScrollPositionChanged += (_, _) =>
            {
                var other = ReferenceEquals(preview, sourcePreview) ? resultPreview : sourcePreview;
                var position = preview.ScrollPosition;
                other.ScrollToRelativePosition(position.X, position.Y);
            };
        PreviewDragOver += (sender, e) =>
        {
            if (_workspaceBrowser?.Visibility == Visibility.Visible) return;
            if (IsMoodboardDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropMoodboard(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsAsepriteDropTarget(e.OriginalSource as DependencyObject, e.Data))
            {
                e.Effects = CanDropAseprite(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsBackgroundDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropBackground(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true; return;
            }
            if (IsIconDropTarget(e.OriginalSource as DependencyObject))
            {
                e.Effects = CanDropIcon(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
                return;
            }
            if (HistoryDrop(e, commit: false)) return;
            bool supported = e.Data.GetDataPresent(GenerationDragFormat)
                ? TryGetDraggedGeneration(e.Data, out _, out _) && IsSourceDropTarget(e.OriginalSource as DependencyObject)
                : DropPaths(e).Any(IsSupportedImage);
            e.Effects = !_processing && supported ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += async (_, e) =>
        {
            if (_workspaceBrowser?.Visibility == Visibility.Visible) return;
            if (HistoryDrop(e, commit: true)) return;
            e.Handled = true;
            if (_workspaceReady && !IsMoodboardDropTarget(e.OriginalSource as DependencyObject) && e.Data.GetData(DataFormats.FileDrop) is string[] workspacePaths)
            {
                await ImportWorkspaceFilesAsync(workspacePaths);
                if (_workspaceCatalog?.State.SelectedAsset is { } selectedId && _workspaceCatalog.State.Assets.FirstOrDefault(a => a.Id == selectedId) is { } selectedAsset) await OpenWorkspaceAssetAsync(selectedAsset);
                return;
            }
            if (IsMoodboardDropTarget(e.OriginalSource as DependencyObject)) { await DropMoodboardAsync(e); return; }
            if (IsAsepriteDropTarget(e.OriginalSource as DependencyObject, e.Data)) { await DropAsepriteAsync(e); return; }
            if (IsBackgroundDropTarget(e.OriginalSource as DependencyObject)) { await DropBackgroundAsync(e); return; }
            if (IsIconDropTarget(e.OriginalSource as DependencyObject)) { await DropIconAsync(e); return; }
            if (_processing) { e.Effects = DragDropEffects.None; return; }
            if (e.Data.GetDataPresent(GenerationDragFormat))
            {
                if (IsSourceDropTarget(e.OriginalSource as DependencyObject) && TryGetDraggedGeneration(e.Data, out var source, out var generation))
                {
                    e.Effects = DragDropEffects.Copy;
                    await AddGenerationAsSourceAsync(source, generation);
                }
                else e.Effects = DragDropEffects.None;
                return;
            }
            string[] paths = DropPaths(e);
            if (paths.Length > 0) await AddImagesAsync(paths);
        };
        try
        {
            foreach (var source in _library.Load()) _sources.Add(source);
            if (_sources.Count > 0) SelectSource(_sources[^1]);
            if (_library.LoadWarnings.Count > 0)
                statusLabel.Text = Localization.Format("История открыта. Не удалось прочитать записей: {0}.", _library.LoadWarnings.Count);
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось загрузить историю: {0}", ex.Message); }
    }

    private static string[] DropPaths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
    private static bool IsSupportedImage(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private async Task AddImagesAsync(IEnumerable<string> paths)
    {
        if (_workspaceReady)
        {
            await ImportWorkspaceFilesAsync(paths.ToArray());
            if (_workspaceCatalog?.State.Assets.FirstOrDefault(a => a.Id == _workspaceCatalog.State.SelectedAsset) is { } asset) await OpenWorkspaceAssetAsync(asset);
            return;
        }
        if (_processing) return;
        SetProcessingState(true);
        var failures = new List<string>();
        SourceEntry? selected = null;
        int added = 0;
        try
        {
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    statusLabel.Text = Localization.Format("Добавление: {0}…", Path.GetFileName(path));
                    var existing = _sources.ToArray();
                    var source = await Task.Run(() => _library.Import(path, existing));
                    if (!_sources.Contains(source)) { _sources.Add(source); added++; }
                    selected = source;
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }
            if (selected is not null) SelectSource(selected);
            statusLabel.Text = failures.Count == 0
                ? Localization.Format("Добавлено: {0}. Исходников в ленте: {1}. История сохраняется автоматически.", added, _sources.Count)
                : Localization.Format("Добавлено: {0}. Не удалось открыть: {1}. {2}", added, failures.Count, failures[0]);
            statusLabel.ToolTip = failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
        }
        finally { SetProcessingState(false); }
    }

    private void LoadImage(string path)
    {
        var source = _library.Import(path, _sources);
        if (!_sources.Contains(source)) _sources.Add(source);
        SelectSource(source);
    }

    private void SelectSource(SourceEntry source)
    {
        AdoptWorkspaceSource(source);
        ChooseSourceFolder(source);
        if (ReferenceEquals(_activeSource, source)) { ProcessingBasis = 0; SyncProcessingSelection(); ShowEditor(); return; }
        var bitmap = ImageLibrary.ReadBitmap(_library.SourcePath(source));
        ShowEditor();
        if (_activeSource is not null) _activeSource.DraftSettings = CaptureSettings();
        _restoringHistory = true;
        try
        {
            sourcePreview.Image = null;
            _sourceImage?.Dispose();
            _sourceImage = bitmap;
            _activeSource = source;
            _selectedFilePath = source.OriginalPath;
            ClearDisplayedResult();
            sourceStrip.SelectedItem = source;
            sourceStrip.ScrollIntoView(source);
            generationStrip.ItemsSource = source.Generations;
            ConfigureHistorySource(source);
            fileLabel.Text = source.Label;
            fileLabel.ToolTip = source.OriginalPath;
        }
        finally { _restoringHistory = false; }
        if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
        else
        {
            if (source.DraftSettings is { } draft) RestoreSettings(draft);
            else if (aspectLock.IsChecked == true) SizeChangedByUser(widthChanged: true);
            UpdateSourcePreview();
            UpdateSizeHint();
            statusLabel.Text = Localization.Text("Исходник выбран. Перетащите ещё изображения или запустите обработку.");
        }
        sourcePreview.ScrollToRelativePosition(0, 0);
        resultPreview.ScrollToRelativePosition(0, 0);
        ProcessingBasis = 0;
        SyncProcessingSelection();
        SetProcessingState(_processing);
        RestoreWorkspaceImageView(source);
    }

    private void ClearDisplayedResult()
    {
        ClearDisplayedLivePreview();
        resultPreview.Image = null;
        _downscaledResult?.Dispose(); _downscaledResult = null;
        _resultStale = _resultPreservesTransparency = _resultIsAlignment = false;
        resultCaption.Text = Localization.Text("РЕЗУЛЬТАТ");
        processingLog.Clear();
    }

    private void SelectGeneration(GenerationEntry generation)
    {
        if (_activeSource is null || !_activeSource.Generations.Contains(generation)) return;
        ClearDisplayedLivePreview();
        var output = ImageLibrary.ReadBitmap(_library.ResultPath(_activeSource, generation));
        if (generation.OperationId != ProcessingOperations.Macro) RestoreSettings(generation.Settings);
        _restoringHistory = true;
        try
        {
            sourcePreview.Image = null;
            resultPreview.Image = null;
            _downscaledResult?.Dispose(); _downscaledResult = output;
            resultPreview.Image = output;
            _resultIsAlignment = generation.IsAlignment;
            _resultPreservesTransparency = generation.PreservesTransparency;
            _resultStale = false;
            resultCaption.Text = Localization.StoredText(generation.Caption);
            processingLog.Text = Localization.StoredText(generation.Log);
            _activeSource.SelectedGeneration = generation;
            generationStrip.SelectedItem = generation;
            generationStrip.ScrollIntoView(generation);
        }
        finally { _restoringHistory = false; }
        ProcessingBasis = 1;
        SyncProcessingSelection();
        UpdateSourcePreview();
        UpdateSizeHint();
        SetProcessingState(_processing);
        statusLabel.Text = Localization.Format("Результат от {0:dd.MM.yyyy HH:mm:ss}. Сохранён в истории.", generation.CreatedAt);
    }

    private void SyncProcessingSelection()
    {
        if (_restoringHistory) return;
        _restoringHistory = true;
        try
        {
            sourceStrip.SelectedItem = ProcessingBasis == 0 ? _activeSource : null;
            generationStrip.SelectedItem = ProcessingBasis == 1 ? _activeSource?.SelectedGeneration : null;
        }
        finally { _restoringHistory = false; }
    }

    private void RemoveLibraryItem(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_processing || sender is not FrameworkElement element) return;
        _dragCandidate = null;
        try
        {
            if (element.DataContext is SourceEntry source) RemoveSource(source);
            else if (element.DataContext is GenerationEntry generation) RemoveGeneration(generation);
        }
        catch (Exception ex) { statusLabel.Text = Localization.Format("Не удалось удалить запись или открыть соседнюю: {0}", ex.Message); }
        finally { SetProcessingState(_processing); }
    }

    private void RemoveSource(SourceEntry source)
    {
        int index = _sources.IndexOf(source);
        if (index < 0) return;
        bool selected = ReferenceEquals(_activeSource, source);
        var visible = (_workspaceReady ? _workspaceSources : _sources).ToList();
        int visibleIndex = visible.IndexOf(source);
        var next = visible.Count <= 1 || visibleIndex < 0 ? null : visible[visibleIndex + 1 < visible.Count ? visibleIndex + 1 : visibleIndex - 1];
        _library.RemoveSource(source);
        _restoringHistory = true;
        try
        {
            _sources.Remove(source);
            if (selected)
            {
                ClearDisplayedResult();
                sourcePreview.Image = null;
                _sourceImage?.Dispose(); _sourceImage = null;
                _activeSource = null;
                _selectedFilePath = null;
                sourceStrip.SelectedItem = null;
                generationStrip.ItemsSource = null;
                if (_trayHistorySource is not null) _trayHistorySource.Generations.CollectionChanged -= HistoryChanged;
                _trayHistorySource = null; RefreshHistoryLayout();
                fileLabel.Text = Localization.Text("Откройте изображение");
                fileLabel.ToolTip = null;
                UpdateSourcePreview();
                UpdateSizeHint();
            }
        }
        finally { _restoringHistory = false; }
        if (selected && next is not null) SelectSource(next);
        else if (selected && _workspaceReady) { _workspaceActive = null; ShowHome(); }
        statusLabel.Text = _sources.Count == 0
            ? Localization.Text("Все исходники удалены из программы. Откройте или перетащите новое изображение.")
            : Localization.Text("Исходник и его результаты удалены из программы.");
    }

    private void RemoveGeneration(GenerationEntry generation)
    {
        var source = _sources.FirstOrDefault(s => s.Generations.Contains(generation));
        if (source is null) return;
        int index = source.Generations.IndexOf(generation);
        bool selected = ReferenceEquals(source.SelectedGeneration, generation);
        bool visible = ReferenceEquals(source, _activeSource);
        bool resultWasSelected = visible && ProcessingBasis == 1;
        var next = source.Generations.Count <= 1 ? null
            : source.Generations[index + 1 < source.Generations.Count ? index + 1 : index - 1];
        _library.RemoveGeneration(source, generation);
        _restoringHistory = true;
        try
        {
            source.Generations.Remove(generation);
            if (selected)
            {
                source.SelectedGeneration = visible ? null : next;
                if (visible)
                {
                    generationStrip.SelectedItem = null;
                    ClearDisplayedResult();
                }
            }
        }
        finally { _restoringHistory = false; }
        if (selected && visible)
        {
            if (next is not null)
            {
                SelectGeneration(next);
                if (!resultWasSelected) { ProcessingBasis = 0; SyncProcessingSelection(); }
            }
            else
            {
                ProcessingBasis = 0; SyncProcessingSelection();
                UpdateSourcePreview(); UpdateSizeHint();
            }
        }
        statusLabel.Text = Localization.Text("Результат удалён из истории.");
    }

    private EditorSettings CaptureReadyModeSettings()
    {
        _capturingReadyMode = true;
        try { return CaptureSettings(); }
        finally { _capturingReadyMode = false; }
    }

    private EditorSettings CaptureSettings() => new()
    {
        Mode = detailsMode.IsChecked == true ? 2 : backgroundMode.IsChecked == true ? 0 : 1,
        Downscale = BuildOptionsFromUi(), CellSize = gridCellInput.Value,
        ChannelAdjustment = _channelAdjustment,
        ConnectedPalette = BuildConnectedOptions(),
        ConnectedPaletteTileSize = connectedTileSizeInput.Value,
        ContourWindowSize = ContourWindowSize,
        ContourStrictness = ContourCleanStrictness.Strong,
        ContourProtectDetails = contourDetailGuardInput.IsChecked == true,
        ContourSimilarColorDistance = contourSimilarityInput.Value,
        ContourMinimumPixelGradient = contourPixelGradientInput.Value,
        ContourMinimumNeighborGradient = contourNeighborGradientInput.Value,
        ContourMinimumSimilarNeighbors = contourNeighborCountInput.Value,
        ContourMinimumAgreement = contourAgreementInput.Value,
        ContourMinimumOpposition = contourOppositionInput.Value,
        DetectGrid = detectGridInput.IsChecked == true, AlignmentMode = SelectedGridAlignmentMode,
        SnapToGlobalGrid = SelectedGridAlignmentMode == GridAlignmentMode.Local && snapLocalGridInput.IsChecked == true,
        AspectLock = aspectLock.IsChecked == true
    };

    private void RestoreSettings(EditorSettings settings)
    {
        _restoringHistory = true;
        try
        {
            (settings.Mode == 2 ? detailsMode : settings.Mode == 0 ? backgroundMode : sceneMode).IsChecked = true;
            UpdateModeDescription();
            var options = settings.Downscale;
            widthInput.Value = options.TargetWidth; heightInput.Value = options.TargetHeight;
            spriteInput.IsChecked = options.SpriteMode; alphaInput.Value = options.AlphaThreshold;
            cropHorizontalInput.SelectedIndex = Math.Clamp((int)options.CropHorizontal, 0, 2);
            cropVerticalInput.SelectedIndex = Math.Clamp((int)options.CropVertical, 0, 2);
            paletteInput.SelectedIndex = Math.Clamp((int)options.Palette - 1, 0, 4);
            paletteStepInput.Value = options.PaletteStep;
            quantizationInput.SelectedIndex = Math.Clamp((int)options.Quantization, 0, 2);
            quantizationColorsInput.Value = options.QuantizationColors;
            paletteColorModeInput.IsChecked = options.IndependentColorMode == IndependentColorMode.Palette ||
                options.IndependentColorMode == IndependentColorMode.Combined && options.Palette != PaletteKind.None;
            quantizationColorModeInput.IsChecked = paletteColorModeInput.IsChecked != true;
            localColorPassesInput.Value = options.LocalColorPasses;
            // Older histories store target brightness/contrast rather than cleanup parameters.
            // They open with safe cleanup defaults; their old values are never reinterpreted as tolerances.
            var cleanup = options.Cleanup ?? new ColorCleanupOptions();
            localBrightnessInput.Value = cleanup.ColorTolerance / .4;
            localContrastInput.Value = cleanup.Strength * 100;
            localDetailGuardInput.IsChecked = cleanup.DetailProtection > 0;
            localEdgeInput.Value = cleanup.EdgeProtection * 100;
            colorWeightsInput.IsChecked = options.UseColorWeights;
            threadsInput.Value = options.ThreadCount;
            automaticInput.IsChecked = options.BlockMode != BlockSelectionMode.Manual;
            manualInput.IsChecked = options.BlockMode == BlockSelectionMode.Manual;
            brightnessInput.Value = (options.ManualCriteria?.TargetBrightness ?? .5) * 100;
            contrastInput.Value = (options.ManualCriteria?.TargetContrast ?? .5) * 100;
            saturationInput.Value = (options.ManualCriteria?.TargetSaturation ?? .5) * 100;
            edgeInput.Value = (options.ManualCriteria?.TargetEdge ?? .5) * 100;
            aspectLock.IsChecked = settings.AspectLock;
            RestoreChannelAdjustment(settings.ChannelAdjustment);
            var connected = settings.ConnectedPalette ?? new ConnectivityPaletteOptions();
            connectedColorsInput.Value = Math.Clamp(connected.TargetColors, 1, 4096);
            connectedConnectivityInput.Value = Math.Clamp((int)Math.Round(connected.ConnectivityWeight * 100), 0, 100);
            connectedTileSizeInput.Value = Math.Clamp(settings.ConnectedPaletteTileSize, 4, 256);
            connectedTilesInput.IsChecked = connected.TileSize != 0;
            connectedLocalInput.Value = Math.Clamp((int)Math.Round(connected.LocalWeight * 100), 0, 100);
            connectedDetailInput.Value = Math.Clamp((int)Math.Round(connected.DetailWeight * 100), 0, 100);
            connectedContextInput.Value = Math.Clamp((int)Math.Round(connected.ContextWeight * 100), 0, 100);
            connectedSpaceInput.SelectedIndex = connected.DistanceSpace == PaletteDistanceSpace.CieLab ? 1 : 0;
            contourWindowInput.Value = settings.ContourWindowSize is >= 3 and <= 15 && settings.ContourWindowSize % 2 == 1 ? settings.ContourWindowSize : 15;
            contourDetailGuardInput.IsChecked = settings.ContourProtectDetails;
            contourSimilarityInput.Value = settings.ContourSimilarColorDistance;
            contourPixelGradientInput.Value = settings.ContourMinimumPixelGradient;
            contourNeighborGradientInput.Value = settings.ContourMinimumNeighborGradient;
            contourNeighborCountInput.Value = settings.ContourMinimumSimilarNeighbors;
            contourAgreementInput.Value = settings.ContourMinimumAgreement;
            contourOppositionInput.Value = settings.ContourMinimumOpposition;
            (settings.AlignmentMode == GridAlignmentMode.Local ? localGridModeInput : globalGridModeInput).IsChecked = true;
            snapLocalGridInput.IsChecked = settings.SnapToGlobalGrid;
            UpdateGridAlignmentMode();
            gridCellInput.Value = settings.CellSize; detectGridInput.IsChecked = settings.DetectGrid;
            UpdateDependentControls();
            ditheringInput.IsChecked = options.Palette is >= PaletteKind.DB16 and <= PaletteKind.GameBoy && options.EnableDithering;
            profileState.Text = Localization.Text("Параметры выбранного результата");
        }
        finally { _restoringHistory = false; }
    }
}
