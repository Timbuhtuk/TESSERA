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

namespace PixelArtAlignment.Tests;

internal static class FinalistUiChecks
{
    public static void Run() => Run(null);

    public static void Run(string? labRoot)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(labRoot is null ? null : Path.GetFullPath(labRoot)); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2))) throw new Exception("Finalist WPF test timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(string? labRoot)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.GetFullPath(Path.Combine("artifacts", "finalists-ui-verification", Guid.NewGuid().ToString("N")));
        string library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
        var window = new MainWindow(library) { Width = 1000, Height = 800, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            string name = "T_Bowman_UNP.png";
            string input = labRoot is null ? Path.Combine(root, "input.png") : Path.Combine(labRoot, "data/originals", name);
            if (labRoot is null)
            {
                using var sample = new Bitmap(37, 29, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < sample.Height; y++) for (int x = 0; x < sample.Width; x++)
                    sample.SetPixel(x, y, System.Drawing.Color.FromArgb((x * 19 + y * 37) % 256, x * 6, y * 8, 173));
                sample.Save(input, System.Drawing.Imaging.ImageFormat.Png);
            }
            Invoke(window, "LoadImage", input); Pump();
            var menu = (MenuItem)window.FindName("finalistMenuItem");
            Require(((MenuItem)window.FindName("sizeToolsMenu")).Items.Contains(menu), "Tool is absent from Tools/Size and grid");
            NavigationTest.Click(window, "finalistMenuItem"); Pump();
            var group = Get<GroupBox>(window, "_finalistGroup");
            Require(group.IsVisible && Window.GetWindow(group) is { IsVisible: true }, "Finalist tool is not visible in WPF");
            Capture(Window.GetWindow(group)!, Path.Combine(root, "finalist-tool.png"));
            var apply = Get<Button>(window, "_finalistApply");
            var factorInput = Get<ComboBox>(window, "_finalistFactor");
            Require(apply.IsEnabled, "Finalist action is disabled for an image");
            Require(factorInput.SelectedItem is 8, "Default reduction factor is not 8×");

            foreach (var (field, mode, reference) in new[]
            {
                ("_t3Mode", FinalistMode.T3P44, "reports/gallery/assets/candidates/T3/ce3b649abf52/p44/" + name),
                ("_p5Mode", FinalistMode.P5Off, "reports/gallery/assets/candidates/P5/8151a7614f2a/OFF/" + name),
                ("_mixedMode", FinalistMode.HalfAndHalf, "reports/gallery/previews/t3-p44_p5-off_50-50/" + name)
            })
            {
                window.ProcessingBasis = 0;
                Get<RadioButton>(window, field).IsChecked = true;
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Until(() => !Get<bool>(window, "_processing"));
                var generation = Get<SourceEntry>(window, "_activeSource").SelectedGeneration!;
                Require(generation.OperationId == ProcessingOperations.FinalistReduce &&
                    generation.RecipeSnapshot?.Steps.Single().FinalistMode == mode &&
                    generation.RecipeSnapshot?.Steps.Single().FinalistFactor == 8, "Mode or factor was not saved in the result recipe");
                using var original = new Bitmap(input);
                using var expected = labRoot is null ? FinalistReducer.Reduce(original, mode) : new Bitmap(Path.Combine(labRoot, reference));
                using var actual = (Bitmap)Get<Bitmap>(window, "_downscaledResult").Clone();
                Equal(expected, actual);
                var macroStore = new MacroStore(Path.Combine(root, "macros"));
                var macro = macroStore.Save(new MacroDefinition { Name = mode.ToString(), Steps = generation.RecipeSnapshot!.Steps });
                using (var source = new Bitmap(input))
                using (var replay = new WorkflowExecutor().Execute(source, new ProcessingRecipe { Steps = MacroStore.Read(macroStore.PathFor(macro.Id)).Steps })) Equal(expected, replay);
                string export = Path.Combine(root, "ui-" + mode + ".png");
                Invoke(window, "SaveResult", export);
                using var reopened = new Bitmap(export);
                Equal(expected, reopened);
                foreach (var (zoom, label) in new[] { (5, "1x"), (8, "8x") })
                {
                    window.PreviewZoom = zoom; Pump();
                    Require(window.PreviewZoom == zoom, "Preview zoom did not change");
                    Capture(window, Path.Combine(root, "ui-" + mode + "-" + label + ".png"));
                }
                Console.WriteLine("  WPF mode, history, PNG export/reopen, 1×/8× preview: " + mode);
            }
            window.ProcessingBasis = 0;
            factorInput.SelectedItem = 4;
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Get<bool>(window, "_processing"));
            var custom = Get<SourceEntry>(window, "_activeSource").SelectedGeneration!;
            Require(custom.RecipeSnapshot?.Steps.Single().FinalistFactor == 4, "Custom factor was not saved in history");
            var customStore = new MacroStore(Path.Combine(root, "macros-custom"));
            customStore.Save(new MacroDefinition { Name = "TCR 4x", Steps = custom.RecipeSnapshot!.Steps });
            using (var source = new Bitmap(input))
            using (var expected = FinalistReducer.Reduce(source, FinalistMode.HalfAndHalf, 4))
            using (var actual = (Bitmap)Get<Bitmap>(window, "_downscaledResult").Clone())
            using (var replay = new WorkflowExecutor().Execute(source, custom.RecipeSnapshot!))
            {
                Equal(expected, actual);
                Equal(expected, replay);
                string export = Path.Combine(root, "ui-4x.png");
                Invoke(window, "SaveResult", export);
                using var reopened = new Bitmap(export);
                Equal(expected, reopened);
            }
            Require(!factorInput.Items.Contains(1) && !factorInput.Items.Contains(65),
                "Factor selector contains an out-of-range value");
            var history = Get<SourceEntry>(window, "_activeSource").Generations;
            Invoke(window, "SelectGeneration", history.First());
            Require(factorInput.SelectedItem is 8 && Get<RadioButton>(window, "_t3Mode").IsChecked == true,
                "Selecting an older result did not restore its TCR mode/factor");
            Invoke(window, "SelectGeneration", custom);
            Require(factorInput.SelectedItem is 4 && Get<RadioButton>(window, "_mixedMode").IsChecked == true,
                "Selecting a custom result did not restore its TCR mode/factor");
            Console.WriteLine("PASS finalists WPF: " + root);
        }
        finally { window.Close(); Pump(); }
    }

    private static T Get<T>(MainWindow window, string field)
        => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string method, params object[] arguments)
        => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(10); }
        Require(condition(), "WPF finalist action timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Capture(Window window, string path)
    {
        var visual = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Equal(Bitmap first, Bitmap second)
    {
        Require(first.Size == second.Size, "WPF image dimensions differ");
        for (int y = 0; y < first.Height; y++)
        for (int x = 0; x < first.Width; x++)
            Require(first.GetPixel(x, y).ToArgb() == second.GetPixel(x, y).ToArgb(), $"WPF RGBA differs at {x},{y}");
    }
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
