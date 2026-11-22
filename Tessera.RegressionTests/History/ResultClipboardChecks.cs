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
        using var image = new Bitmap(4, 3, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        image.SetPixel(0, 0, Color.FromArgb(96, 12, 34, 56));
        image.SetPixel(1, 0, Color.FromArgb(255, 210, 20, 90));
        image.Save(path);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            Call(window, "LoadImage", path);
            var source = (SourceEntry)Field(window, "_activeSource")!;
            var library = (ImageLibrary)Field(window, "_library")!;
            var first = library.SaveGeneration(source, image, null, new GenerationEntry { Operation = "Проверка" });
            Require((bool)Call(window, "CommitGeneration", source, first, true)!, "Initial result was not committed");
            var strip = (ListBox)window.FindControl("generationStrip");
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
                Require(decoded.GetPixel(0, 0).ToArgb() == image.GetPixel(0, 0).ToArgb(),
                    $"Clipboard PNG lost alpha or pixel colors: {decoded.GetPixel(0, 0).ToArgb():X8} != {image.GetPixel(0, 0).ToArgb():X8}");
            var bitmapOnly = new DataObject();
            bitmapOnly.SetImage(PixelPreview.ToBitmapSource(image));
            using (var decoded = (Bitmap)Call(null, "ReadClipboardImage", bitmapOnly)!)
                Require(decoded.GetPixel(1, 0).ToArgb() == image.GetPixel(1, 0).ToArgb(),
                    "Pasting a regular clipboard bitmap lost an opaque pixel");

            ((CheckBox)window.FindControl("insertHistoryInput")).IsChecked = true;
            Call(window, "PasteClipboardImageAsBranch", data); Pump();
            Require(source.Generations.Count == 2 && source.Generations[^1].ParentGenerationId is null,
                "Clipboard image was not inserted as an independent root");
            Require(ReferenceEquals(source.SelectedGeneration, source.Generations[^1]) &&
                strip.SelectedItem == source.Generations[^1], "Pasted branch was not selected");
            using (var pasted = ImageLibrary.ReadBitmap(library.ResultPath(source, source.Generations[^1])))
                Require(pasted.GetPixel(0, 0).ToArgb() == image.GetPixel(0, 0).ToArgb(), "Pasted branch lost PNG alpha");
            var layout = HistoryBranchLayout.Build(source.Generations.ToArray(), new BranchPresentation());
            Require(layout.Roots.Count == 2, "Pasted result is connected to the previous branch");
            var undo = Field(window, "_historyUndo")!;
            var snapshot = undo.GetType().GetMethod("Undo")!.Invoke(undo, null);
            Require(snapshot is not null, "Clipboard paste has no undo snapshot");
            Call(window, "RestoreHistorySource", snapshot); Pump();
            Require(source.Generations.Count == 1, "Undo did not remove the pasted branch");
        }
        finally { window.Close(); Pump(); }
    }

    private static object? Call(MainWindow? window, string name, params object?[] args)
         => window is not null
            ? WindowTestAccess.Invoke(window, name, BindingFlags.NonPublic | BindingFlags.Instance, args)
            : typeof(EditorHistory).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    private static object? Field(MainWindow window, string name)
        => WindowTestAccess.GetField(window, name, BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
