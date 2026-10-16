using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class TileInspectorChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Tile seams: exact match, axis mapping, singleton and transparent hidden RGB", () =>
        {
            byte[] left = [0, 0, 0, 255, 255, 255, 255, 255, 12, 34, 56, 0];
            byte[] right = [0, 0, 0, 255, 0, 0, 0, 255, 200, 150, 100, 0];
            byte[] top = [0, 0, 0, 255, 0, 0, 0, 255];
            byte[] bottom = [255, 255, 255, 255, 0, 0, 0, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(left, right, top, bottom);
            Require(result.Horizontal.Length == 3 && result.Vertical.Length == 2, "Non-square edge lengths swapped");
            Require(result.Horizontal[0] == 0 && Math.Abs(result.Horizontal[1] - 100) < .001 && result.Horizontal[2] == 0,
                "Wrong edge row or hidden transparent colors counted");
            Require(Math.Abs(result.Vertical[0] - 100) < .001 && result.Vertical[1] == 0, "Wrong column comparison");
            byte[] pixel = [30, 90, 180, 127];
            var single = TileSeamAnalyzer.AnalyzeEdges(pixel, pixel, pixel, pixel);
            Require(single.Horizontal[0] == 0 && single.Vertical[0] == 0, "Single pixel cannot repeat");
        });
        check("Tile seams: opacity is visible on black OR white, symmetry and threshold equality", () =>
        {
            byte[] black = [0, 0, 0, 255], clear = [0, 0, 0, 0], white = [255, 255, 255, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(black, clear, clear, white);
            Require(Math.Abs(result.Horizontal[0] - 100) < .001 && Math.Abs(result.Vertical[0] - 100) < .001,
                "Black or white background hid alpha mismatch");
            var reverse = TileSeamAnalyzer.AnalyzeEdges(clear, black, white, clear);
            Require(result.Horizontal.SequenceEqual(reverse.Horizontal) && result.Vertical.SequenceEqual(reverse.Vertical), "Distance is not symmetric");
            var summary = TileSeamAnalysis.Summarize([0, 5, 10], 5);
            Require(summary.Mean == 5 && summary.Maximum == 10 && summary.AboveThreshold == 1 && summary.Count == 3,
                "Summary or threshold boundary incorrect");
            Require(TileSeamAnalysis.Summarize([0, 0], 0).AboveThreshold == 0, "Exact match rejected at zero threshold");
        });
        check("Tile seams: published OKLab red-green distance and invalid inputs", () =>
        {
            // Published OKLab sRGB primary coordinates give red-green distance approximately 0.5198.
            byte[] red = [0, 0, 255, 255], green = [0, 255, 0, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(red, green, red, green);
            Require(Math.Abs(result.Horizontal[0] - 51.9813) < .01, "OKLab conversion incorrect");
            bool rejected = false;
            try { TileSeamAnalyzer.AnalyzeEdges([0], [0], red, green); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Malformed BGRA accepted");
            rejected = false;
            try { TileSeamAnalysis.Summarize([1], double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid threshold accepted");
        });
        check("Tile inspector WPF: entry, tiling axes, heatmap, zoom, reload and file watching", RunUi);
        TileSetChecks.Run(check);
    }

    private static void RunUi()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifyUi(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Tile inspector UI timed out");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    private static void VerifyUi()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "tiles-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var home = new MainWindow(Path.Combine(folder, "library")) { ShowActivated = false };
        TileInspectorWindow? window = null;
        try
        {
            home.Show(); Pump();
            Get<Button>(home, "homeTilesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            window = (TileInspectorWindow)typeof(MainWindow).GetField("_tileInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
            Require(window.IsVisible && Get<StackPanel>(window, "onboarding").IsVisible, "Separate tool did not open from home");
            Capture(window, Path.Combine(folder, "onboarding.png"));
            string file = Path.Combine(folder, "fixture.png");
            SaveFixture(file, false);
            Await(window.LoadFileAsync(file)); Pump();
            var preview = Get<TilePreview>(window, "preview");
            Require(preview.Source!.PixelWidth == 16 && preview.Source.PixelHeight == 12 && preview.Columns == 3 && preview.Rows == 3,
                "Image dimensions or repeat count incorrect");
            Require(window.Analysis!.Horizontal[5] > 50 && window.Analysis.Horizontal[4] == 0 && window.Analysis.Vertical.All(v => v == 0),
                "File edge extraction lost row positions or included interior pixels");
            Require(Get<TextBlock>(window, "xSummary").Text.Contains("1/12") && Get<TextBlock>(window, "ySummary").Text.Contains("края совпадают"),
                "Summary does not identify the single broken row");
            Capture(window, Path.Combine(folder, "both-axes-heat.png"));
            var heatOn = Render(preview);
            Click(window, "heatInput", false);
            var heatOff = Render(preview);
            Require(!Pixels(heatOn).SequenceEqual(Pixels(heatOff)), "Heatmap toggle did not change rendering");
            Capture(window, Path.Combine(folder, "both-axes-clean.png"));
            Click(window, "heatInput", true);
            Get<Slider>(window, "opacityInput").Value = 0; Pump();
            Require(Pixels(Render(preview)).SequenceEqual(Pixels(heatOff)), "Zero opacity did not restore source rendering");
            Get<Slider>(window, "opacityInput").Value = 70;
            Get<Slider>(window, "thresholdInput").Value = 0;
            Require(Get<TextBlock>(window, "xSummary").Text.Contains("1/12"), "Zero threshold summary wrong");
            Click(window, "repeatYInput", false);
            Require(preview.Columns == 3 && preview.Rows == 1 && Get<TextBlock>(window, "ySummary").Text.Contains("выключено"), "X-only mode incorrect");
            Capture(window, Path.Combine(folder, "x-only.png"));
            Click(window, "repeatXInput", false); Click(window, "repeatYInput", true);
            Require(preview.Columns == 1 && preview.Rows == 3, "Y-only mode incorrect");
            Capture(window, Path.Combine(folder, "y-only.png"));
            Click(window, "repeatYInput", false);
            Require(preview.Columns == 1 && preview.Rows == 1, "Single tile mode incorrect");
            Click(window, "repeatXInput", true); Click(window, "repeatYInput", true);
            Get<ComboBox>(window, "copiesInput").SelectedIndex = 2;
            Get<ComboBox>(window, "zoomInput").SelectedIndex = 6; Pump();
            Require(preview.Columns == 7 && preview.Rows == 7 && preview.PixelScale == 32 &&
                Get<ScrollViewer>(window, "previewScroll").ScrollableWidth > 0 && Get<ScrollViewer>(window, "previewScroll").ScrollableHeight > 0,
                "Fixed pixel zoom or two-axis scrolling failed");
            Get<ComboBox>(window, "zoomInput").SelectedIndex = 0;
            Get<ComboBox>(window, "copiesInput").SelectedIndex = 0;
            window.Width = 720; window.Height = 560; Pump();
            Require(preview.IsVisible && preview.ActualWidth <= Get<ScrollViewer>(window, "previewScroll").ViewportWidth + 1,
                "Fit mode clipped in minimum layout");
            Capture(window, Path.Combine(folder, "minimum-layout.png"));
            window.Width = 1180; window.Height = 800; Pump();

            var previous = window.Analysis;
            Await(window.LoadFileAsync(Path.Combine(folder, "missing.png")));
            Require(ReferenceEquals(previous, window.Analysis) && Get<TextBlock>(window, "statusLabel").Text.Contains("предыдущая версия"),
                "Failed import destroyed the loaded tile");
            SaveFixture(file, true);
            WaitUntil(() => window.Analysis!.Horizontal.All(v => v == 0));
            Require(Get<TextBlock>(window, "statusLabel").Text.Contains("Обновлено"), "Watcher did not report successful refresh");
            // Atomic rename/replacement is used by sprite editors when saving.
            string replacement = Path.Combine(folder, "replacement.png");
            SaveFixture(replacement, false);
            File.Move(replacement, file, true);
            WaitUntil(() => window.Analysis!.Horizontal[5] > 50);
            Click(window, "watchInput", false);
            previous = window.Analysis;
            SaveFixture(file, true);
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 900) { Pump(); Thread.Sleep(10); }
            Require(ReferenceEquals(previous, window.Analysis), "Disabled watcher still refreshed");
            Get<Button>(window, "reloadButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => window.Analysis!.Horizontal.All(v => v == 0));
            Get<ComboBox>(window, "backgroundInput").SelectedIndex = 1;
            Capture(window, Path.Combine(folder, "matching-edges.png"));
            string latestFile = Path.Combine(folder, "latest.png");
            SaveFixture(latestFile, false);
            var olderLoad = window.LoadFileAsync(file);
            var latestLoad = window.LoadFileAsync(latestFile);
            Await(Task.WhenAll(olderLoad, latestLoad));
            Require(window.SourcePath == latestFile && window.Analysis!.Horizontal[5] > 50,
                "An older load replaced the most recently selected file");
            Click(window, "watchInput", true);
            // Changes in a formerly watched file must not interfere with the newly selected source.
            SaveFixture(file, false);
            SaveFixture(latestFile, true);
            WaitUntil(() => window.Analysis!.Horizontal.All(v => v == 0));
            Require(window.SourcePath == latestFile, "Watcher followed the previous source");
            VerifyCollection(window, folder);
            Console.WriteLine($"  Tile inspector captures: {folder}");
        }
        finally { window?.DiscardAndClose(); home.Close(); }
    }

    private static void SaveFixture(string path, bool matching)
    {
        using var bitmap = new Bitmap(16, 12);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(58, 69, 86));
            graphics.FillRectangle(System.Drawing.Brushes.CadetBlue, 4, 3, 8, 6);
            graphics.FillRectangle(System.Drawing.Brushes.LightSlateGray, 6, 4, 4, 3);
        }
        bitmap.SetPixel(0, 5, matching ? Color.FromArgb(58, 69, 86) : Color.White);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void VerifyCollection(TileInspectorWindow window, string folder)
    {
        string aPath = Path.Combine(folder, "tile-a.png"), bPath = Path.Combine(folder, "tile-b.png");
        using (var a = new Bitmap(8, 6))
        using (var b = new Bitmap(8, 6))
        {
            using (var graphics = Graphics.FromImage(a)) graphics.Clear(Color.Black);
            using (var graphics = Graphics.FromImage(b)) graphics.Clear(Color.White);
            for (int q = 0; q < 6; q++) a.SetPixel(7, q, Color.White);
            for (int q = 0; q < 8; q++) a.SetPixel(q, 5, Color.White);
            // Corner pixels belong to both directions; assertions below use interior edge samples.
            a.Save(aPath, System.Drawing.Imaging.ImageFormat.Png);
            b.Save(bPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        int beforeCount = window.Assets.Count;
        Await(window.AddFilesAsync([aPath, bPath]));
        Require(window.Assets.Count == beforeCount + 2, "Multiple imports did not build a collection");
        var assetA = window.Assets.Single(asset => asset.SourcePath == aPath);
        var assetB = window.Assets.Single(asset => asset.SourcePath == bPath);
        window.SelectAsset(assetA.Id); window.SelectCompanion(assetB.Id); Pump();
        Require(window.Analysis!.Horizontal[2] == 0 && window.Analysis.Vertical[2] == 0 &&
            window.ReverseAnalysis!.Horizontal[2] > 99 && window.ReverseAnalysis.Vertical[2] > 99,
            "A->B and B->A seam orientations were confused");
        var preview = Get<TilePreview>(window, "preview");
        Require(preview.Companion == assetB.Image && preview.Hit(new System.Windows.Point(8.5 * preview.PixelScale, 2.5 * preview.PixelScale))?.Companion == true,
            "Alternating preview or hit testing does not identify B");
        var rendered = Render(preview);
        byte[] view = Pixels(rendered);
        int green = ((int)(2.5 * preview.PixelScale) * rendered.PixelWidth + (int)(8 * preview.PixelScale - 1)) * 4;
        int red = ((int)(2.5 * preview.PixelScale) * rendered.PixelWidth + (int)(16 * preview.PixelScale - 1)) * 4;
        Capture(window, Path.Combine(folder, "collection-pair.png"));
        Require(view[green + 1] > view[green + 2] && view[red + 2] > view[red + 1],
            $"Heatmap direction incorrect: scale={preview.PixelScale}, green BGRA={string.Join(',', view.Skip(green).Take(4))}, red BGRA={string.Join(',', view.Skip(red).Take(4))}. Captures: {folder}");

        byte[] originalA = (byte[])assetA.Pixels.Clone();
        Get<ComboBox>(window, "paintToolInput").SelectedIndex = 1;
        Get<TextBox>(window, "paintColorInput").Text = "#FF000000";
        Paint(new TilePointer(false, 7, 2, 0, 0), true);
        Paint(new TilePointer(false, 7, 4, 0, 0), false);
        // Crossing into another copy must not accidentally change B.
        Paint(new TilePointer(true, 0, 4, 1, 0), false);
        Finish();
        Require(assetA.GetPixel(7, 3) == System.Windows.Media.Colors.Black && assetB.GetPixel(0, 4) == System.Windows.Media.Colors.White &&
            window.Analysis!.Horizontal[3] > 99 && assetA.CanUndo, "Continuous pencil stroke, asset isolation or live analysis failed");
        Get<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(assetA.Pixels.SequenceEqual(originalA), "Undo did not restore the complete stroke");
        Get<Button>(window, "redoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(assetA.GetPixel(7, 3) == System.Windows.Media.Colors.Black, "Redo lost the stroke");
        Get<ComboBox>(window, "paintToolInput").SelectedIndex = 2;
        Paint(new TilePointer(true, 0, 2, 1, 0), true); Finish();
        Require(assetB.GetPixel(0, 2).A == 0, "Eraser did not clear alpha in B");
        Get<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(assetB.GetPixel(0, 2) == System.Windows.Media.Colors.White, "Undo did not target the last painted asset B");
        Get<ComboBox>(window, "paintToolInput").SelectedIndex = 3;
        Paint(new TilePointer(true, 0, 2, 1, 0), true);
        Require(Get<TextBox>(window, "paintColorInput").Text == "#FFFFFFFF", "Eyedropper sampled the heat overlay instead of the source");
        Get<ComboBox>(window, "paintToolInput").SelectedIndex = 0;
        using (var changed = new Bitmap(8, 6)) changed.Save(aPath, System.Drawing.Imaging.ImageFormat.Png);
        WaitUntil(() => Get<TextBlock>(window, "statusLabel").Text.Contains("локальные правки"));
        Require(assetA.GetPixel(7, 3) == System.Windows.Media.Colors.Black && assetA.GetPixel(7, 1) == System.Windows.Media.Colors.White,
            "External file watching overwrote local editing");

        string project = Path.Combine(folder, "collection.ttiles");
        window.SaveCollectionToFile(project);
        byte[] expectedA = (byte[])assetA.Pixels.Clone();
        File.Move(aPath, aPath + ".moved");
        window.LoadCollectionFromFile(project); Pump();
        Require(window.Assets.Count == beforeCount + 2 && window.ActiveAsset!.Id == assetA.Id &&
            window.ActiveAsset.Pixels.SequenceEqual(expectedA) && preview.Companion is not null &&
            window.Assets.Single(asset => asset.Id == assetA.Id).HasLocalEdits,
            "Project did not embed edited images, IDs, local-edit protection or selected pair");
        string exported = Path.Combine(folder, "tile-edited.png");
        window.ExportActivePng(exported);
        using (var result = new Bitmap(exported)) Require(result.Width == 8 && result.Height == 6 && result.GetPixel(7, 3).ToArgb() == Color.Black.ToArgb(), "PNG export lost dimensions or edits");
        bool protectedFile = false;
        try { window.ExportActivePng(bPath); } catch (IOException) { protectedFile = true; }
        Require(protectedFile, "PNG export could overwrite an imported source");
        string corrupt = Path.Combine(folder, "broken.ttiles"); File.WriteAllText(corrupt, "invalid");
        var selectedBefore = window.ActiveAsset;
        try { window.LoadCollectionFromFile(corrupt); } catch (InvalidDataException) { }
        Require(window.ActiveAsset == selectedBefore && window.ActiveAsset!.Pixels.SequenceEqual(expectedA), "Failed project load destroyed the collection");
        window.SelectAsset(window.Assets.First(asset => asset.Width != 8).Id);
        bool incompatible = false;
        try { window.SelectCompanion(assetB.Id); } catch (ArgumentException) { incompatible = true; }
        Require(incompatible, "Different-size tiles were silently stretched into a pair");

        string smoothPath = Path.Combine(folder, "smooth.png");
        byte[] expectedSmooth;
        using (var bitmap = new Bitmap(9, 9))
        {
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.FromArgb(100, 100, 100));
            bitmap.SetPixel(4, 4, Color.FromArgb(110, 110, 110));
            bitmap.SetPixel(0, 3, Color.FromArgb(110, 110, 110));
            bitmap.SetPixel(2, 2, Color.FromArgb(127, 95, 95, 95));
            bitmap.Save(smoothPath, System.Drawing.Imaging.ImageFormat.Png);
            using var expected = IndependentImageProcessor.ApplyNeighborColors(bitmap, new DownscaleOptions { LocalColorPasses = 2 });
            expectedSmooth = ReadDrawingPixels(expected);
        }
        Await(window.LoadFileAsync(smoothPath));
        var smoothAsset = window.ActiveAsset!;
        byte[] beforeSmooth = (byte[])smoothAsset.Pixels.Clone();
        var smoothTask = window.SmoothActiveAsync(2, false);
        Require(!Get<Button>(window, "smoothButton").IsEnabled && !Get<ListBox>(window, "assetList").IsEnabled, "Smoothing did not protect its working asset");
        Await(smoothTask);
        Require(Get<Button>(window, "exportButton").IsEnabled && Get<Button>(window, "reloadButton").IsEnabled,
            "Smoothing left export or reload disabled");
        Require(smoothAsset.Pixels.SequenceEqual(expectedSmooth) && !smoothAsset.Pixels.SequenceEqual(beforeSmooth) && smoothAsset.GetPixel(2, 2).A == 127,
            "Tool smoothing differs from the existing shared algorithm or changed alpha");
        Get<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(smoothAsset.Pixels.SequenceEqual(beforeSmooth), "Smoothing cannot be undone as one action");
        Await(window.SmoothActiveAsync(2, true));
        Require(smoothAsset.GetPixel(4, 4).R == 110 && smoothAsset.GetPixel(0, 3).R == 100,
            "Edge-only smoothing modified the interior or did not smooth the edge");
        Capture(window, Path.Combine(folder, "collection-smoothing.png"));
        window.SelectAsset(assetA.Id); window.SelectCompanion(assetB.Id);
        window.Width = 720; window.Height = 560; Pump();
        Require(preview.ActualHeight > 40 && Get<Expander>(window, "editTools").IsVisible, "Pair editor lost its preview at minimum size");
        Capture(window, Path.Combine(folder, "collection-minimum.png"));
        Get<Expander>(window, "editTools").IsExpanded = true; Pump();
        window.UpdateLayout();
        Capture(window, Path.Combine(folder, "collection-minimum-expanded.png"));
        Require(Get<ScrollViewer>(window, "editScroll").ScrollableHeight > 0 && preview.ActualHeight > 20,
            $"Expanded tools: scroll={Get<ScrollViewer>(window, "editScroll").ScrollableHeight}, height={Get<ScrollViewer>(window, "editScroll").ActualHeight}, preview={preview.ActualHeight}, viewport={Get<ScrollViewer>(window, "previewScroll").ViewportHeight}. Captures: {folder}");
        window.Width = 1180; window.Height = 800; Pump();
        window.SaveCollectionToFile(Path.Combine(folder, "collection-final.ttiles"));

        void Paint(TilePointer point, bool begin) => typeof(TileInspectorWindow).GetMethod("HandlePaint", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [point, begin]);
        void Finish() => typeof(TileInspectorWindow).GetMethod("FinishStroke", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
    }

    private static byte[] ReadDrawingPixels(Bitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y); int q = (y * bitmap.Width + x) * 4;
                pixels[q] = color.B; pixels[q + 1] = color.G; pixels[q + 2] = color.R; pixels[q + 3] = color.A;
            }
        return pixels;
    }

    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Click(TileInspectorWindow window, string name, bool value)
    {
        var box = Get<CheckBox>(window, name);
        box.IsChecked = value;
        box.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
    }
    private static void Await(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void WaitUntil(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed < TimeSpan.FromSeconds(12)) { Pump(); Thread.Sleep(10); }
        Pump(); Require(predicate(), "Timed out waiting for tile update");
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        // A VisualBrush excludes the element's parent-layout offset from the capture.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual); return bitmap;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    private static void Capture(FrameworkElement element, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(element)));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
