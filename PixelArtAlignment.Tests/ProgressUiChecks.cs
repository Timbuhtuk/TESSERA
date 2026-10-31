using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;

namespace PixelArtAlignment.Tests;

internal static class ProgressUiChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string directory = Path.GetFullPath(Path.Combine("artifacts", "wpf", "progress"));
        Directory.CreateDirectory(directory);
        var window = new MainWindow(Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N")))
        { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            var indicator = (ProgressBar)window.FindName("busyIndicator");
            var panel = (StackPanel)window.FindName("operationProgressPanel");
            var label = (TextBlock)window.FindName("progressPercent");
            Require(!indicator.IsIndeterminate && panel.Visibility == Visibility.Collapsed, "Idle progress is visible or indeterminate");
            using var first = Begin();
            first.Report(.25); Pump();
            Require(indicator.Value == 25 && label.Text == "25%", "The footer did not receive actual work");
            using var second = Begin();
            second.Report(.5); first.Complete(); Pump();
            Require(indicator.Value == 50, "A late callback replaced a newer operation");
            Thread.Sleep(55); second.Report(.1); Pump();
            Require(indicator.Value == 50, "Progress regressed");
            foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light, TesseraTheme.Contrast })
            {
                AppearanceResources.Apply(theme); Pump(); indicator.ApplyTemplate();
                var canvas = (Canvas)indicator.Template.FindName("CellsCanvas", indicator);
                Require(canvas.Children.Count == 40, "The NuGet Cells template did not produce 20 cells");
                int filled = canvas.Children.OfType<Border>().Where((_, q) => q % 2 == 1).Count(b => b.Width > 0);
                Require(filled == 10, "Cells fill does not represent 50% completion");
                Capture(window, Path.Combine(directory, theme + "-50.png"));
                window.Width = 560; Pump();
                Require(indicator.Width == 180 && panel.ActualWidth <= window.ActualWidth - 36, "Footer clips at minimum width");
                Capture(window, Path.Combine(directory, theme + "-narrow.png"));
                window.Width = 1220; Pump();
            }
            second.Complete(); Pump();
            Require(indicator.Value == 100 && label.Text == "100%", "Success did not reach 100%");
            Capture(window, Path.Combine(directory, "completed.png"));
            Thread.Sleep(370); Pump();
            Require(panel.Visibility == Visibility.Collapsed, "Completed progress did not hide");

            var canceled = Begin(); canceled.Report(.3); Pump(); canceled.Dispose(); Pump();
            Require(indicator.Value < 100 && panel.Visibility == Visibility.Collapsed, "Canceled work was shown as success");
            using var old = Begin(); old.Report(.6); Pump(); old.Complete(); Pump();
            Thread.Sleep(180);
            using var replacement = Begin(); replacement.Report(.2); Pump(); Thread.Sleep(190); Pump();
            Require(panel.Visibility == Visibility.Visible && indicator.Value == 20, "Old completion timer hid a new operation");
            replacement.Dispose(); Pump();
            using var foreground = Begin(); foreground.Report(.4); Pump();
            using var preview = Begin(preview: true); preview.Report(.9); preview.Complete(); Pump();
            Require(indicator.Value == 40, "Preview progress overrode an explicit action");
            foreground.Dispose(); Pump();
            using var observed = Begin();
            int updates = 0;
            indicator.ValueChanged += (_, _) => updates++;
            Task.Run(() => { for (int q = 0; q < 10000; q++) observed.Report(q / 10000.0); }).GetAwaiter().GetResult();
            Pump(); observed.Complete(); Pump();
            Require(updates < 15 && indicator.Value == 100, "Progress flooded the dispatcher or lost completion");
            using var sourceImage = new System.Drawing.Bitmap(256, 192);
            using (var graphics = System.Drawing.Graphics.FromImage(sourceImage))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.FillRectangle(System.Drawing.Brushes.Coral, 30, 25, 180, 130);
            }
            typeof(MainWindow).GetMethod("ShowIconWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var icons = (IconWorkspace)window.FindName("iconWorkspace");
            icons.SetImage(sourceImage, "progress fixture");
            Wait(icons.RefreshFramesAsync());
            Require(indicator.Value == 100, "Actual ICO preview did not complete its progress stream");
            Require(((TextBlock)window.FindName("statusLabel")).Text == DomainColorTest.Localization.Text("Предпросмотр иконок готов. Сохраните ICO."),
                "Completed ICO preview retained the running status");
            Wait(icons.SaveAsync(Path.Combine(directory, "progress.ico"), true));
            Require(File.Exists(Path.Combine(directory, "progress.ico")) && indicator.Value == 100, "Actual ICO export did not complete");
            Capture(window, Path.Combine(directory, "icon-export.png"));
            typeof(MainWindow).GetMethod("ShowBackgroundWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var background = (BackgroundWorkspace)window.FindName("backgroundWorkspace");
            background.SetImage(sourceImage, "progress fixture");
            Wait(background.RefreshPreviewAsync());
            Require(background.HasResult && indicator.Value == 100, "Actual background removal did not complete its progress stream");
            Require(((TextBlock)window.FindName("statusLabel")).Text == DomainColorTest.Localization.Text("Результат удаления фона готов. Сохраните PNG или добавьте в историю."),
                "Completed background preview retained the running status");
            Wait(background.SaveAsync(Path.Combine(directory, "progress-background.png"), true));
            Require(File.Exists(Path.Combine(directory, "progress-background.png")) && indicator.Value == 100, "Actual PNG export did not complete");
            Capture(window, Path.Combine(directory, "background-removal.png"));
            Console.WriteLine("  Progress WPF: actual values, 20 cells, three themes, narrow width, stale callbacks, cancel/error, priority, throttling and completion lifecycle passed.");

            OperationProgressSession Begin(bool preview = false) => (OperationProgressSession)typeof(MainWindow)
                .GetMethod("BeginOperationProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [window, null, preview])!;
        }
        finally { window.Close(); Pump(); AppearanceResources.Apply(TesseraTheme.Dark); }
    }

    private static void Capture(MainWindow window, string path)
    {
        Pump(); window.UpdateLayout();
        var content = (Grid)window.FindName("rootLayout");
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output);
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(2); }
        Require(task.IsCompleted, "Workspace progress verification timed out");
        task.GetAwaiter().GetResult(); Pump();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
