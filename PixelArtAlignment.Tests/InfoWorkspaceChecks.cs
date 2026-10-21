using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DomainColorTest;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class InfoWorkspaceChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(35))) throw new Exception("Image information verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw new Exception("Image information verification failed", failure); }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "image-info-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "rgba.png");
        using (var image = new Bitmap(4, 2))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Black);
            image.SetPixel(0, 0, Color.FromArgb(0, 19, 29, 39));
            image.SetPixel(1, 0, Color.FromArgb(128, 255, 0, 0));
            image.SetPixel(2, 0, Color.Red); image.SetPixel(3, 0, Color.Lime);
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        byte[] original = File.ReadAllBytes(path);
        var window = new MainWindow(Path.Combine(folder, "library")) { ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([path]));
            var asset = window.Workspace!.State.Assets.Single();
            Wait(window.OpenWorkspaceAssetAsync(asset));
            NavigationTest.Click(window, "infoMenuItem");
            Until(() => Get<TextBlock>(window, "infoSourceSummary").Text.Contains("4 × 2") && Get<ItemsControl>(window, "infoPaletteList").Items.Count == 3,
                "Docked information does not see the image opened in the workspace editor");
            Require(Window.GetWindow(Get<TextBlock>(window, "infoSourceSummary")) == window, "Information did not use the docked workspace");
            Require(Get<TextBlock>(window, "infoResultSummary").Text == "Пока нет результата", "Source-only image pretends to have a result");
            string details = Get<TextBlock>(window, "infoSourceDetails").Text;
            Require(details.Contains("2 : 1") && details.Contains("32") && details.Contains("RGB") && details.Contains("Прозрачные: 1") &&
                details.Contains("Полупрозрачные: 1") && details.Contains("Непрозрачные: 6"), "Resolution, pixel layout or alpha statistics are incorrect: " + details);

            Get<IntegerInput>(window, "widthInput").Value = 8;
            Get<IntegerInput>(window, "heightInput").Value = 4;
            NavigationTest.Click(window, "scaleOnlyButton");
            Until(() => !Field<bool>(window, "_processing"), "Resize did not finish");
            var source = Field<SourceEntry>(window, "_activeSource");
            var parent = source.SelectedGeneration!;
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 1;
            Until(() => Get<TextBlock>(window, "infoResultSummary").Text.Contains("8 × 4") &&
                ReferenceEquals(Field<Bitmap>(window, "_infoBasisShown"), Field<Bitmap>(window, "_downscaledResult")), "Result information or selected basis did not update");
            PaletteButton(window, Color.Red).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var dialog = window.OwnedWindows.OfType<ColorReplaceWindow>().Single();
            Require(dialog.Owner == window, "Docked replacement dialog has the wrong owner");
            Get<TextBox>(dialog, "newHexInput").Text = "#112233";
            int previousCount = source.Generations.Count;
            Get<Button>(dialog, "replaceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Field<bool>(window, "_processing") && source.Generations.Count == previousCount + 1, "Palette replacement did not create a result");
            Require(source.SelectedGeneration!.ParentGenerationId == parent.Id, "Replacement lost the selected result parent");
            using (var result = new Bitmap(Field<ImageLibrary>(window, "_library").ResultPath(source, source.SelectedGeneration!)))
            {
                Require(result.Width == 8 && result.Height == 4 && result.GetPixel(2, 0).ToArgb() == Color.FromArgb(128, 17, 34, 51).ToArgb(), "Replacement changed dimensions, RGB or partial alpha");
                Require(result.GetPixel(0, 0).A == 0, "Replacement made a transparent pixel visible");
            }
            Require(File.ReadAllBytes(path).SequenceEqual(original) && Field<Bitmap>(window, "_sourceImage").GetPixel(2, 0).ToArgb() == Color.Red.ToArgb(), "Replacement modified the original");

            Get<ListBox>(window, "generationStrip").SelectedItem = parent; Pump();
            Until(() => Get<ItemsControl>(window, "infoPaletteList").Items.Cast<PaletteColorEntry>().Any(c => c.Color.ToArgb() == Color.Red.ToArgb()), "History selection did not refresh the palette");
            PaletteButton(window, Color.Red).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            dialog = window.OwnedWindows.OfType<ColorReplaceWindow>().Single();
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 0; Pump();
            Require(!dialog.IsVisible, "Changing the processing basis left a stale replacement dialog open");
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            NavigationTest.Click(window, "replaceColorMenuItem"); Pump();
            dialog = window.OwnedWindows.OfType<ColorReplaceWindow>().Single();
            Require(dialog.Owner == window && dialog.IsVisible, "Direct replacement menu does not open the information tool and dialog");
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            Require(!dialog.IsVisible && Field<Bitmap?>(window, "_infoBasisShown") is null, "Closing information retained its dialog or snapshot");

            string gray = Path.Combine(folder, "gray.png");
            using (var image = new Bitmap(2, 1)) { image.SetPixel(0, 0, Color.Gray); image.SetPixel(1, 0, Color.White); image.Save(gray, System.Drawing.Imaging.ImageFormat.Png); }
            Wait(window.ImportWorkspaceFilesAsync([gray]));
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id != asset.Id)));
            NavigationTest.Click(window, "infoMenuItem");
            Until(() => Get<TextBlock>(window, "infoSourceSummary").Text.Contains("2 × 1") && Get<TextBlock>(window, "infoSourceDetails").Text.Contains("Серые тона"), "Switching images retained old information or misclassified grayscale pixels");
            window.Width = 620; Pump();
            Require(Get<Border>(window, "_workspaceDock").Visibility == Visibility.Visible && Get<TextBlock>(window, "infoSourceDetails").TextWrapping == TextWrapping.Wrap, "Compact information layout is unavailable");
            DomainColorTest.Localization.Initialize("en");
            NavigationTest.Click(window, "sizeMenuItem"); NavigationTest.Click(window, "infoMenuItem");
            Until(() => Get<TextBlock>(window, "infoSourceDetails").Text.Contains("Grayscale"), "Extended information is not localized");
        }
        finally { window.Close(); Pump(); DomainColorTest.Localization.Initialize("ru"); }

        var floating = new MainWindow(Path.Combine(folder, "floating-library")) { ShowActivated = false };
        try
        {
            floating.Show(); Invoke(floating, "LoadImage", path); Pump();
            NavigationTest.Click(floating, "replaceColorMenuItem"); Pump();
            var dialog = floating.OwnedWindows.Cast<Window>().SelectMany(w => w.OwnedWindows.OfType<ColorReplaceWindow>()).Single();
            Require(dialog.IsVisible && dialog.Owner != floating, "Floating information/replacement compatibility is broken");
            dialog.Close();
        }
        finally { floating.Close(); Pump(); }
        Console.WriteLine("  Image information: docked source/result, palette replacement/history/alpha, direct menu, basis/image changes, panel closure, details, narrow layout, EN and floating compatibility passed.");
    }

    private static Button PaletteButton(MainWindow window, Color color)
    {
        Pump();
        return Descendants(Get<ItemsControl>(window, "infoPaletteList")).OfType<Button>().First(button => button.DataContext is PaletteColorEntry entry && entry.Color.ToArgb() == color.ToArgb());
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (int q = 0; q < VisualTreeHelper.GetChildrenCount(item); q++)
        {
            var child = VisualTreeHelper.GetChild(item, q); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static T Get<T>(FrameworkElement owner, string name) => (T)(owner.FindName(name) ?? owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner))!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object[] values) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, values);
    private static void Wait(Task task) { Until(() => task.IsCompleted, "Workspace operation timed out"); task.GetAwaiter().GetResult(); Pump(); }
    private static void Until(Func<bool> condition, string message)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(8)) { Pump(); Thread.Sleep(5); }
        Require(condition(), message); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
