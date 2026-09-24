using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtAseprite;
using Ase = PixelArtAseprite.Tests.Fixtures;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class AsepriteUiChecks
{
    public static void Run()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "ase-ui-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string first = Write("Walk.aseprite", 3), second = Write("Jump.ase", 4), third = Write("Other\\Walk.aseprite", 5);
        string broken = Path.Combine(folder, "Broken.aseprite"); File.WriteAllText(broken, "broken");
        var original = File.ReadAllBytes(first);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Show(); Pump();
        var screen = (AsepriteWorkspace)window.FindName("asepriteWorkspace");
        try
        {
            var home = (StackPanel)window.FindName("homeContent");
            Require(home.Children.IndexOf((UIElement)window.FindName("asepriteBanner")) < home.Children.IndexOf((UIElement)window.FindName("iconBanner")), "Animation banner is not in the middle");
            ((Button)window.FindName("homeAsepriteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(screen.Visibility == Visibility.Visible && ((ScrollViewer)window.FindName("homeScroll")).Visibility == Visibility.Collapsed, "Banner did not open the workspace");
            Require(!Get<Button>("sheetSaveAllButton").IsEnabled, "Empty batch can be exported");
            Drop(new[] { first, broken, second }, Get<Border>("sheetDropPanel"));
            var appended = screen.AddFilesAsync(new[] { third, first });
            window.Close(); Require(window.IsVisible, "Window closed during conversion");
            Wait(appended);
            var items = Entries();
            Require(items.Length == 4 && items.Count(e => e.Ready) == 3 && items.Single(e => e.InputPath == broken).State == "Ошибка", "Mixed/drop batch failed or duplicate path was added");
            Require(((ListBox)window.FindName("sourceStrip")).Items.Count == 0, "Animation batch entered the image library");
            Require(items.Single(e => e.InputPath == first).Result!.FrameCount == 3, "Conversion did not start automatically");
            Require(Get<PixelPreview>("sheetPreview").Image is not null, "Ready sheet has no preview");
            Require(File.ReadAllBytes(first).SequenceEqual(original), "Import modified its source");

            string output = Path.Combine(folder, "export"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "Walk.png"), "keep");
            var save = screen.SaveAsync(output, true); Wait(save);
            Require(save.Result.Count == 3 && save.Result.All(r => r.Error is null), "Ready results were not saved as a batch");
            Require(File.ReadAllText(Path.Combine(output, "Walk.png")) == "keep", "Existing result was overwritten");
            Require(save.Result.Select(r => r.ImagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "Same-name sources overwrote one another");
            foreach (var result in save.Result)
            {
                var item = items.Single(e => e.InputPath == result.Input);
                Require(File.ReadAllBytes(result.ImagePath!).SequenceEqual(File.ReadAllBytes(item.Result!.ImagePath)), "Export differs from the converted PNG");
                using var json = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(result.ImagePath!, ".json")));
                Require(json.RootElement.GetProperty("image").GetString() == Path.GetFileName(result.ImagePath), "Collision suffix broke the JSON image reference");
                Require(json.RootElement.GetProperty("frames").GetArrayLength() == item.Result.FrameCount, "Frame metadata missing");
            }
            File.Delete(first); File.Delete(second); File.Delete(third);
            Get<ComboBox>("sheetLayout").SelectedIndex = 2;
            Get<IntegerInput>("sheetColumns").Value = 2;
            Get<IntegerInput>("sheetPadding").Value = 2;
            Wait(screen.RebuildAsync());
            Require(items.Count(e => e.Ready) == 3 && items.Single(e => e.InputPath == first).Result!.Width == 66, "Layout rebuild did not use the imported snapshot");
            Get<ListBox>("sheetStrip").SelectedItem = items.Single(e => e.InputPath == second);
            var single = screen.SaveAsync(Path.Combine(folder, "single"), false); Wait(single);
            Require(single.Result.Count == 1 && single.Result[0].Input == second, "Selected export saved the wrong batch");
            string blocked = Path.Combine(folder, "not-a-folder"); File.WriteAllText(blocked, "keep");
            var failures = screen.SaveAsync(blocked, true); Wait(failures);
            Require(failures.Result.Count == 3 && failures.Result.All(r => r.Error is not null) && items.Count(e => e.Ready) == 3, "Save failure destroyed converted results or stopped batch error reporting");

            Get<ComboBox>("sheetZoom").SelectedIndex = 3; Get<ComboBox>("sheetBackground").SelectedIndex = 1; Pump();
            Require(Get<PixelPreview>("sheetPreview").Zoom == 2 && Get<PixelPreview>("sheetPreview").PreviewBackground == 1, "Preview controls ignored");
            Get<ComboBox>("sheetZoom").SelectedIndex = 0; Get<ComboBox>("sheetBackground").SelectedIndex = 0;
            Get<ComboBox>("sheetLayout").SelectedIndex = 0; Wait(screen.RebuildAsync());
            Capture("aseprite-workspace.png");
            window.Width = 560; window.Height = 700; Pump();
            Require(Get<Border>("sheetSettingsPanel").ActualHeight >= 100,
                "Narrow layout hides animation settings");
            Require(Get<PixelPreview>("sheetPreview").ActualHeight >= 120 && Get<PixelPreview>("sheetPreview").ActualWidth >= 490, "Narrow layout collapsed the preview");
            Capture("aseprite-narrow.png");

            var removal = new Button { DataContext = items.Single(e => e.InputPath == broken) };
            typeof(AsepriteWorkspace).GetMethod("RemoveSheet", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, new object[] { removal, new RoutedEventArgs(Button.ClickEvent) });
            Require(Entries().Length == 3, "Remove did not update the filmstrip");
            screen.Clear(); Require(Entries().Length == 0 && Directory.GetFiles(output, "*.json").Length == 3, "Clear deleted exported files");
            string cancelPath = Write("Cancel.aseprite", 3);
            var canceled = screen.AddFilesAsync(new[] { cancelPath });
            Get<Button>("sheetCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(canceled);
            Require(!screen.IsBusy && Entries().Single().State == "Отменено", "Cancellation did not stop the queued conversion");
            screen.Clear();
            Get<Button>("sheetBackButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Drop(new[] { cancelPath }, (Border)window.FindName("asepriteBanner")); WaitIdle();
            Require(screen.Visibility == Visibility.Visible && Entries().Single().Ready, "Drop on the home banner did not convert");
            Console.WriteLine("  Aseprite UI: centred banner, multi-drop/queued imports, errors, snapshots, layouts, batch/selected saving, collision-safe PNG+JSON, cancellation and narrow preview passed.");
        }
        finally { window.Close(); }

        T Get<T>(string name) => (T)screen.FindName(name);
        AsepriteWorkspace.SheetEntry[] Entries() => Get<ListBox>("sheetStrip").Items.Cast<AsepriteWorkspace.SheetEntry>().ToArray();
        void Wait(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(task.IsCompleted, "Aseprite UI task timed out"); task.GetAwaiter().GetResult(); Pump();
        }
        void WaitIdle()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (screen.IsBusy && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!screen.IsBusy, "Aseprite workspace remained busy"); Pump();
        }
        void Drop(string[] paths, UIElement target)
        {
            var data = new DataObject(DataFormats.FileDrop, paths);
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0) }, null)!;
            e.RoutedEvent = DragDrop.PreviewDropEvent; target.RaiseEvent(e);
            Require(e.Effects == DragDropEffects.Copy, "Supported Aseprite drop rejected");
        }
        string Write(string name, int frames)
        {
            var data = new byte[32 * 32 * 4];
            for (int y = 5; y < 28; y++) for (int x = 7; x < 25; x++)
            {
                if (y > 20 && x is >= 14 and <= 17) continue;
                int q = (y * 32 + x) * 4;
                data[q] = (byte)(y < 13 ? 235 : 90); data[q + 1] = (byte)(y < 13 ? 225 : 125); data[q + 2] = (byte)(y < 13 ? 210 : 145);
                data[q + 3] = (byte)(x == 7 ? 128 : 255);
            }
            var inputFrames = new List<Ase.Frame> { new(100, Ase.Layer(), Ase.Cel(32, 32, data)) };
            for (int q = 1; q < frames; q++) inputFrames.Add(new Ase.Frame(100 + q * 10, Ase.Link(0, (short)(q % 3 - 1), (short)(q % 2))));
            string path = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Ase.File(32, 32, inputFrames.ToArray())); return path;
        }
        void Capture(string name)
        {
            Pump(); window.UpdateLayout();
            var root = (Grid)window.FindName("rootLayout"); root.Background = window.Background;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine("artifacts", "wpf", "verification", name)); encoder.Save(stream);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
