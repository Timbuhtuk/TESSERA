using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private EditorSettings CaptureReadyModeSettings()
    {
        _capturingReadyMode = true;
        try { return CaptureSettings(); }
        finally { _capturingReadyMode = false; }
    }

    internal EditorSettings CaptureSettings() => new()
    {
        Mode = _editor.detailsMode.IsChecked == true ? 2 : _editor.backgroundMode.IsChecked == true ? 0 : 1,
        Downscale = BuildOptionsFromUi(),
        ConnectedPalette = BuildConnectedPaletteOptions(),
        CellSize = _editor.gridCellInput.Value,
        ChannelAdjustment = _channelAdjustment,
        ContourWindowSize = ContourWindowSize,
        ContourStrictness = ContourCleanStrictness.Strong,
        ContourProtectDetails = _editor.contourDetailGuardInput.IsChecked == true,
        ContourSimilarColorDistance = _editor.contourSimilarityInput.Value,
        ContourMinimumPixelGradient = _editor.contourPixelGradientInput.Value,
        ContourMinimumNeighborGradient = _editor.contourNeighborGradientInput.Value,
        ContourMinimumSimilarNeighbors = _editor.contourNeighborCountInput.Value,
        ContourMinimumAgreement = _editor.contourAgreementInput.Value,
        ContourMinimumOpposition = _editor.contourOppositionInput.Value,
        DetectGrid = _editor.detectGridInput.IsChecked == true,
        AlignmentMode = SelectedGridAlignmentMode,
        SnapToGlobalGrid = SelectedGridAlignmentMode == EGridAlignmentMode.Local && _editor.snapLocalGridInput.IsChecked == true,
        AspectLock = _editor.aspectLock.IsChecked == true
    };

    internal void RestoreSettings(EditorSettings settings)
    {
        _editor.History.WithSelectionPaused(() =>
        {
            (settings.Mode == 2 ? _editor.detailsMode : settings.Mode == 0 ? _editor.backgroundMode : _editor.sceneMode).IsChecked = true;
            UpdateModeDescription();
            var options = settings.Downscale;
            _editor.widthInput.Value = options.TargetWidth; _editor.heightInput.Value = options.TargetHeight;
            _editor.spriteInput.IsChecked = options.SpriteMode; _editor.alphaInput.Value = options.AlphaThreshold;
            _editor.cropHorizontalInput.SelectedIndex = Math.Clamp((int)options.CropHorizontal, 0, 2);
            _editor.cropVerticalInput.SelectedIndex = Math.Clamp((int)options.CropVertical, 0, 2);
            _editor.paletteInput.SelectedIndex = Math.Clamp((int)options.Palette - 1, 0, 4);
            RestoreCustomPalette(options);
            _editor.paletteStepInput.Value = options.PaletteStep;
            _editor.quantizationInput.SelectedIndex = Math.Clamp((int)options.Quantization, 0, 2);
            _editor.quantizationColorsInput.Value = options.QuantizationColors;
            var connected = settings.ConnectedPalette;
            connectedPaletteInput.IsChecked = connected is not null;
            connectedConnectivityInput.Value = (int)Math.Round((connected?.ConnectivityWeight ?? .75) * 100);
            connectedTilesInput.IsChecked = (connected?.TileSize ?? 16) != 0;
            connectedTileSizeInput.Value = connected?.TileSize is > 0 ? connected.TileSize : 16;
            connectedLocalInput.Value = (int)Math.Round((connected?.LocalWeight ?? .55) * 100);
            connectedDetailInput.Value = (int)Math.Round((connected?.DetailWeight ?? .66) * 100);
            connectedContextInput.Value = (int)Math.Round((connected?.ContextWeight ?? .3) * 100);
            connectedSpaceInput.SelectedIndex = connected?.DistanceSpace == EPaletteDistanceSpace.CieLab ? 1 : 0;
            _editor.paletteColorModeInput.IsChecked = options.IndependentColorMode == EIndependentColorMode.Palette ||
                options.IndependentColorMode == EIndependentColorMode.Combined && options.Palette != EPaletteKind.None;
            _editor.quantizationColorModeInput.IsChecked = _editor.paletteColorModeInput.IsChecked != true;
            _editor.localColorPassesInput.Value = options.LocalColorPasses;
            var cleanup = options.Cleanup ?? new ColorCleanupOptions();
            _editor.localBrightnessInput.Value = cleanup.ColorTolerance / .4;
            _editor.localContrastInput.Value = cleanup.Strength * 100;
            _editor.localDetailGuardInput.IsChecked = cleanup.DetailProtection > 0;
            _editor.localEdgeInput.Value = cleanup.EdgeProtection * 100;
            _editor.colorWeightsInput.IsChecked = options.UseColorWeights;
            _editor.threadsInput.Value = options.ThreadCount;
            _editor.automaticInput.IsChecked = options.BlockMode != EBlockSelectionMode.Manual;
            _editor.manualInput.IsChecked = options.BlockMode == EBlockSelectionMode.Manual;
            _editor.brightnessInput.Value = (options.ManualCriteria?.TargetBrightness ?? .5) * 100;
            _editor.contrastInput.Value = (options.ManualCriteria?.TargetContrast ?? .5) * 100;
            _editor.saturationInput.Value = (options.ManualCriteria?.TargetSaturation ?? .5) * 100;
            _editor.edgeInput.Value = (options.ManualCriteria?.TargetEdge ?? .5) * 100;
            _editor.aspectLock.IsChecked = settings.AspectLock;
            RestoreChannelAdjustment(settings.ChannelAdjustment);
            _editor.contourWindowInput.Value = settings.ContourWindowSize is >= 3 and <= 15 && settings.ContourWindowSize % 2 == 1 ? settings.ContourWindowSize : 15;
            _editor.contourDetailGuardInput.IsChecked = settings.ContourProtectDetails;
            _editor.contourSimilarityInput.Value = settings.ContourSimilarColorDistance;
            _editor.contourPixelGradientInput.Value = settings.ContourMinimumPixelGradient;
            _editor.contourNeighborGradientInput.Value = settings.ContourMinimumNeighborGradient;
            _editor.contourNeighborCountInput.Value = settings.ContourMinimumSimilarNeighbors;
            _editor.contourAgreementInput.Value = settings.ContourMinimumAgreement;
            _editor.contourOppositionInput.Value = settings.ContourMinimumOpposition;
            (settings.AlignmentMode == EGridAlignmentMode.Local ? _editor.localGridModeInput : _editor.globalGridModeInput).IsChecked = true;
            _editor.snapLocalGridInput.IsChecked = settings.SnapToGlobalGrid;
            UpdateGridAlignmentMode();
            _editor.gridCellInput.Value = settings.CellSize; _editor.detectGridInput.IsChecked = settings.DetectGrid;
            _editor.contourWindowInput.Value = settings.ContourWindowSize is >= 3 and <= 15 && settings.ContourWindowSize % 2 == 1
                ? settings.ContourWindowSize : 15;
            _editor.contourDetailGuardInput.IsChecked = settings.ContourProtectDetails;
            _editor.contourSimilarityInput.Value = settings.ContourSimilarColorDistance;
            _editor.contourPixelGradientInput.Value = settings.ContourMinimumPixelGradient;
            _editor.contourNeighborGradientInput.Value = settings.ContourMinimumNeighborGradient;
            _editor.contourNeighborCountInput.Value = settings.ContourMinimumSimilarNeighbors;
            _editor.contourAgreementInput.Value = settings.ContourMinimumAgreement;
            _editor.contourOppositionInput.Value = settings.ContourMinimumOpposition;
            UpdateDependentControls();
            UpdateConnectedControls();
            _editor.ditheringInput.IsChecked = options.Palette is >= EPaletteKind.DB16 and <= EPaletteKind.GameBoy or EPaletteKind.Custom && options.EnableDithering;
            _editor.profileState.Text = Localization.Text("SelectedResultSettings");
        });
    }
}
