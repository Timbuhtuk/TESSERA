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

internal static class ChannelAdjustmentUiChecks
{
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
            catch (Exception ex) { Console.WriteLine(ex); failure = ex; }
            finally { Localization.Initialize("ru"); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("RGBA channel UI checks timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(bool docked)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "channels-" + Guid.NewGuid().ToString("N")));
        string library = Path.Combine(folder, "library"); Directory.CreateDirectory(library);
        new AppearanceSettings { Language = docked ? "en" : "ru" }.Save(Path.Combine(library, "appearance.json"));
        string path = Path.Combine(folder, "source.png");
        using var source = new Bitmap(4, 2);
        Color[] pixels = [Color.FromArgb(0, 12, 34, 56), Color.FromArgb(64, 100, 150, 200), Color.FromArgb(128, 0, 127, 255), Color.FromArgb(255, 255, 0, 2)];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++) source.SetPixel(x, y, pixels[x]);
        source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        byte[] sourceBytes = File.ReadAllBytes(path);
        var window = Open();
        Bitmap? expectedLast = null;
        try
        {
            Require(!Get<Button>("channelApplyButton").IsEnabled, "Empty editor allows channel adjustment");
            if (docked)
            {
                window.EnableWorkspacePrototype();
                Wait(window.ImportWorkspaceFilesAsync([path]));
                Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            }
            else Invoke("LoadImage", path);
            var menu = Get<MenuItem>("channelMenuItem");
            Require(Get<MenuItem>("colorToolsMenu").Items.Contains(menu), "RGBA tool is outside Tools/Color");
            NavigationTest.Click(window, "channelMenuItem"); Pump();
            Require(Get<ComboBox>("channelInput").IsVisible && (Window.GetWindow(Get<ComboBox>("channelInput")) == window) == docked,
                "RGBA tool did not open in the correct host");
            Require(menu.Header.ToString() == (docked ? "RGBA channels…" : "Каналы RGBA…"), "RGBA menu is not localized");
            NavigationTest.Click(window, "colorMenuItem"); Pump();
            Until(() => Get<Border>("livePreviewFrame").Visibility == Visibility.Visible);
            var oldPreview = Get<PixelPreview>("resultPreview").Image;
            Get<IntegerInput>("quantizationColorsInput").Value = 1;
            Until(() => Get<PixelPreview>("resultPreview").Image is { } image && !ReferenceEquals(image, oldPreview));
            NavigationTest.Click(window, "channelMenuItem"); Pump();
            Require(Get<Border>("livePreviewFrame").Visibility == Visibility.Collapsed &&
                Get<PixelPreview>("sourcePreview").SelectedRegion is null,
                "RGBA tool retained another tool's live preview or crop overlay");
            Apply(new() { Channel = RgbaChannel.R, Mode = ChannelAdjustmentMode.Percent, Amount = 50 }, source, null);
            var first = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            using var firstPixels = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            window.ProcessingBasis = 1;
            Apply(new() { Channel = RgbaChannel.A, Amount = 32, Subtract = true }, firstPixels, first.Id);
            var second = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Require(second.RecipeSnapshot?.Steps.Count == 2, "Channel history lost its parent recipe");
            using (var replay = new WorkflowExecutor().Execute(source, second.RecipeSnapshot!)) Equal(replay, Get<Bitmap>("_downscaledResult"));
            string export = Path.Combine(folder, "adjusted.png"); Invoke("SaveResult", export);
            using (var reopened = new Bitmap(export)) Equal(reopened, Get<Bitmap>("_downscaledResult"));
            foreach (string invalid in new[] { "", "oops", "NaN", "-1", "Infinity" })
            {
                Get<TextBox>("channelAmountInput").Text = invalid;
                Require(!Get<Button>("channelApplyButton").IsEnabled, "Invalid amount was accepted: " + invalid);
            }
            Get<TextBox>("channelAmountInput").Text = "0,5";
            Require(Get<Button>("channelApplyButton").IsEnabled, "Decimal comma is rejected");
            window.ProcessingBasis = 0;
            Apply(new() { Channel = RgbaChannel.G, Mode = ChannelAdjustmentMode.Percent, Amount = 25.5, Subtract = true }, source, null);
            expectedLast = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            window.Width = 620; Pump();
            var host = Window.GetWindow(Get<ComboBox>("channelInput"))!;
            Capture(host, Path.Combine(folder, docked ? "docked-en.png" : "floating-ru.png"));
            if (host != window) host.Close();
            window.Close(); window = Open();
            Equal(expectedLast, Get<Bitmap>("_downscaledResult"));
            Require(Get<ComboBox>("channelInput").SelectedIndex == (int)RgbaChannel.G && Get<ComboBox>("channelModeInput").SelectedIndex == 1 &&
                Get<ComboBox>("channelDirectionInput").SelectedIndex == 1 && double.Parse(Get<TextBox>("channelAmountInput").Text,
                System.Globalization.CultureInfo.CurrentCulture) == 25.5, "Saved channel settings were not restored");
            Require(File.ReadAllBytes(path).SequenceEqual(sourceBytes), "Channel adjustment overwrote the original file");
            Console.WriteLine($"  RGBA UI: docked={docked}, add/subtract, source/result, recipe, PNG, validation, restart and localization passed: {folder}");
        }
        finally { expectedLast?.Dispose(); window.Close(); Pump(); }

        void Apply(ChannelAdjustmentOptions options, Bitmap basis, Guid? parent)
        {
            Get<ComboBox>("channelInput").SelectedIndex = (int)options.Channel;
            Get<ComboBox>("channelModeInput").SelectedIndex = (int)options.Mode;
            Get<ComboBox>("channelDirectionInput").SelectedIndex = options.Subtract ? 1 : 0;
            Get<TextBox>("channelAmountInput").Text = options.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Get<Button>("channelApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !Get<bool>("_processing"));
            Require(window.ProcessingBasis == 1 && Get<ListBox>("sourceStrip").SelectedItem is null &&
                ReferenceEquals(Get<ListBox>("generationStrip").SelectedItem, Get<SourceEntry>("_activeSource").SelectedGeneration),
                "RGBA apply did not select exactly the resulting history card");
            Require(Get<Border>("livePreviewFrame").Visibility == Visibility.Collapsed, "RGBA result was replaced by another tool's preview");
            using var expected = ChannelAdjustment.Apply(basis, options);
            Equal(expected, Get<Bitmap>("_downscaledResult"));
            Require(Get<SourceEntry>("_activeSource").SelectedGeneration!.ParentGenerationId == parent, "Channel operation used the wrong processing basis");
        }
        MainWindow Open()
        {
            var result = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "RGBA dimensions changed");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Unexpected RGBA at {x},{y}");
    }
    private static void Capture(FrameworkElement target, string path)
    {
        var visual = target is Window window ? (FrameworkElement)window.Content : target;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "RGBA editor operation timed out"); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
