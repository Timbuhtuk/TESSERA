using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtAlignment;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class EditorRegressionChecks
{
    public static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "editor-regressions-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "source.png");
        using var source = new Bitmap(120, 80);
        for (int q = 0; q < source.Width; q++)
            for (int e = 0; e < source.Height; e++)
                source.SetPixel(q, e, q < 10 && e < 10 ? Color.Transparent :
                    q >= 90 && e >= 60 ? Color.FromArgb(128, 0, 0, 255) :
                    q >= 30 && q < 90 && e >= 25 && e < 55 ? Color.Lime : Color.Red);
        source.Save(input);
        MainWindow window = null!;
        var failures = new List<string>();
        Check("tool-state", VerifyTools);
        Check("aspect-lock", VerifyAspectLock);
        Check("grid-basis", VerifyGridBasis);
        Check("export-protection", VerifyExportProtection);
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));

        void Check(string name, Action verify)
        {
            Console.WriteLine($"  Editor regression: {name} starting");
            window = new MainWindow(Path.Combine(folder, name)) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            string? dialogFailure = null;
            var dialogWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            dialogWatch.Tick += (_, _) =>
            {
                foreach (var dialog in window.OwnedWindows.OfType<TesseraDialog>().ToArray())
                {
                    dialogFailure = ((TextBlock)dialog.FindName("dialogMessage")).Text;
                    Console.WriteLine($"  Editor regression: unexpected dialog: {dialogFailure}");
                    dialog.Close();
                }
            };
            dialogWatch.Start();
            try
            {
                window.Show(); Pump();
                Console.WriteLine($"  Editor regression: {name} window ready");
                Invoke("LoadImage", input);
                Console.WriteLine($"  Editor regression: {name} source loaded");
                Get<CheckBox>("aspectLock").IsChecked = false;
                Get<IntegerInput>("widthInput").Value = 128;
                Get<IntegerInput>("heightInput").Value = 259;
                Get<RadioButton>("manualInput").IsChecked = true;
                Get<Slider>("brightnessInput").Value = 82;
                Get<Slider>("contrastInput").Value = 23;
                Get<Slider>("localBrightnessInput").Value = 74;
                Console.WriteLine($"  Editor regression: {name} starting fixture resize");
                Click("scaleOnlyButton"); Wait();
                Require(dialogFailure is null, "Unexpected editor dialog: " + dialogFailure);
                Console.WriteLine($"  Editor regression: {name} fixture ready");
                Require(Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(128, 259), "Fixture resize failed");
                verify();
                Require(dialogFailure is null, "Unexpected editor dialog: " + dialogFailure);
                Console.WriteLine($"  Editor regression: {name} passed.");
            }
            catch (Exception ex) { Console.WriteLine($"  Editor regression: {name} failed: {ex}"); failures.Add(name + ": " + ex.Message); }
            finally { dialogWatch.Stop(); window.Close(); Pump(); }
        }

        void VerifyTools()
        {
            string settings = Settings();
            string caption = Get<TextBlock>("resultCaption").Text;
            string status = Get<TextBlock>("statusLabel").Text;
            var changes = new List<string>();
            Get<Slider>("brightnessInput").ValueChanged += (_, e) => changes.Add($"brightness {e.OldValue} -> {e.NewValue}");
            foreach (var pair in new[] { ("sizeMenuItem", "widthInput"), ("smoothingMenuItem", "localBrightnessInput"),
                ("colorMenuItem", "paletteInput"), ("gridMenuItem", "gridCellInput") })
                for (int q = 0; q < 2; q++)
                {
                    Click(pair.Item1); Pump();
                    var tool = Window.GetWindow(Get<FrameworkElement>(pair.Item2));
                    Require(tool is not null && tool != window && tool.IsVisible, "Tool did not open: " + pair.Item1);
                    VerifyUnchanged("opening " + pair.Item1);
                    tool!.Close(); Pump();
                    VerifyUnchanged("closing " + pair.Item1);
                }
            Get<IntegerInput>("widthInput").Value++;
            Require(Get<bool>("_resultStale"), "A real size edit did not mark the result stale");

            foreach (var pair in new[] { ("sizeMenuItem", "scaleOnlyButton"), ("gridMenuItem", "alignButton"), ("filtersMenuItem", "monochromeButton") })
                for (int q = 0; q < 2; q++)
                {
                    Click(pair.Item1); Pump();
                    var button = Get<Button>(pair.Item2);
                    button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                    Window.GetWindow(button)!.Close();
                    button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                    Pump();
                }

            void VerifyUnchanged(string action)
            {
                Require(Settings() == settings, action + " changed saved parameters");
                Require(!Get<bool>("_resultStale") && Get<TextBlock>("resultCaption").Text == caption &&
                    Get<TextBlock>("statusLabel").Text == status, action + " invalidated a fresh result: " + string.Join(", ", changes));
            }
        }

        void VerifyAspectLock()
        {
            var basis = NavigationTest.Select(window, "ProcessingBasis");
            var width = Get<IntegerInput>("widthInput");
            var height = Get<IntegerInput>("heightInput");
            basis.SelectedIndex = 1;
            Get<CheckBox>("aspectLock").IsChecked = true;
            Click("sizeMenuItem"); Pump();
            basis.SelectedIndex = 0;
            Require(width.Value == 128 && height.Value == 85, $"Source switch kept {width.Value} x {height.Value} with aspect lock enabled");
            basis.SelectedIndex = 1;
            Require(width.Value == 128 && height.Value == 259, "Result switch did not restore its aspect ratio");
            Window.GetWindow(width)!.Close(); Pump();
            basis.SelectedIndex = 0;
            Require(height.Value == 85, "Closed size tool ignored the selected source ratio");
            Click("scaleOnlyButton"); Wait();
            string export = Path.Combine(folder, "aspect-lock.png");
            Invoke("SaveResult", export);
            using (var saved = new Bitmap(export))
                Require(saved.Size == new System.Drawing.Size(128, 85), "PNG export did not preserve the selected source ratio");
            Get<CheckBox>("aspectLock").IsChecked = false;
            height.Value = 200;
            basis.SelectedIndex = 1;
            Require(height.Value == 200, "Switching basis changed dimensions while aspect lock was disabled");
        }

        void VerifyGridBasis()
        {
            var document = Get<SourceEntry>("_activeSource");
            var parent = document.SelectedGeneration!;
            using var basis = (Bitmap)Get<Bitmap>("_downscaledResult").Clone();
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 1;
            Get<CheckBox>("detectGridInput").IsChecked = false;
            Get<IntegerInput>("gridCellInput").Value = 2;
            Click("alignButton"); Wait();
            using (var expected = new PixelGridAligner().Align(basis, new GridAlignmentOptions { CellSize = 2 }))
                EqualPixels(expected.Aligned, Get<Bitmap>("_downscaledResult"), "Alignment of selected result");
            Require(document.SelectedGeneration!.ParentGenerationId == parent.Id, "Alignment lost its selected-result parent");
            EqualPixels(source, Get<Bitmap>("_sourceImage"), "Alignment modified the original");
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 0;
            Get<CheckBox>("detectGridInput").IsChecked = true;
            Click("alignButton"); Wait();
            using (var expected = new PixelGridAligner().Align(source, new GridAlignmentOptions()))
                EqualPixels(expected.Aligned, Get<Bitmap>("_downscaledResult"), "Automatic alignment of source");
            Require(document.SelectedGeneration!.ParentGenerationId is null, "Source alignment retained an unrelated parent");
        }

        void VerifyExportProtection()
        {
            Console.WriteLine("  Export regression: loading sources");
            string second = Path.Combine(folder, "second-source.png");
            File.Copy(input, second);
            Invoke("LoadImage", second); Invoke("LoadImage", input);
            Click("scaleOnlyButton"); Wait();
            Console.WriteLine("  Export regression: checking protected image paths");
            var sources = Get<ListBox>("sourceStrip").Items.Cast<SourceEntry>().ToArray();
            var library = Get<ImageLibrary>("_library");
            var protectedFiles = sources.Select(item => item.OriginalPath).Concat(sources.Select(library.SourcePath)).Concat(
                sources.SelectMany(item => item.Generations.Select(generation => library.ResultPath(item, generation)))).ToArray();
            foreach (string path in protectedFiles)
            {
                byte[] before = File.ReadAllBytes(path);
                Reject(() => Invoke("SaveResult", path.ToUpperInvariant()));
                Reject(() => Invoke("SaveResult", Path.GetRelativePath(Environment.CurrentDirectory, path)));
                Require(File.ReadAllBytes(path).SequenceEqual(before), "Export changed protected file " + path);
            }
            Reject(() => Invoke("SaveResult", Path.Combine(library.DirectoryPath, "new.png")));
            string output = Path.Combine(folder, "safe-result.PNG");
            Invoke("SaveResult", output); Invoke("SaveResult", output);
            Console.WriteLine("  Export regression: checking ASCII paths");
            using (var actual = new Bitmap(output)) EqualPixels(Get<Bitmap>("_downscaledResult"), actual, "Repeated PNG export");

            Reject(() => WaitTask((Task)Invoke("SaveAsciiTextAsync", input)!));
            Reject(() => WaitTask((Task)Invoke("SaveAsciiTextAsync", Path.Combine(library.DirectoryPath, "output.txt"))!));
            string text = Path.Combine(folder, "safe-ascii.TXT");
            WaitTask((Task)Invoke("SaveAsciiTextAsync", text)!);
            Console.WriteLine("  Export regression: ASCII export completed");
            Require(Get<ListBox>("generationStrip").SelectedItem is GenerationEntry && Get<ListBox>("sourceStrip").SelectedItem is null,
                "The saved result must be the sole input after processing");
            string expected = PixelArtDownscale.ImageFilters.ToAscii(Get<Bitmap>("_downscaledResult"), Get<TextBox>("asciiCharactersInput").Text, Get<CheckBox>("asciiInvertSourceInput").IsChecked == true);
            Require(File.ReadAllText(text) == expected, "ASCII export did not write expected text");
            WaitTask((Task)Invoke("SaveAsciiTextAsync", text)!);
            Require(!Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any(), "Rejected export left temporary files");
            Require(!Get<bool>("_processing"), "ASCII export left editor busy");
        }

        void Reject(Action action)
        {
            try { action(); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
            catch (InvalidOperationException) { return; }
            throw new Exception("Export accepted a protected path");
        }

        void WaitTask(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(task.IsCompleted, "Export timed out"); task.GetAwaiter().GetResult(); Pump();
        }

        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
        string Settings() => JsonSerializer.Serialize((EditorSettings)Invoke("CaptureSettings")!);
        void Click(string name) => NavigationTest.Click(window, name);
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Editor operation timed out"); Pump();
        }
    }

    private static void EqualPixels(Bitmap expected, Bitmap actual, string context)
    {
        Require(expected.Size == actual.Size, context + $": expected {expected.Size}, got {actual.Size}");
        for (int q = 0; q < expected.Width; q++)
            for (int e = 0; e < expected.Height; e++)
                Require(expected.GetPixel(q, e).ToArgb() == actual.GetPixel(q, e).ToArgb(), context + $": pixel {q},{e}");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
