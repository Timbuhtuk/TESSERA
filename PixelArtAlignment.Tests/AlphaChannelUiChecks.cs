using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class AlphaChannelUiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Alpha UI verification timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "alpha-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using (var source = new Bitmap(512, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb(x / 2, 210, 70, 30));
            source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        string library = Path.Combine(folder, "library");
        var window = Open();
        try
        {
            Invoke("LoadImage", path);
            Get<CheckBox>("aspectLock").IsChecked = false;
            Get<IntegerInput>("widthInput").Value = 256;
            Get<IntegerInput>("heightInput").Value = 32;
            foreach (bool sprite in new[] { false, true })
            foreach (string button in new[] { "scaleOnlyButton", "processButton" })
            {
                Get<CheckBox>("spriteInput").IsChecked = sprite;
                Get<Button>(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var until = DateTime.UtcNow.AddSeconds(10);
                while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
                Require(!Get<bool>("_processing"), "Image processing timed out");
                Pump();
                VerifyAlpha(Get<Bitmap>("_downscaledResult"));
                Require(Get<bool>("_resultPreservesTransparency"), "Editor did not protect transparent export");
                string output = Path.Combine(folder, $"{button}-{sprite}.png");
                Invoke("SaveResult", output);
                using var reopened = new Bitmap(output);
                VerifyAlpha(reopened);
                try
                {
                    Invoke("SaveResult", Path.Combine(folder, "alpha.jpg"));
                    throw new Exception("Editor accepted JPEG for partial alpha");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
                Console.WriteLine($"  Alpha UI: {button}, sprite={sprite}, export passed.");
            }
            window.Close();
            window = Open();
            VerifyAlpha(Get<Bitmap>("_downscaledResult"));
            Require(Get<SourceEntry>("_activeSource").Generations.Count == 4, "Alpha history did not survive restart");
            Console.WriteLine("  Alpha UI: PNG and persisted history retain every alpha value.");
        }
        finally { window.Close(); Pump(); }

        MainWindow Open()
        {
            var result = new MainWindow(library) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
    }

    private static void VerifyAlpha(Bitmap image)
    {
        Require(image.Width == 256 && image.Height == 32, "Incorrect alpha result size");
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                Require(image.GetPixel(x, y).A == x, $"Alpha {x} changed at row {y}");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
