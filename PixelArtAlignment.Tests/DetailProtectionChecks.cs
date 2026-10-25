using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Color = System.Drawing.Color;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class DetailProtectionChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Detail protection on/off changes cleanup and survives recipe serialization", VerifyProcessing);
        check("Detail protection checkbox maps history, preserves edge settings and renders RU/EN", VerifyUi);
    }

    private static void VerifyProcessing()
    {
        using var source = Fixture();
        using var disabled = IndependentImageProcessor.ApplyNeighborColors(source, Options(0));
        using var enabled = IndependentImageProcessor.ApplyNeighborColors(source, Options(new ColorCleanupOptions().DetailProtection));
        Require(disabled.GetPixel(3, 3).ToArgb() != source.GetPixel(3, 3).ToArgb(), "Disabled sample did not clean the thin line");
        Require(enabled.GetPixel(3, 3).ToArgb() == source.GetPixel(3, 3).ToArgb(), "Enabled protection erased the thin line");
        foreach (double protection in new[] { 0, new ColorCleanupOptions().DetailProtection })
        {
            var recipe = new ProcessingRecipe { Steps = [new() { OperationId = ProcessingOperations.Cleanup, Downscale = Options(protection) }] };
            var restored = JsonSerializer.Deserialize<ProcessingRecipe>(JsonSerializer.Serialize(recipe, WorkflowJson.Options), WorkflowJson.Options)!;
            Require(restored.Steps[0].Downscale!.Cleanup!.DetailProtection == protection, "Recipe changed protection");
            using var result = new WorkflowExecutor().Execute(source, restored);
            using var expected = IndependentImageProcessor.ApplyNeighborColors(source, Options(protection));
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
            {
                Require(result.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb(), "Macro differs from direct cleanup");
                Require(result.GetPixel(x, y).A == source.GetPixel(x, y).A, "Cleanup changed alpha");
            }
        }
    }

    private static DownscaleOptions Options(double protection) => new()
    {
        LocalColorPasses = 4,
        Cleanup = new() { ColorTolerance = 12, Strength = 1, DetailProtection = protection, EdgeProtection = 0 }
    };

    private static Bitmap Fixture()
    {
        var source = new Bitmap(7, 7, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 7; y++) for (int x = 0; x < 7; x++)
            source.SetPixel(x, y, Color.FromArgb(128, 100, 100, 100));
        for (int y = 1; y <= 5; y++) source.SetPixel(3, y, Color.FromArgb(128, 105, 105, 105));
        return source;
    }

    private static void VerifyUi()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (string language in new[] { "ru", "en" }) VerifyWindow(language);
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Detail protection UI timed out");
        if (failure is not null) throw new Exception("Detail protection UI failed", failure);
    }

    private static void VerifyWindow(string language)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "detail-protection", language));
        Directory.CreateDirectory(folder);
        string library = Path.Combine(folder, "library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(library);
        new AppearanceSettings { Language = language }.Save(Path.Combine(library, "appearance.json"));
        var window = new MainWindow(library)
            { ShowActivated = false, ShowInTaskbar = false, Width = 1400, Height = 1000, Left = -10000, Top = 20, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            Localization.Initialize(language); window.Show(); Pump();
            string source = Path.Combine(folder, "source.png");
            using (var fixture = Fixture()) fixture.Save(source, System.Drawing.Imaging.ImageFormat.Png);
            Invoke(window, "LoadImage", source); NavigationTest.Click(window, "smoothingMenuItem"); Pump();
            var toggle = Get<CheckBox>(window, "localDetailGuardInput");
            Require(toggle.IsChecked == true && toggle.IsVisible && toggle.IsThreeState == false, "Detail switch is not enabled and binary");
            Require(toggle.Content?.ToString() == (language == "en" ? "Detail protection" : "Защита деталей"), "Detail switch is not translated");
            Require(System.Windows.Automation.AutomationProperties.GetName(toggle) == toggle.Content?.ToString(), "Detail switch accessibility name is not translated");
            Require(window.FindName("localSaturationInput") is null, "Old detail slider remains");
            var edge = Get<Slider>(window, "localEdgeInput"); edge.Value = 25;
            toggle.IsChecked = false;
            var off = (DownscaleOptions)Invoke(window, "BuildOptionsFromUi")!;
            Require(off.Cleanup!.DetailProtection == 0 && off.Cleanup.EdgeProtection == .25, "Off mapping or edge setting is wrong");
            toggle.IsChecked = true;
            var on = (DownscaleOptions)Invoke(window, "BuildOptionsFromUi")!;
            Require(on.Cleanup!.DetailProtection == new ColorCleanupOptions().DetailProtection && on.Cleanup.EdgeProtection == .25, "On mapping changed the default or edge setting");
            foreach (double oldValue in new[] { 0, .4, 1 })
            {
                Invoke(window, "RestoreSettings", new EditorSettings { Downscale = Options(oldValue) });
                Require(toggle.IsChecked == (oldValue > 0), "Old history did not restore protection state");
            }
            toggle.IsChecked = false; Settle();
            var uncheckedPixels = Pixels(Render(toggle));
            toggle.IsChecked = true; Settle();
            Require(!uncheckedPixels.SequenceEqual(Pixels(Render(toggle))), "Checked and unchecked states look identical");
            var panel = Get<GroupBox>(window, "smoothingToolGroup"); panel.UpdateLayout();
            Require(panel.ActualWidth > 200 && panel.ActualHeight > 100, "Cleanup panel did not render");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(panel)));
            using var file = File.Create(Path.Combine(folder, "cleanup.png")); encoder.Save(file);
        }
        finally { window.Close(); Pump(); Localization.Initialize("ru"); }
    }

    private static T Get<T>(MainWindow window, string name) where T : class
        => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
    private static object? Invoke(MainWindow window, string name, params object[] args)
        => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(drawing);
        return bitmap;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }
    private static void Settle()
    {
        for (int q = 0; q < 20; q++) { Pump(); Thread.Sleep(10); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
