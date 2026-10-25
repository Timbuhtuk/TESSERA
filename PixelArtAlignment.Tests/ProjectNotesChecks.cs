using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class ProjectNotesChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                foreach (bool docked in new[] { false, true }) Verify(docked);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Project notes verification timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(bool docked)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "project-notes-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string small = Fixture("small.png", 64, 32), large = Fixture("large.png", 257, 256), other = Fixture("other.png", 258, 256);
        string library = Path.Combine(folder, "library"); Directory.CreateDirectory(library);
        new AppearanceSettings { Language = docked ? "en" : "ru" }.Save(Path.Combine(library, "appearance.json"));
        var window = new MainWindow(library) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            if (docked) window.EnableWorkspacePrototype();
            window.Show(); Pump(); Open(small);
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            var colorCheck = Get<CheckBox>("colorLivePreviewInput");
            var liveResult = Get<PixelPreview>("resultPreview");
            var liveFrame = Get<Border>("livePreviewFrame");
            Until(() => liveResult.Image is not null && liveFrame.Visibility == Visibility.Visible);
            Require(colorCheck.IsChecked == true, "Small color preview did not auto-enable");
            Require(Get<TextBlock>("livePreviewBadgeText").Text == (docked ? "PREVIEW" : "ПРЕДПРОСМОТР"),
                "Main-pane preview badge is not localized");
            var previous = liveResult.Image;
            Get<IntegerInput>("quantizationColorsInput").Value = 1;
            Until(() => liveResult.Image is { } image && !ReferenceEquals(image, previous));
            var options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            using (var expected = IndependentImageProcessor.ApplyColors(Get<Bitmap>("_sourceImage"), options))
                EqualPixels(expected, liveResult.Image!, "Color live result differs from Apply");
            NavigationTest.Click(window, "smoothingMenuItem"); Pump();
            var smoothCheck = Get<CheckBox>("smoothingLivePreviewInput");
            Until(() => liveResult.Image is not null && liveFrame.Visibility == Visibility.Visible);
            Require(smoothCheck.IsChecked == true, "Small cleanup preview did not auto-enable");
            previous = liveResult.Image;
            Get<Slider>("localContrastInput").Value = 25;
            Until(() => liveResult.Image is { } image && !ReferenceEquals(image, previous));
            options = (DownscaleOptions)Invoke("BuildOptionsFromUi")!;
            using (var expected = IndependentImageProcessor.ApplyNeighborColors(Get<Bitmap>("_sourceImage"), options))
                EqualPixels(expected, liveResult.Image!, "Cleanup live result differs from Apply");
            Require(Get<SourceEntry>("_activeSource").Generations.Count == 0, "Live previews saved history");

            Open(large); NavigationTest.Click(window, "colorMenuItem"); Pump();
            Require(colorCheck.IsChecked != true && smoothCheck.IsChecked != true, "Large input inherited small-input approval");
            Decide(colorCheck, MessageBoxResult.No);
            Require(colorCheck.IsChecked != true && liveResult.Image is null && liveFrame.Visibility == Visibility.Collapsed,
                "Declined large preview remained enabled");
            Decide(colorCheck, MessageBoxResult.Yes);
            try { Until(() => liveResult.Image is not null && liveFrame.Visibility == Visibility.Visible); }
            catch { throw new Exception($"Large color preview: checked={colorCheck.IsChecked}, busy={Get<bool>("_livePreviewBusy")}, tool={Get<string>("_activeLivePreviewTool")}, status={Get<TextBlock>("statusLabel").Text}, image={liveResult.Image is not null}, frame={liveFrame.Visibility}"); }
            Require(colorCheck.IsChecked == true, "Accepted large preview did not enable");
            Open(other); NavigationTest.Click(window, "smoothingMenuItem"); Pump();
            Require(colorCheck.IsChecked != true && smoothCheck.IsChecked != true, "Approval leaked to another large input");
            Decide(smoothCheck, MessageBoxResult.No);
            Require(smoothCheck.IsChecked != true, "Declined large cleanup preview remained enabled");
            Decide(smoothCheck, MessageBoxResult.Yes);
            Until(() => liveResult.Image is not null && liveFrame.Visibility == Visibility.Visible);
            Require(Get<SourceEntry>("_activeSource").Generations.Count == 0 && window.WindowState == WindowState.Normal,
                "Preview/modal changed history or minimized the owner");

            // Drive the same wheel controller as the routed event without physical input.
            using var comparisonImage = new Bitmap(320, 240);
            using (var graphics = System.Drawing.Graphics.FromImage(comparisonImage)) graphics.Clear(Color.Teal);
            typeof(MainWindow).GetField("_downscaledResult", Private)!.SetValue(window, comparisonImage);
            window.AlternativePreview = true; window.PreviewZoom = 8; Pump();
            var comparison = Get<PixelPreview>("comparisonPreview");
            var scroll = (ScrollViewer)comparison.Children[0];
            Require(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth > 0, "Comparison did not expose both scrollbars");
            double before = scroll.VerticalOffset;
            int zoom = window.PreviewZoom;
            Require((bool)Invoke("HandleComparisonWheel", -120, new Point(80, 60), ModifierKeys.Control)!, "Comparison Ctrl+wheel was not handled");
            Pump();
            Require(scroll.VerticalOffset > before && window.PreviewZoom == zoom, "Comparison Ctrl+wheel changed zoom instead of scrolling");
            Require((bool)Invoke("HandleComparisonWheel", 120, new Point(80, 60), ModifierKeys.None)!, "Comparison plain wheel was not handled");
            Pump(); Require(window.PreviewZoom == zoom + 1, "Comparison ordinary wheel did not zoom");
            var routed = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            comparison.RaiseEvent(routed); Pump();
            Require(routed.Handled, "Comparison routed wheel is disconnected from its controller");
            var corner = ((Grid)((FrameworkElement)scroll.Template.FindName("PART_ScrollContentPresenter", scroll)).Parent).Children.OfType<Border>()
                .Single(border => Grid.GetRow(border) == 1 && Grid.GetColumn(border) == 1);
            Require(corner.Background is SolidColorBrush fill && fill.Color == ((SolidColorBrush)window.FindResource("Ts.Canvas")).Color,
                "Two-scrollbar corner does not use the canvas theme");
            Capture(comparison, Path.Combine(folder, "comparison-scrollbars.png"));
            window.AlternativePreview = false;
            typeof(MainWindow).GetField("_downscaledResult", Private)!.SetValue(window, null);

            using var wide = new Bitmap(512, 64);
            using var tall = new Bitmap(32, 128);
            using (var graphics = System.Drawing.Graphics.FromImage(wide)) graphics.Clear(Color.CornflowerBlue);
            using (var graphics = System.Drawing.Graphics.FromImage(tall)) graphics.Clear(Color.Orange);
            var sourcePreview = Get<PixelPreview>("sourcePreview"); var resultPreview = Get<PixelPreview>("resultPreview");
            sourcePreview.Image = wide; resultPreview.Image = tall;
            foreach (int index in new[] { 0, 5, 7 })
            {
                window.PreviewZoom = index; Pump();
                double sourceWidth = sourcePreview.EffectiveZoom * wide.Width, sourceHeight = sourcePreview.EffectiveZoom * wide.Height;
                double resultWidth = resultPreview.EffectiveZoom * tall.Width, resultHeight = resultPreview.EffectiveZoom * tall.Height;
                Require(Math.Abs(sourceWidth / sourceHeight - 8) < .01 && Math.Abs(resultWidth / resultHeight - .25) < .01,
                    "Shared display reference distorted a differing aspect ratio");
                Require(resultWidth <= sourceWidth + 2 && Math.Abs(resultHeight - sourceHeight) < 2,
                    "Tall result does not fit the shared displayed reference");
            }
            window.PreviewZoom = 0; Pump();
            Capture((FrameworkElement)window.Content, Path.Combine(folder, "paired-aspects.png"));
            sourcePreview.Image = Get<Bitmap>("_sourceImage"); resultPreview.Image = null;
            // An in-flight temporary result must not resurrect previews/timers after closure.
            Task pending = (Task)Invoke("RenderLiveToolPreviews")!;
            window.Close(); Wait(pending); Pump();
            Require(liveResult.Image is null && liveFrame.Visibility == Visibility.Collapsed &&
                !Get<DispatcherTimer>("_livePreviewTimer").IsEnabled, "Preview rendering survived closure");
            Console.WriteLine($"  Project notes: docked={docked}, small/large preview decisions, exact tool parity, no history, comparison wheel, differing aspects, corner and closed-window cleanup passed.");
        }
        finally { window.Close(); Pump(); }

        string Fixture(string name, int width, int height)
        {
            string path = Path.Combine(folder, name);
            using var bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb((x + y) % 7 == 0 ? 97 : 255, x % 3 * 100, y % 3 * 100, (x + y) % 2 * 200));
            bitmap.Save(path); return path;
        }
        void Open(string path)
        {
            if (!docked) Invoke("LoadImage", path);
            else
            {
                Wait(window.ImportWorkspaceFilesAsync([path]));
                var asset = window.Workspace!.State.Assets.Single(a => a.Name == Path.GetFileNameWithoutExtension(path) || a.Name == Path.GetFileName(path));
                Wait(window.OpenWorkspaceAssetAsync(asset));
            }
            Pump();
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, Private)!.GetValue(window))!;
        object? Invoke(string method, params object[] arguments) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, arguments);
        void Decide(CheckBox check, MessageBoxResult decision)
        {
            bool seen = false; Exception? problem = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                var dialog = Owned(window).OfType<TesseraDialog>().FirstOrDefault();
                if (dialog is null) return;
                timer.Stop(); seen = true;
                try
                {
                    string text = ((TextBlock)dialog.FindName("dialogMessage")).Text;
                    Require(text.Contains("65 536") || text.Contains("65,536"), "Large-input warning omitted the threshold");
                    var buttons = (StackPanel)dialog.FindName("dialogButtons");
                    buttons.Children.OfType<Button>().Single(button => Equals(button.Tag, decision)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { problem = ex; dialog.Close(); }
            };
            timer.Start();
            try { check.IsChecked = true; }
            finally { timer.Stop(); }
            if (problem is not null) throw problem;
            Require(seen, "Large-input confirmation was not shown"); Pump();
        }
    }

    private static IEnumerable<Window> Owned(Window window)
    {
        foreach (Window child in window.OwnedWindows) { yield return child; foreach (var nested in Owned(child)) yield return nested; }
    }
    private static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(10); }
        Require(ready(), "Project notes asynchronous operation timed out");
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void EqualPixels(Bitmap expected, Bitmap actual, string message)
    {
        Require(expected.Size == actual.Size, message + ": dimensions");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), message + $": {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
