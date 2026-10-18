using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class WorkspaceChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new Exception("Workspace verification timed out");
        if (failure is not null) { Console.Error.WriteLine(failure); throw failure; }
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string root = Path.GetFullPath(Path.Combine("artifacts", "workspace-verification", "run-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "grass.png"), second = Path.Combine(root, "stone.png");
        using (var image = new Bitmap(32, 32))
        {
            using var draw = Graphics.FromImage(image); draw.Clear(Color.Transparent); draw.FillRectangle(System.Drawing.Brushes.ForestGreen, 4, 4, 24, 24); image.Save(input);
        }
        using (var image = new Bitmap(16, 16)) { using var draw = Graphics.FromImage(image); draw.Clear(Color.Gray); image.Save(second); }
        string libraryRoot = Path.Combine(root, "library");
        var images = new ImageLibrary(libraryRoot);
        var source = images.Import(input, []);
        using var result = new Bitmap(8, 8);
        using (var draw = Graphics.FromImage(result)) draw.Clear(Color.DarkGreen);
        var first = images.SaveGeneration(source, result, null, new GenerationEntry { Operation = "Размер" }); source.Generations.Add(first);
        var last = images.SaveGeneration(source, result, null, new GenerationEntry { Operation = "Палитра", ParentGenerationId = first.Id }); source.Generations.Add(last); source.SelectedGeneration = last;
        var catalog = new WorkspaceCatalog(libraryRoot); catalog.SyncImages([source]);
        var asset = catalog.State.Assets.Single(); asset.Tags = ["лес", "трава"]; asset.Transparent = true; asset.Width = 32; asset.Height = 32; asset.SelectedResult = last.Id;
        Require(catalog.Query("unassigned", null, null, 0, "трава", 1, "32", "32", 1).Count == 1, "Standalone asset and combined filters");
        var projectA = catalog.CreateProject("Игра A"); var projectB = catalog.CreateProject("Игра B");
        var forest = catalog.CreateFolder(projectA.Id, null, "Окружение"); var grass = catalog.CreateFolder(projectA.Id, forest.Id, "Лес");
        var other = catalog.CreateFolder(projectB.Id, null, "Фоны");
        catalog.Link(asset.Id, projectA.Id, grass.Id); catalog.Link(asset.Id, projectB.Id, other.Id);
        Require(catalog.Query("project", projectA.Id, forest.Id, 0, "лес трава", 0, "", "", 0).Single().Id == asset.Id, "Nested folder search");
        asset.Tags.Add("общий"); catalog.Save();
        Require(catalog.Query("project", projectB.Id, null, 1, "общий", 0, "", "", 0).Single().Id == asset.Id, "Shared asset edits visible in both projects");
        MustFail(() => catalog.Link(asset.Id, projectB.Id, grass.Id)); MustFail(() => catalog.MoveFolder(forest.Id, grass.Id));
        var clone = catalog.CopyForProject(asset.Id, projectB.Id, images);
        Require(catalog.State.Links.Single(l => l.ProjectId == projectA.Id).AssetId == asset.Id && catalog.State.Links.Single(l => l.ProjectId == projectB.Id).AssetId == clone.Id, "Copy replaces current project membership only");
        var clonedSource = images.Load().Single(s => s.Id == clone.Id);
        Require(clonedSource.Generations.Count == 2 && clonedSource.Generations.All(g => source.Generations.All(old => old.Id != g.Id)), "Copy owns independent history IDs");
        Require(clonedSource.Generations[1].ParentGenerationId == clonedSource.Generations[0].Id, "History parent links remapped");
        Require(File.ReadAllBytes(images.ResultPath(source, last)).SequenceEqual(File.ReadAllBytes(images.ResultPath(clonedSource, clonedSource.Generations[1]))), "Copy preserves saved result pixels");
        var extra = images.SaveGeneration(clonedSource, result, null, new GenerationEntry()); clonedSource.Generations.Add(extra);
        Require(images.Load().Single(s => s.Id == source.Id).Generations.Count == 2, "Editing independent copy preserves original");
        catalog.RemoveFolder(forest.Id);
        Require(grass.ParentId is null && catalog.State.Links.Any(l => l.FolderId == grass.Id), "Deleting folder promotes children and preserves members");
        catalog.RemoveProject(projectB.Id); Require(catalog.State.Assets.Any(a => a.Id == clone.Id), "Deleting project preserves assets");
        catalog.Save(); catalog = new WorkspaceCatalog(libraryRoot);
        Require(catalog.State.Assets.Single(a => a.Id == asset.Id).Tags.Contains("общий") && catalog.State.Folders.Single().Name == "Лес", "Catalog persists structure and tags");

        var tile = new TileAsset(TileAsset.DecodeFile(input)) { Name = "Grass" };
        string collection = Path.Combine(root, "test.ttiles"); TileCollectionFile.Save(collection, [tile], new());
        var document = catalog.ImportDocument(collection); catalog.Link(document.Id, projectA.Id, null);
        var documentCopy = catalog.CopyForProject(document.Id, projectA.Id, images);
        Require(catalog.ContentPath(document) != catalog.ContentPath(documentCopy) && TileCollectionFile.Load(catalog.ContentPath(documentCopy)).Assets.Count == 1, "Document copies have independent embedded content");

        var window = new MainWindow(libraryRoot) { Width = 1440, Height = 900, ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([second]));
            Require(window.Workspace!.State.Assets.Count == 5, "Existing library and imported standalone files appear together");
            var browser = window.Browser!;
            browser.Navigate("project", projectA.Id); Pump();
            Require(Get<ListBox>(browser, "assetList").Items.Count >= 2, "Project browser shows referenced materials");
            var list = Get<ListBox>(browser, "assetList");
            foreach (var item in list.Items.Cast<object>().ToArray()) list.SelectedItems.Add(item);
            Require(list.SelectedItems.Count >= 2, "Multiple selection works");
            Get<TextBox>(browser, "searchInput").Text = "общий"; Pump(); Require(list.Items.Count == 1, "UI search filters tags");
            Get<TextBox>(browser, "searchInput").Clear();
            Capture(window, Path.Combine(root, "workspace-wide.png"));
            window.Width = 620; window.Height = 820; Pump();
            Require(Get<Border>(browser, "detailsBorder").Visibility == Visibility.Collapsed && list.ActualWidth > 250, "Narrow layout retains usable library and context actions");
            Capture(window, Path.Combine(root, "workspace-narrow.png"));
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == asset.Id))); Pump();
            Require(Get<System.Windows.Controls.ScrollViewer>(window, "workspaceHost").Visibility == Visibility.Visible, "Asset opens existing editor");
            Require(window.OwnedWindows.Count == 0, "Processing settings are docked, not floating windows");
            Get<DomainColorTest.IntegerInput>(window, "widthInput").Value = 4; Get<DomainColorTest.IntegerInput>(window, "heightInput").Value = 4;
            Get<Button>(window, "scaleOnlyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => !(bool)typeof(MainWindow).GetField("_processing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
            Require(source.Id == asset.Id && images.Load().Single(s => s.Id == asset.Id).Generations.Count == 3, "Docked action adds persistent shared history");
            window.Width = 1440; Pump(); Capture(window, Path.Combine(root, "workspace-editor.png"));

            Wait(Action("background", window.Workspace.State.Assets.Single(a => a.Id == asset.Id)));
            var background = Get<BackgroundWorkspace>(window, "backgroundWorkspace"); Wait(background.RefreshPreviewAsync());
            var applyBackground = (Button)typeof(BackgroundWorkspace).GetField("_workspaceApplyButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(background)!;
            applyBackground.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(images.Load().Single(s => s.Id == asset.Id).Generations.Count == 4, "Background result returns directly to shared image history");

            Wait(Action("tiles", window.Workspace.State.Assets.Single(a => a.Id == asset.Id)));
            var inspector = window.OwnedWindows.OfType<TileInspectorWindow>().Single(); var activeTile = inspector.ActiveAsset!;
            var pixels = (byte[])activeTile.Pixels.Clone(); pixels[0] = 240; pixels[3] = 255; activeTile.ReplacePixels(pixels);
            var applyTile = Get<WrapPanel>(inspector, "workspaceTileToolbar").Children.OfType<Button>().Single(b => Equals(b.Content, "В историю ассета"));
            applyTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(images.Load().Single(s => s.Id == asset.Id).Generations.Count == 5, "Tile corrections return to original image history");
            inspector.DiscardAndClose(); Pump();

            Get<ListBox>(window, "generationStrip").SelectedIndex = 0; Pump();
            var beforeCopy = window.Workspace.State.Assets.Select(a => a.Id).ToHashSet();
            Wait(Action("copy", window.Workspace.State.Assets.Single(a => a.Id == asset.Id)));
            var stateCopy = window.Workspace.State.Assets.Single(a => !beforeCopy.Contains(a.Id));
            var stateCopySource = images.Load().Single(s => s.Id == stateCopy.Id);
            Require(stateCopy.SelectedResult == stateCopySource.Generations[0].Id, "Independent UI copy preserves an older selected result rather than jumping to the latest");

            string boardPath = Path.Combine(root, "references.tboard");
            var boardFixture = new MoodboardWorkspace(); boardFixture.CreateWorkspaceBoard(256, 128); Wait(boardFixture.AddFilesAsync([input])); boardFixture.SaveToFile(boardPath);
            byte[] boardOriginal = File.ReadAllBytes(boardPath);
            string animationPath = Path.Combine(root, "walk.aseprite");
            File.WriteAllBytes(animationPath, PixelArtAseprite.Tests.Fixtures.File(2, 2, [new PixelArtAseprite.Tests.Fixtures.Frame(100, PixelArtAseprite.Tests.Fixtures.Layer(), PixelArtAseprite.Tests.Fixtures.Cel(2, 2, [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255]))]));
            Wait(window.ImportWorkspaceFilesAsync([boardPath, animationPath]));
            Require(window.Workspace.State.Assets.Single(a => a.Kind == WorkspaceAssetKind.Board).Thumbnail is not null, "Board thumbnail is available before opening the document");
            var animation = window.Workspace.State.Assets.Single(a => a.Kind == WorkspaceAssetKind.Animation);
            Wait(window.OpenWorkspaceAssetAsync(animation));
            var aseprite = Get<AsepriteWorkspace>(window, "asepriteWorkspace");
            Require(aseprite.CurrentSheetPath is not null && animation.Thumbnail is not null, "Animation source is retained and its converted preview enters the catalog");
            Get<ComboBox>(aseprite, "sheetLayout").SelectedIndex = 2; Get<IntegerInput>(aseprite, "sheetColumns").Value = 1; Wait(aseprite.RebuildAsync());
            typeof(MainWindow).GetMethod("ShowHome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); Pump();
            Require(animation.SheetLayout == 2 && animation.SheetColumns == 1, "Animation layout persists per asset");
            string animationOutput = Path.Combine(root, "animation-export"); Directory.CreateDirectory(animationOutput); Wait(window.OpenWorkspaceAssetAsync(animation)); Wait(aseprite.SaveAsync(animationOutput, false));
            Require(Directory.EnumerateFiles(animationOutput, "*.png").Any() && Directory.EnumerateFiles(animationOutput, "*.json").Any(), "Animation exports PNG and JSON");

            var board = window.Workspace.State.Assets.Single(a => a.Kind == WorkspaceAssetKind.Board);
            Wait(window.OpenWorkspaceAssetAsync(board)); var boardEditor = Get<MoodboardWorkspace>(window, "moodboardWorkspace");
            Wait(boardEditor.AddFilesAsync([second])); Wait(boardEditor.SaveWorkspaceAsync());
            Require(File.ReadAllBytes(boardPath).SequenceEqual(boardOriginal) && board.Thumbnail is not null, "Board edits and thumbnail save to owned document, preserving external original");
            var boardReload = new MoodboardWorkspace(); boardReload.LoadFromFile(window.Workspace.ContentPath(board));
            Require(Get<ListBox>(boardReload, "layerList").Items.Count == 2, "Board keeps independently editable layers after saving");

            var hover = new Button(); Tessera.WpfStyles.Motion.HoverMotion.SetIsEnabled(hover, true); hover.Background = System.Windows.Media.Brushes.Black; hover.BorderBrush = System.Windows.Media.Brushes.White;
            hover.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Require(hover.Background is SolidColorBrush { IsFrozen: false }, "Frozen theme brushes remain safe for hover motion");

            Task Action(string name, WorkspaceAsset selectedAsset) => (Task)typeof(MainWindow).GetMethod("WorkspaceActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [name, selectedAsset])!;
        }
        finally { window.Close(); Pump(); }
        var reopened = new WorkspaceCatalog(libraryRoot);
        Require(reopened.State.ProjectId == projectA.Id && reopened.State.SelectedAsset is not null, "Navigation survives restart");
        Console.WriteLine("  Workspace artifacts: " + root);
        Console.WriteLine("  Workspace storage, shared/copy history, WPF layouts, background/tile roundtrips, boards and animation passed");
    }

    private static T Get<T>(FrameworkElement element, string name) => (T)element.FindName(name);
    private static void Capture(Window window, string path)
    {
        Pump(); window.UpdateLayout(); var root = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    private static void Wait(Task task) { WaitUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(25); while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Operation timed out"); Pump();
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void MustFail(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid project/folder action accepted"); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
