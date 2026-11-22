using System.Windows;
using Bitmap = System.Drawing.Bitmap;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    private enum IndependentOperation { Size, Colors, NeighborColors }

    internal void ScaleOnlyImage(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.Size);

    internal void ApplyColorsOnly(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.Colors);

    internal void ApplyNeighborColorsOnly(object? sender, EventArgs e) => RunIndependentOperation(IndependentOperation.NeighborColors);

    internal void InitializeCleanupControls()
    {
        _editor.smoothingToolGroup.Header = Localization.Text("Smoothing");
        var defaults = new ColorCleanupOptions();
        _editor.localBrightnessInput.Value = defaults.ColorTolerance / .4;
        _editor.localContrastInput.Value = defaults.Strength * 100;
        _editor.localDetailGuardInput.IsChecked = defaults.DetailProtection > 0;
        _editor.localEdgeInput.Value = defaults.EdgeProtection * 100;
        _editor.cleanupDescription.Text = Localization.Text("ConsolidatesNearbyShadesUsingNeighboringPixels");
        _editor.neighborColorsButton.Content = Localization.Text("ApplySmoothing");
        _editor.neighborColorsButton.ToolTip = Localization.Text("RemovesColorNoiseConsolidatesNearbyShades");
        _editor.localColorPassesInput.ToolTip = Localization.Text("RepeatsCleanupUpSpecifiedNumberPasses");
        _editor.localBrightnessInput.ToolTip = Localization.Text("MaximumDifferenceBetweenNearbyShades");
        _editor.localContrastInput.ToolTip = Localization.Text("HowStronglyNeighboringPixelsInfluenceColor");
        _editor.localDetailGuardInput.ToolTip = Localization.Text("PreservesThinLinesAndTheirEnds");
        _editor.localEdgeInput.ToolTip = Localization.Text("ProtectsEdgesEstablishedColorRegionsErosion");
        System.Windows.Automation.AutomationProperties.SetName(_editor.localBrightnessInput, Localization.Text("ColorSimilarity"));
        System.Windows.Automation.AutomationProperties.SetName(_editor.localContrastInput, Localization.Text("CleanupStrength"));
        System.Windows.Automation.AutomationProperties.SetName(_editor.localDetailGuardInput, Localization.Text("DetailProtection"));
        System.Windows.Automation.AutomationProperties.SetName(_editor.localEdgeInput, Localization.Text("EdgeProtection"));
        UpdateDependentControls();
    }

    private async void RunIndependentOperation(IndependentOperation operation)
    {
        if (_editor._sourceImage is null || _editor._activeSource is null || _editor._processing) return;
        if (operation == IndependentOperation.Size && !TryGetTargetSize(out _, out _)) { UpdateSizeHint(); return; }
        CommitInputs();
        var settings = CaptureSettings();
        DownscaleOptions options = settings.Downscale;
        bool useResult = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null;
        var basis = useResult ? _editor._downscaledResult! : _editor._sourceImage;
        bool changeSize = operation == IndependentOperation.Size;
        bool neighborColors = operation == IndependentOperation.NeighborColors;
        double? scaleFactor = changeSize && _editor.scaleFactorModeInput.IsChecked == true && TryGetScaleFactor(out double factor) ? factor : null;
        bool enlarged = changeSize && (options.TargetWidth > basis.Width || options.TargetHeight > basis.Height);

        var document = _editor._activeSource;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = operation switch
        {
            IndependentOperation.Size => enlarged ? Localization.Text("UpscalingImageAction") : Localization.Text("ResizingAction"),
            IndependentOperation.NeighborColors => Localization.Text("SmoothingNearbyShadesAction"),
            _ => Localization.Text("ProcessingColorsAction")
        };
        try
        {
            var generation = await Task.Run(() =>
            {
                using var result = operation switch
                {
                    IndependentOperation.Size => IndependentImageProcessor.Scale(source, options, Host.CurrentProcessingProgress),
                    IndependentOperation.NeighborColors => IndependentImageProcessor.ApplyNeighborColors(source, options, Host.CurrentProcessingProgress),
                    _ => PaletteProcessor.Apply(source, options, settings.ConnectedPalette, Host.CurrentProcessingProgress)
                };
                string colorOperation = options.IndependentColorMode switch
                {
                    EIndependentColorMode.Quantization => Localization.Text("Quantization"),
                    EIndependentColorMode.Palette => Localization.Text("Palette"),
                    _ => Localization.Text("Color")
                };
                if (settings.ConnectedPalette is not null && !changeSize && !neighborColors)
                    colorOperation = Localization.Text("PaletteConnectedSelection");
                string label = changeSize ? enlarged ? Localization.Text("Upscale") : Localization.Text("SIZE") :
                    neighborColors ? Localization.Text("SmoothingHeading") : colorOperation.ToUpperInvariant();
                return Host._library.SaveGeneration(document, result, null, new GenerationEntry
                {
                    Settings = settings,
                    OperationId = changeSize ? ProcessingOperations.Resize : neighborColors ? ProcessingOperations.Cleanup : ProcessingOperations.Colors,
                    AppliedRecipe = HistoryRecipes.Single(new ProcessingStep
                    {
                        OperationId = changeSize ? ProcessingOperations.Resize : neighborColors ? ProcessingOperations.Cleanup : ProcessingOperations.Colors,
                        Downscale = scaleFactor is null ? options : null,
                        ResizeFactor = scaleFactor is { } value ? ResizeFactorSettings.From(options, value) : null,
                        ConnectedPalette = !changeSize && !neighborColors ? settings.ConnectedPalette : null
                    }),
                    Operation = changeSize ? enlarged ? Localization.Text("UpscaleLabel") : Localization.Text("Size") :
                        neighborColors ? Localization.Text("Smoothing") : colorOperation,
                    ParentGenerationId = parentId,
                    PreservesTransparency = true,
                    Caption = $"{label} · {result.Width} × {result.Height}",
                    Log = changeSize
                        ? Localization.Format("ResizedFormat", source.Width, source.Height, result.Width, result.Height) + (enlarged ? Localization.Text("UpscaleWithoutSmoothing") : "")
                        : neighborColors
                            ? Localization.Format("NearbyShadeCleanupEStrengthDetailFormat",
                                options.Cleanup!.ColorTolerance, options.Cleanup.Strength * 100, options.Cleanup.DetailProtection * 100,
                                options.Cleanup.EdgeProtection * 100, options.LocalColorPasses)
                            : Localization.Format("SizePreservedFormat", colorOperation, result.Width, result.Height)
                });
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
}
