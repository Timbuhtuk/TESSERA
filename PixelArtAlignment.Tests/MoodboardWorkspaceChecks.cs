using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class MoodboardWorkspaceChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); VerifyFileProtection(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(120))) throw new Exception("Moodboard check timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "moodboard-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            var banner = GetWindow<Border>("moodboardBanner");
            Require(banner.Visibility == Visibility.Visible && GetWindow<Button>("homeMoodboardButton").IsEnabled, "Moodboard banner is missing");
            GetWindow<Button>("homeMoodboardButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var board = GetWindow<MoodboardWorkspace>("moodboardWorkspace");
            Require(board.Visibility == Visibility.Visible && !board.HasBoard, "Moodboard onboarding did not open");
            Capture(board, Path.Combine(folder, "onboarding.png"));
            var create = GetBoard<Button>(board, "createButton");
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(board.HasBoard && GetBoard<Canvas>(board, "boardSurface").Width == 1200, "Canvas was not created");
            var picker = new ColorReplaceWindow(Color.White) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            picker.Show(); Pump();
            Require(GetPicker<DockPanel>(picker, "oldColorPanel").Visibility == Visibility.Collapsed,
                "Existing palette picker did not open in background-color mode");
            GetPicker<TextBox>(picker, "newHexInput").Text = "#0C2238";
            GetPicker<Button>(picker, "replaceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(picker.SelectedColor?.ToArgb() == Color.FromArgb(12, 34, 56).ToArgb(),
                "Palette picker did not return the selected background color");
            board.SetBackgroundColor(System.Windows.Media.Color.FromRgb(12, 34, 56));
            Require(GetBoard<TextBlock>(board, "backgroundHex").Text == "#0C2238", "Canvas color control did not update");

            var zoom = GetBoard<ComboBox>(board, "boardZoomInput");
            var scroll = GetBoard<ScrollViewer>(board, "boardScroll");
            zoom.SelectedIndex = 5; Pump(); scroll.UpdateLayout();
            Require(scroll.ScrollableWidth > 0 && scroll.ScrollableHeight > 0,
                "Fixed canvas zoom did not enable scrolling in both directions");
            Point viewportPoint = new(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2);
            Point anchor = scroll.TranslatePoint(viewportPoint, GetBoard<Canvas>(board, "boardSurface"));
            var changeZoom = typeof(MoodboardWorkspace).GetMethod("ChangeBoardZoom", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require((bool)changeZoom.Invoke(board, [120, viewportPoint, anchor, ModifierKeys.Control])!,
                "Ctrl+wheel did not change canvas zoom");
            Pump(); scroll.UpdateLayout();
            Point anchored = GetBoard<Canvas>(board, "boardSurface").TranslatePoint(anchor, scroll);
            Require(zoom.SelectedIndex == 6 && Math.Abs(anchored.X - viewportPoint.X) < 3 && Math.Abs(anchored.Y - viewportPoint.Y) < 3,
                "Ctrl+wheel zoom did not keep the canvas point under the cursor");
            scroll.ScrollToHorizontalOffset(100); scroll.ScrollToVerticalOffset(80); Pump();
            Require(scroll.HorizontalOffset > 0 && scroll.VerticalOffset > 0, "Canvas cannot be moved by scrolling");
            zoom.SelectedIndex = 0; Pump();

            string redPath = Path.Combine(folder, "red.png"), bluePath = Path.Combine(folder, "blue.png");
            using (var red = new Bitmap(100, 80)) { using var graphics = System.Drawing.Graphics.FromImage(red); graphics.Clear(Color.Red); red.Save(redPath); }
            using (var blue = new Bitmap(80, 60)) { using var graphics = System.Drawing.Graphics.FromImage(blue); graphics.Clear(Color.Blue); blue.Save(bluePath); }
            board.AddFiles([redPath, bluePath]); Pump();
            Capture(board, Path.Combine(folder, "editor-wide.png"));
            window.Width = 700; Pump();
            Require(GetBoard<Canvas>(board, "boardSurface").IsVisible, "Canvas disappeared in narrow layout");
            Capture(board, Path.Combine(folder, "editor-narrow.png"));
            window.Width = 1220; Pump();
            var layers = GetBoard<ListBox>(board, "layerList");
            Require(layers.Items.Count == 2 && GetBoard<Canvas>(board, "boardSurface").Children.Count == 2,
                "Each imported image must create one selectable layer");
            string png = Path.Combine(folder, "composition.png");
            board.ExportToPng(png);
            using (var image = new Bitmap(png))
                Require(image.Width == 1200 && image.Height == 800 && image.GetPixel(600, 400).ToArgb() == Color.Blue.ToArgb() &&
                    image.GetPixel(0, 0).ToArgb() == Color.FromArgb(12, 34, 56).ToArgb(),
                    "Top layer did not appear in the exported composition");

            layers.SelectedIndex = 1;
            GetBoard<Button>(board, "upButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            board.ExportToPng(png);
            using (var image = new Bitmap(png))
                Require(image.GetPixel(600, 400).ToArgb() == Color.Red.ToArgb(), "Raising a layer did not change stacking order");
            GetBoard<Slider>(board, "rotationSlider").Value = 35;
            GetBoard<Slider>(board, "scaleSlider").Value = 140;
            Require(GetBoard<Slider>(board, "rotationSlider").IsEnabled && GetBoard<Slider>(board, "scaleSlider").IsEnabled,
                "Reordering lost the selected layer");
            string project = Path.Combine(folder, "composition.tboard");
            board.SaveToFile(project);
            using (var archive = ZipFile.OpenRead(project))
            using (var manifest = new StreamReader(archive.GetEntry("board.json")!.Open()))
            {
                string contents = manifest.ReadToEnd();
                Require(contents.Contains("\"Version\":2") && contents.Contains("\"BackgroundColor\":\"#0C2238\"") &&
                    contents.Contains("\"Angle\":35") && contents.Contains("\"Scale\":1.4") &&
                    archive.Entries.Count == 3, "Layer transforms or images were not persisted in the project");
            }
            AwaitWithPump(board.LoadFromFileAsync(project)); Pump();
            Require(board.BackgroundColor == System.Windows.Media.Color.FromRgb(12, 34, 56),
                "Reopened board lost its background color");
            Require(layers.Items.Count == 2 && ((string)layers.Items[0].GetType().GetProperty("Name")!.GetValue(layers.Items[0])!) == "red.png",
                "Saved layer order did not survive reopening the board");
            board.ExportToPng(png);
            using (var image = new Bitmap(png))
                Require(image.GetPixel(600, 400).ToArgb() == Color.Red.ToArgb(), "Reopened board lost its image composition");

            string greenPath = Path.Combine(folder, "green.png");
            using (var green = new Bitmap(60, 40)) { using var graphics = System.Drawing.Graphics.FromImage(green); graphics.Clear(Color.Green); green.Save(greenPath); }
            board.AddFiles([greenPath]); Pump();
            object greenLayer = FindLayer(layers, "green.png"), redLayer = FindLayer(layers, "red.png"), blueLayer = FindLayer(layers, "blue.png");
            var selectLayer = typeof(MoodboardWorkspace).GetMethod("SelectLayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            selectLayer.Invoke(board, [redLayer, ModifierKeys.Shift]);
            Require(layers.SelectedItems.Count == 2, "Shift-click did not add a canvas layer to the selection");
            selectLayer.Invoke(board, [greenLayer, ModifierKeys.Control]);
            Require(layers.SelectedItems.Count == 1 && ReferenceEquals(layers.SelectedItem, redLayer),
                "Ctrl-click did not remove a canvas layer from the selection");
            layers.SelectedItems.Add(blueLayer); Pump();
            Require(layers.SelectedItems.Count == 2, "Layer list did not keep multiple selected items");
            double redX = LayerNumber(redLayer, "X"), blueX = LayerNumber(blueLayer, "X"), greenX = LayerNumber(greenLayer, "X");
            typeof(MoodboardWorkspace).GetMethod("TranslateSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(board, [25d, -10d]);
            Require(LayerNumber(redLayer, "X") == redX + 25 && LayerNumber(blueLayer, "X") == blueX + 25 &&
                LayerNumber(greenLayer, "X") == greenX, "Group movement changed the wrong layers");
            GetBoard<Slider>(board, "rotationSlider").Value = 30;
            Require(LayerNumber(redLayer, "Angle") == 65 && LayerNumber(blueLayer, "Angle") == 30 &&
                LayerNumber(greenLayer, "Angle") == 0, "Group rotation did not preserve each layer's angle offset");
            GetBoard<Slider>(board, "scaleSlider").Value = 120;
            Require(Math.Abs(LayerNumber(redLayer, "Scale") - 1.68) < .001 &&
                Math.Abs(LayerNumber(blueLayer, "Scale") - 1.2) < .001 && LayerNumber(greenLayer, "Scale") == 1,
                "Group scaling did not preserve relative sizes");
            GetBoard<Button>(board, "toFrontButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(LayerNames(layers).SequenceEqual(["red.png", "blue.png", "green.png"]) && layers.SelectedItems.Count == 2,
                "Send to front lost layer order or group selection");
            Capture(board, Path.Combine(folder, "multi-selection.png"));
            GetBoard<Button>(board, "toBackButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(LayerNames(layers).SequenceEqual(["green.png", "red.png", "blue.png"]) && layers.SelectedItems.Count == 2,
                "Send to back lost layer order or group selection");
            GetBoard<Button>(board, "deleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(LayerNames(layers).SequenceEqual(["green.png"]) && GetBoard<Canvas>(board, "boardSurface").Children.Count == 1,
                "Deleting a selection removed an unselected layer");

            string legacy = Path.Combine(folder, "legacy.tboard");
            File.WriteAllText(legacy, JsonSerializer.Serialize(new { Version = 1, Width = 1200, Height = 800,
                Layers = new[] { new { Name = "legacy.png", Png = File.ReadAllBytes(bluePath), Width = 80, Height = 60,
                    X = 560, Y = 370, Angle = 0, Scale = 1.0 } } }));
            board.LoadFromFile(legacy); Pump();
            Require(layers.Items.Count == 1 && board.BackgroundColor == Colors.White,
                "Existing JSON boards no longer open with a white background");

            zoom.SelectedIndex = 6;
            GetBoard<Button>(board, "newButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            GetBoard<IntegerInput>(board, "widthInput").Value = 8192;
            GetBoard<IntegerInput>(board, "heightInput").Value = 8192;
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(zoom.SelectedIndex == 0, "New large canvas did not return to fit zoom");
            string largeSource = Path.Combine(folder, "large-source.png");
            using (var source = new Bitmap(3000, 2000))
            {
                using var graphics = System.Drawing.Graphics.FromImage(source);
                graphics.Clear(Color.White);
                graphics.FillRectangle(System.Drawing.Brushes.Red, 1000, 1000, 20, 20);
                source.Save(largeSource, System.Drawing.Imaging.ImageFormat.Png);
            }
            var import = board.AddFilesAsync([largeSource]);
            Require(board.IsBusy, "Large import did not enter background busy state");
            AwaitWithPump(import);
            Require(!board.IsBusy, "Large import did not leave busy state");
            var surface = GetBoard<Canvas>(board, "boardSurface");
            var imageControl = (System.Windows.Controls.Image)((Border)((Grid)surface.Children[0]).Children[0]).Child;
            Require(((BitmapSource)imageControl.Source).PixelWidth <= 1536, "Large source was decoded fully for the editor preview");
            string largePng = Path.Combine(folder, "large.png");
            var export = board.ExportToPngAsync(largePng);
            Require(board.IsBusy && GetWindow<ProgressBar>("busyIndicator").Visibility == Visibility.Visible,
                "Large export did not enter background busy state");
            AwaitWithPump(export);
            Require(!board.IsBusy, "Large export did not leave busy state");
            using (var image = new Bitmap(largePng))
                Require(image.Width == 8192 && image.Height == 8192 && image.GetPixel(3600, 4100).ToArgb() == Color.Red.ToArgb() &&
                    image.GetPixel(4096, 4096).ToArgb() == Color.White.ToArgb(),
                    "Large canvas export lost size or layer content");
            string tallPath = Path.Combine(folder, "tall-source.png");
            using (var tall = new Bitmap(8, 6000)) tall.Save(tallPath, System.Drawing.Imaging.ImageFormat.Png);
            AwaitWithPump(board.AddFilesAsync([tallPath]));
            var tallControl = (System.Windows.Controls.Image)((Border)((Grid)surface.Children[1]).Children[0]).Child;
            Require(((BitmapSource)tallControl.Source).PixelHeight <= 1536,
                "Tall source was decoded fully for the editor preview");
            GetBoard<Button>(board, "deleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(surface.Children.Count == 1, "Deleting a tall test layer removed another image");
            string jpegPath = Path.Combine(folder, "photo.jpg");
            using (var photo = new Bitmap(640, 480))
            {
                using var graphics = System.Drawing.Graphics.FromImage(photo);
                graphics.Clear(Color.Blue);
                photo.Save(jpegPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            board.AddFiles([jpegPath]);
            string largeProject = Path.Combine(folder, "large.tboard");
            AwaitWithPump(board.SaveToFileAsync(largeProject));
            using (var archive = ZipFile.OpenRead(largeProject))
                Require(archive.GetEntry("images/0001.bin")!.Length == new FileInfo(jpegPath).Length,
                    "JPEG was expanded when added to the board project");

            GetBoard<Button>(board, "newButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            GetBoard<IntegerInput>(board, "widthInput").Value = 16384;
            GetBoard<IntegerInput>(board, "heightInput").Value = 1024;
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            board.AddFiles([redPath]);
            string widePng = Path.Combine(folder, "wide.png");
            AwaitWithPump(board.ExportToPngAsync(widePng));
            using (var image = new Bitmap(widePng))
                Require(image.Width == 16384 && image.Height == 1024 && image.GetPixel(8192, 512).ToArgb() == Color.Red.ToArgb(),
                    "Maximum-width canvas export failed");
            board.SaveToFile(Path.Combine(folder, "wide.tboard"));
            GetBoard<Button>(board, "backButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(GetWindow<ScrollViewer>("homeScroll").Visibility == Visibility.Visible, "Back did not return to the home screen");
        }
        finally { window.Close(); }

        T GetWindow<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    }

    private static void VerifyFileProtection()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "board-protection-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "source.png"), project = Path.Combine(folder, "board.tboard");
        using (var image = new Bitmap(24, 16)) { image.SetPixel(0, 0, Color.Coral); image.Save(source); }
        byte[] sourceBytes = File.ReadAllBytes(source);
        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        var board = (MoodboardWorkspace)window.FindName("moodboardWorkspace");
        try
        {
            window.Show(); Pump();
            board.AddFiles([source]);
            board.SaveToFile(project);
            byte[] projectBytes = File.ReadAllBytes(project);
            VerifyProtected(board);
            Require(!GetBoard<bool>(board, "_dirty") && !board.IsBusy, "Rejected writes changed the saved state or left the board busy");
            board.SetBackgroundColor(Colors.Teal);
            VerifyProtected(board);
            Require(GetBoard<bool>(board, "_dirty"), "Rejected export cleared unsaved changes");
            Require(File.ReadAllBytes(project).SequenceEqual(projectBytes), "Rejected export changed the project bytes");

            string png = Path.Combine(folder, "composition.PNG");
            AwaitWithPump(board.ExportToPngAsync(png));
            board.ExportToPng(png);
            using (var image = new Bitmap(png)) Require(image.Width == 1200 && image.Height == 800, "Protected export broke valid PNG output");
            AwaitWithPump(board.SaveToFileAsync(project));
            var reopened = new MoodboardWorkspace();
            reopened.LoadFromFile(project);
            VerifyProtected(reopened);
            GetBoard<ListBox>(reopened, "layerList").SelectedIndex = 0;
            GetBoard<Button>(reopened, "deleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(GetBoard<ListBox>(reopened, "layerList").Items.Count == 0, "Source-protection check did not remove its layer");
            Reject(() => reopened.ExportToPng(source));

            string renamedProject = Path.Combine(folder, "renamed-project.png");
            File.Copy(project, renamedProject);
            byte[] renamedBytes = File.ReadAllBytes(renamedProject);
            reopened.LoadFromFile(renamedProject);
            Reject(() => AwaitWithPump(reopened.ExportToPngAsync(renamedProject)));
            Require(File.ReadAllBytes(renamedProject).SequenceEqual(renamedBytes), "PNG-named project was overwritten by PNG export");

            string olderProject = Path.Combine(folder, "older-v2.tboard");
            File.Copy(project, olderProject);
            using (var archive = ZipFile.Open(olderProject, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("board.json")!;
                System.Text.Json.Nodes.JsonNode manifest;
                using (var stream = entry.Open()) manifest = System.Text.Json.Nodes.JsonNode.Parse(stream)!;
                manifest.AsObject().Remove("SourcePaths");
                entry.Delete();
                using var output = archive.CreateEntry("board.json").Open();
                JsonSerializer.Serialize(output, manifest);
            }
            reopened.LoadFromFile(olderProject);
            Require(GetBoard<ListBox>(reopened, "layerList").Items.Count == 1, "Existing v2 project without source metadata no longer opens");
            Reject(() => reopened.ExportToPng(source));
            Reject(() => AwaitWithPump(reopened.ExportToPngAsync(source)));
            string legacyProject = Path.Combine(folder, "older-v1.tboard");
            File.WriteAllText(legacyProject, JsonSerializer.Serialize(new { Version = 1, Width = 1200, Height = 800,
                Layers = new[] { new { Name = "source.png", Png = sourceBytes, Width = 24, Height = 16,
                    X = 0, Y = 0, Angle = 0, Scale = 1 } } }));
            reopened.LoadFromFile(legacyProject);
            Reject(() => reopened.ExportToPng(source));
            Require(File.ReadAllBytes(source).SequenceEqual(sourceBytes), "Protected source bytes changed");
            Require(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "Rejected write left temporary files");
            Console.WriteLine("  Board protection: source/project bytes preserved, sync/async paths rejected, dirty state intact, reopen and old formats passed.");
        }
        finally
        {
            typeof(MoodboardWorkspace).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(board, false);
            window.Close();
        }

        void VerifyProtected(MoodboardWorkspace target)
        {
            Reject(() => target.ExportToPng(source));
            Reject(() => AwaitWithPump(target.ExportToPngAsync(Path.GetRelativePath(Environment.CurrentDirectory, source))));
            Reject(() => target.ExportToPng(source.ToUpperInvariant()));
            Reject(() => target.SaveToFile(source));
            Reject(() => AwaitWithPump(target.SaveToFileAsync(source)));
            Reject(() => target.ExportToPng(project));
            Reject(() => AwaitWithPump(target.ExportToPngAsync(Path.Combine(folder, ".", "board.tboard"))));
            Reject(() => target.ExportToPng(Path.Combine(folder, "other.tboard")));
            Reject(() => target.SaveToFile(Path.Combine(folder, "other.png")));
            Require(File.ReadAllBytes(source).SequenceEqual(sourceBytes), "A rejected write replaced its source");
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe board output path was accepted");
    }

    private static T GetBoard<T>(MoodboardWorkspace board, string name) =>
        (T)typeof(MoodboardWorkspace).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(board)!;

    private static T GetPicker<T>(ColorReplaceWindow picker, string name) =>
        (T)typeof(ColorReplaceWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker)!;

    private static object FindLayer(ListBox list, string name) => list.Items.Cast<object>().Single(item =>
        item.GetType().GetProperty("Name")!.GetValue(item) as string == name);
    private static double LayerNumber(object layer, string name) =>
        (double)layer.GetType().GetProperty(name)!.GetValue(layer)!;
    private static string[] LayerNames(ListBox list) => list.Items.Cast<object>().Select(item =>
        (string)item.GetType().GetProperty("Name")!.GetValue(item)!).ToArray();

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void AwaitWithPump(Task task)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Background moodboard operation timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
