using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PixelArtDownscale;

namespace DomainColorTest;

public partial class MainWindow
{
    private OperationProgressSession? _visibleProgress;
    private OperationProgressSession? _mainProgress;
    private readonly DispatcherTimer _progressHide = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _progressClosed;

    private IProgress<double>? CurrentProcessingProgress => _mainProgress?.Work;

    private void InitializeOperationProgress()
    {
        iconWorkspace.ProgressFactory = (label, preview) => BeginOperationProgress(iconWorkspace, label, preview);
        backgroundWorkspace.ProgressFactory = (label, preview) => BeginOperationProgress(backgroundWorkspace, label, preview);
        asepriteWorkspace.ProgressFactory = (label, preview) => BeginOperationProgress(asepriteWorkspace, label, preview);
        moodboardWorkspace.ProgressFactory = (label, preview) => BeginOperationProgress(moodboardWorkspace, label, preview);
        _progressHide.Tick += (_, _) =>
        {
            _progressHide.Stop();
            _visibleProgress = null;
            RefreshOperationProgressVisibility();
        };
        SizeChanged += (_, _) => busyIndicator.Width = ActualWidth < 760 ? 180 : 240;
        Closed += (_, _) => { _progressClosed = true; _progressHide.Stop(); _mainProgress?.Dispose(); _visibleProgress?.Dispose(); };
    }

    private OperationProgressSession BeginOperationProgress(object owner, string? label = null, bool preview = false)
    {
        bool hidden = owner is FrameworkElement element && !element.IsVisible;
        bool foreground = _visibleProgress is { IsPreview: false, IsFinished: false };
        if (_progressClosed || preview && (hidden || foreground || WorkspaceBusy)) return new OperationProgressSession(Dispatcher);
        var operation = new OperationProgressSession(Dispatcher, ReceiveOperationProgress) { Owner = owner, IsPreview = preview };
        _progressHide.Stop();
        _visibleProgress = operation;
        busyIndicator.Value = 0;
        if (label is not null) statusLabel.Text = label;
        RefreshOperationProgressVisibility();
        return operation;
    }

    private void ReceiveOperationProgress(OperationProgressSession operation, double value, bool? completed)
    {
        if (_progressClosed || !ReferenceEquals(_visibleProgress, operation)) return;
        if (operation.IsPreview && operation.Owner is FrameworkElement { IsVisible: false }) completed = false;
        if (completed == false)
        {
            _visibleProgress = null;
            RefreshOperationProgressVisibility();
            return;
        }
        busyIndicator.Value = Math.Max(busyIndicator.Value, value * 100);
        if (completed == true) _progressHide.Start();
        RefreshOperationProgressVisibility();
    }

    private void RefreshOperationProgressVisibility()
    {
        SetVisible(operationProgressPanel, _visibleProgress is not null);
        SetVisible(busyIndicator, _visibleProgress is not null);
    }

    private void CompleteProcessingProgress() => _mainProgress?.Complete();

    private async Task SaveResultWithProgressAsync(string path)
    {
        if (_processing || _downscaledResult is null) return;
        string output = System.IO.Path.GetFullPath(path);
        string extension = System.IO.Path.GetExtension(output).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || _resultPreservesTransparency && extension != ".png")
            throw new InvalidOperationException(Localization.Text("Выберите формат PNG, JPEG или BMP."));
        EnsureExportPathAllowed(output);
        using var image = (System.Drawing.Bitmap)_downscaledResult.Clone();
        var format = extension is ".jpg" or ".jpeg" ? System.Drawing.Imaging.ImageFormat.Jpeg
            : extension == ".bmp" ? System.Drawing.Imaging.ImageFormat.Bmp : System.Drawing.Imaging.ImageFormat.Png;
        SetProcessingState(true);
        statusLabel.Text = Localization.Text("Сохранение результатов…");
        try
        {
            await Task.Run(() =>
            {
                string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { image.Save(temporary, format); System.IO.File.Move(temporary, output, true); }
                finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
            });
            CompleteProcessingProgress();
            statusLabel.Text = Localization.Format("Сохранено: {0}", output) + (_resultStale ? Localization.Text(" · результат предыдущих настроек") : "");
        }
        finally { SetProcessingState(false); }
    }
}
