using System.Windows;
using System.Windows.Controls;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorTools
{
    internal int ContourWindowSize => 3 + 2 * Math.Clamp((int)Math.Round((_editor.contourWindowInput.Value - 3) / 2), 0, 6);

    internal void InitializeContourTool() => _editor.contourApplyButton.Click += ApplyContourHeatmap;

    internal void UpdateContourWindowLabel()
        => _editor.contourWindowLabel.Text = Localization.Format("AnalysisWindowFormat", ContourWindowSize);

    internal ContourCleanOptions BuildContourOptions() => new()
    {
        WindowSize = ContourWindowSize,
        Strictness = ContourCleanStrictness.Strong,
        ProtectDetails = _editor.contourDetailGuardInput.IsChecked == true,
        SimilarColorDistance = _editor.contourSimilarityInput.Value,
        MinimumPixelGradient = _editor.contourPixelGradientInput.Value,
        MinimumNeighborGradient = _editor.contourNeighborGradientInput.Value,
        MinimumSimilarNeighbors = _editor.contourNeighborCountInput.Value,
        MinimumAgreement = _editor.contourAgreementInput.Value / 100.0,
        MinimumOpposition = _editor.contourOppositionInput.Value / 100.0
    };

    private void CloseContourTool()
    {
        if (Host._workspaceReady) _editor.Dock.CloseDockedTool("contours");
        else _editor.Dock.FloatingWindow("contours")?.Close();
    }

    private async void ApplyContourHeatmap(object? sender, RoutedEventArgs e)
    {
        if (!_editor.Preview.IsContourToolOpen || _editor._sourceImage is null || _editor._activeSource is null || _editor._processing ||
            _editor.Preview.LivePreviewBasis is not { } basis) return;
        var options = BuildContourOptions();
        var settings = CaptureSettings();
        var document = _editor._activeSource;
        bool useResult = _editor.ProcessingBasis == 1 && _editor._downscaledResult is not null;
        Guid? parentId = useResult ? document.SelectedGeneration?.Id : null;
        using var source = (Bitmap)basis.Clone();
        _editor.Preview.InvalidateContourPreview();
        _editor.SetProcessingState(true);
        Host.statusLabel.Text = Localization.Text("CreatingCandidateMapAction");
        try
        {
            var generation = await Task.Run(() =>
            {
                var analysis = ContourNoiseCleaner.Analyze(source, options, Host.CurrentProcessingProgress);
                using (analysis.Heatmap)
                    return Host._library.SaveGeneration(document, analysis.Heatmap, null, new GenerationEntry
                    {
                        Settings = settings,
                        Operation = Localization.Text("ContourMap"),
                        OperationId = "contours.heatmap",
                        ParentGenerationId = parentId,
                        PreservesTransparency = true,
                        Caption = Localization.Format("ContourMapCaption", analysis.Heatmap.Width, analysis.Heatmap.Height),
                        Log = Localization.Format("CandidatesFormatMessage", analysis.CandidateCount,
                            options.WindowSize, options.SimilarColorDistance, options.MinimumPixelGradient,
                            options.MinimumNeighborGradient, options.MinimumSimilarNeighbors,
                            options.MinimumAgreement * 100, options.MinimumOpposition * 100,
                            Localization.Text(options.ProtectDetails ? "Yes" : "No"))
                    });
            });
            if (!_editor.History.CommitGeneration(document, generation)) return;
            CloseContourTool();
            Host.statusLabel.Text = Localization.Text("CandidateMapSavedHistoryResult");
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            Host.statusLabel.Text = Localization.Text("CouldNotCreateTheCandidateMap");
            TesseraDialog.Show(Host, ex.Message, Localization.Text("ContourMapError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _editor.SetProcessingState(false); }
    }
}
