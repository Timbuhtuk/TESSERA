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

namespace PixelArtAlignment.Tests;

internal static class AsciiPreviewChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("ASCII preview check timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "ascii-preview", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using var source = new Bitmap(48, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(System.Drawing.Brushes.Black, 6, 4, 28, 20);
            graphics.FillRectangle(System.Drawing.Brushes.Gray, 29, 10, 12, 16);
        }
        source.Save(path);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            typeof(MainWindow).GetMethod("LoadImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [path]);
            NavigationTest.Click(window, "asciiMenuItem"); Pump();
            var toggle = (CheckBox)window.FindName("asciiLivePreviewInput");
            var preview = (PixelPreview)window.FindName("resultPreview");
            var badge = (Border)window.FindName("livePreviewFrame");
            toggle.IsChecked = true;
            Until(() => badge.Visibility == Visibility.Visible && preview.Image is not null);
            using var expected = ImageFilters.RenderAscii(source);
            Equal(expected, preview.Image!);

            var first = preview.Image;
            ((TextBox)window.FindName("asciiCharactersInput")).Text = "@ ";
            Until(() => badge.Visibility == Visibility.Visible && preview.Image is not null && !ReferenceEquals(preview.Image, first));
            using var changed = ImageFilters.RenderAscii(source, characters: "@ ");
            Equal(changed, preview.Image!);
            toggle.IsChecked = false; Pump();
            Require(badge.Visibility == Visibility.Collapsed && preview.Image is null, "ASCII preview remained after switching it off");
            ((Button)window.FindName("asciiRenderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => ((SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!).Generations.Count == 1);
            var saved = ((SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!).Generations.Single();
            Require(saved.AppliedRecipe?.Steps.Single().Ascii?.FontSize == ImageFilters.DefaultAsciiFontSize &&
                toggle.IsChecked == false, "Saved ASCII history lost the 16 px glyph size or kept live preview enabled");

            var thumbnail = new BitmapImage(new Uri(path)); thumbnail.Freeze();
            var card = (Grid)((DataTemplate)window.FindResource("GenerationCardTemplate")).LoadContent();
            card.Resources.MergedDictionaries.Add(window.Resources);
            card.DataContext = new GenerationEntry { OperationId = "filter.ascii", Operation = "ASCII", Width = 48, Height = 32, Thumbnail = thumbnail };
            card.Measure(new System.Windows.Size(112, 58)); card.Arrange(new Rect(0, 0, 112, 58)); card.UpdateLayout();
            Require(card.Children.OfType<ResultIcon>().Single().Width == 32 &&
                card.Children.OfType<TextBlock>().Count() == 0 && card.Children.OfType<Button>().Count() == 0,
                "Result card still contains small icon, text or remove button");
            Require(card.ToolTip is string hint && hint.Contains("48 × 32 px"), "Result card hint lost its resolution");
            var render = new RenderTargetBitmap(112, 58, 96, 96, PixelFormats.Pbgra32);
            render.Render(card);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
            using var file = File.Create(Path.Combine(folder, "result-card.png")); encoder.Save(file);
        }
        finally { window.Close(); Pump(); }
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "ASCII preview dimensions differ");
        for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"ASCII preview pixel differs at {x},{y}");
    }

    private static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!condition() && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        Require(condition(), "ASCII preview update timed out"); Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
