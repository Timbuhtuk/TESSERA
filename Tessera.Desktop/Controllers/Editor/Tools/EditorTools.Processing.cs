using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private readonly ImageEditorPage _editor;
    private MainWindow Host => _editor.Host;

    internal EditorTools(ImageEditorPage editor)
    {
        _editor = editor;
    }

    internal void InitializeInputs()
    {
        _editor.threadsInput.Maximum = _editor.threadsInput.Value = Math.Max(1, Environment.ProcessorCount);
        foreach (var mode in new[] { _editor.backgroundMode, _editor.sceneMode, _editor.detailsMode })
            mode.Checked += (_, _) => UpdateModeDescription();
        foreach (var box in new[] { _editor.paletteInput, _editor.quantizationInput, _editor.cropHorizontalInput, _editor.cropVerticalInput })
            box.SelectionChanged += SettingsChanged;
        foreach (var input in new[] { _editor.paletteStepInput, _editor.alphaInput, _editor.quantizationColorsInput, _editor.localColorPassesInput })
            input.ValueChanged += SettingsChanged;
        foreach (var check in new ToggleButton[] { _editor.spriteInput, _editor.ditheringInput, _editor.colorWeightsInput, _editor.automaticInput, _editor.manualInput,
            _editor.quantizationColorModeInput, _editor.paletteColorModeInput, _editor.localDetailGuardInput })
        {
            check.Checked += SettingsChanged;
            check.Unchecked += SettingsChanged;
        }
        foreach (var slider in new[] { _editor.brightnessInput, _editor.contrastInput, _editor.saturationInput, _editor.edgeInput,
            _editor.localBrightnessInput, _editor.localContrastInput, _editor.localEdgeInput })
            slider.ValueChanged += (_, _) => SettingsChanged(null, EventArgs.Empty);
        _editor.widthInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: true);
        _editor.heightInput.ValueChanged += (_, _) => SizeChangedByUser(widthChanged: false);
        _editor.aspectLock.Checked += (_, _) => SizeChangedByUser(widthChanged: true);
        _editor.detectGridInput.Checked += (_, _) => { _editor.gridCellInput.IsEnabled = false; AlignmentSettingsChanged(); };
        _editor.detectGridInput.Unchecked += (_, _) => { _editor.gridCellInput.IsEnabled = true; AlignmentSettingsChanged(); };
        _editor.gridCellInput.ValueChanged += (_, _) => AlignmentSettingsChanged();
        _editor.globalGridModeInput.Checked += (_, _) => UpdateGridAlignmentMode();
        _editor.localGridModeInput.Checked += (_, _) => UpdateGridAlignmentMode();
        _editor.snapLocalGridInput.Checked += (_, _) => UpdateGridAlignmentMode();
        _editor.snapLocalGridInput.Unchecked += (_, _) => UpdateGridAlignmentMode();
        UpdateGridAlignmentMode();
        _editor.processButton.Click += ProcessImage;
        _editor.scaleOnlyButton.Click += ScaleOnlyImage;
        _editor.trimTransparentButton.Click += TrimTransparentEdges;
        _editor.colorOnlyButton.Click += ApplyColorsOnly;
        _editor.neighborColorsButton.Click += ApplyNeighborColorsOnly;
        _editor.alignButton.Click += AlignImage;
        _editor.sceneMode.IsChecked = true;
        InitializeCleanupControls();
    }

    private readonly PixelArtDownscaler _downscaler = new();

    private bool _applyingProfile;

    private bool _capturingReadyMode;

    private bool _updatingSize;

    private string CurrentModeName => _editor.detailsMode.IsChecked == true ? Localization.Text("Details") : _editor.backgroundMode.IsChecked == true ? Localization.Text("Background") : Localization.Text("Scene");

    internal void UpdateModeDescription()
    {
        _editor.modeDescription.Text = _editor.detailsMode.IsChecked == true
            ? Localization.Text("PixelArtSpritesPrioritizeOutlinesTransparency")
            : _editor.backgroundMode.IsChecked == true
                ? Localization.Text("LargeBackgroundsSolidColorRegionsOverall")
                : Localization.Text("ScenesModerateDetailPreserveMeaningfulShapes");
        _editor.profileState.Text = Localization.Text("PresetSettingsAreAppliedWhenProcessing");
    }

    private void ApplyProfile()
    {
        _applyingProfile = true;
        try
        {
            bool details = _editor.detailsMode.IsChecked == true;
            _editor.quantizationInput.SelectedIndex = details ? 0 : 1;
            _editor.quantizationColorsInput.Value = 64;
            _editor.colorWeightsInput.IsChecked = false;
            _editor.paletteInput.SelectedIndex = details ? 0 : 4;
            _editor.paletteStepInput.Value = 16;
            _editor.spriteInput.IsChecked = details;
            _editor.alphaInput.Value = details ? 75 : 50;
            _editor.automaticInput.IsChecked = true;
            _editor.ditheringInput.IsChecked = false;
            _editor.cropHorizontalInput.SelectedIndex = _editor.cropVerticalInput.SelectedIndex = 0;
            _editor.brightnessInput.Value = _editor.contrastInput.Value = _editor.saturationInput.Value = _editor.edgeInput.Value = 50;
            _editor.profileState.Text = Localization.Text("ModeSettings");
            UpdateDependentControls();
            MarkResultStale();
            UpdateSizeHint();
            Host.statusLabel.Text = Localization.Format("SettingsForPresetAppliedFormat", CurrentModeName);
        }
        finally { _applyingProfile = false; }
    }

    internal void SettingsChanged(object? sender, EventArgs e)
    {
        if (_applyingProfile || _editor.History.SelectionUpdatesPaused) return;
        UpdateDependentControls();
        _editor.profileState.Text = Localization.Text("SettingsChangedManually");
        MarkResultStale();
        UpdateSizeHint();
    }

    internal void UpdateDependentControls()
    {
        bool quantizationSelected = _editor.quantizationColorModeInput.IsChecked == true;
        bool paletteSelected = _editor.paletteColorModeInput.IsChecked == true;
        _editor.quantizationColorControls.IsEnabled = quantizationSelected;
        _editor.paletteColorControls.IsEnabled = paletteSelected;
        _editor.quantizationColorControls.Opacity = quantizationSelected ? 1 : .55;
        _editor.paletteColorControls.Opacity = paletteSelected ? 1 : .55;
        UpdateConnectedControls();
        MainWindow.SetVisible(_editor.stepControls, _editor.paletteInput.SelectedIndex == 4);
        MainWindow.SetVisible(_editor.cropControls, _editor.spriteInput.IsChecked != true);
        MainWindow.SetVisible(_editor.alphaControls, _editor.spriteInput.IsChecked == true);
        bool ditheringAllowed = _editor.paletteInput.SelectedIndex is >= 0 and <= 3 or >= 5;
        _editor.ditheringInput.IsEnabled = paletteSelected && ditheringAllowed;
        if (!ditheringAllowed) _editor.ditheringInput.IsChecked = false;
        MainWindow.SetVisible(_editor.manualControls, _editor.manualInput.IsChecked == true);
        _editor.brightnessLabel.Text = Localization.Format("BrightnessFormat", _editor.brightnessInput.Value);
        _editor.contrastLabel.Text = Localization.Format("ContrastFormat", _editor.contrastInput.Value);
        _editor.saturationLabel.Text = Localization.Format("SaturationFormat", _editor.saturationInput.Value);
        _editor.edgeLabel.Text = Localization.Format("OutlineFormat", _editor.edgeInput.Value);
        _editor.localBrightnessLabel.Text = Localization.Format("ColorSimilarityEFormat", _editor.localBrightnessInput.Value * .4);
        _editor.localContrastLabel.Text = Localization.Format("CleanupStrengthFormat", _editor.localContrastInput.Value);
        _editor.localEdgeLabel.Text = Localization.Format("EdgeProtectionFormat", _editor.localEdgeInput.Value);
    }

    internal void SizeChangedByUser(bool widthChanged)
    {
        if (_updatingSize || _editor.History.SelectionUpdatesPaused) return;
        _updatingSize = true;
        try
        {
            var sizeSource = _editor.ProcessingBasis == 1 ? _editor._downscaledResult : _editor._sourceImage;
            if (_editor.aspectLock.IsChecked == true && sizeSource is not null)
            {
                var input = widthChanged ? _editor.heightInput : _editor.widthInput;
                double value = widthChanged
                    ? Math.Round((double)_editor.widthInput.Value * sizeSource.Height / sizeSource.Width)
                    : Math.Round((double)_editor.heightInput.Value * sizeSource.Width / sizeSource.Height);
                input.Value = (int)Math.Clamp(value, input.Minimum, input.Maximum);
            }
        }
        finally { _updatingSize = false; }
        MarkResultStale();
        UpdateSizeHint();
    }

    internal void UpdateSizeHint()
    {
        _editor.Preview.UpdateCropPreview();
        var sizeSource = _editor.ProcessingBasis == 1 ? _editor._downscaledResult : _editor._sourceImage;
        bool validTarget = TryGetTargetSize(out int width, out int height);
        _editor.scaleOnlyButton.IsEnabled = !_editor._processing && _editor._sourceImage is not null && validTarget;
        if (!validTarget && _editor.scaleFactorModeInput.IsChecked == true && sizeSource is not null)
        {
            _editor.sizeHint.Text = Localization.Text("EnterPositiveFactorResultingDimensionsMust");
            _editor.sizeToolGroup.ToolTip = _editor.sizeHint.Text;
            _editor.frameToolGroup.IsEnabled = _editor.pixelToolGroup.IsEnabled = false;
            return;
        }
        if (sizeSource is null)
        {
            _editor.sizeHint.Text = Localization.Text("SetTheOutputWidthAndHeight");
            _editor.sizeToolGroup.ToolTip = _editor.sizeHint.Text;
            _editor.frameToolGroup.IsEnabled = _editor.pixelToolGroup.IsEnabled = true;
            return;
        }
        if (_editor.scaleFactorModeInput.IsChecked == true)
        {
            _editor.sizeHint.Text = Localization.Format("InputResultPxFormat", sizeSource.Width, sizeSource.Height, width, height);
            _editor.sizeToolGroup.ToolTip = _editor.sizeHint.Text;
            bool factorReducing = width <= sizeSource.Width && height <= sizeSource.Height;
            _editor.frameToolGroup.IsEnabled = _editor.pixelToolGroup.IsEnabled = factorReducing;
            return;
        }
        if (width > sizeSource.Width || height > sizeSource.Height)
            _editor.sizeHint.Text = Localization.Format("InputFormat", sizeSource.Width, sizeSource.Height);
        else if (_editor.spriteInput.IsChecked == true && Math.Abs((double)width / height / ((double)sizeSource.Width / sizeSource.Height) - 1) > .02)
            _editor.sizeHint.Text = Localization.Text("AspectRatioDiffersSourceWholeFrame");
        else
            _editor.sizeHint.Text = _editor.spriteInput.IsChecked == true
                ? Localization.Text("TheEntireSourceFitsIntoThe")
                : Localization.Text("SourceAreaCroppedWholeBlocks");
        _editor.sizeToolGroup.ToolTip = _editor.sizeHint.Text;
        bool reducing = width <= sizeSource.Width && height <= sizeSource.Height;
        _editor.frameToolGroup.IsEnabled = _editor.pixelToolGroup.IsEnabled = reducing;
    }

    private void MarkResultStale()
    {
        if (_editor._resultIsAlignment || _editor.History.SelectionUpdatesPaused || _editor._activeSource?.SelectedGeneration?.IsGridReduction == true) return;
        if (_editor._downscaledResult is null) return;
        _editor._resultStale = true;
        _editor.resultCaption.Text = Localization.Format("ResultPreviousSettingsFormat", _editor._downscaledResult.Width, _editor._downscaledResult.Height);
        Host.statusLabel.Text = Localization.Text("SettingsChanged");
    }

    internal async void ProcessImage(object? sender, EventArgs e)
    {
        if (_editor._sourceImage is null || _editor._activeSource is null || _editor._processing) return;
        if (!TryGetTargetSize(out _, out _)) { UpdateSizeHint(); return; }
        ApplyProfile();
        CommitInputs();
        var settings = CaptureReadyModeSettings();
        DownscaleOptions options = settings.Downscale;
        bool useResult = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null;
        var basis = useResult ? _editor._downscaledResult! : _editor._sourceImage;
        if (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height)
        {
            Host.statusLabel.Text = Localization.Text("ReduceOutputDimensionsTheyCannotExceed");
            return;
        }
        string mode = CurrentModeName;
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Format("ProcessingAction", mode, options.TargetWidth, options.TargetHeight);
        try
        {
            var generation = await Task.Run(() =>
            {
                var result = _downscaler.Process(source, options, Host.CurrentProcessingProgress);
                try
                {
                    return Host._library.SaveGeneration(document, result.Downscaled, result.CroppedSource, new GenerationEntry
                    {
                        Settings = settings,
                        OperationId = ProcessingOperations.Profile,
                        AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Profile, Downscale = options }),
                        ParentGenerationId = parentId,
                        PreservesTransparency = options.SpriteMode || System.Drawing.Image.IsAlphaPixelFormat(result.Downscaled.PixelFormat),
                        Caption = Localization.Format("ResultCaption", result.Downscaled.Width, result.Downscaled.Height, mode),
                        Log = string.Join(Environment.NewLine, result.StageTimingsSeconds.Select(stage => Localization.Format("SFormat", stage.Key, stage.Value))) +
                            Localization.Format("DominantColorFormat", Environment.NewLine, result.DominantColor)
                    });
                }
                finally { result.CroppedSource.Dispose(); result.Downscaled.Dispose(); }
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.statusLabel.Text = Localization.Format("ResultSavedToHistory", generation.Width, generation.Height);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.statusLabel.Text = Localization.Text("ProcessingOrHistorySavingStillProgress");
            TesseraDialog.Show(Host, ex.Message, Localization.Text("ProcessingError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }

    private void CommitInputs()
    {
        foreach (var input in new[] { _editor.widthInput, _editor.heightInput, _editor.gridCellInput, _editor.paletteStepInput, _editor.alphaInput, _editor.quantizationColorsInput, _editor.localColorPassesInput, _editor.threadsInput })
            input.Commit();
    }

    internal DownscaleOptions BuildOptionsFromUi() => new()
    {
        TargetWidth = TryGetTargetSize(out int width, out _) ? width : (int)_editor.widthInput.Value,
        TargetHeight = TryGetTargetSize(out _, out int height) ? height : (int)_editor.heightInput.Value,
        SpriteMode = _editor.spriteInput.IsChecked == true,
        AlphaThreshold = (int)_editor.alphaInput.Value,
        CropHorizontal = (ECropHorizontalAlignment)_editor.cropHorizontalInput.SelectedIndex,
        CropVertical = (ECropVerticalAlignment)_editor.cropVerticalInput.SelectedIndex,
        Palette = _capturingReadyMode && _editor.detailsMode.IsChecked == true
            ? EPaletteKind.None : _editor.paletteInput.SelectedIndex >= 5 ? EPaletteKind.Custom : (EPaletteKind)(_editor.paletteInput.SelectedIndex + 1),
        CustomPalette = SelectedCustomPalette(),
        PaletteStep = (int)_editor.paletteStepInput.Value,
        Quantization = (EQuantizationMethod)_editor.quantizationInput.SelectedIndex,
        QuantizationColors = (int)_editor.quantizationColorsInput.Value,
        IndependentColorMode = _editor.paletteColorModeInput.IsChecked == true
            ? EIndependentColorMode.Palette : EIndependentColorMode.Quantization,
        LocalColorPasses = (int)_editor.localColorPassesInput.Value,
        Cleanup = new ColorCleanupOptions
        {
            ColorTolerance = _editor.localBrightnessInput.Value * .4,
            Strength = _editor.localContrastInput.Value / 100.0,
            DetailProtection = _editor.localDetailGuardInput.IsChecked == true ? new ColorCleanupOptions().DetailProtection : 0,
            EdgeProtection = _editor.localEdgeInput.Value / 100.0
        },
        UseColorWeights = _editor.colorWeightsInput.IsChecked == true,
        ThreadCount = (int)_editor.threadsInput.Value,
        EnableDithering = (!_capturingReadyMode || _editor.spriteInput.IsChecked != true) &&
            (_editor.paletteInput.SelectedIndex is >= 0 and <= 3 or >= 5) && _editor.ditheringInput.IsChecked == true,
        BlockMode = _editor.manualInput.IsChecked == true ? EBlockSelectionMode.Manual : EBlockSelectionMode.Automatic,
        ManualCriteria = _editor.manualInput.IsChecked == true ? new ManualBlockCriteria
        {
            TargetBrightness = _editor.brightnessInput.Value / 100.0,
            TargetContrast = _editor.contrastInput.Value / 100.0,
            TargetSaturation = _editor.saturationInput.Value / 100.0,
            TargetEdge = _editor.edgeInput.Value / 100.0
        } : null
    };

    private EGridAlignmentMode SelectedGridAlignmentMode => _editor.localGridModeInput.IsChecked == true ? EGridAlignmentMode.Local : EGridAlignmentMode.Global;

    internal void UpdateGridAlignmentMode()
    {
        bool local = SelectedGridAlignmentMode == EGridAlignmentMode.Local;
        _editor.gridCellInput.Maximum = local ? 64 : 256;
        _editor.gridCellInput.Value = Math.Min(_editor.gridCellInput.Value, _editor.gridCellInput.Maximum);
        _editor.globalGridHint.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        bool snap = _editor.snapLocalGridInput.IsChecked == true;
        _editor.snapLocalGridInput.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        _editor.localGridHint.Visibility = local && !snap ? Visibility.Visible : Visibility.Collapsed;
        _editor.snappedGridHint.Visibility = local && snap ? Visibility.Visible : Visibility.Collapsed;
        AlignmentSettingsChanged();
    }

    internal void AlignmentSettingsChanged()
    {
        if (_editor.History.SelectionUpdatesPaused || !_editor._resultIsAlignment || _editor._downscaledResult is null) return;
        _editor._resultStale = true;
        _editor.resultCaption.Text = Localization.Format("AlignedGRIDPreviousSettingsFormat", _editor._downscaledResult.Width, _editor._downscaledResult.Height);
        Host.statusLabel.Text = Localization.Text("GridSettingsChanged");
    }

    internal async void AlignImage(object? sender, EventArgs e)
    {
        if (_editor._sourceImage is null || _editor._activeSource is null || _editor._processing) return;
        CommitInputs();
        var settings = CaptureSettings();
        var options = new GridAlignmentOptions
        {
            CellSize = settings.DetectGrid ? null : settings.CellSize,
            Mode = settings.AlignmentMode,
            SnapToGlobalGrid = settings.SnapToGlobalGrid
        };
        string operation = settings.AlignmentMode == EGridAlignmentMode.Global ? ProcessingOperations.Align
            : settings.SnapToGlobalGrid ? ProcessingOperations.AlignLocalSnapped : ProcessingOperations.AlignLocal;
        bool useResult = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null;
        var basis = useResult ? _editor._downscaledResult! : _editor._sourceImage;
        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("AligningPixelGridAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = new PixelGridAligner().Align(source, options, Host.CurrentProcessingProgress);
                return Host._library.SaveGeneration(document, result.Aligned, null, new GenerationEntry
                {
                    OperationId = operation,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = operation, CellSize = options.CellSize }),
                    Settings = settings,
                    IsAlignment = true,
                    PreservesTransparency = true,
                    CellSize = result.CellSize,
                    ParentGenerationId = parentId,
                    Caption = Localization.Format(settings.AlignmentMode == EGridAlignmentMode.Local
                        ? settings.SnapToGlobalGrid ? "SnappedLocalGRIDCellPxFormat"
                            : "LocalGRIDCellPxFormat" : "AlignedGRIDCellPxFormat",
                        result.Aligned.Width, result.Aligned.Height, result.CellSize),
                    Log = settings.AlignmentMode == EGridAlignmentMode.Local
                        ? Localization.Format("LocalAlignmentSFormat", result.ElapsedSeconds, result.CellSize, result.LocalRegionCount)
                            + (settings.SnapToGlobalGrid ? "\n" + Localization.Format("SharedGridSnappingRegionsMovedCellFormat",
                                result.ShiftedRegionCount, result.ConflictingCellCount) : "")
                        : Localization.Format("AlignmentSFormat", result.ElapsedSeconds, result.CellSize)
                });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            Host.statusLabel.Text = Localization.Format("GridAlignmentSaved", generation.CellSize);
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.statusLabel.Text = Localization.Text("AlignmentOrHistorySavingStillProgress");
            TesseraDialog.Show(Host, ex.Message, Localization.Text("GridAlignment"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
