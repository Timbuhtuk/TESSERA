using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class MaterialMapChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Material maps: flat field, arbitrary dimensions and original alpha", Flat);
        check("Material maps: gradients, cavities and normal conventions", Relief);
        check("Material maps: independent paint layers survive height regeneration and separate reset", Layers);
        check("Material maps: interpolated strokes, soft brush, eraser and undo/redo", Painting);
        check("Material maps: portable lossless project and invalid-file rejection", Persistence);
        check("Material maps: megapixel generation keeps dimensions and finite maps", LargeImage);
        check("Material maps: asset Tools integration, all canvases, PNGs and compact layout", UI);
        check("Material maps: shared four-pane viewport and workspace asset persistence", WorkspaceUI);
    }
    private static MaterialMapDocument Fixture(int width = 17, int height = 11)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int q = 0; q < width * height; q++)
        { pixels[q * 4] = pixels[q * 4 + 1] = pixels[q * 4 + 2] = 128; pixels[q * 4 + 3] = 255; }
        return new(width, height, pixels);
    }
    private static void Flat()
    {
        var document = Fixture();
        Require(document.GeneratedNormals.All(n => n == Vector3.UnitZ) && document.GeneratedAO.All(a => a == 1), "Flat field has false relief/occlusion");
        foreach (var dimensions in new[] { (1, 1), (1, 19), (23, 1), (4097, 3) })
        {
            var image = Fixture(dimensions.Item1, dimensions.Item2);
            Require(image.MapPixels(MaterialMapKind.Normal).Length == dimensions.Item1 * dimensions.Item2 * 4, "Non-square image was cropped");
        }
        var pixels = (byte[])document.Source.Clone(); pixels[3] = 0; pixels[7] = 91;
        var alpha = new MaterialMapDocument(document.Width, document.Height, pixels);
        foreach (var kind in Enum.GetValues<MaterialMapKind>())
        {
            byte[] output = alpha.MapPixels(kind);
            Require(output[3] == 0 && output[7] == 91, "Map destroyed source transparency");
        }
        Require(alpha.LitPixels(alpha.Width, alpha.Height, Vector3.UnitZ, true, true, true).SequenceEqual(pixels), "Original comparison changes colors or alpha");
        Require(document.Source.All(b => b is 128 or 255), "Generation mutated the source");
    }
    private static void Relief()
    {
        var document = Fixture(9, 9);
        for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++) document.Heights[y * 9 + x] = (x + y) / 16f;
        document.Recalculate();
        Require(document.GeneratedNormals[40].X < 0 && document.GeneratedNormals[40].Y > 0, "Height gradients point the wrong way");
        byte[] dx = document.MapPixels(MaterialMapKind.Normal); document.DirectX = false;
        byte[] gl = document.MapPixels(MaterialMapKind.Normal);
        Require(dx[161] + gl[161] == 255 && dx[160] == gl[160] && dx[162] == gl[162], "Convention changed channels other than green");
        Array.Fill(document.Heights, 1); document.Heights[40] = 0; document.Recalculate();
        Require(document.GeneratedAO[40] < .9f && document.GeneratedAO[0] == 1, "AO did not darken a cavity or darkened an exposed plateau");
        document.NormalStrength = 0; document.Recalculate();
        Require(document.GeneratedNormals.All(n => n == Vector3.UnitZ), "Zero strength does not flatten generated normals");
    }
    private static void Layers()
    {
        var document = Fixture();
        var target = Vector3.Normalize(new Vector3(.8f, .3f, 1));
        document.PaintLine(MaterialMapKind.Normal, 5, 5, 5, 5, 1, .5f, target, 1, 0, false);
        document.PaintLine(MaterialMapKind.AO, 5, 5, 5, 5, 1, .2f, Vector3.UnitZ, 1, 0, false);
        var original = document.Capture();
        document.PaintLine(MaterialMapKind.Height, 4, 5, 4, 5, 1, 1, Vector3.UnitZ, 1, 0, false); document.Recalculate();
        int index = 5 * document.Width + 5;
        Require(document.GeneratedNormals[index].X > 0 && Vector3.Distance(document.NormalAt(index), target) < .00001f, "Height destroyed manual normal");
        Require(document.AoAt(index) == .2f && original.NormalMask.SequenceEqual(document.NormalMask) && original.OcclusionMask.SequenceEqual(document.AoMask), "Height changed manual layer contents");
        document.ResetManual(MaterialMapKind.Normal);
        Require(document.NormalAt(index) == document.GeneratedNormals[index] && document.AoAt(index) == .2f, "Normal reset affected AO");
        document.Undo(); Require(Vector3.Distance(document.NormalAt(index), target) < .00001f, "Cannot undo a manual layer reset");
        document.ResetManual(MaterialMapKind.AO);
        Require(document.AoAt(index) == document.GeneratedAO[index] && Vector3.Distance(document.NormalAt(index), target) < .00001f, "AO reset affected Normal");
    }
    private static void Painting()
    {
        var document = Fixture(); var before = document.Capture();
        document.PaintLine(MaterialMapKind.Height, 0, 5, 16, 5, 1, 1, Vector3.UnitZ, 1, 0, false);
        document.Recalculate(); Require(document.Commit(before), "Stroke was not recorded");
        Require(Enumerable.Range(0, 17).All(x => document.Heights[85 + x] == 1), "Fast stroke has holes");
        document.Undo(); Require(document.Heights.SequenceEqual(before.Heights), "Undo failed to restore height");
        document.Redo(); Require(document.Heights[85] == 1 && document.Heights[101] == 1, "Redo failed");
        document.PaintLine(MaterialMapKind.AO, 8, 5, 8, 5, 7, 0, Vector3.UnitZ, 1, 1, false);
        Require(document.AoMask[93] > document.AoMask[91] && document.AoMask[91] > 0, "Softness has no radial falloff");
        document.PaintLine(MaterialMapKind.AO, 8, 5, 8, 5, 1, 1, Vector3.UnitZ, 1, 0, true);
        Require(document.AoMask[93] == 0, "Eraser did not uncover the generated AO");
        document.Source[3] = 0;
        document.PaintLine(MaterialMapKind.Normal, 0, 0, 0, 0, 1, 1, Vector3.UnitX, 1, 0, false);
        Require(document.NormalMask[0] == 0, "Brush painted outside source alpha");
    }
    private static void Persistence()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "material-map-tests")); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roundtrip.tmaps");
        var document = Fixture();
        document.PaintLine(MaterialMapKind.Height, 1, 1, 8, 7, 3, .9f, Vector3.UnitZ, .7f, .5f, false);
        document.PaintLine(MaterialMapKind.Normal, 3, 4, 7, 5, 2, 1, Vector3.Normalize(new Vector3(1, 1, 1)), .65f, .4f, false);
        document.PaintLine(MaterialMapKind.AO, 3, 3, 8, 5, 2, .1f, Vector3.UnitZ, .3f, .2f, false);
        document.Recalculate(); MaterialMapFile.Save(document, path);
        var loaded = MaterialMapFile.Load(path);
        foreach (var kind in Enum.GetValues<MaterialMapKind>()) Require(document.MapPixels(kind).SequenceEqual(loaded.MapPixels(kind)), "Project changed " + kind);
        Require(document.NormalMask.SequenceEqual(loaded.NormalMask) && document.AoMask.SequenceEqual(loaded.AoMask) && !loaded.HasChanges, "Project lost masks or dirty state");
        loaded.PaintLine(MaterialMapKind.Height, 8, 8, 8, 8, 1, 0, Vector3.UnitZ, 1, 0, false); loaded.Recalculate();
        Require(loaded.NormalMask.SequenceEqual(document.NormalMask), "Reloaded manual layer became destructive");
        string invalid = Path.Combine(folder, "invalid.tmaps"); File.WriteAllText(invalid, "not a material");
        try { MaterialMapFile.Load(invalid); throw new Exception("Invalid project accepted"); } catch (InvalidDataException) { }
    }
    private static void LargeImage()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var document = Fixture(1024, 1024);
        for (int y = 0; y < 1024; y++) for (int x = 0; x < 1024; x++) document.Heights[y * 1024 + x] = ((x / 16 + y / 16) % 2) * .8f;
        document.Recalculate();
        Require(document.GeneratedAO.All(float.IsFinite) && document.GeneratedNormals.All(n => Math.Abs(n.LengthSquared() - 1) < .001f), "Large maps contain non-finite or unnormalized data");
        Console.WriteLine($"  1024 × 1024, initial generation + height regeneration: {timer.Elapsed.TotalMilliseconds:F0} ms");
    }
    private static void UI()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifyUI(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(55))) throw new Exception("Material map UI verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw new Exception("Material map UI failed: " + failure.Message, failure); }
    }
    private static void VerifyUI()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "material-map-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "sprite.png");
        using (var image = new Bitmap(32, 24))
        {
            using var graphics = Graphics.FromImage(image); graphics.Clear(Color.Gray); image.SetPixel(0, 0, Color.Transparent);
            image.SetPixel(1, 0, Color.FromArgb(90, 70, 80, 90)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        byte[] source = File.ReadAllBytes(path);
        var main = new MainWindow(Path.Combine(folder, "library")) { ShowActivated = false };
        MaterialMapsWindow? maps = null;
        try
        {
            main.EnableWorkspacePrototype(); main.Show(); Pump();
            Require(!((MenuItem)main.FindName("materialMapsMenuItem")).IsEnabled, "Maps command enabled without an image");
            Wait(main.ImportWorkspaceFilesAsync([path])); Wait(main.OpenWorkspaceAssetAsync(main.Workspace!.State.Assets.Single()));
            NavigationTest.Click(main, "materialMapsMenuItem");
            Until(() => main.OwnedWindows.OfType<MaterialMapsWindow>().Any(w => !w.IsBusy && w.Document is not null));
            maps = main.OwnedWindows.OfType<MaterialMapsWindow>().Single();
            Require(maps.Document!.Width == 32 && maps.Document.Height == 24, "Tools opened the wrong asset");
            NavigationTest.Click(main, "materialMapsMenuItem");
            Require(main.OwnedWindows.OfType<MaterialMapsWindow>().Count() == 1, "Repeated Tools command lost the session or duplicated it");
            Require(new[] { "heightView", "normalView", "aoView", "litView" }.All(n => ((MaterialMapView)maps.FindName(n)).Image is not null), "One of the four previews is missing");
            VerifyGrid(maps);
            // Exercise the same controller used by pointer strokes, including linked regeneration.
            Paint(maps, MaterialMapKind.Normal, 6, 6); Wait(maps.FinishStrokeAsync());
            Paint(maps, MaterialMapKind.AO, 6, 6); Wait(maps.FinishStrokeAsync());
            var normal = (float[])maps.Document.NormalMask.Clone(); var ao = (float[])maps.Document.AoMask.Clone();
            ((Slider)maps.FindName("valueInput")).Value = 1;
            Paint(maps, MaterialMapKind.Height, 5, 6); Wait(maps.FinishStrokeAsync());
            Require(normal.SequenceEqual(maps.Document.NormalMask) && ao.SequenceEqual(maps.Document.AoMask), "UI height edit destroyed manual layers");
            ((Button)maps.FindName("resetNormalButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(maps.Document.NormalMask.All(v => v == 0) && ao.SequenceEqual(maps.Document.AoMask), "UI reset is not independent");
            ((Button)maps.FindName("undoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(normal.SequenceEqual(maps.Document.NormalMask), "UI undo did not restore the normal layer");
            foreach (var kind in Enum.GetValues<MaterialMapKind>())
            {
                string output = Path.Combine(folder, kind + ".png"); maps.ExportMap(kind, output);
                using var image = new Bitmap(output);
                Require(image.Width == 32 && image.Height == 24 && image.GetPixel(0, 0).A == 0 && image.GetPixel(1, 0).A == 90, "PNG dimensions/alpha changed");
            }
            bool protectedSource = false;
            try { maps.ExportMap(MaterialMapKind.AO, path); } catch (InvalidOperationException) { protectedSource = true; }
            Require(protectedSource && File.ReadAllBytes(path).SequenceEqual(source), "Export can overwrite the original");
            string project = Path.Combine(folder, "edited.tmaps"); maps.SaveProject(project);
            Require(!maps.Document.HasChanges && MaterialMapFile.Load(project).AoMask.SequenceEqual(ao), "UI save lost edits");
            // Save immediately after a slider edit, before its debounce tick.
            ((Slider)maps.FindName("normalStrengthInput")).Value = 7;
            maps.SaveProject(project);
            Require(MaterialMapFile.Load(project).NormalStrength == 7, "Save discarded a pending generation setting");
            Snapshot(maps, Path.Combine(folder, "wide.png"));
            maps.Width = 860; maps.Height = 620; Pump();
            Require(new[] { "heightView", "normalView", "aoView" }.All(n => ((MaterialMapView)maps.FindName(n)).ActualWidth > 100), "Compact layout loses a canvas");
            VerifyGrid(maps);
            Snapshot(maps, Path.Combine(folder, "compact.png"));
            var entry = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            using (var result = new Bitmap(7, 5))
            {
                using var graphics = Graphics.FromImage(result); graphics.Clear(Color.Coral);
                var library = (ImageLibrary)typeof(MainWindow).GetField("_library", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                var generation = library.SaveGeneration(entry, result, null, new GenerationEntry()); entry.Generations.Add(generation);
                typeof(MainWindow).GetMethod("SelectGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [generation]);
                main.ProcessingBasis = 1;
            }
            NavigationTest.Click(main, "materialMapsMenuItem");
            Until(() => main.OwnedWindows.OfType<MaterialMapsWindow>().Any(w => !w.IsBusy && w.Document?.Width == 7));
            var resultMaps = main.OwnedWindows.OfType<MaterialMapsWindow>().Single(w => w != maps);
            Require(resultMaps.Document!.Height == 5 && maps.Document.Width == 32 && maps.Document.AoMask.SequenceEqual(ao), "Result basis changed dimensions or redirected source edits");
            resultMaps.Close();
        }
        finally { maps?.Document?.MarkSaved(); maps?.Close(); main.Close(); Pump(); }
    }
    private static void VerifyGrid(MaterialMapsWindow window)
    {
        Pump(); window.UpdateLayout();
        var views = new[] { "heightView", "normalView", "aoView", "litView" }.Select(n => (MaterialMapView)window.FindName(n)).ToArray();
        var positions = views.Select(v => v.TranslatePoint(new Point(), window)).ToArray();
        Require(Math.Abs(positions[0].Y - positions[1].Y) < 1 && Math.Abs(positions[2].Y - positions[3].Y) < 1 &&
            positions[2].Y > positions[0].Y && Math.Abs(positions[0].X - positions[2].X) < 1 && Math.Abs(positions[1].X - positions[3].X) < 1, "Four panes are not a 2 by 2 square");
        Require(views.All(v => Math.Abs(v.ActualWidth - views[0].ActualWidth) <= 1 && Math.Abs(v.ActualHeight - views[0].ActualHeight) <= 1), "Viewport sizes differ");
    }

    private static void WorkspaceUI()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { VerifyWorkspace(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(70))) throw new Exception("Material workspace verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw new Exception("Material workspace failed: " + failure.Message, failure); }
    }

    private static void VerifyWorkspace()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "material-workspace-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "relief.png"), libraryPath = Path.Combine(folder, "library");
        using (var bitmap = new Bitmap(769, 577))
        { using var draw = Graphics.FromImage(bitmap); draw.Clear(Color.Gray); bitmap.SetPixel(0, 0, Color.Transparent); bitmap.Save(path); }
        byte[] original = File.ReadAllBytes(path);
        MainWindow main = new(libraryPath) { ShowActivated = false };
        MaterialMapsWindow? maps = null;
        string? saveError = null;
        var dialogWatch = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(100) };
        dialogWatch.Tick += (_, _) =>
        {
            foreach (var dialog in main.OwnedWindows.OfType<TesseraDialog>().ToArray())
            {
                saveError = ((TextBlock)dialog.FindName("dialogMessage")).Text;
                Console.Error.WriteLine("  Workspace unexpected dialog: " + saveError);
                dialog.Close();
            }
        };
        dialogWatch.Start();
        try
        {
            main.EnableWorkspacePrototype(); main.Show(); Wait(main.ImportWorkspaceFilesAsync([path]));
            var catalog = main.Workspace!; var source = catalog.State.Assets.Single();
            var project = catalog.CreateProject("Maps"); var other = catalog.CreateProject("Elsewhere");
            var leaf = catalog.CreateFolder(project.Id, null, "Materials");
            catalog.Link(source.Id, project.Id, leaf.Id); catalog.Link(source.Id, other.Id, null);
            main.Browser!.Navigate("project", project.Id, leaf.Id);
            Wait(main.OpenWorkspaceAssetAsync(source)); NavigationTest.Click(main, "materialMapsMenuItem");
            Until(() => main.OwnedWindows.OfType<MaterialMapsWindow>().Any(w => !w.IsBusy && w.Document is not null) && catalog.State.Assets.Count == 4);
            maps = main.OwnedWindows.OfType<MaterialMapsWindow>().Single();
            var session = catalog.State.MaterialSessions.Single();
            Console.WriteLine("  Workspace: generated sibling assets");
            Require(Enum.GetValues<MaterialMapKind>().All(k => catalog.State.Links.Any(l => l.AssetId == session.MapId(k) && l.ProjectId == project.Id && l.FolderId == leaf.Id)), "Maps are not siblings in the source folder");
            Require(catalog.State.Links.Count(l => l.ProjectId == other.Id) == 1, "Current-folder generation added assets to another project");
            Require(((Button)maps.FindName("saveProjectButton")).Visibility == Visibility.Collapsed && ((Button)maps.FindName("returnButton")).Visibility == Visibility.Visible, "Workspace still requires standalone map-project saving");
            VerifyGrid(maps);
            var views = new[] { "heightView", "normalView", "aoView", "litView" }.Select(n => (MaterialMapView)maps.FindName(n)).ToArray();
            Require(views[3].Image!.PixelWidth == 512 && views[3].LogicalSize.Width == 769, "Lighting preview lost full-resolution coordinates");
            var zoom = (ComboBox)maps.FindName("zoomInput"); zoom.SelectedIndex = 2; Pump();
            foreach (var view in views)
            {
                view.CenterOn(new Point(380, 280)); Pump();
                var scroll = (ScrollViewer)view.Children[0];
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 37); scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 23); Pump();
                Require(views.All(v => Math.Abs(v.Scale - view.Scale) < .00001 && (v.ViewCenter - view.ViewCenter).Length < 1), "Scrollbars or zoom diverged from " + view.Name);
                double before = scroll.VerticalOffset;
                scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
                scroll.UpdateLayout(); Pump();
                Require(scroll.VerticalOffset > before && views.All(v => (v.ViewCenter - view.ViewCenter).Length < 1), "Mouse wheel did not synchronize " + view.Name);
            }
            zoom.SelectedIndex = 3; Pump();
            Require(views.All(v => Math.Abs(v.Scale - 4) < .00001 && (v.ViewCenter - views[0].ViewCenter).Length < 1), "Zoom changed the common center");
            maps.Width = 860; maps.Height = 620; Pump(); VerifyGrid(maps);
            zoom.SelectedIndex = 0; Pump();
            Console.WriteLine("  Workspace: synchronized viewport");
            Require(views.All(v => Math.Abs(v.Scale - views[0].Scale) < .00001), "Fit uses different scales");
            Paint(maps, MaterialMapKind.Normal, 8, 8); Wait(maps.FinishStrokeAsync());
            Paint(maps, MaterialMapKind.AO, 9, 9); Wait(maps.FinishStrokeAsync());
            var normals = (float[])maps.Document!.NormalMask.Clone(); var ao = (float[])maps.Document.AoMask.Clone();
            Until(() => !maps.Document.HasChanges);
            Console.WriteLine("  Workspace: autosaved paint layers");
            string stored = catalog.MaterialSessionPath(session);
            Require(MaterialMapFile.Load(stored).AoMask.SequenceEqual(ao), "Idle autosave lost manual layers");
            ((Slider)maps.FindName("normalStrengthInput")).Value = 5;
            Console.WriteLine("  Workspace: starting concurrent generation/save");
            Task generation = maps.ApplyGenerationAsync();
            Require(maps.IsBusy, "The concurrent-save fixture did not start generation");
            Task saving = (Task)typeof(MainWindow).GetMethod("SaveNavigationDocumentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            Console.WriteLine("  Workspace: waiting for active generation");
            Wait(generation);
            Console.WriteLine("  Workspace: waiting for queued project save");
            Wait(saving);
            Require(saveError is null, "Project save opened an error dialog: " + saveError);
            Require(MaterialMapFile.Load(stored).NormalStrength == 5, "Project save did not await the running map generation");
            Console.WriteLine("  Workspace: saved during active generation");
            ((Slider)maps.FindName("normalStrengthInput")).Value = 6;
            NavigationTest.Click(main, "saveDocumentMenuItem");
            Require(saveError is null, "Main-menu save opened an error dialog: " + saveError);
            Require(MaterialMapFile.Load(stored).NormalStrength == 6, "Whole-project save lost a pending slider change");
            Console.WriteLine("  Workspace: saved from main menu");
            Paint(maps, MaterialMapKind.AO, 10, 10); Wait(maps.FinishStrokeAsync());
            ao = (float[])maps.Document.AoMask.Clone();
            using (var locked = new FileStream(stored, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                maps.Close(); Pump();
                Require(main.OwnedWindows.OfType<MaterialMapsWindow>().Contains(maps) && maps.Document.HasChanges, "Failed persistence closed the editor or cleared dirty state");
            }
            maps.Close(); Pump(); Require(!main.OwnedWindows.OfType<MaterialMapsWindow>().Any(), "Return still waits for a file dialog"); maps = null;
            Console.WriteLine("  Workspace: returned after failed-save retry");
            Require(File.ReadAllBytes(path).SequenceEqual(original) && catalog.State.Assets.Count == 4, "Autosave changed the original or duplicated map assets");
            main.Close(); Pump();
            main = new MainWindow(libraryPath) { ShowActivated = false }; main.EnableWorkspacePrototype(); main.Show(); Pump();
            catalog = main.Workspace!; session = catalog.State.MaterialSessions.Single();
            var normalAsset = catalog.State.Assets.Single(a => a.Id == session.NormalId);
            Wait(main.OpenWorkspaceAssetAsync(normalAsset)); NavigationTest.Click(main, "materialMapsMenuItem");
            Until(() => main.OwnedWindows.OfType<MaterialMapsWindow>().Any(w => !w.IsBusy && w.Document is not null));
            maps = main.OwnedWindows.OfType<MaterialMapsWindow>().Single();
            Require(maps.Document!.NormalMask.SequenceEqual(normals) && maps.Document.AoMask.SequenceEqual(ao), "Reopening a map asset lost independent layers");
            Require(catalog.State.Assets.Count == 4 && maps.Document.NormalStrength == 6, "Reload generated duplicate assets or reset settings");
            foreach (var kind in Enum.GetValues<MaterialMapKind>())
            {
                var entry = new ImageLibrary(libraryPath).Load().Single(s => s.Id == session.MapId(kind));
                var image = TileAsset.DecodeFile(new ImageLibrary(libraryPath).SourcePath(entry));
                var pixels = new byte[769 * 577 * 4]; image.CopyPixels(pixels, 769 * 4, 0);
                Require(pixels.SequenceEqual(maps.Document.MapPixels(kind)), "Stored asset is stale: " + kind);
            }
            var relocated = catalog.CreateFolder(project.Id, null, "Moved maps");
            catalog.Link(session.HeightId, project.Id, relocated.Id);
            main.Browser!.Navigate("project", other.Id, null);
            Wait(main.OpenWorkspaceAssetAsync(catalog.State.Assets.Single(a => a.Id == session.SourceId)));
            NavigationTest.Click(main, "materialMapsMenuItem"); Pump();
            Require(main.OwnedWindows.OfType<MaterialMapsWindow>().Single() == maps, "Shared source opened a duplicate map editor");
            Require(Enum.GetValues<MaterialMapKind>().All(k => catalog.State.Links.Any(l => l.AssetId == session.MapId(k) && l.ProjectId == other.Id && l.FolderId is null)), "Existing maps were not linked beside the source in the active project");
            Require(catalog.State.Links.Single(l => l.AssetId == session.HeightId && l.ProjectId == project.Id).FolderId == relocated.Id, "Opening another project moved an existing map");
            maps.SaveWorkspace(true);
            Require(catalog.State.Links.Single(l => l.AssetId == session.HeightId && l.ProjectId == project.Id).FolderId == relocated.Id, "Autosaving reset a manually moved map");
            maps.Close(); Pump(); maps = null;
            NavigationTest.Click(main, "materialMapsMenuItem");
            Until(() => main.OwnedWindows.OfType<MaterialMapsWindow>().Any(w => !w.IsBusy && w.Document is not null));
            maps = main.OwnedWindows.OfType<MaterialMapsWindow>().Single();
            Require(catalog.State.Assets.Count == 4 && catalog.State.MaterialSessions.Count == 1 && catalog.State.Links.Count(l => l.ProjectId == other.Id) == 4, "Reopening shared maps duplicated assets, sessions or links");
            Console.WriteLine("  Workspace: shared source placement and manual moves preserved");
            Console.WriteLine("  Material workspace verification: " + folder);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); throw; }
        finally { maps?.Close(); main.Close(); Pump(); dialogWatch.Stop(); }
    }
    private static void Paint(MaterialMapsWindow maps, MaterialMapKind kind, int x, int y)
        => typeof(MaterialMapsWindow).GetMethod("Paint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(maps, [kind, x, y, true]);
    private static void Snapshot(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new Exception("UI action timed out"); Pump(); Thread.Sleep(5); }
        Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
