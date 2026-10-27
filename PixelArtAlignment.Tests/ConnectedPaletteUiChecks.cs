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
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class ConnectedPaletteUiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                foreach (bool docked in new[] { false, true })
                    foreach (string language in new[] { "ru", "en" }) Verify(docked, language);
            }
            catch (Exception ex) { Console.WriteLine(ex); failure = ex; }
            finally { Localization.Initialize("ru"); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(100))) throw new Exception("Connected palette UI checks timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(bool docked, string language)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "connected-" + Guid.NewGuid().ToString("N")));
        string library = Path.Combine(folder, "library"); Directory.CreateDirectory(library);
        new AppearanceSettings { Language = language }.Save(Path.Combine(library, "appearance.json"));
        string path = Path.Combine(folder, "source.png");
        using var source = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                source.SetPixel(x, y, Color.FromArgb(x < 16 ? 255 : 127, 32 + 56 * (x / 8), 32 + 56 * (y / 8), 100));
        source.SetPixel(0, 0, Color.FromArgb(0, 12, 34, 56));
        source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        byte[] original = File.ReadAllBytes(path);
        var window = Open();
        try
        {
            Require(!Get<Button>("connectedApplyButton").IsEnabled, "Empty editor permits palette processing");
            if (docked)
            {
                window.EnableWorkspacePrototype();
                Wait(window.ImportWorkspaceFilesAsync([path]));
                Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            }
            else Invoke("LoadImage", path);
            var document = Get<SourceEntry>("_activeSource");
            NavigationTest.Click(window, "connectedPaletteMenuItem");
            Require(Get<MenuItem>("connectedPaletteMenuItem").Header.ToString() ==
                (language == "en" ? "Connected palette…" : "Связная палитра…"), "Menu translation missing");
            Get<IntegerInput>("connectedColorsInput").Value = 8;
            Until(() => Visible() && Get<PixelPreview>("resultPreview").Image is { } image && Colors(image) == 8);
            Require(document.Generations.Count == 0 && !Get<MenuItem>("saveMenuItem").IsEnabled, "Preview entered history/export");
            Get<IntegerInput>("connectedConnectivityInput").Value = 25;
            Get<IntegerInput>("connectedTileSizeInput").Value = 16;
            Get<CheckBox>("connectedTilesInput").IsChecked = false;
            Get<IntegerInput>("connectedLocalInput").Value = 65;
            Get<IntegerInput>("connectedDetailInput").Value = 80;
            Get<IntegerInput>("connectedContextInput").Value = 15;
            Get<ComboBox>("connectedSpaceInput").SelectedIndex = 1;
            Get<IntegerInput>("connectedColorsInput").Value = 3;
            var options = new ConnectivityPaletteOptions { TargetColors = 3, ConnectivityWeight = .25,
                TileSize = 0, LocalWeight = .65, DetailWeight = .8, ContextWeight = .15,
                DistanceSpace = PaletteDistanceSpace.CieLab };
            using var expected = ConnectivityPaletteReducer.Process(source, options).Image;
            Until(() => Visible() && Get<PixelPreview>("resultPreview").Image is { } image && Same(expected, image));
            Capture(window, Path.Combine(folder, "preview-" + language + ".png"));
            NavigationTest.Click(window, "connectedApplyButton");
            Until(() => document.Generations.Count == 1 && !Get<bool>("_processing"));
            Require(Same(expected, Get<Bitmap>("_downscaledResult")), "Saved result differs from preview");
            Require(document.SelectedGeneration!.ParentGenerationId is null, "First result has incorrect ancestry");
            var recipe = document.SelectedGeneration.RecipeSnapshot;
            Require(recipe is { Steps.Count: 1 } && recipe.Steps[0].OperationId == ProcessingOperations.ConnectedPalette,
                "Connected palette result cannot create a macro");
            var store = new MacroStore(library);
            var macro = store.Save(new MacroDefinition { Name = "Palette replay", Steps = recipe!.Steps });
            var restored = MacroStore.Read(store.PathFor(macro.Id));
            using (var replay = new WorkflowExecutor().Execute(source, new ProcessingRecipe { Steps = restored.Steps }))
                Require(Same(expected, replay), "Saved palette macro lost settings or RGBA pixels");
            string export = Path.Combine(folder, "result.png"); Invoke("SaveResult", export);
            using (var saved = new Bitmap(export)) Require(Same(expected, saved), "Export changed RGBA pixels");
            var parent = document.SelectedGeneration.Id;
            NavigationTest.Click(window, "connectedPaletteMenuItem");
            Get<IntegerInput>("connectedColorsInput").Value = 2;
            Until(() => Visible() && Get<PixelPreview>("resultPreview").Image is { } image && Colors(image) == 2);
            NavigationTest.Click(window, "connectedApplyButton");
            Until(() => document.Generations.Count == 2 && !Get<bool>("_processing"));
            Require(document.SelectedGeneration!.ParentGenerationId == parent, "Result processing lost selected ancestry");
            using var final = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            NavigationTest.Click(window, "connectedPaletteMenuItem");
            Get<IntegerInput>("connectedColorsInput").Value = 1;
            NavigationTest.Click(window, "contoursMenuItem");
            Until(() => !(bool)typeof(MainWindow).GetProperty("ConnectedPreviewVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
            var end = DateTime.UtcNow.AddMilliseconds(700);
            while (DateTime.UtcNow < end) { Pump(); Thread.Sleep(5); }
            Require(document.Generations.Count == 2, "Tool switch created history");
            window.Close(); window = Open();
            Require(Same(final, Get<Bitmap>("_downscaledResult")), "Restart lost saved result");
            Require(Get<IntegerInput>("connectedColorsInput").Value == 2 &&
                Get<IntegerInput>("connectedConnectivityInput").Value == 25 &&
                Get<IntegerInput>("connectedTileSizeInput").Value == 16 &&
                Get<CheckBox>("connectedTilesInput").IsChecked == false &&
                Get<IntegerInput>("connectedLocalInput").Value == 65 &&
                Get<IntegerInput>("connectedDetailInput").Value == 80 &&
                Get<IntegerInput>("connectedContextInput").Value == 15 &&
                Get<ComboBox>("connectedSpaceInput").SelectedIndex == 1, "Settings did not survive reopening");
            Require(original.SequenceEqual(File.ReadAllBytes(path)), "Source PNG changed");
            Console.WriteLine($"  Connected palette: {language}, docked={docked}; preview, all controls, source/result, PNG, restart and isolation: {folder}");
        }
        finally { window.Close(); Pump(); }

        MainWindow Open() { var result = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false }; result.Show(); Pump(); return result; }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        bool Visible() => (bool)typeof(MainWindow).GetProperty("ConnectedPreviewVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    }

    private static int Colors(Bitmap image)
    {
        var colors = new HashSet<int>();
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            if (image.GetPixel(x, y).A > 0) colors.Add(image.GetPixel(x, y).ToArgb() & 0xFFFFFF);
        return colors.Count;
    }
    private static bool Same(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
        return true;
    }
    private static void Capture(Window window, string path)
    {
        var visual = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(18);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Connected palette operation timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
