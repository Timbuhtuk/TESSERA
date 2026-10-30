using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using DomainColorTest;
using Tessera.WpfStyles.Themes;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class CompactSwitchChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light, TesseraTheme.Contrast })
                    foreach (string language in new[] { "ru", "en" }) Verify(theme, language);
            }
            catch (Exception ex) { failure = ex; }
            finally { Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Compact switch check timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(TesseraTheme theme, string language)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "compact-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        new AppearanceSettings { Language = language, Theme = theme }.Save(Path.Combine(folder, "appearance.json"));
        var owner = new MainWindow(folder) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        string text = language == "ru" ? "Сохранить размер исходника" : "Keep source size";
        var check = new CheckBox { Content = text, Style = (Style)owner.FindResource("Ts.SwitchBox"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var host = new Window { Content = new Grid { Children = { check } }, Width = 440, Height = 120, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        foreach (string key in new[] { "Ts.Text", "Ts.TextSecondary", "Ts.Canvas", "Ts.UiFont" }) host.Resources[key] = owner.FindResource(key);
        try
        {
            host.Show(); Pump(); check.ApplyTemplate();
            var thumb = (Ellipse)check.Template.FindName("SwitchThumb", check)!;
            var track = Parents(thumb).OfType<Border>().First(b => b.Width == 42);
            Require(track.ActualWidth == 42 && track.ActualHeight == 22 && thumb.ActualWidth == 14 && thumb.ActualHeight == 14, "Switch size differs from the compact contract");
            Require(check.MinHeight == 32 && check.ActualHeight <= 33, "Compact switch still has an oversized row");
            foreach (bool enabled in new[] { true, false }) foreach (bool value in new[] { false, true })
            {
                check.IsEnabled = enabled; check.IsChecked = value; Pump();
                Require(Canvas.GetLeft(thumb) == (value ? 24 : 4), "Compact switch thumb does not follow its state");
                Require(check.Opacity == (enabled ? 1 : .4), "Disabled switch state is missing");
                Require(Equals(check.Content, text) && check.Focusable, "Compact switch lost its label or focus behavior");
                var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(check.ActualWidth)), 32, 96, 96, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(new VisualBrush(check), null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                bitmap.Render(drawing);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"{theme}-{language}-{enabled}-{value}.png")); encoder.Save(file);
            }
        }
        finally { host.Close(); owner.Close(); Pump(); }
    }

    private static IEnumerable<DependencyObject> Parents(DependencyObject value)
    {
        while (VisualTreeHelper.GetParent(value) is { } parent) { yield return parent; value = parent; }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
