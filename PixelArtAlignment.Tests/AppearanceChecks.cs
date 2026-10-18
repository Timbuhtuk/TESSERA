using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;
using MediaColor = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PixelArtAlignment.Tests;

internal static class AppearanceChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string directory = Path.GetFullPath(Path.Combine("artifacts", "wpf", "appearance"));
        string library = Path.Combine(directory, "library-" + Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(library, "appearance.json");
        Directory.CreateDirectory(directory);
        Require(AppearanceSettings.Load(settingsPath).Theme == TesseraTheme.Dark, "Missing appearance settings must default to dark");
        var window = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            Click(window, "appearanceButton"); Pump();
            var appearance = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Click(window, "appearanceButton"); Pump();
            Require(window.OwnedWindows.OfType<AppearanceWindow>().Count() == 1, "Appearance button created duplicate settings windows");
            appearance.Left = appearance.Top = -10000;
            foreach (var theme in Themes)
            {
                Select(appearance, theme);
                Require(AppearanceSettings.Load(settingsPath).Theme == theme, "Theme choice was not saved immediately");
                CheckChrome(window, theme);
                CheckChrome(appearance, theme);
                foreach (string option in new[] { "lightTheme", "darkTheme", "contrastTheme" })
                    Require(ColorOf(Get<RadioButton>(appearance, option).Foreground) == ColorOf((Brush)appearance.FindResource("Ts.Text")),
                        "Appearance choices contain unreadable fixed text colors");
                Require(ColorOf(Get<Button>(window, "homeOpenButton").Background) == ColorOf((Brush)window.FindResource("Ts.Primary")),
                    "Animated primary button retained the previous theme");
                Capture(window, Path.Combine(directory, $"{theme}-home.png"));
                Capture(appearance, Path.Combine(directory, $"{theme}-appearance.png"));
                window.Width = 560; Pump();
                Require(Get<ScrollViewer>(window, "homeScroll").ScrollableWidth == 0, "Themed home overflows at minimum width");
                var button = Get<Button>(window, "appearanceButton");
                Require(button.ActualWidth >= 32 && button.Content is SettingsIcon && button.TranslatePoint(new System.Windows.Point(), window).X >= 0,
                    "Appearance entry is clipped on narrow windows");
                Capture(window, Path.Combine(directory, $"{theme}-home-narrow.png"));
                window.Width = 1220;
            }

            string input = Path.Combine(directory, "theme-fixture.png");
            using var bitmap = new Bitmap(240, 160);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.FillRectangle(System.Drawing.Brushes.SteelBlue, 20, 20, 200, 120);
                graphics.FillRectangle(System.Drawing.Brushes.Gold, 60, 40, 60, 80);
                graphics.FillRectangle(System.Drawing.Brushes.Coral, 140, 80, 60, 40);
            }
            bitmap.Save(input);
            Invoke(window, "LoadImage", input);
            foreach (string name in new[] { "sizeToolButton", "colorToolButton", "smoothingToolButton", "profileToolButton", "gridToolButton", "infoToolButton", "filtersToolButton" })
                Click(window, name);
            Pump();
            var tools = window.OwnedWindows.Cast<Window>().Where(w => w is not AppearanceWindow).ToArray();
            foreach (var tool in tools) tool.Left = tool.Top = -10000;
            var picker = new ColorReplaceWindow(System.Drawing.Color.Coral) { Owner = window, Left = -10000, Top = -10000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
            picker.Show();
            var dialog = new TesseraDialog("Текст диалога должен оставаться читаемым при переключении темы.", "Проверка темы", MessageBoxButton.OK, MessageBoxImage.Information)
            {
                Owner = window, Left = -10000, Top = -10000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false
            };
            dialog.Show(); Pump();
            var preview = Get<PixelPreview>(window, "sourcePreview");
            var source = preview.Image;
            var originalPixel = source!.GetPixel(60, 40);
            Get<ComboBox>(window, "backgroundInput").SelectedIndex = 1;
            foreach (var theme in Themes)
            {
                Select(appearance, theme);
                CheckChrome(window, theme);
                foreach (var tool in tools)
                {
                    CheckChrome(tool, theme);
                    Capture(tool, Path.Combine(directory, $"{theme}-{tool.Title.Split('·')[0].Trim()}.png"));
                }
                var toggle = FindVisual<ToggleButton>(Get<ComboBox>(window, "operationSourceInput"))!;
                var arrow = FindVisual<System.Windows.Shapes.Path>(toggle)!;
                var presenter = FindVisual<ContentPresenter>(toggle)!;
                Require(arrow.ActualWidth <= presenter.ActualWidth && arrow.ActualHeight <= presenter.ActualHeight,
                    "ComboBox dropdown arrow is clipped by the button's padding");
                CheckChrome(picker, theme);
                Require(ColorOf(Get<Border>(picker, "oldPreview").Background) == MediaColor.FromRgb(255, 127, 80),
                    "Appearance theme changed the user's selected color");
                var frame = Get<Border>(dialog, "dialogFrame");
                Require(ColorOf(frame.Background) == ColorOf((Brush)dialog.FindResource("Ts.Panel")),
                    "An already open dialog retained the previous theme");
                Require(ReferenceEquals(preview.Image, source) && source.GetPixel(60, 40) == originalPixel,
                    "Theme switching changed source image pixels");
                foreach (int background in new[] { 0, 1, 2 })
                {
                    Get<ComboBox>(window, "backgroundInput").SelectedIndex = background; Pump();
                    Require(background == 0 ? preview.Background is DrawingBrush : ColorOf(preview.Background) ==
                        (background == 1 ? MediaColor.FromRgb(17, 17, 17) : MediaColor.FromRgb(224, 224, 224)),
                        "Appearance theme overrides explicit preview backgrounds");
                }
                Get<ComboBox>(window, "backgroundInput").SelectedIndex = 0;
                Capture(window, Path.Combine(directory, $"{theme}-editor.png"));
                Capture(picker, Path.Combine(directory, $"{theme}-picker.png"));
                Capture(dialog, Path.Combine(directory, $"{theme}-dialog.png"));
                Click(window, "fileMenuButton"); Pump();
                var menu = Get<Popup>(window, "fileMenuPopup");
                Require(ColorOf(((Border)menu.Child).Background) == ColorOf((Brush)window.FindResource("Ts.Panel")),
                    "File menu retained a fixed dark background");
                Capture((FrameworkElement)menu.Child, Path.Combine(directory, $"{theme}-file-menu.png"));
                menu.IsOpen = false;
            }
            picker.Close(); dialog.Close();
            foreach (var tool in tools) tool.Close();
            foreach (var theme in Themes)
            {
                Select(appearance, theme);
                Invoke(window, "ShowHome"); Pump();
                var home = Get<ScrollViewer>(window, "homeScroll");
                home.ScrollToBottom(); Pump();
                Capture(window, Path.Combine(directory, $"{theme}-home-bottom.png"));
                foreach (string button in new[] { "homeIconButton", "homeBackgroundButton", "homeAsepriteButton", "homeMoodboardButton" })
                {
                    Click(window, button); Pump();
                    Capture(window, Path.Combine(directory, $"{theme}-{button}.png"));
                    Invoke(window, "ShowHome"); Pump();
                }
            }
            appearance.Close();
            Click(window, "appearanceButton"); Pump();
            appearance = window.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(Get<RadioButton>(appearance, "contrastTheme").IsChecked == true, "Reopened appearance window lost the selected theme");
        }
        finally { window.Close(); Pump(); }

        var restarted = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            restarted.Show(); Pump();
            CheckChrome(restarted, TesseraTheme.Contrast);
            Click(restarted, "appearanceButton"); Pump();
            var settings = restarted.OwnedWindows.OfType<AppearanceWindow>().Single();
            Require(Get<RadioButton>(settings, "contrastTheme").IsChecked == true, "Theme was not restored after restart");
        }
        finally { restarted.Close(); Pump(); }
        foreach (string invalid in new[] { "broken json", "{\"Theme\":\"Unknown\"}", "{\"Theme\":999}", "null" })
        {
            File.WriteAllText(settingsPath, invalid);
            Require(AppearanceSettings.Load(settingsPath).Theme == TesseraTheme.Dark, "Invalid appearance settings did not fall back to dark");
        }
        AppearanceResources.Apply(TesseraTheme.Dark);
        Console.WriteLine("  Appearance: three live themes, open tools/dialogs, preview backgrounds, persistence/restart, invalid settings and narrow layouts passed.");
    }

    private static readonly TesseraTheme[] Themes = [TesseraTheme.Light, TesseraTheme.Dark, TesseraTheme.Contrast];

    private static void Select(AppearanceWindow window, TesseraTheme theme)
    {
        string name = theme.ToString().ToLowerInvariant() + "Theme";
        Get<RadioButton>(window, name).IsChecked = true;
        Pump();
        Require(ThemeManager.Current == theme, "Library theme manager did not receive the selected theme");
    }

    private static void CheckChrome(Window window, TesseraTheme theme)
    {
        string canvas = theme == TesseraTheme.Light ? "#E5E2D5" : theme == TesseraTheme.Dark ? "#101010" : "#0C0C0C";
        string text = theme == TesseraTheme.Light ? "#383B35" : theme == TesseraTheme.Dark ? "#EEEEEE" : "#F1F1F1";
        Require(ColorOf(window.Background) == (MediaColor)ColorConverter.ConvertFromString(canvas), $"{window.Title}: incorrect {theme} canvas");
        Require(ColorOf(window.Foreground) == (MediaColor)ColorConverter.ConvertFromString(text), $"{window.Title}: incorrect {theme} foreground");
    }

    private static MediaColor ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static T Get<T>(FrameworkElement element, string name) => (T)element.FindName(name);
    private static void Click(MainWindow window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static object? Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void Capture(FrameworkElement element, string path)
    {
        Pump(); element.UpdateLayout();
        if (element is MainWindow main)
        {
            element = Get<Grid>(main, "rootLayout");
            element.SetResourceReference(Panel.BackgroundProperty, "Ts.Canvas");
        }
        var screenshot = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        screenshot.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisual<T>(DependencyObject element) where T : DependencyObject
    {
        for (int q = 0; q < VisualTreeHelper.GetChildrenCount(element); q++)
        {
            var child = VisualTreeHelper.GetChild(element, q);
            if (child is T match) return match;
            var nested = FindVisual<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
