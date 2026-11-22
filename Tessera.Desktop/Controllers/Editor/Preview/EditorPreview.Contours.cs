using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorPreview
{
    private readonly DispatcherTimer _contourPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private int _contourPreviewRevision;

    private bool _contourPreviewBusy;

    internal bool IsContourToolOpen => ActiveLivePreviewTool == "contours";

    private bool CanRenderContourPreview => !AlternativePreview && (IsContourToolOpen || _comparisonLens == "contours");

    private Bitmap? _contourPreviewBitmap, _contourPreviewBasis;

    private string? _contourPreviewCaption;

    internal bool ContourPreviewVisible => _contourPreviewBitmap is not null &&
        ReferenceEquals(_editor.resultPreview.Image, _contourPreviewBitmap);

    internal void InitializeContourPreview()
    {
        _contourPreviewTimer.Tick += async (_, _) =>
        {
            _contourPreviewTimer.Stop();
            await RenderContourPreview(false);
        };
        _editor.contourWindowInput.ValueChanged += (_, _) =>
        {
            int window = _editor.Tools.ContourWindowSize;
            _editor.contourWindowLabel.Text = Localization.Format("AnalysisWindowFormat", window);
            ScheduleContourPreview();
        };
        foreach (var input in new[] { _editor.contourSimilarityInput, _editor.contourPixelGradientInput,
            _editor.contourNeighborGradientInput, _editor.contourNeighborCountInput, _editor.contourAgreementInput, _editor.contourOppositionInput })
            input.ValueChanged += (_, _) => ScheduleContourPreview();
        _editor.Tools.UpdateContourWindowLabel();
        _editor.contourDetailGuardInput.Checked += (_, _) => ScheduleContourPreview();
        _editor.contourDetailGuardInput.Unchecked += (_, _) => ScheduleContourPreview();
        _editor.contourPreviewButton.Click += async (_, _) => await RenderContourPreview(true);
        Host.Closed += (_, _) => ClearContourPreview();
    }

    internal void ScheduleContourPreview()
    {
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
        if (!CanRenderContourPreview || _editor._processing || _editor.History.SelectionUpdatesPaused) { ClearContourPreview(); return; }
        if (!_editor.IsLoaded) return;
        var basis = LivePreviewBasis;
        if (!ReferenceEquals(basis, _contourPreviewBasis))
        {
            ClearContourPreview();
            _contourPreviewBasis = basis;
        }
        _editor.contourApplyButton.IsEnabled = _editor.contourPreviewButton.IsEnabled = basis is not null;
        if (basis is null)
        {
            _editor.contourPreviewStatus.Text = Localization.Text("OpenAnImage");
            return;
        }
        if (IsContourToolOpen && (long)basis.Width * basis.Height > LivePreviewPixelLimit)
        {
            if (ContourPreviewVisible)
            {
                ClearContourPreview();
                _contourPreviewBasis = basis;
            }
            _editor.contourPreviewStatus.Text = Localization.Text("LargeImageClickRefreshPreview");
            return;
        }
        _editor.contourPreviewStatus.Text = Localization.Text("CalculatingMapAction");
        _contourPreviewTimer.Start();
    }

    internal void InvalidateContourPreview()
    {
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
    }

    internal void ClearContourPreview()
    {
        _contourPreviewRevision++;
        _contourPreviewTimer.Stop();
        HideContourPreview();
        _contourPreviewBitmap?.Dispose();
        _contourPreviewBitmap = null;
        _contourPreviewBasis = null;
        _contourPreviewCaption = null;
    }

    internal void UpdateContourComparison()
    {
        if (!CanRenderContourPreview)
        {
            ClearContourPreview();
            return;
        }
        if (!IsLensVisible("contours", IsContourToolOpen))
        {
            HideContourPreview();
            return;
        }
        if (_contourPreviewBitmap is not null && ReferenceEquals(_contourPreviewBasis, LivePreviewBasis))
            ShowContourPreview();
        else if (!IsContourToolOpen)
            _ = RenderContourPreview(false);
    }

    private void ShowContourPreview()
    {
        _editor.resultPreview.Image = _contourPreviewBitmap;
        _editor.livePreviewFrame.Visibility = _editor.livePreviewBadge.Visibility = Visibility.Visible;
        _editor.resultPreview.ToolTip = Localization.Text("MapPreview");
        _editor.resultCaption.Text = _contourPreviewCaption;
        Host.UpdateNavigationState();
    }

    private void HideContourPreview()
    {
        if (!ContourPreviewVisible) return;
        _editor.resultPreview.Image = _livePreviewBitmap ?? _editor._downscaledResult;
        _editor.resultCaption.Text = _editor._activeSource?.SelectedGeneration?.Caption is { } caption
            ? Localization.StoredText(caption) : Localization.Text("Result");
        _editor.resultPreview.ToolTip = ResultPreviewHint;
        _editor.livePreviewFrame.Visibility = _editor.livePreviewBadge.Visibility = _livePreviewBitmap is null ? Visibility.Collapsed : Visibility.Visible;
        Host.UpdateNavigationState();
    }

    private async Task RenderContourPreview(bool manual)
    {
        if (_contourPreviewBusy || _editor._processing || !CanRenderContourPreview || LivePreviewBasis is not { } basis) return;
        if (!manual && IsContourToolOpen && (long)basis.Width * basis.Height > LivePreviewPixelLimit) return;

        int revision = _contourPreviewRevision;
        var options = _editor.Tools.BuildContourOptions();
        using var source = (Bitmap)basis.Clone();
        _contourPreviewBusy = true;
        using var operation = Host.BeginOperationProgress(Host, preview: true);
        ContourAnalysisResult? analysis = null;
        try
        {
            _editor.contourPreviewStatus.Text = Localization.Text("CalculatingMapAction");
            analysis = await Task.Run(() => ContourNoiseCleaner.Analyze(source, options, operation.Work));
            if (revision != _contourPreviewRevision || _editor._processing || !CanRenderContourPreview || !_editor.IsLoaded ||
                !ReferenceEquals(basis, LivePreviewBasis)) return;
            var previous = _contourPreviewBitmap;
            _contourPreviewBitmap = analysis.Heatmap;
            _contourPreviewBasis = basis;
            _contourPreviewCaption = Localization.Format("MAPPreviewFormat", analysis.Heatmap.Width, analysis.Heatmap.Height);
            if (IsLensVisible("contours", IsContourToolOpen)) ShowContourPreview();
            previous?.Dispose();
            _editor.contourPreviewStatus.Text = Localization.Format("CandidatesFormat", analysis.CandidateCount);
            analysis = null;
            operation.Complete();
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            if (revision == _contourPreviewRevision) _editor.contourPreviewStatus.Text = Localization.Format("PreviewUnavailableFormat", ex.Message);
        }
        finally
        {
            analysis?.Heatmap.Dispose();
            _contourPreviewBusy = false;
            if (revision != _contourPreviewRevision) ScheduleContourPreview();
        }
    }

}
