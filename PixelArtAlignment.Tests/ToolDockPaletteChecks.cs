using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class ToolDockPaletteChecks
{
    public static void Run()
    {
        VerifyRichSource();
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Palette/dock WPF verification timed out");
        if (failure is not null) throw failure;
    }

    private static void VerifyRichSource()
    {
        using (var alpha = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                alpha.SetPixel(x, y, Color.FromArgb(y * 16 + x, 20 + 24 * (x % 8), 40, 80));
            foreach (bool enabled in new[] { false, true })
            {
                using var result = PaletteProcessor.Apply(alpha, new DownscaleOptions
                {
                    IndependentColorMode = IndependentColorMode.Quantization, QuantizationColors = 2
                }, enabled ? new ConnectivityPaletteOptions { TargetColors = 2 } : null);
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                {
                    Require(result.GetPixel(x, y).A == alpha.GetPixel(x, y).A, "Combined palette changed an alpha level");
                    if (alpha.GetPixel(x, y).A == 0)
                        Require(result.GetPixel(x, y).ToArgb() == alpha.GetPixel(x, y).ToArgb(), "Combined palette changed hidden RGB");
                }
            }
        }
        using var source = new Bitmap(65, 65);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int q = y * source.Width + x;
            source.SetPixel(x, y, Color.FromArgb(q == 0 ? 128 : 255,
                q & 255, q >> 8, q * 37 & 255));
        }
        var connected = new ConnectivityPaletteOptions { TargetColors = 8 };
        foreach (var mode in new[] { IndependentColorMode.Quantization, IndependentColorMode.Palette })
        {
            var options = new DownscaleOptions
            {
                IndependentColorMode = mode, Palette = PaletteKind.Step, PaletteStep = 1,
                Quantization = QuantizationMethod.MedianCut
            };
            using var result = PaletteProcessor.Apply(source, options, connected);
            var visible = new HashSet<int>();
            for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                var color = result.GetPixel(x, y);
                Require(color.A == source.GetPixel(x, y).A, "Rich-source palette changed alpha");
                visible.Add(color.ToArgb() & 0xFFFFFF);
            }
            Require(visible.Count is > 0 and <= 8, "Rich-source connected palette missed the target");
        }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "palette-dock-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "input.png");
        using (var image = new Bitmap(8, 4))
        {
            Color[] colors = [Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Cyan, Color.Magenta];
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, Color.FromArgb(x == 0 && y == 0 ? 0 : x == 1 ? 128 : 255,
                    colors[(x + y) % colors.Length]));
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        var window = new MainWindow(Path.Combine(folder, "library")) { Width = 1200, Height = 800, ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([path]));
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            var legacy = new EditorSettings { ConnectedPalette = new ConnectivityPaletteOptions { TargetColors = 3 } };
            Invoke(window, "RestoreSettings", legacy);
            Require(Get<CheckBox>(window, "connectedPaletteInput").IsChecked == false,
                "Old independent-color history enabled connected selection");
            Invoke(window, "RestoreGenerationSettings", legacy, ProcessingOperations.ConnectedPalette);
            Require(Get<CheckBox>(window, "connectedPaletteInput").IsChecked == true &&
                Get<IntegerInput>(window, "quantizationColorsInput").Value == 3,
                "Legacy standalone connected-palette history lost its settings");
            Invoke(window, "RestoreSettings", new EditorSettings());
            Require(Get<MenuItem>(window, "sizeMenuItem").IsChecked, "Default size tool is not marked open");
            Require(window.FindName("processingMenu") is null && window.FindName("profileMenuItem") is null &&
                window.FindName("replaceColorMenuItem") is null, "Removed menu items remain accessible");
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            var palette = Get<GroupBox>(window, "colorToolGroup");
            Require(Window.GetWindow(palette) == window && Get<MenuItem>(window, "colorMenuItem").IsChecked,
                "Palette did not open in the dock");
            Get<CheckBox>(window, "colorLivePreviewInput").IsChecked = false;
            Get<IntegerInput>(window, "quantizationColorsInput").Value = 2;
            Get<RadioButton>(window, "paletteColorModeInput").IsChecked = true;
            Get<ComboBox>(window, "paletteInput").SelectedIndex = 0;
            Get<CheckBox>(window, "connectedPaletteInput").IsChecked = true;
            Get<Button>(window, "colorOnlyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Get<bool>(window, "_processing") && Get<SourceEntry>(window, "_activeSource").Generations.Count > 0);
            var generation = Get<SourceEntry>(window, "_activeSource").SelectedGeneration!;
            var step = generation.RecipeSnapshot!.Steps.Single();
            Require(step.OperationId == ProcessingOperations.Colors && step.ConnectedPalette?.TargetColors == 2,
                "Connected selection was not saved in the recipe");
            Require(Get<CheckBox>(window, "connectedPaletteInput").IsChecked == true &&
                generation.Settings.ConnectedPalette?.TargetColors == 2,
                "Connected settings were lost after saving the result");
            using (var source = new Bitmap(path))
            using (var actual = new Bitmap(Get<ImageLibrary>(window, "_library").ResultPath(Get<SourceEntry>(window, "_activeSource"), generation)))
            using (var replay = new WorkflowExecutor().Execute(source, WorkflowJson.Snapshot(generation.RecipeSnapshot!)))
            {
                var db16 = Palettes.GetPalette(PaletteKind.DB16).ToHashSet();
                var visible = new HashSet<int>();
                for (int y = 0; y < actual.Height; y++)
                for (int x = 0; x < actual.Width; x++)
                {
                    var color = actual.GetPixel(x, y);
                    Require(color.A == source.GetPixel(x, y).A, "Palette changed alpha");
                    Require(color.ToArgb() == replay.GetPixel(x, y).ToArgb(), "Connected recipe replay differs");
                    if (color.A > 0) { int rgb = color.ToArgb() & 0xFFFFFF; Require(db16.Contains(rgb), "Output escaped DB16"); visible.Add(rgb); }
                }
                Require(visible.Count is > 0 and <= 2, "Connected palette ignored the color target");
            }
            Capture(window, Path.Combine(folder, "palette.png"));

            NavigationTest.Click(window, "smoothingMenuItem"); Pump();
            Require(Get<IDictionary>(window, "_dockedTools").Count == 3 &&
                Get<MenuItem>(window, "openToolsMenu").Items.Count == 3,
                "Opening another tool replaced the docked palette");
            var tabs = Get<TabControl>(window, "_workspaceTabs");
            tabs.UpdateLayout();
            var items = tabs.Items.Cast<TabItem>().ToArray();
            Require(items.All(item => Math.Abs(item.ActualWidth - 146) < 1 && Math.Abs(item.ActualHeight - 36) < 1) &&
                Math.Abs(items[0].TranslatePoint(new Point(), tabs).Y - items[1].TranslatePoint(new Point(), tabs).Y) < 1 &&
                items[2].TranslatePoint(new Point(), tabs).Y > items[0].TranslatePoint(new Point(), tabs).Y,
                "Dock tabs did not form aligned rows within the panel");
            var tip = new ToolTip { Content = new string('A', 300), Style = (Style)window.FindResource(typeof(ToolTip)) };
            var selector = tip.ContentTemplateSelector as WrappedToolTipSelector;
            var wrapped = selector?.SelectTemplate(tip.Content, tip)?.LoadContent() as TextBlock;
            Require(tip.MaxWidth == 400 && wrapped?.TextWrapping == TextWrapping.Wrap && wrapped.MaxWidth == 380 &&
                selector?.SelectTemplate(new Border(), tip) is null,
                $"Text hints do not wrap: max={tip.MaxWidth}, selector={selector is not null}, wrapped={wrapped?.TextWrapping}, textMax={wrapped?.MaxWidth}, rich={selector?.SelectTemplate(new Border(), tip) is null}");
            Capture(window, Path.Combine(folder, "docked-tools.png"));
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            Require(Window.GetWindow(palette) is Window floating && floating != window &&
                Get<IDictionary>(window, "_dockedTools").Count == 2 && Get<MenuItem>(window, "colorMenuItem").IsChecked,
                "Second click did not float the existing palette");
            var toolWindow = Window.GetWindow(palette)!;
            var dockButton = Descendants(toolWindow).OfType<Button>().Single(button => Equals(button.Content, "⇤"));
            dockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Window.GetWindow(palette) == window && Get<IDictionary>(window, "_dockedTools").Count == 3,
                "Floating palette did not return to the dock");
            Invoke(window, "CloseDockedTool", "color", false); Pump();
            Require(!Get<MenuItem>(window, "colorMenuItem").IsChecked && Get<IDictionary>(window, "_dockedTools").Count == 2,
                "Closed palette remains marked open");
            NavigationTest.Click(window, "finalistMenuItem"); Pump();
            tabs = Get<TabControl>(window, "_workspaceTabs");
            var tcrTab = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, "finalists"));
            Require(Descendants(tcrTab.Header as DependencyObject ?? tcrTab).OfType<TextBlock>().Any(text => text.Text == "TCR"),
                "Docked TCR tab lost its compact title");
            var factor = Get<ComboBox>(window, "_finalistFactor");
            var row = factor.Parent as DockPanel;
            Require(row is not null && row.Children.OfType<TextBlock>().Single().ActualWidth == 132 &&
                factor.ActualWidth > 0 && factor.ActualWidth < row.ActualWidth,
                "TCR factor is not aligned beside its label in the narrow dock");
            Capture(window, Path.Combine(folder, "tcr-docked.png"));
            Console.WriteLine("  Palette/dock WPF: connected DB16, alpha, recipe replay, tabs, float, redock, menu tracking. " + folder);
        }
        finally { window.Close(); Pump(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (int q = 0; q < VisualTreeHelper.GetChildrenCount(item); q++)
        {
            var child = VisualTreeHelper.GetChild(item, q); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static T Get<T>(object owner, string name)
    {
        if (owner is FrameworkElement element && element.FindName(name) is T found) return found;
        return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    }
    private static void Invoke(object owner, string name, params object[] values)
        => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, values);
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "WPF operation timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
