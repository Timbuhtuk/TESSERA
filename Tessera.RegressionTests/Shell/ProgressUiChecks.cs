using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.WpfStyles.Themes;

namespace Tessera.RegressionTests;

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
            var indicator = (ProgressBar)window.FindControl("busyIndicator");
            var panel = (StackPanel)window.FindControl("operationProgressPanel");
            var layout = (Grid)window.FindControl("rootLayout");
            var titleBar = (Border)window.FindControl("titleBar");
            var settings = (Button)window.FindControl("appearanceButton");
            var menu = (Menu)window.FindControl("mainMenu");
            var status = (TextBlock)window.FindControl("statusLabel");
            var notifications = (NotificationDisplay)window.FindControl("notificationDisplay");
            Require(titleBar.IsAncestorOf(indicator) && !titleBar.IsAncestorOf(status),
                "Status text shares the title bar with progress");
            status.Text = "";
            Pump();
            double contentHeight = layout.RowDefinitions[1].ActualHeight;
            Require(!notifications.Cards.Any() && layout.RowDefinitions.Count == 2, "An empty status panel takes up a footer row");
            status.Text = "Operation feedback";
            Pump();
            var statusPanel = notifications.Cards.Single();
            Require(statusPanel.IsVisible && statusPanel.Message.Text == status.Text && Grid.GetRow(notifications) == 1 &&
                statusPanel.Parent is StackPanel { VerticalAlignment: VerticalAlignment.Top } && layout.RowDefinitions[1].ActualHeight == contentHeight,
                "Operation feedback did not open above the content or resized the workspace");
            var countdownDeadline = DateTime.UtcNow.AddSeconds(1);
            while (statusPanel.CountdownScale.ScaleX >= 0.98 && DateTime.UtcNow < countdownDeadline)
            {
                Pump();
                Thread.Sleep(15);
            }
            Require(statusPanel.CountdownScale.ScaleX is > 0 and < 0.98, "Notification countdown is not decreasing");
            status.Text = "Updated operation feedback";
            Require(ReferenceEquals(notifications.Cards.Single(), statusPanel), "Operation updates spawned duplicate cards");
            status.Text = "";
            Require(!indicator.IsIndeterminate && panel.Visibility == Visibility.Collapsed, "Idle progress is visible or indeterminate");
            WindowTestAccess.Invoke(window, "ShowEditor", BindingFlags.Instance | BindingFlags.NonPublic, null);
            using var first = Begin();
            first.Report(.25); Pump();
            Require(indicator.Value == 25, "Title bar progress did not receive actual work");
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
                double menuRight = menu.TranslatePoint(new System.Windows.Point(menu.ActualWidth, 0), titleBar).X;
                double indicatorLeft = indicator.TranslatePoint(new System.Windows.Point(), titleBar).X;
                double settingsLeft = settings.TranslatePoint(new System.Windows.Point(), titleBar).X;
                Require(indicator.ActualWidth == 84 && indicatorLeft >= menuRight && indicatorLeft + indicator.ActualWidth < settingsLeft,
                    "Title bar progress overlaps the menu or settings at minimum width");
                Capture(window, Path.Combine(directory, theme + "-narrow.png"));
                window.Width = 1220; Pump();
            }
            second.Complete(); Pump();
            Require(indicator.Value == 100, "Success did not reach 100%");
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
            WindowTestAccess.Invoke(window, "ShowIconWorkspace", BindingFlags.Instance | BindingFlags.NonPublic, null);
            var icons = (IconWorkspace)window.FindControl("iconWorkspace");
            icons.SetImage(sourceImage, "progress fixture");
            Wait(icons.RefreshFramesAsync());
            Require(indicator.Value == 100, "Actual ICO preview did not complete its progress stream");
            Require(((TextBlock)window.FindControl("statusLabel")).Text == Tessera.Desktop.Localization.Text("IconPreviewsAreReady"),
                "Completed ICO preview retained the running status");
            Wait(icons.SaveAsync(Path.Combine(directory, "progress.ico"), true));
            Require(File.Exists(Path.Combine(directory, "progress.ico")) && indicator.Value == 100, "Actual ICO export did not complete");
            Capture(window, Path.Combine(directory, "icon-export.png"));
            WindowTestAccess.Invoke(window, "ShowBackgroundWorkspace", BindingFlags.Instance | BindingFlags.NonPublic, null);
            var background = (BackgroundWorkspace)window.FindControl("backgroundWorkspace");
            background.SetImage(sourceImage, "progress fixture");
            Wait(background.RefreshPreviewAsync());
            Require(background.HasResult && indicator.Value == 100, "Actual background removal did not complete its progress stream");
            Require(((TextBlock)window.FindControl("statusLabel")).Text == Tessera.Desktop.Localization.Text("BackgroundRemovalIsReady"),
                "Completed background preview retained the running status");
            Wait(background.SaveAsync(Path.Combine(directory, "progress-background.png"), true));
            Require(File.Exists(Path.Combine(directory, "progress-background.png")) && indicator.Value == 100, "Actual PNG export did not complete");
            Capture(window, Path.Combine(directory, "background-removal.png"));
            statusPanel = notifications.Cards.Single();
            var deadline = DateTime.UtcNow.AddSeconds(6);
            bool slidOut = false;
            var offset = (TranslateTransform)statusPanel.RenderTransform;
            while (statusPanel.IsVisible && DateTime.UtcNow < deadline)
            {
                Pump();
                slidOut |= statusPanel.IsVisible && statusPanel.CountdownScale.ScaleX <= 0.01 && offset.X > 2;
                Thread.Sleep(20);
            }
            Require(!statusPanel.IsVisible, "The status notification did not expire");
            Require(!SystemParameters.ClientAreaAnimation || slidOut, "The expired notification did not slide out to its right edge");

            notifications.Show("First", NotificationKind.Success);
            notifications.Show("Second notification", NotificationKind.Warning);
            notifications.Show("Third", NotificationKind.Error);
            Pump();
            var cards = notifications.Cards.ToArray();
            Require(cards.Length == 3 && cards.All(card => card.IsVisible), "Separate notifications did not stack");
            Require(cards.All(card => card.ActualWidth == cards[0].ActualWidth), "Stacked cards have different widths");
            double firstBottom = cards[0].TranslatePoint(new System.Windows.Point(0, cards[0].ActualHeight), notifications).Y;
            double secondTop = cards[1].TranslatePoint(new System.Windows.Point(), notifications).Y;
            Require(secondTop > firstBottom, "Stacked notifications overlap vertically");
            cards[1].DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(2);
            while (cards[1].IsVisible && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(20); }
            Require(notifications.Cards.SequenceEqual(new[] { cards[0], cards[2] }),
                "Closing one card dismissed the other notifications");
            deadline = DateTime.UtcNow.AddSeconds(6);
            while (cards[0].IsVisible && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(20); }
            Require(notifications.Cards.SequenceEqual(new[] { cards[2] }),
                "Automatic dismissal did not preserve the longer-running card");
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
        var content = (Grid)window.FindControl("rootLayout");
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
