using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Tessera.Core.Workflows;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace Tessera.RegressionTests;

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
        using var source = new Bitmap(65, 65);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int q = y * source.Width + x;
            source.SetPixel(x, y, Color.FromArgb(q == 0 ? 128 : 255,
                q & 255, q >> 8, q * 37 & 255));
        }
        var connected = new ConnectivityPaletteOptions { TargetColors = 8 };
        foreach (var mode in new[] { EIndependentColorMode.Quantization, EIndependentColorMode.Palette })
        {
            var options = new DownscaleOptions
            {
                IndependentColorMode = mode, Palette = EPaletteKind.Step, PaletteStep = 1,
                Quantization = EQuantizationMethod.MedianCut
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
        var window = new MainWindow(Path.Combine(folder, "library")) { Width = 1240, Height = 800, ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([path]));
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            var dock = Get<Border>(window, "_workspaceDock");
            var editor = Get<Grid>(window, "workspaceGrid");
            var previewArea = Get<Grid>(window, "previewArea");
            var sourcePreview = Get<PixelPreview>(window, "sourcePreview");
            var resultPreview = Get<PixelPreview>(window, "resultPreview");
            double dockedWidth = previewArea.ActualWidth;
            double dockedSourceWidth = sourcePreview.ActualWidth;
            double dockedResultWidth = resultPreview.ActualWidth;
            Require(Get<MenuItem>(window, "sizeMenuItem").IsChecked, "Default size tool is not marked open");
            Require(window.FindControl("processingMenu") is null && window.FindControl("profileMenuItem") is null &&
                window.FindControl("replaceColorMenuItem") is null, "Removed menu items remain accessible");
            var frame = Get<GroupBox>(window, "frameToolGroup");
            var frameExpander = (Expander)frame.Parent;
            frameExpander.IsExpanded = true; Pump();
            Require(Equals(frameExpander.Header, frame.Header), "Docked frame section lost its caption");
            VerifyToolGroup(frame);
            frameExpander.IsExpanded = false;
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            RequireEmptyDock();
            Require(previewArea.ActualWidth > dockedWidth && sourcePreview.ActualWidth > dockedSourceWidth &&
                resultPreview.ActualWidth > dockedResultWidth, "The previews did not reclaim the empty tool column");
            var sizeWindow = Window.GetWindow(Get<GroupBox>(window, "sizeToolGroup"))!;
            var advanced = Descendants(sizeWindow).OfType<Expander>().Single();
            advanced.IsExpanded = true; Pump();
            Require(VisualTreeHelper.GetChild(frame, 0) is StackPanel section && section.Children.Count == 2 &&
                section.Children[0] is ContentPresenter heading && Equals(heading.Content, frame.Header) &&
                section.Children[1] is ContentPresenter, "Floating frame section should have a heading and no frame");
            Require(DragSizeTool(editor.ActualWidth / 2, commit: false) == DragDropEffects.None &&
                DragSizeTool(editor.ActualWidth - 12, commit: false) == DragDropEffects.Move &&
                DragSizeTool(editor.ActualWidth - 12, commit: true) == DragDropEffects.Move,
                "The floating tool could not dock at the right edge of the editor");
            Pump();
            Require(dock.IsVisible && Math.Abs(previewArea.ActualWidth - dockedWidth) < 1,
                "Redocking the first tool did not restore the right column");
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            var palette = Get<GroupBox>(window, "colorToolGroup");
            Require(Window.GetWindow(palette) == window && Get<MenuItem>(window, "colorMenuItem").IsChecked,
                "Palette did not open in the dock");
            Get<CheckBox>(window, "colorLivePreviewInput").IsChecked = false;
            Get<IntegerInput>(window, "quantizationColorsInput").Value = 2;
            Get<RadioButton>(window, "paletteColorModeInput").IsChecked = true;
            Get<ComboBox>(window, "paletteInput").SelectedIndex = 0;
            var swatches = Get<Tessera.WpfStyles.Controls.PaletteSwatches>(window, "paletteSwatches");
            Require(swatches.Visibility == Visibility.Visible && swatches.ItemsSource is { } colors &&
                colors.Select(color => (color.R << 16) | (color.G << 8) | color.B).SequenceEqual(Palettes.GetPalette(EPaletteKind.DB16)),
                "Selected palette did not populate the library swatches in order");
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
                var db16 = Palettes.GetPalette(EPaletteKind.DB16).ToHashSet();
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
                Get<MenuItem>(window, "colorMenuItem").IsChecked && Get<MenuItem>(window, "smoothingMenuItem").IsChecked,
                "Opening another tool replaced the docked palette");
            var smoothing = Get<GroupBox>(window, "smoothingToolGroup");
            VerifyToolGroup(smoothing);
            var tabs = Get<TabControl>(window, "_workspaceTabs");
            tabs.UpdateLayout();
            var items = tabs.Items.Cast<TabItem>().ToArray();
            Require(items.All(item => Math.Abs(item.ActualWidth - 146) < 1 && Math.Abs(item.ActualHeight - 36) < 1) &&
                Math.Abs(items[0].TranslatePoint(new Point(), tabs).Y - items[1].TranslatePoint(new Point(), tabs).Y) < 1 &&
                items[2].TranslatePoint(new Point(), tabs).Y > items[0].TranslatePoint(new Point(), tabs).Y,
                "Dock tabs did not form aligned rows within the panel");
            var tip = new ToolTip { Content = new string('A', 300), Style = (Style)window.FindResource(typeof(ToolTip)) };
            var selector = tip.ContentTemplateSelector as Tessera.WpfStyles.Controls.WrappedToolTipSelector;
            var wrapped = selector?.SelectTemplate(tip.Content, tip)?.LoadContent() as TextBlock;
            Require(tip.MaxWidth == 400 && wrapped?.TextWrapping == TextWrapping.Wrap && wrapped.MaxWidth == 380 &&
                selector?.SelectTemplate(new Border(), tip) is null,
                $"Text hints do not wrap: max={tip.MaxWidth}, selector={selector is not null}, wrapped={wrapped?.TextWrapping}, textMax={wrapped?.MaxWidth}, rich={selector?.SelectTemplate(new Border(), tip) is null}");
            Capture(window, Path.Combine(folder, "docked-tools.png"));
            double edge = Get<Slider>(window, "localEdgeInput").Value;
            NavigationTest.Click(window, "smoothingMenuItem"); Pump();
            var smoothingWindow = Window.GetWindow(smoothing)!;
            Require(smoothingWindow != window && Get<Slider>(window, "localEdgeInput").Value == edge,
                "Floating smoothing lost its host or setting");
            VerifyToolGroup(smoothing);
            Capture(smoothingWindow, Path.Combine(folder, "floating-smoothing.png"));
            Descendants(smoothingWindow).OfType<Button>().Single(button => Equals(button.Content, "⇤"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Window.GetWindow(smoothing) == window && Get<Slider>(window, "localEdgeInput").Value == edge,
                "Redocking smoothing lost its host or setting");
            VerifyToolGroup(smoothing);
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            Require(Window.GetWindow(palette) is Window floating && floating != window &&
                Get<IDictionary>(window, "_dockedTools").Count == 2 && Get<MenuItem>(window, "colorMenuItem").IsChecked,
                "Second click did not float the existing palette");
            var toolWindow = Window.GetWindow(palette)!;
            var dockButton = Descendants(toolWindow).OfType<Button>().Single(button => Equals(button.Content, "⇤"));
            dockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Window.GetWindow(palette) == window && Get<IDictionary>(window, "_dockedTools").Count == 3,
                "Floating palette did not return to the dock");
            var paletteTab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Tag, "color"));
            var close = ((Grid)paletteTab.Header).Children.OfType<Button>().Single();
            var cross = (System.Windows.Shapes.Path)close.Content;
            var crossCenter = cross.TranslatePoint(new Point(cross.ActualWidth / 2, cross.ActualHeight / 2), close);
            Require(VisualTreeHelper.GetChild(close, 0) is Border { BorderThickness: var outline } && outline == new Thickness() &&
                Math.Abs(crossCenter.X - close.ActualWidth / 2) <= .5 && Math.Abs(crossCenter.Y - close.ActualHeight / 2) <= .5,
                "The dock close button has an outline or an off-center cross");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(!Get<MenuItem>(window, "colorMenuItem").IsChecked && Get<IDictionary>(window, "_dockedTools").Count == 2,
                "Closed palette remains marked open");
            CloseTab("size");
            Require(dock.IsVisible, "The panel disappeared while another tool remained open");
            CloseTab("smoothing");
            RequireEmptyDock();

            window.Width = 900; Pump();
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            Require(dock.IsVisible && editor.RowDefinitions[0].ActualHeight > 0, "The compact panel did not appear");
            CloseTab("size");
            RequireEmptyDock();
            Console.WriteLine("  Palette/dock WPF: recipe replay, tabs, empty layout, float and redock. " + folder);

            void RequireEmptyDock()
            {
                Require(dock.Visibility == Visibility.Collapsed && editor.ColumnDefinitions[1].ActualWidth == 0 &&
                    editor.RowDefinitions[0].ActualHeight == 0 && Math.Abs(previewArea.ActualWidth - editor.ActualWidth) < 1,
                    "The empty tool dock still occupies editor space");
            }

            void CloseTab(string key)
            {
                var tab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Tag, key));
                ((Grid)tab.Header).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
            }

            DragDropEffects DragSizeTool(double x, bool commit)
            {
                var data = new DataObject("Tessera.ToolWidget", "size");
                var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [data, DragDropKeyStates.None, DragDropEffects.Move, editor, new Point(x, 100)], null)!;
                e.RoutedEvent = commit ? DragDrop.PreviewDropEvent : DragDrop.PreviewDragOverEvent;
                editor.RaiseEvent(e);
                return e.Effects;
            }
        }
        finally { window.Close(); Pump(); }
    }

    private static void VerifyToolGroup(GroupBox group)
    {
        Require(VisualTreeHelper.GetChildrenCount(group) == 1 &&
            VisualTreeHelper.GetChild(group, 0) is ContentPresenter presenter && ReferenceEquals(presenter.Content, group.Content),
            $"{group.Header} should show its controls without a frame or repeated heading");
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
        if (owner is FrameworkElement element && element.FindControl(name) is T found) return found;
        return (T)WindowTestAccess.GetField(owner, name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
    private static void Invoke(object owner, string name, params object[] values)
        => WindowTestAccess.Invoke(owner, name, BindingFlags.Instance | BindingFlags.NonPublic, values);
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
