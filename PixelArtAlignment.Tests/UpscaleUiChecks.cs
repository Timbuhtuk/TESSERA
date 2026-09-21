using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DomainColorTest;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class UpscaleUiChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "upscale-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string sourcePath = Path.Combine(folder, "source.png");
        using var source = new Bitmap(4, 3, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb((x + y) % 4 == 0 ? 96 : 255, x * 50, y * 70, x * 20 + y));
        source.Save(sourcePath);

        string library = Path.Combine(folder, "library");
        var window = Open();
        try
        {
            Invoke("LoadImage", sourcePath);
            Require(Get<Button>("scaleOnlyButton").IsEnabled, "Size action did not enable after loading");
            Get<CheckBox>("aspectLock").IsChecked = false;
            Get<IntegerInput>("widthInput").Value = 8;
            Get<IntegerInput>("heightInput").Value = 6;
            Require(Get<TextBlock>("sizeHint").Text.Contains("Увеличение"), "Size hint still rejects enlargement: " + Get<TextBlock>("sizeHint").Text + $" ({Get<IntegerInput>("widthInput").Value} × {Get<IntegerInput>("heightInput").Value})");
            Click("scaleOnlyButton"); Wait();
            var first = Get<Bitmap>("_downscaledResult");
            var firstEntry = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Require(first.Size == new System.Drawing.Size(8, 6) && firstEntry.Operation == "Увеличение" && firstEntry.ParentGenerationId is null,
                "Upscale did not create a correctly labelled generation");
            CompareNearest(source, first);
            using var snapshot = (Bitmap)first.Clone();
            string export = Path.Combine(folder, "upscaled.png");
            Invoke("SaveResult", export);
            using (var saved = new Bitmap(export)) CompareNearest(source, saved);

            Get<ComboBox>("operationSourceInput").SelectedIndex = 1;
            Get<IntegerInput>("widthInput").Value = 12;
            Get<IntegerInput>("heightInput").Value = 9;
            Click("scaleOnlyButton"); Wait();
            var second = Get<Bitmap>("_downscaledResult");
            var secondEntry = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Require(second.Size == new System.Drawing.Size(12, 9) && secondEntry.ParentGenerationId == firstEntry.Id,
                "Upscaling a selected result lost its parent or dimensions");
            CompareNearest(snapshot, second);
            window.Close();

            window = Open();
            var restored = Get<SourceEntry>("_activeSource");
            Require(restored.Generations.Count == 2 && restored.SelectedGeneration?.Id == secondEntry.Id &&
                Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(12, 9),
                "Enlargement history did not survive restart");
            CompareNearest(snapshot, Get<Bitmap>("_downscaledResult"));
            Console.WriteLine("  Upscale UI: source and selected-result enlargement, exact pixels/alpha, export and restart passed.");
        }
        finally { window.Close(); }

        MainWindow Open()
        {
            var result = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
        void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Upscale timed out"); Pump();
        }
    }

    private static void CompareNearest(Bitmap source, Bitmap output)
    {
        for (int y = 0; y < output.Height; y++)
            for (int x = 0; x < output.Width; x++)
            {
                int sx = (int)((long)x * source.Width / output.Width);
                int sy = (int)((long)y * source.Height / output.Height);
                Require(output.GetPixel(x, y).ToArgb() == source.GetPixel(sx, sy).ToArgb(),
                    "Upscale blended colors or changed alpha");
            }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
