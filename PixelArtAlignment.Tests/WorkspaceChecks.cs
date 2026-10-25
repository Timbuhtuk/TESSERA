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
        string previousData = Path.Combine(root, "previous-data");
        string previousAsset = Path.Combine(previousData, "Tessera", "Library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(previousAsset, "generations"));
        File.WriteAllText(Path.Combine(previousAsset, "source.json"), "original source");
        File.WriteAllText(Path.Combine(previousAsset, "generations", "history.txt"), "original history");
        string portableDirectory = Path.Combine(root, "portable");
        string portableLibrary = WorkspacePrototypeLocation.Prepare(portableDirectory, previousData);
        string copiedAsset = Path.Combine(portableLibrary, Path.GetFileName(previousAsset));
        Require(File.ReadAllText(Path.Combine(copiedAsset, "generations", "history.txt")) == "original history" &&
            File.Exists(Path.Combine(previousAsset, "source.json")), "Portable workspace migration lost history or changed the previous library");
        File.WriteAllText(Path.Combine(copiedAsset, "source.json"), "workspace edit");
        WorkspacePrototypeLocation.Prepare(portableDirectory, previousData);
        Require(File.ReadAllText(Path.Combine(copiedAsset, "source.json")) == "workspace edit",
            "A later launch overwrote the portable workspace from the previous library");
        string oldData = Path.Combine(root, "pixelizator-data");
        string oldAsset = Path.Combine(oldData, "Pixelizator", "Library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldAsset);
        File.WriteAllText(Path.Combine(oldAsset, "source.json"), "legacy source");
        string migratedLibrary = WorkspacePrototypeLocation.Prepare(Path.Combine(root, "legacy-portable"), oldData);
        Require(File.ReadAllText(Path.Combine(migratedLibrary, Path.GetFileName(oldAsset), "source.json")) == "legacy source" &&
            File.Exists(Path.Combine(oldAsset, "source.json")), "Direct upgrade from Pixelizator lost the old library");
        var releaseWindow = new MainWindow(Path.Combine(root, "release-library"));
        try
        {
            releaseWindow.EnableWorkspacePrototype(enableUpdates: true);
            var updateMenu = (MenuItem)typeof(MainWindow).GetField("checkUpdatesMenuItem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(releaseWindow)!;
            bool automaticUpdates = (bool)typeof(MainWindow).GetField("_autoCheckUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(releaseWindow)!;
            Require(Get<Button>(releaseWindow, "updateButton").IsEnabled && updateMenu.IsEnabled && automaticUpdates,
                "Released workspace disabled startup or manual updates");
        }
        finally { releaseWindow.Close(); }
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
            var sources = Get<ListBox>(window, "sourceStrip");
            Require(SourceIds().SetEquals([asset.Id]) && images.Load().Count == 3, "Project filmstrip excludes unrelated images without changing the library");
            browser.Navigate("all"); Wait(Open(clone.Id));
            Require(SourceIds().SetEquals(images.Load().Select(s => s.Id)), "All-assets editor retains the complete image library");
            browser.Navigate("unassigned"); Wait(Open(clone.Id));
            Require(SourceIds().Count == 2 && !SourceIds().Contains(asset.Id), "Unassigned editor excludes project images");
            var sharedProject = window.Workspace.CreateProject("Общий проект");
            window.Workspace.Link(asset.Id, sharedProject.Id, null); window.Workspace.Link(clone.Id, sharedProject.Id, null);
            browser.Navigate("project", sharedProject.Id); Wait(Open(asset.Id));
            Require(SourceIds().SetEquals([asset.Id, clone.Id]), "Shared asset uses the project it was opened from");
            browser.Navigate("project", projectA.Id, grass.Id); Wait(Open(asset.Id));
            var stone = window.Workspace.State.Assets.Single(a => a.Name == Path.GetFileName(second));
            window.Workspace.Link(stone.Id, projectA.Id, null); Wait(Open(asset.Id));
            Require(SourceIds().SetEquals([asset.Id]), "Compact source tray leaked images from another logical folder");
            typeof(MainWindow).GetMethod("NavigateSourceFolder", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [null, false]); Pump();
            Require(SourceIds().SetEquals([stone.Id]), "Project root navigation did not show root images");
            sources.SelectedItem = sources.Items.OfType<SourceEntry>().Single(s => s.Id == stone.Id); Pump();
            Require(SourceIds().SetEquals([stone.Id]), "Switching filmstrip images did not preserve project-root scope");
            Wait(Open(asset.Id));
            string scopedInput = Path.Combine(root, "scoped.png");
            using (var image = new Bitmap(12, 12)) { using var draw = Graphics.FromImage(image); draw.Clear(Color.Gold); image.Save(scopedInput); }
            Wait((Task)typeof(MainWindow).GetMethod("AddImagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new[] { scopedInput }])!);
            var scoped = window.Workspace.State.Assets.Single(a => a.Name == Path.GetFileName(scopedInput));
            Require(SourceIds().SetEquals([asset.Id, scoped.Id]), "Project import refreshes the scoped filmstrip after linking");
            Wait(Open(scoped.Id)); Remove(scoped.Id);
            Require(sources.SelectedItem is SourceEntry neighbor && SourceIds().Contains(neighbor.Id), "Deleting a source selects a neighbor inside the project");
            Wait(Open(stone.Id)); Remove(stone.Id);
            Require(sources.SelectedItem is SourceEntry remaining && remaining.Id == asset.Id && images.Load().Count == 2, "Deleting a project neighbor keeps unrelated global sources intact");
            Wait(Open(asset.Id));
            Get<DomainColorTest.IntegerInput>(window, "widthInput").Value = 4; Get<DomainColorTest.IntegerInput>(window, "heightInput").Value = 4;
            Get<Button>(window, "scaleOnlyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => !(bool)typeof(MainWindow).GetField("_processing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!);
            Require(source.Id == asset.Id && images.Load().Single(s => s.Id == asset.Id).Generations.Count == 3, "Docked action adds persistent shared history");
            window.Width = 1440; Pump(); Capture(window, Path.Combine(root, "workspace-editor.png"));
            var editedSource = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Require(window.ProcessingBasis == 1 && sources.SelectedItem is null &&
                Get<ListBox>(window, "generationStrip").SelectedItem is GenerationEntry,
                "Processing must select exactly the result while retaining its active source document");
            Wait((Task)typeof(MainWindow).GetMethod("AddGenerationAsSourceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [editedSource, editedSource.Generations.Last()])!);
            var derived = (SourceEntry)sources.SelectedItem;
            Require(SourceIds().SetEquals([asset.Id, derived.Id]) && window.Workspace.State.Links.Any(l => l.AssetId == derived.Id && l.ProjectId == projectA.Id && l.FolderId == grass.Id), "Result-to-source drop stays in the current project and folder");
            Remove(derived.Id);

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
            Require(SourceIds().SetEquals([stateCopy.Id]), "Independent copy replaces the source in the current project filmstrip");
            Wait(Open(stateCopy.Id)); Remove(stateCopy.Id);
            Require(browser.Visibility == Visibility.Visible && images.Load().Count == 2 && SourceIds().Count == 0, "Deleting the last project image returns to its browser instead of opening another project");

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
            Require(Get<MenuItem>(window, "saveDocumentMenuItem").IsEnabled && Equals(Get<MenuItem>(window, "saveMenuItem").Header, "PNG + JSON…") && Get<MenuItem>(window, "exportIcoMenuItem").Visibility == Visibility.Collapsed, "File menu follows the active animation and hides image-only exports");
            Require(aseprite.CurrentSheetPath is not null && animation.Thumbnail is not null, "Animation source is retained and its converted preview enters the catalog");
            Get<ComboBox>(aseprite, "sheetLayout").SelectedIndex = 2; Get<IntegerInput>(aseprite, "sheetColumns").Value = 1; Wait(aseprite.RebuildAsync());
            typeof(MainWindow).GetMethod("ShowHome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); Pump();
            Require(animation.SheetLayout == 2 && animation.SheetColumns == 1, "Animation layout persists per asset");
            string animationOutput = Path.Combine(root, "animation-export"); Directory.CreateDirectory(animationOutput); Wait(window.OpenWorkspaceAssetAsync(animation)); Wait(aseprite.SaveAsync(animationOutput, false));
            Require(Directory.EnumerateFiles(animationOutput, "*.png").Any() && Directory.EnumerateFiles(animationOutput, "*.json").Any(), "Animation exports PNG and JSON");

            var board = window.Workspace.State.Assets.Single(a => a.Kind == WorkspaceAssetKind.Board);
            Wait(window.OpenWorkspaceAssetAsync(board)); var boardEditor = Get<MoodboardWorkspace>(window, "moodboardWorkspace");
            Require(Get<MenuItem>(window, "saveDocumentMenuItem").IsEnabled && !Get<MenuItem>(window, "toolsMenu").IsEnabled, "Board file commands are available while image tools are disabled");
            Wait(boardEditor.AddFilesAsync([second])); Wait(boardEditor.SaveWorkspaceAsync());
            Require(File.ReadAllBytes(boardPath).SequenceEqual(boardOriginal) && board.Thumbnail is not null, "Board edits and thumbnail save to owned document, preserving external original");
            var boardReload = new MoodboardWorkspace(); boardReload.LoadFromFile(window.Workspace.ContentPath(board));
            Require(Get<ListBox>(boardReload, "layerList").Items.Count == 2, "Board keeps independently editable layers after saving");

            var hover = new Button(); Tessera.WpfStyles.Motion.HoverMotion.SetIsEnabled(hover, true); hover.Background = System.Windows.Media.Brushes.Black; hover.BorderBrush = System.Windows.Media.Brushes.White;
            hover.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Require(hover.Background is SolidColorBrush { IsFrozen: false }, "Frozen theme brushes remain safe for hover motion");

            Task Action(string name, WorkspaceAsset selectedAsset) => (Task)typeof(MainWindow).GetMethod("WorkspaceActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [name, selectedAsset])!;
            Task Open(Guid id) => window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == id));
            HashSet<Guid> SourceIds() => sources.Items.OfType<SourceEntry>().Select(s => s.Id).ToHashSet();
            void Remove(Guid id)
            {
                var selected = sources.Items.OfType<SourceEntry>().Single(s => s.Id == id);
                typeof(MainWindow).GetMethod("RemoveSource", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [selected]); Pump();
            }
        }
        finally { window.Close(); Pump(); }
        var reopened = new WorkspaceCatalog(libraryRoot);
        Require(reopened.State.ProjectId == projectA.Id && reopened.State.SelectedAsset is not null, "Navigation survives restart");
        Console.WriteLine("  Workspace artifacts: " + root);
        Console.WriteLine("  Workspace storage, project source scope, shared/copy history, WPF layouts, background/tile roundtrips, boards and animation passed");
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
