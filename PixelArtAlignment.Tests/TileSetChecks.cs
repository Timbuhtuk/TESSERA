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

internal static class TileSetChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Tileset: exact grids, rectangular cells, gutters and row-major regions", () =>
        {
            var layout = TileSetLayout.FromTileSize(96, 192, 32, 32);
            Require(layout.Columns == 3 && layout.Rows == 6 && layout.Count == 18 && layout == TileSetLayout.FromGrid(96, 192, 3, 6), "96x192 did not produce 3x6 cells of 32x32");
            var regions = TileSetSlicer.GetRegions(96, 192, layout);
            Require(regions[0] == new TileSetRegion(0, 0, 32, 32, 0, 0) && regions[3].X == 0 && regions[3].Y == 32 &&
                regions[^1] == new TileSetRegion(64, 160, 32, 32, 2, 5), "Tile order or last cell origin incorrect");
            var rectangular = TileSetLayout.FromTileSize(96, 128, 24, 16);
            Require(rectangular.Columns == 4 && rectangular.Rows == 8 && rectangular.Count == 32, "Rectangular cells rejected");
            var gutters = TileSetLayout.FromTileSize(100, 199, 32, 32, 1, 1, 1, 1);
            Require(gutters == TileSetLayout.FromGrid(100, 199, 3, 6, 1, 1, 1, 1), "Grid and pixel modes disagree about spacing");
            var spaced = TileSetSlicer.GetRegions(100, 199, gutters);
            Require(spaced[1].X == 34 && spaced[3].Y == 34 && spaced[^1].X == 67 && spaced[^1].Y == 166, "Margins/gutters included inside tiles");
            Reject(() => TileSetLayout.FromTileSize(95, 192, 32, 32));
            Reject(() => TileSetLayout.FromGrid(96, 192, 5, 6));
            Reject(() => TileSetLayout.FromTileSize(96, 192, 0, 32));
            Reject(() => TileSetLayout.FromTileSize(128, 128, 1, 1));
            Reject(() => TileSetSlicer.GetRegions(96, 192, new(32, 32, 4, 6)));
        });
        check("Tileset: automatic 96x192 suggestion, valid alternatives and ambiguous flat images", () =>
        {
            byte[] pixels = new byte[96 * 192 * 4];
            var suggestions = TileSetSlicer.Suggest(pixels, 96, 192);
            Require(suggestions[0].Layout.TileWidth == 32 && suggestions[0].Layout.Columns == 3 && suggestions[0].Layout.Rows == 6,
                "Common 96x192 atlas was not proposed as 3x6");
            Require(suggestions.Count > 1 && suggestions[0].Reason.Contains("Проверьте"), "Ambiguous image was presented as a unique answer");
            foreach (var suggestion in suggestions) Require(TileSetSlicer.GetRegions(96, 192, suggestion.Layout).Count == suggestion.Layout.Count, "Auto mode produced an invalid layout");
            var small = TileSetSlicer.Suggest(new byte[3 * 5 * 4], 3, 5);
            Require(small.Count == 0, "Tiny image received an invented common tile size");
            Reject(() => TileSetSlicer.Suggest(new byte[3], 96, 192));
        });
        check("Tileset WPF: auto/manual slicing, exact alpha, empty cells, collection and source protection", RunUi);
    }

    private static void RunUi()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Tileset UI verification timed out");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "tileset-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "atlas-96x192.png"), spacedPath = Path.Combine(folder, "atlas-spaced.png");
        SaveAtlas(path, false); SaveAtlas(spacedPath, true);
        byte[] originalFile = File.ReadAllBytes(path);
        var source = TileAsset.DecodeFile(path);
        byte[] originalPixels = new byte[96 * 192 * 4]; source.CopyPixels(originalPixels, 96 * 4, 0);
        var owner = new TileInspectorWindow { ShowActivated = false };
        var importer = new TileSetImportWindow { ShowActivated = false };
        try
        {
            owner.Show(); importer.Owner = owner; importer.Show(); Pump();
            Await(importer.LoadFileAsync(path));
            Require(importer.Layout == TileSetLayout.FromGrid(96, 192, 3, 6) && Get<Button>(importer, "importButton").IsEnabled, "Automatic atlas load did not enable 18-cell slicing");
            Require(Get<TileSetPreview>(importer, "atlasPreview").Layout == importer.Layout && Get<TextBlock>(importer, "layoutSummary").Text.Contains("18"), "Grid overlay or summary did not update");
            Capture(importer, Path.Combine(folder, "automatic-96x192.png"));
            Get<ComboBox>(importer, "modeInput").SelectedIndex = 1;
            Require(Get<TextBox>(importer, "firstInput").Text == "3" && Get<TextBox>(importer, "secondInput").Text == "6", "Switching to grid mode lost the size conversion");
            Get<ComboBox>(importer, "modeInput").SelectedIndex = 0;
            Get<TextBox>(importer, "firstInput").Text = "16";
            Require(importer.Layout!.Count == 36, "Manual rectangular slicing did not update");
            Get<TextBox>(importer, "firstInput").Text = "0";
            Require(importer.Layout is null && !Get<Button>(importer, "importButton").IsEnabled && Get<TileSetPreview>(importer, "atlasPreview").Layout is null,
                "Invalid dimensions left an old grid importable");
            Await(importer.DetectAsync());
            Require(importer.Layout!.Count == 18, "Auto mode did not restore a valid grid");
            var tiles = importer.BuildAssets();
            Require(tiles.Count == 18 && tiles[0].Name.EndsWith("[r01,c01]") && tiles[3].Name.EndsWith("[r02,c01]") && tiles[^1].Name.EndsWith("[r06,c03]"), "Atlas cells lost stable row/column names");
            for (int q = 0; q < tiles.Count; q++)
            {
                var tile = tiles[q]; int originX = q % 3 * 32, originY = q / 3 * 32;
                Require(tile.Width == 32 && tile.Height == 32 && tile.SourcePath is null && tile.AtlasSourcePath == path, "Tile dimensions or atlas provenance incorrect");
                for (int y = 0; y < 32; y++)
                    Require(tile.Pixels.AsSpan(y * 32 * 4, 32 * 4).SequenceEqual(originalPixels.AsSpan(((originY + y) * 96 + originX) * 4, 32 * 4)), "Slicing changed color, transparency or cell boundaries");
            }
            Get<CheckBox>(importer, "skipEmptyInput").IsChecked = true;
            Require(importer.BuildAssets().Count == 17, "Fully transparent tile was not skipped");
            Get<CheckBox>(importer, "skipEmptyInput").IsChecked = false;
            foreach (string name in new[] { "marginXInput", "marginYInput", "spacingXInput", "spacingYInput" }) Get<TextBox>(importer, name).Text = "1";
            Await(importer.LoadFileAsync(spacedPath));
            Require(importer.Layout!.Count == 18 && importer.Layout.MarginX == 1 && importer.BuildAssets()[1].GetPixel(0, 0).A == 127,
                "Gutters were not excluded from extracted cells");
            foreach (string name in new[] { "marginXInput", "marginYInput", "spacingXInput", "spacingYInput" }) Get<TextBox>(importer, name).Text = "0";
            Await(importer.LoadFileAsync(path));
            importer.Width = 640; importer.Height = 560; Pump();
            Require(Get<TileSetPreview>(importer, "atlasPreview").ActualWidth > 100 && Get<Button>(importer, "importButton").IsVisible,
                "Minimum atlas import layout lost its preview or action");
            Capture(importer, Path.Combine(folder, "minimum-layout.png"));
            owner.AddTileSetAssets(tiles); Pump();
            Require(owner.Assets.Count == 18 && owner.ActiveAsset == tiles[0], "Slices were not added as independent collection assets");
            owner.SelectCompanion(tiles[1].Id);
            Require(owner.Analysis!.Horizontal.Length == 32, "Sliced assets cannot be compared");
            var before = tiles[0].Capture();
            tiles[0].PaintLine(1, 1, 1, 1, Colors.Magenta, 1); tiles[0].FinishEdit(before);
            Require(tiles[1].GetPixel(1, 1) != Colors.Magenta && File.ReadAllBytes(path).SequenceEqual(originalFile), "Editing a slice changed another cell or source atlas");
            string project = Path.Combine(folder, "sliced.ttiles");
            owner.SaveCollectionToFile(project); owner.LoadCollectionFromFile(project);
            Require(owner.Assets.Count == 18 && owner.Assets[0].AtlasSourcePath == path && owner.Assets[0].GetPixel(1, 1) == Colors.Magenta,
                "Project lost sliced pixels, edits or atlas protection");
            bool protectedPng = false, protectedProject = false;
            try { owner.ExportActivePng(path); } catch (IOException) { protectedPng = true; }
            try { owner.SaveCollectionToFile(path); } catch (IOException) { protectedProject = true; }
            Require(protectedPng && protectedProject && File.ReadAllBytes(path).SequenceEqual(originalFile), "Export could replace the source atlas");
            Capture(owner, Path.Combine(folder, "sliced-collection.png"));
            Console.WriteLine("  Tileset captures: " + folder);
        }
        finally { importer.Close(); owner.DiscardAndClose(); }
    }

    private static void SaveAtlas(string path, bool spaced)
    {
        using var bitmap = new Bitmap(spaced ? 100 : 96, spaced ? 199 : 192);
        for (int row = 0; row < 6; row++)
            for (int column = 0; column < 3; column++)
            {
                int q = row * 3 + column, x0 = (spaced ? 1 : 0) + column * (spaced ? 33 : 32), y0 = (spaced ? 1 : 0) + row * (spaced ? 33 : 32);
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        bitmap.SetPixel(x0 + x, y0 + y, q == 5 ? Color.Transparent : Color.FromArgb(x == 0 && y == 0 ? 127 : 255, 20 + q * 11, 220 - q * 9, 40 + q * 7));
            }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Await(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "Tileset load timed out"); task.GetAwaiter().GetResult(); Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Reject(Action action)
    {
        bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid tileset settings were accepted");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
