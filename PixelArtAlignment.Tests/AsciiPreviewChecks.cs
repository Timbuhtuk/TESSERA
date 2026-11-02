using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.WpfStyles.Themes;
using DomainColorTest;
using Localization = DomainColorTest.Localization;
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
            Until(() => ActiveSource().Generations.Count == 1 && !Processing());
            var saved = ActiveSource().Generations.Single();
            Require(saved.AppliedRecipe?.Steps.Single().Ascii?.FontSize == ImageFilters.DefaultAsciiFontSize &&
                toggle.IsChecked == false, "Saved ASCII history lost the 16 px glyph size or kept live preview enabled");

            var card = (Grid)((DataTemplate)window.FindResource("GenerationCardTemplate")).LoadContent();
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
            VerifyHintPopups(window, saved, folder);

            var save = (Task)typeof(MainWindow).GetMethod("SaveResultWithProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [path])!;
            Until(() => save.IsCompleted); save.GetAwaiter().GetResult();
            using (var exported = new Bitmap(path)) Equal(changed, exported);
            using (var librarySource = ImageLibrary.ReadBitmap(new ImageLibrary(Path.Combine(folder, "library")).SourcePath(ActiveSource())))
                Equal(source, librarySource);
            source.Save(path);
            typeof(MainWindow).GetMethod("SaveResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [path]);
            using (var exported = new Bitmap(path)) Equal(changed, exported);
            string internalPath = new ImageLibrary(Path.Combine(folder, "library")).SourcePath(ActiveSource());
            try
            {
                typeof(MainWindow).GetMethod("SaveResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [internalPath]);
                throw new Exception("Internal Tessera source was unexpectedly writable through export");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(ActiveSource().Generations.Count == 0, "The result card cross did not remove its generation");
            ((Button)window.FindName("asciiRenderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => ActiveSource().Generations.Count == 1 && !Processing());
            var second = ActiveSource().Generations.Single();
            var strip = (ListBox)window.FindName("generationStrip");
            strip.SelectedItem = second; Pump();
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            strip.RaiseEvent(key); Pump();
            Require(key.Handled && ActiveSource().Generations.Count == 0, "Delete did not perform the cross's removal action");
        }
        finally { window.Close(); Pump(); }

        SourceEntry ActiveSource() => (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        bool Processing() => (bool)typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
    }

    private static void VerifyHintPopups(MainWindow window, GenerationEntry saved, string folder)
    {
        var card = Visuals(window).OfType<Grid>().First(g => ReferenceEquals(g.DataContext, saved) && g.ToolTip is ToolTip { ContentTemplate: not null });
        var tooltip = (ToolTip)card.ToolTip;
        try
        {
            tooltip.PlacementTarget = card;
            tooltip.IsOpen = true; Pump();
            Require(ReferenceEquals(tooltip.Content, saved) && tooltip.IsVisible && Visuals(tooltip).OfType<TextBlock>().Any(t => t.Text == saved.ToolTipSize),
                "Live result tooltip did not open with its result context");
            tooltip.IsOpen = false; Pump();
            foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light, TesseraTheme.Contrast })
            foreach (string language in new[] { "ru", "en" })
            {
                AppearanceResources.Apply(theme); Localization.Initialize(language);
                var entry = new GenerationEntry { Operation = "ASCII", Width = 48, Height = 32,
                    Log = "ASCII: 48 × 32 px; " + string.Join(" ", Enumerable.Repeat("Long result details / Длинное описание результата.", 90)) };
                card.DataContext = entry;
                tooltip.IsOpen = true; Pump(); tooltip.UpdateLayout();
                Require(ReferenceEquals(tooltip.Content, entry), "Tooltip result context did not update");
                var scroll = Visuals(tooltip).OfType<ScrollViewer>().Single(s => s.Width == 350);
                var rows = Visuals(scroll).OfType<TextBlock>().Where(t => t.Parent is StackPanel).ToArray();
                Require(rows.Length == 5 && rows[0].Text == "ASCII" && rows[1].Text.StartsWith(language == "en" ? "Size:" : "Размер:") &&
                    rows[2].Text.StartsWith(language == "en" ? "Created:" : "Создан:") && rows[3].Visibility == Visibility.Collapsed &&
                    !rows[4].Text.Contains("48 × 32"), "Tooltip order, language, empty branch or duplicate dimensions differ");
                Require(scroll.ActualWidth == 350 && scroll.ActualHeight <= 300 && scroll.ScrollableHeight > 0 && rows[4].TextWrapping == TextWrapping.Wrap,
                    "Long tooltip details do not wrap and scroll within the bounds");
                var primary = ((SolidColorBrush)window.FindResource("Ts.Text")).Color;
                var secondary = ((SolidColorBrush)window.FindResource("Ts.TextSecondary")).Color;
                Require(new[] { 0, 3, 4 }.All(q => ((SolidColorBrush)rows[q].Foreground).Color == primary) &&
                    new[] { 1, 2 }.All(q => ((SolidColorBrush)rows[q].Foreground).Color == secondary), "Tooltip lost theme text colors");
                entry.PresentationHint = Localization.Text("Отдельная ветка."); Pump();
                Require(rows[3].Visibility == Visibility.Visible && rows[3].Text == entry.PresentationHint, "Branch hint binding did not update");
                scroll.ScrollToBottom(); Pump(); Require(scroll.VerticalOffset > 0, "Tooltip details cannot scroll");
                var render = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth), (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                render.Render(tooltip);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using var file = File.Create(Path.Combine(folder, $"result-hint-{theme}-{language}.png")); encoder.Save(file);
                tooltip.IsOpen = false; Pump();
            }
            Console.WriteLine("Result hints: live popup, context, RU/EN, three themes, wrapping, scrolling and branch updates passed without physical input.");
        }
        finally
        {
            tooltip.IsOpen = false; card.DataContext = saved;
            Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); Pump();
        }
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (int q = 0; q < VisualTreeHelper.GetChildrenCount(root); q++)
        {
            var child = VisualTreeHelper.GetChild(root, q); yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
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
