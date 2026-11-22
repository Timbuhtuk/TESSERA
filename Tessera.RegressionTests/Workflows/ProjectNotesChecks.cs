using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace Tessera.RegressionTests;

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
            using var comparisonImage = new Bitmap(129, 128);
            using (var graphics = System.Drawing.Graphics.FromImage(comparisonImage)) graphics.Clear(Color.Teal);
            WindowTestAccess.SetField(window, "_downscaledResult", Private, comparisonImage);
            window.AlternativePreview = true; window.PreviewZoom = 8; Pump();
            var comparison = Get<PixelPreview>("comparisonPreview");
            var originalFlag = typeof(EditorPreview).GetField("_comparisonOriginal", Private)!;
            var originalImage = Get<Bitmap>("_sourceImage");
            CheckComparisonKey(Key.Space);
            NavigationTest.Click(window, "appearanceButton"); Pump();
            var settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            var keyInput = (Button)settings.FindName("comparisonKeyInput");
            Require(Equals(keyInput.Content, docked ? "Space" : "Пробел"), "Settings did not show the localized default comparison key");
            keyInput.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            keyInput.RaiseEvent(KeyEvent(settings, Key.F7));
            Require(Equals(keyInput.Content, "F7") && AppearanceSettings.Load(Path.Combine(library, "appearance.json")).ComparisonPreviewKey == Key.F7,
                "Rebinding the comparison key did not save it in settings");
            settings.Close(); Pump(); comparison.Focus();
            Invoke("ComparisonKeyDown", window, KeyEvent(window, Key.Space));
            Require(ReferenceEquals(comparison.Image, comparisonImage), "The old comparison binding is still active");
            CheckComparisonKey(Key.F7);
            Require(((string)comparison.ToolTip).Contains("F7"), "Comparison hint still shows the old key");
            NavigationTest.Click(window, "appearanceButton"); Pump();
            settings = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            ((Button)settings.FindName("comparisonKeyReset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(AppearanceSettings.Load(Path.Combine(library, "appearance.json")).ComparisonPreviewKey == Key.Space,
                "Reset did not restore Space in settings");
            settings.Close(); Pump(); comparison.Focus();
            foreach (int step in new[] { 0, 8 })
            {
                window.PreviewZoom = step; Pump();
                double resultWidth = comparison.EffectiveZoom * comparisonImage.Width;
                double resultHeight = comparison.EffectiveZoom * comparisonImage.Height;
                originalFlag.SetValue(window.Editor.Preview, true); Invoke("UpdateComparisonPreview"); Pump();
                Require(ReferenceEquals(comparison.Image, originalImage) &&
                    Math.Abs(comparison.EffectiveZoom * originalImage.Width - resultWidth) < 2 &&
                    Math.Abs(comparison.EffectiveZoom * originalImage.Height - resultHeight) < 2,
                    "Alternative preview changed the displayed size between original and result");
                originalFlag.SetValue(window.Editor.Preview, false); Invoke("UpdateComparisonPreview"); Pump();
            }
            var scroll = (ScrollViewer)comparison.Children[0];
            Require(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth > 0, "Comparison did not expose both scrollbars");
            int zoom = window.PreviewZoom;
            Require(!(bool)Invoke("ChangePreviewZoom", comparison, -120, new Point(80, 60), ModifierKeys.Control)!, "Comparison Ctrl+wheel was intercepted for zoom");
            Require(!(bool)Invoke("ChangePreviewZoom", comparison, -120, new Point(80, 60), ModifierKeys.Alt)!, "Comparison Alt+wheel was intercepted for zoom");
            var routed = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            scroll.RaiseEvent(routed);
            Pump();
            Require(routed.Handled && window.PreviewZoom == zoom + 1, "Unmodified wheel did not zoom the comparison preview");
            var corner = ((Grid)((FrameworkElement)scroll.Template.FindName("PART_ScrollContentPresenter", scroll)).Parent).Children.OfType<Border>()
                .Single(border => Grid.GetRow(border) == 1 && Grid.GetColumn(border) == 1);
            Require(corner.Background is SolidColorBrush fill && fill.Color == ((SolidColorBrush)window.FindResource("Ts.Canvas")).Color,
                "Two-scrollbar corner does not use the canvas theme");
            Capture(comparison, Path.Combine(folder, "comparison-scrollbars.png"));

            void CheckComparisonKey(Key key)
            {
                Invoke("ComparisonKeyDown", window, KeyEvent(window, key));
                Require(ReferenceEquals(comparison.Image, originalImage), "The comparison key did not show the source");
                Invoke("ComparisonKeyUp", window, KeyEvent(window, key, up: true));
                Require(ReferenceEquals(comparison.Image, comparisonImage), "Releasing the comparison key did not restore the result");
            }

            window.AlternativePreview = false;
            WindowTestAccess.SetField(window, "_downscaledResult", Private, null);

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
        T Get<T>(string name) => (T)(window.FindControl(name) ?? WindowTestAccess.GetField(window, name, Private))!;
        object? Invoke(string method, params object[] arguments) => WindowTestAccess.Invoke(window, method, Private, arguments);
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
                    string text = (string)((ContentPresenter)dialog.FindName("DialogBody")).Content;
                    Require(text.Contains("65 536") || text.Contains("65,536"), "Large-input warning omitted the threshold");
                    var buttons = (Panel)dialog.FindName("ActionButtons");
                    buttons.Children.OfType<Button>().ElementAt(decision == MessageBoxResult.Yes ? 0 : 1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
    private static KeyEventArgs KeyEvent(Window window, Key key, bool up = false) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        { RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
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
