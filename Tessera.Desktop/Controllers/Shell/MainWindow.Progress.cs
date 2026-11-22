using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessera.Core.Imaging;

namespace Tessera.Desktop;

public partial class MainWindow
{
    private OperationProgressSession? _visibleProgress;

    internal OperationProgressSession? _mainProgress;

    private readonly DispatcherTimer _progressHide = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private bool _progressClosed;

    private bool _settingNotification;

    internal readonly TextBlock statusLabel = new() { Name = "statusLabel" };

    internal IProgress<double>? CurrentProcessingProgress => _mainProgress?.Work;

    private void InitializeOperationProgress()
    {
        InitializeStatusFeedback();
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
        SizeChanged += (_, _) => busyIndicator.Width = Math.Clamp(ActualWidth - MinWidth + 84, 84, 240);
        Closed += (_, _) => { _progressClosed = true; _progressHide.Stop(); _mainProgress?.Dispose(); _visibleProgress?.Dispose(); };
    }

    private void InitializeStatusFeedback()
    {
        RegisterName(statusLabel.Name, statusLabel);
        notificationDisplay.Placement = _appearanceSettings.NotificationAnchor;
        var text = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        text.AddValueChanged(statusLabel, StatusChanged);
        Closed += (_, _) => text.RemoveValueChanged(statusLabel, StatusChanged);

        void StatusChanged(object? sender, EventArgs e)
        {
            if (!_settingNotification) notificationDisplay.UpdateStatus(statusLabel.Text, Localization.Text("Notification"));
        }
    }

    internal void Notify(string message, NotificationKind kind)
    {
        if (kind == NotificationKind.Information)
        {
            statusLabel.Text = message;
            return;
        }

        _settingNotification = true;
        try { statusLabel.Text = message; }
        finally { _settingNotification = false; }
        notificationDisplay.UpdateStatus(null);
        string heading = kind switch
        {
            NotificationKind.Success => "Done",
            NotificationKind.Warning => "Warning",
            NotificationKind.Error => "Error",
            _ => "Notification"
        };
        notificationDisplay.Show(message, kind, Localization.Text(heading));
    }

    internal static NotificationKind BatchNotificationKind(int succeeded, int failed)
    {
        if (failed == 0) return succeeded > 0 ? NotificationKind.Success : NotificationKind.Information;
        return succeeded == 0 ? NotificationKind.Error : NotificationKind.Warning;
    }

    internal OperationProgressSession BeginOperationProgress(object owner, string? label = null, bool preview = false)
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

    internal void CompleteProcessingProgress() => _mainProgress?.Complete();

    internal async Task SaveResultWithProgressAsync(string path)
    {
        if (Editor._processing || Editor._downscaledResult is null) return;
        string output = System.IO.Path.GetFullPath(path);
        string extension = System.IO.Path.GetExtension(output).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp") || Editor._resultPreservesTransparency && extension != ".png")
            throw new InvalidOperationException(Localization.Text("ChoosePNGJPEGOrBMPFormat"));
        EnsureExportPathAllowed(output);
        using var image = (System.Drawing.Bitmap)Editor._downscaledResult.Clone();
        var format = extension is ".jpg" or ".jpeg" ? System.Drawing.Imaging.ImageFormat.Jpeg
            : extension == ".bmp" ? System.Drawing.Imaging.ImageFormat.Bmp : System.Drawing.Imaging.ImageFormat.Png;
        Editor.SetProcessingState(true);
        statusLabel.Text = Localization.Text("SavingResultsAction");
        try
        {
            await Task.Run(() =>
            {
                string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { image.Save(temporary, format); System.IO.File.Move(temporary, output, true); }
                finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
            });
            CompleteProcessingProgress();
            bool stale = Editor._resultStale;
            Notify(Localization.Format("SavedFormat", output) + (stale ? Localization.Text("ResultFromPreviousSettings") : ""),
                stale ? NotificationKind.Warning : NotificationKind.Success);
        }
        finally { Editor.SetProcessingState(false); }
    }
}
