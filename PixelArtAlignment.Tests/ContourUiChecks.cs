using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class ContourUiChecks
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
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Contour UI checks timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(bool docked, string language)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "contours-" + Guid.NewGuid().ToString("N")));
        string library = Path.Combine(folder, "library"); Directory.CreateDirectory(library);
        new AppearanceSettings { Language = language }.Save(Path.Combine(library, "appearance.json"));
        string path = Path.Combine(folder, "source.png");
        using var source = new Bitmap(9, 9, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.White);
        source.SetPixel(4, 4, Color.Black);
        source.SetPixel(5, 4, Color.FromArgb(12, 12, 12));
        source.SetPixel(4, 5, Color.FromArgb(18, 18, 18));
        source.SetPixel(5, 5, Color.FromArgb(24, 24, 24));
        foreach (int alpha in new[] { 0, 1, 97, 128, 254 }) source.SetPixel(alpha % 5, 0, Color.FromArgb(alpha, 12, 34, 56));
        source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        byte[] original = File.ReadAllBytes(path);
        var window = Open();
        Bitmap? final = null;
        try
        {
            Require(!Get<Button>("contourApplyButton").IsEnabled, "Empty editor permits a contour result");
            if (docked)
            {
                window.EnableWorkspacePrototype();
                Wait(window.ImportWorkspaceFilesAsync([path]));
                Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            }
            else Invoke("LoadImage", path);
            var document = Get<SourceEntry>("_activeSource");
            NavigationTest.Click(window, "colorMenuItem");
            Until(() => Get<PixelPreview>("resultPreview").Image is not null);
            NavigationTest.Click(window, "contoursMenuItem");
            Until(() => Visible());
            Require(document.Generations.Count == 0, "Preview created a history card");
            Require(Get<MenuItem>("colorToolsMenu").Items.Contains(Get<MenuItem>("contoursMenuItem")), "Contours is outside Tools/Color");
            Require(Get<MenuItem>("contoursMenuItem").Header.ToString() == (language == "en" ? "Contours…" : "Контуры…"), "Contour menu is not localized");
            Require(Get<Button>("contourApplyButton").Content.ToString() == (language == "en" ? "Create map result" : "Создать результат-карту"), "Contour action is not localized");
            var controls = Get<Slider>("contourWindowInput");
            Require(!docked || ReferenceEquals(Window.GetWindow(controls), window), "Workspace contours detached from its native panel");
            Require(controls.Minimum == 3, "Contours does not allow the 3x3 window");
            controls.Value = 3;
            Get<IntegerInput>("contourAgreementInput").Value = 0;
            Get<IntegerInput>("contourOppositionInput").Value = 0;
            Get<CheckBox>("contourDetailGuardInput").IsChecked = false;
            WaitForMap(source, new() { WindowSize = 3, ProtectDetails = false, MinimumAgreement = 0, MinimumOpposition = 0 });
            Require(Get<PixelPreview>("resultPreview").Image!.GetPixel(4, 4).ToArgb() == Color.Red.ToArgb(), "Expected direction candidate missing");
            Require(Get<TextBlock>("contourWindowLabel").Text.Contains("3 × 3"), "Analysis window label does not reflect the 3x3 setting");
            foreach (var (name, threshold, originalValue) in new[] {
                ("contourSimilarityInput", 0, 24), ("contourPixelGradientInput", 20000, 120),
                ("contourNeighborGradientInput", 20000, 80), ("contourNeighborCountInput", 224, 2),
                ("contourAgreementInput", 100, 0), ("contourOppositionInput", 100, 0) })
            {
                var old = Get<PixelPreview>("resultPreview").Image;
                Get<IntegerInput>(name).Value = threshold;
                Until(() => Visible() && !ReferenceEquals(Get<PixelPreview>("resultPreview").Image, old));
                Require(Get<PixelPreview>("resultPreview").Image!.GetPixel(4, 4).ToArgb() != Color.Red.ToArgb(), "Threshold did not affect selection: " + name);
                Get<IntegerInput>(name).Value = originalValue;
                WaitForMap(source, new() { WindowSize = 3, ProtectDetails = false, MinimumAgreement = 0, MinimumOpposition = 0 });
            }
            Require(!Get<MenuItem>("saveMenuItem").IsEnabled, "Temporary map can be exported as a saved result");
            Capture(window, Path.Combine(folder, "preview-" + language + ".png"));
            NavigationTest.Click(window, "contourApplyButton");
            Until(() => document.Generations.Count == 1 && !Get<bool>("_processing"));
            var first = document.SelectedGeneration!;
            using var expected = ContourNoiseCleaner.Analyze(source, new() { WindowSize = 3, ProtectDetails = false, MinimumAgreement = 0, MinimumOpposition = 0 }).Heatmap;
            Equal(expected, Get<Bitmap>("_downscaledResult"));
            Require(first.ParentGenerationId is null && window.ProcessingBasis == 1, "Contour result used an unselected input");
            Require(Get<ListBox>("sourceStrip").SelectedItem is null && ReferenceEquals(Get<ListBox>("generationStrip").SelectedItem, first), "Contour result is not the sole selected card");
            Require(!Visible() && Get<Border>("livePreviewFrame").Visibility == Visibility.Collapsed, "Applied result remains a temporary preview");
            string export = Path.Combine(folder, "map.png"); Invoke("SaveResult", export);
            using (var reopened = new Bitmap(export)) Equal(expected, reopened);
            using var basis = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            NavigationTest.Click(window, "contoursMenuItem");
            controls.Value = 5;
            Get<IntegerInput>("contourSimilarityInput").Value = 31;
            Get<IntegerInput>("contourPixelGradientInput").Value = 210;
            Get<IntegerInput>("contourNeighborGradientInput").Value = 125;
            Get<IntegerInput>("contourNeighborCountInput").Value = 224;
            Get<IntegerInput>("contourAgreementInput").Value = 65;
            Get<IntegerInput>("contourOppositionInput").Value = 45;
            Get<CheckBox>("contourDetailGuardInput").IsChecked = false;
            WaitForMap(basis, new() { WindowSize = 5, ProtectDetails = false, SimilarColorDistance = 31, MinimumPixelGradient = 210, MinimumNeighborGradient = 125, MinimumSimilarNeighbors = 224, MinimumAgreement = .65, MinimumOpposition = .45 });
            NavigationTest.Click(window, "contourApplyButton");
            Until(() => document.Generations.Count == 2 && !Get<bool>("_processing"));
            Require(document.SelectedGeneration!.ParentGenerationId == first.Id, "Contour result lost its selected-result parent");
            final = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            window.ProcessingBasis = 0;
            NavigationTest.Click(window, "contoursMenuItem"); Until(() => Visible());
            var preview = Get<PixelPreview>("resultPreview").Image;
            controls.Value = 15;
            NavigationTest.Click(window, "channelMenuItem");
            Until(() => Get<Border>("livePreviewFrame").Visibility == Visibility.Collapsed);
            var until = DateTime.UtcNow.AddMilliseconds(650);
            while (DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Visible() && !ReferenceEquals(Get<PixelPreview>("resultPreview").Image, preview), "Stale contour computation replaced another tool");
            Require(document.Generations.Count == 2, "Tool switch created history");
            window.Close(); window = Open();
            Equal(final, Get<Bitmap>("_downscaledResult"));
            Require(Get<Slider>("contourWindowInput").Value == 5 && Get<IntegerInput>("contourSimilarityInput").Value == 31 && Get<IntegerInput>("contourPixelGradientInput").Value == 210 &&
                Get<IntegerInput>("contourNeighborGradientInput").Value == 125 && Get<IntegerInput>("contourNeighborCountInput").Value == 224 &&
                Get<IntegerInput>("contourAgreementInput").Value == 65 && Get<IntegerInput>("contourOppositionInput").Value == 45 && Get<CheckBox>("contourDetailGuardInput").IsChecked == false,
                "Saved contour settings did not survive reopening");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Contours overwrote its original source");
            Console.WriteLine($"  Contours: docked={docked}, language={language}, selected source/result, map, alpha, history, PNG, restart and tool isolation: {folder}");
        }
        finally { final?.Dispose(); window.Close(); Pump(); }

        MainWindow Open() { var result = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false }; result.Show(); Pump(); return result; }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        bool Visible() => (bool)typeof(MainWindow).GetProperty("ContourPreviewVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void WaitForMap(Bitmap input, ContourCleanOptions options)
        {
            var old = Get<PixelPreview>("resultPreview").Image;
            Until(() => Visible() && !ReferenceEquals(Get<PixelPreview>("resultPreview").Image, old));
            using var analysis = ContourNoiseCleaner.Analyze(input, options).Heatmap;
            Equal(analysis, Get<PixelPreview>("resultPreview").Image!);
        }
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Contour map dimensions changed");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++) Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Contour pixels differ at {x},{y}");
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
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Contour operation timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
