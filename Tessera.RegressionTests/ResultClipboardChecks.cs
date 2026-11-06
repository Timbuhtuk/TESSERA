using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Tessera.Desktop;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace Tessera.RegressionTests;

internal static class ResultClipboardChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Result clipboard check timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "result-clipboard", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using var image = new Bitmap(256, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int x = 0; x < 256; x++) image.SetPixel(x, 1, Color.FromArgb(x, (x * 17) % 256, 80, 190));
        image.SetPixel(0, 0, Color.FromArgb(96, 12, 34, 56));
        image.SetPixel(1, 0, Color.FromArgb(255, 210, 20, 90));
        image.Save(path);
        byte[] original = File.ReadAllBytes(path);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            Call(window, "LoadImage", path);
            var source = (SourceEntry)Field(window, "_activeSource")!;
            var library = (ImageLibrary)Field(window, "_library")!;
            var first = library.SaveGeneration(source, image, null, new GenerationEntry { Operation = "Проверка" });
            Require((bool)Call(window, "CommitGeneration", source, first, true)!, "Initial result was not committed");
            var strip = (ListBox)window.FindName("generationStrip");
            Require(strip.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.C && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Copy) &&
                strip.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.V && b.Modifiers == ModifierKeys.Control && b.Command == ApplicationCommands.Paste),
                "Result browser lacks Ctrl+C or Ctrl+V bindings");

            var data = (DataObject)Call(null, "CreateResultClipboardData", image)!;
            Require(data.GetDataPresent("PNG", false), "Clipboard has no native PNG format");
            var png = (Stream)data.GetData("PNG", false)!;
            {
                png.Position = 0;
                byte[] signature = new byte[8];
                Require(png.Read(signature, 0, signature.Length) == signature.Length &&
                    signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Clipboard PNG signature is invalid");
            }
            using (var decoded = (Bitmap)Call(null, "ReadClipboardImage", data)!)
                Equal(image, decoded, "Native clipboard PNG");
            var bitmapOnly = new DataObject();
            bitmapOnly.SetImage(PixelPreview.ToBitmapSource(image));
            using (var decoded = (Bitmap)Call(null, "ReadClipboardImage", bitmapOnly)!)
                Require(decoded.GetPixel(1, 0).ToArgb() == image.GetPixel(1, 0).ToArgb(),
                    "Pasting a regular clipboard bitmap lost an opaque pixel");

            ((CheckBox)window.FindName("insertHistoryInput")).IsChecked = true;
            typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            try
            {
                Call(window, "PasteClipboardImageAsBranch", data); Pump();
                Require(source.Generations.Count == 1 && !ApplicationCommands.Copy.CanExecute(null, strip), "Busy clipboard actions changed history or stayed available");
            }
            finally { typeof(MainWindow).GetField("_processing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false); }
            Call(window, "PasteClipboardImageAsBranch", new DataObject("text", "no image"));
            var invalid = new DataObject(); invalid.SetData("PNG", new byte[] { 1, 2, 3 });
            Call(window, "PasteClipboardImageAsBranch", invalid);
            Require(source.Generations.Count == 1, "Unsupported clipboard data changed history");
            Call(window, "PasteClipboardImageAsBranch", data); Pump();
            Require(source.Generations.Count == 2 && source.Generations[^1].ParentGenerationId is null,
                "Clipboard image was not inserted as an independent root");
            Require(ReferenceEquals(source.SelectedGeneration, source.Generations[^1]) &&
                strip.SelectedItem == source.Generations[^1], "Pasted branch was not selected");
            using (var pasted = ImageLibrary.ReadBitmap(library.ResultPath(source, source.Generations[^1])))
                Equal(image, pasted, "Pasted history PNG");
            var layout = HistoryBranchLayout.Build(source.Generations.ToArray(), new BranchPresentation());
            Require(layout.Roots.Count == 2, "Pasted result is connected to the previous branch");
            Guid pastedId = source.Generations[^1].Id;
            window.Close(); Pump();
            window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            window.Show(); Pump(); Call(window, "LoadImage", path);
            source = (SourceEntry)Field(window, "_activeSource")!;
            library = (ImageLibrary)Field(window, "_library")!;
            strip = (ListBox)window.FindName("generationStrip");
            Require(source.Generations.Count == 2 && source.Generations.Single(g => g.Id == pastedId).ParentGenerationId is null,
                "Pasted branch did not survive restart independently");
            var undo = Field(window, "_historyUndo")!;
            var snapshot = undo.GetType().GetMethod("Undo")!.Invoke(undo, null);
            Require(snapshot is not null, "Clipboard paste has no undo snapshot");
            Call(window, "RestoreHistorySource", snapshot); Pump();
            Require(source.Generations.Count == 1, "Undo did not remove the pasted branch");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Clipboard changed the external source");
            using (var stored = ImageLibrary.ReadBitmap(library.SourcePath(source))) Equal(image, stored, "Internal source after clipboard use");
            Tessera.Desktop.Localization.Initialize("en");
            Require(Tessera.Desktop.Localization.StoredText("Буфер обмена") == "Clipboard" &&
                Tessera.Desktop.Localization.Text("Изображение вставлено отдельной веткой истории.") == "Image pasted as an independent history branch.",
                "Clipboard history or status is not available in English");
            Tessera.Desktop.Localization.Initialize("ru");

            // The runner owns its Windows desktop; never replace the user's local clipboard during programmatic checks.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                try
                {
                    strip.SelectedItem = source.Generations.Single(); Pump();
                    Require(ApplicationCommands.Copy.CanExecute(null, strip), "Copy command is unavailable for a saved result");
                    ApplicationCommands.Copy.Execute(null, strip);
                    Require(Clipboard.ContainsData("PNG"), "Real copy command did not publish the native PNG format");
                    using (var copied = (Bitmap)Call(null, "ReadClipboardImage", Clipboard.GetDataObject())!) Equal(image, copied, "System clipboard command");
                    Require(ApplicationCommands.Paste.CanExecute(null, strip), "Paste command did not detect the system clipboard image");
                    ApplicationCommands.Paste.Execute(null, strip); Pump();
                    Require(source.Generations.Count == 2 && source.Generations[^1].ParentGenerationId is null, "Real paste command did not create an independent branch");
                    using (var pasted = ImageLibrary.ReadBitmap(library.ResultPath(source, source.Generations[^1]))) Equal(image, pasted, "System paste command");
                    Console.WriteLine("  Result clipboard: real Windows copy/paste command routing and native PNG passed on the CI desktop.");
                }
                finally { Clipboard.Clear(); }
            }
            Console.WriteLine("  Result clipboard: all 256 alpha values, bitmap fallback, busy/invalid guards, independent branch, persisted undo, source isolation and RU/EN passed.");
        }
        finally { window.Close(); Pump(); }
    }

    private static object? Call(MainWindow? window, string name, params object?[] args)
        => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | (window is null ? BindingFlags.Static : BindingFlags.Instance))!
            .Invoke(window, args);
    private static object? Field(MainWindow window, string name)
        => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal(Bitmap expected, Bitmap actual, string label)
    {
        Require(expected.Size == actual.Size, label + " dimensions differ");
        for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), label + $" differs at {x},{y}");
    }
}
