using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace Tessera.RegressionTests;

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
            WindowTestAccess.Invoke(window, "LoadImage", BindingFlags.Instance | BindingFlags.NonPublic, [path]);
            NavigationTest.Click(window, "asciiMenuItem"); Pump();
            var toggle = (CheckBox)window.FindControl("asciiLivePreviewInput");
            var preview = (PixelPreview)window.FindControl("resultPreview");
            var badge = (Border)window.FindControl("livePreviewFrame");
            toggle.IsChecked = true;
            Until(() => badge.Visibility == Visibility.Visible && preview.Image is not null);
            using var expected = ImageFilters.RenderAscii(source);
            Equal(expected, preview.Image!);

            var first = preview.Image;
            ((TextBox)window.FindControl("asciiCharactersInput")).Text = "@ ";
            Until(() => badge.Visibility == Visibility.Visible && preview.Image is not null && !ReferenceEquals(preview.Image, first));
            using var changed = ImageFilters.RenderAscii(source, characters: "@ ");
            Equal(changed, preview.Image!);
            toggle.IsChecked = false; Pump();
            Require(badge.Visibility == Visibility.Collapsed && preview.Image is null, "ASCII preview remained after switching it off");
            ((Button)window.FindControl("asciiRenderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => ActiveSource().Generations.Count == 1 && !Processing());
            var saved = ActiveSource().Generations.Single();
            Require(saved.AppliedRecipe?.Steps.Single().Ascii?.FontSize == ImageFilters.DefaultAsciiFontSize &&
                toggle.IsChecked == false, "Saved ASCII history lost the 16 px glyph size or kept live preview enabled");

            var card = (Grid)((DataTemplate)window.Editor.FindResource("GenerationCardTemplate")).LoadContent();
            card.Resources.MergedDictionaries.Add(window.Resources);
            card.DataContext = saved;
            card.Measure(new System.Windows.Size(112, 58)); card.Arrange(new Rect(0, 0, 112, 58)); card.UpdateLayout();
            var remove = card.Children.OfType<Button>().Single();
            Require(card.Children.OfType<ResultIcon>().Single().Width == 32 &&
                card.Children.OfType<TextBlock>().Count() == 0 &&
                AutomationProperties.GetName(remove) == saved.RemoveDescription &&
                remove.HorizontalAlignment == HorizontalAlignment.Right && remove.VerticalAlignment == VerticalAlignment.Top,
                "Result card lost its large icon, text-free layout or upper-right remove button");
            Require(card.ToolTip is ToolTip { ContentTemplate: not null } &&
                saved.ResultTreeDescription.Contains("Размер: 48 × 32 px") &&
                saved.ResultTreeDescription.Contains("Создан:"), "Result card lost its standardized hint");
            var render = new RenderTargetBitmap(112, 58, 96, 96, PixelFormats.Pbgra32);
            render.Render(card);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
            using var file = File.Create(Path.Combine(folder, "result-card.png")); encoder.Save(file);
            var tooltip = (ToolTip)card.ToolTip;
            var hintContent = (FrameworkElement)tooltip.ContentTemplate!.LoadContent();
            hintContent.Resources.MergedDictionaries.Add(window.Resources);
            hintContent.DataContext = saved;
            hintContent.Measure(new System.Windows.Size(360, 300));
            hintContent.Arrange(new Rect(0, 0, hintContent.DesiredSize.Width, hintContent.DesiredSize.Height));
            hintContent.UpdateLayout();
            var hintRender = new RenderTargetBitmap((int)Math.Ceiling(hintContent.ActualWidth), (int)Math.Ceiling(hintContent.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            hintRender.Render(hintContent);
            var hintEncoder = new PngBitmapEncoder(); hintEncoder.Frames.Add(BitmapFrame.Create(hintRender));
            using var hintFile = File.Create(Path.Combine(folder, "result-hint.png")); hintEncoder.Save(hintFile);

            string output = Path.Combine(folder, "ascii-export.png");
            var save = (Task)WindowTestAccess.Invoke(window, "SaveResultWithProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic, [output])!;
            Until(() => save.IsCompleted); save.GetAwaiter().GetResult();
            using (var exported = new Bitmap(output)) Equal(changed, exported);
            using (var librarySource = ImageLibrary.ReadBitmap(new ImageLibrary(Path.Combine(folder, "library")).SourcePath(ActiveSource())))
                Equal(source, librarySource);
            using (var original = new Bitmap(path)) Equal(source, original);
            WindowTestAccess.Invoke(window, "SaveResult", BindingFlags.Instance | BindingFlags.NonPublic, [output]);
            using (var exported = new Bitmap(output)) Equal(changed, exported);
            string internalPath = new ImageLibrary(Path.Combine(folder, "library")).SourcePath(ActiveSource());
            try
            {
                WindowTestAccess.Invoke(window, "SaveResult", BindingFlags.Instance | BindingFlags.NonPublic, [internalPath]);
                throw new Exception("Internal Tessera source was unexpectedly writable through export");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(ActiveSource().Generations.Count == 0, "The result card cross did not remove its generation");
            ((Button)window.FindControl("asciiRenderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => ActiveSource().Generations.Count == 1 && !Processing());
            var second = ActiveSource().Generations.Single();
            var strip = (ListBox)window.FindControl("generationStrip");
            strip.SelectedItem = second; Pump();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            strip.RaiseEvent(key); Pump();
            Require(key.Handled && ActiveSource().Generations.Count == 0, "Delete did not perform the cross's removal action");
        }
        finally { window.Close(); Pump(); }

        SourceEntry ActiveSource() => (SourceEntry)WindowTestAccess.GetField(window, "_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Processing() => (bool)WindowTestAccess.GetField(window, "_processing", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
