using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace Tessera.RegressionTests;

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
        VerifyPagedCatalog(root);
        VerifyImageViewportInput();
        ResourceLifetimeChecks.Verify(root);
        var releaseWindow = new MainWindow(Path.Combine(root, "release-library"));
        try
        {
            releaseWindow.EnableWorkspacePrototype(enableUpdates: true);
            var updateMenu = (MenuItem)WindowTestAccess.GetField(releaseWindow, "checkUpdatesMenuItem", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool automaticUpdates = (bool)WindowTestAccess.GetField(releaseWindow, "_autoCheckUpdates", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
        var asset = catalog.State.Assets.Single(); asset.Transparent = true; asset.Width = 32; asset.Height = 32; asset.SelectedResult = last.Id;
        Require(catalog.Query("unassigned", null, null, "grass", 1, "32", "32", 1).Count == 1, "Standalone asset and combined filters");
        var projectA = catalog.CreateProject("Игра A"); var projectB = catalog.CreateProject("Игра B");
        var forest = catalog.CreateFolder(projectA.Id, null, "Окружение"); var grass = catalog.CreateFolder(projectA.Id, forest.Id, "Лес");
        var other = catalog.CreateFolder(projectB.Id, null, "Фоны");
        catalog.Link(asset.Id, projectA.Id, grass.Id); catalog.Link(asset.Id, projectB.Id, other.Id);
        Require(catalog.Query("project", projectA.Id, forest.Id, "grass png", 0, "", "", 0).Single().Id == asset.Id, "Nested folder search");
        Require(asset.Locations.ToHashSet().SetEquals(["Игра A / Окружение / Лес", "Игра B / Фоны"]), "Metadata shows every project location");
        Require(catalog.Query("project", projectB.Id, null, "grass", 0, "", "", 0).Single().Id == asset.Id, "Shared asset visible in both projects");
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
        catalog.Save();
        catalog = new WorkspaceCatalog(libraryRoot);
        Require(catalog.State.Folders.Single().Name == "Лес", "Catalog persists structure");

        string documentPath = Path.Combine(root, "copy-test.tboard");
        var copyFixture = new MoodboardWorkspace();
        copyFixture.CreateWorkspaceBoard(128, 128);
        copyFixture.SaveToFile(documentPath);
        var document = catalog.ImportDocument(documentPath); catalog.Link(document.Id, projectA.Id, null);
        var documentCopy = catalog.CopyForProject(document.Id, projectA.Id, images);
        Require(catalog.ContentPath(document) != catalog.ContentPath(documentCopy) &&
            File.ReadAllBytes(catalog.ContentPath(document)).SequenceEqual(File.ReadAllBytes(catalog.ContentPath(documentCopy))),
            "Document copies have independent embedded content");

        catalog.State.View = "project";
        catalog.State.ProjectId = projectA.Id;
        catalog.State.FolderId = grass.Id;
        catalog.Save();

        var window = new MainWindow(libraryRoot) { MinWidth = 1440, Width = 1440, Height = 900, ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            var browser = window.Browser!;
            var list = Get<ListBox>(browser, "assetList");
            Require(browser.FindName("scopeInput") is null && list.Items.Cast<WorkspaceAsset>().Single().Id == asset.Id,
                "A saved whole-library scope must open in the selected folder tree");
            browser.Navigate("all");
            Wait(window.ImportWorkspaceFilesAsync([second]));
            Require(window.Workspace!.State.Assets.Count == 5, "Existing library and imported standalone files appear together");
            browser.Navigate("project", projectA.Id); Pump();
            Require(list.Items.Count >= 2, "Project browser shows referenced materials");
            var search = Get<TextBox>(browser, "searchInput");
            search.Text = "copy-test"; Pump();
            Require(list.Items.Cast<WorkspaceAsset>().Any(a => a.Id == documentCopy.Id), "Project root search includes root assets");
            browser.Navigate("project", projectA.Id, grass.Id); Pump();
            Require(list.Items.Count == 0, "Folder search excludes assets from its parent");
            search.Clear(); browser.Navigate("project", projectA.Id); Pump();
            foreach (var item in list.Items.Cast<object>().ToArray()) list.SelectedItems.Add(item);
            Require(list.SelectedItems.Count >= 2, "Multiple selection works");
            Get<TextBox>(browser, "searchInput").Text = "grass"; Pump(); Require(list.Items.Count == 1, "UI search filters asset names");
            var selected = (WorkspaceAsset)list.Items[0];
            list.SelectedItem = selected;
            foreach (string viewButton in new[] { "assetViewSmallTiles", "assetViewList", "assetViewTiles" })
            {
                Get<RadioButton>(browser, viewButton).IsChecked = true;
                Pump();
                var card = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(selected);
                Require(!ReferenceEquals(card.ContextMenu, list.ContextMenu) &&
                    card.ContextMenu.Items.OfType<MenuItem>().Where(item => item.Visibility == Visibility.Visible)
                        .Select(item => (string)item.Tag).SequenceEqual(["open", "rename", "material-maps", "link", "move", "copy", "unlink", "delete"]),
                    "Wrong asset context menu in " + viewButton);
                var frame = (Border)card.Template.FindName("frame", card);
                var hint = (ToolTip)frame.ToolTip;
                hint.PlacementTarget = frame;
                hint.IsOpen = true;
                try
                {
                    Pump();
                    Require(ReferenceEquals(hint.Content, selected), "Card tooltip does not follow asset data in " + viewButton);
                    var blocks = Descendants<TextBlock>(hint).ToArray();
                    var title = blocks.Single(block => block.Text == selected.DisplayName);
                    var fields = blocks.Where(block => block != title).ToArray();
                    var values = fields.Where(block => block.TextAlignment == TextAlignment.Right).ToArray();
                    var labels = fields.Except(values).ToArray();
                    Require(labels.Select(block => block.Text).SequenceEqual(["Тип", "Размер", "Файл", "AO", "Normal", "Height", "Используется"]) &&
                        values.Select(block => block.Text).SequenceEqual([selected.KindLabel, selected.MetadataLabel, ".png", "-", "-", "-", "Игра A / Лес"]),
                        "The asset tooltip showed the wrong fields in " + viewButton);
                    double left = title.TranslatePoint(new Point(), hint).X;
                    double right = title.TranslatePoint(new Point(title.ActualWidth, 0), hint).X;
                    Require(labels.All(block => Math.Abs(block.TranslatePoint(new Point(), hint).X - left) < 1) &&
                        values.All(block => Math.Abs(block.TranslatePoint(new Point(block.ActualWidth, 0), hint).X - right) < 1) &&
                        blocks.All(block => block.FontFamily.Source.Contains("Cascadia Code", StringComparison.Ordinal)) &&
                        !Descendants<System.Windows.Controls.Image>(hint).Any(), "Tooltip columns or font are incorrect in " + viewButton);
                }
                finally { hint.IsOpen = false; }
            }
            Require(list.SelectedItem == selected && list.ContextMenu.Items.OfType<MenuItem>()
                .Select(item => (string)item.Tag).SequenceEqual(["new-board", "import"]),
                "The background should offer creation and import even when an asset is selected");
            Get<TextBox>(browser, "searchInput").Clear();
            Wait(window.OpenWorkspaceAssetAsync(selected));
            Get<Button>(window, "libraryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            browser.Navigate("project", projectA.Id); Pump();
            list.SelectedItem = selected; Pump();
            VerifyRenameDialog(browser, () => PressF2(list), "grass", "cancelled", Key.Escape);
            Require(selected.Name == "grass.png", "Cancel changed the asset name");
            VerifyRenameDialog(browser, () => PressF2(list), "grass", "grass-renamed", Key.Enter);
            Require(selected.DisplayName == "grass-renamed" && list.SelectedItem == selected, "F2 did not rename and retain the selected asset");
            var renamedSource = images.Load().Single(s => s.Id == asset.Id);
            Require(renamedSource.Label == "grass-renamed.png" && renamedSource.OriginalPath == input &&
                renamedSource.Generations.Select(g => g.Id).SequenceEqual([first.Id, last.Id]),
                "Image rename did not persist or changed its source and history");

            var boardAsset = list.Items.Cast<WorkspaceAsset>().Single(a => a.Id == documentCopy.Id);
            list.SelectedItem = boardAsset; Pump();
            var boardCard = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(boardAsset);
            var renameItem = boardCard.ContextMenu.Items.OfType<MenuItem>().Single(item => (string)item.Tag == "rename");
            Require(renameItem.InputGestureText == "F2" && renameItem.Visibility == Visibility.Visible, "Rename was missing from the asset menu");
            string boardContent = window.Workspace.ContentPath(boardAsset);
            VerifyRenameDialog(browser, () => renameItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), boardAsset.DisplayName, "references-renamed.tboard", Key.Enter);
            var reloaded = new WorkspaceCatalog(libraryRoot);
            Require(reloaded.State.Assets.Single(a => a.Id == boardAsset.Id).Name == "references-renamed.tboard" &&
                window.Workspace.ContentPath(boardAsset) == boardContent && File.Exists(boardContent) &&
                reloaded.State.Assets.Single(a => a.Id == selected.Id).Name == "grass-renamed.png",
                "Rename did not persist both names while keeping the board payload in place");
            var filters = Get<Border>(browser, "filtersPane");
            var filtersToggle = Get<ToggleButton>(browser, "filtersToggle");
            double filterLeft = filters.TranslatePoint(new Point(), list).X;
            Require(filters.IsVisible && filterLeft >= list.ActualWidth - 1 && list.ActualWidth > 900 &&
                Descendants<TextBox>(filters).Contains(search),
                $"Filters should be docked to the right of the wide gallery (window {window.ActualWidth:F1}, gallery {list.ActualWidth:F1}, filters at {filterLeft:F1})");
            Capture(window, Path.Combine(root, "workspace-wide.png"));
            window.MinWidth = 560;
            window.Width = 620; window.Height = 820; Pump();
            Require(!filters.IsVisible && list.ActualWidth > 250, "The narrow gallery should start with its full width available");
            Capture(window, Path.Combine(root, "workspace-narrow.png"));
            double narrowGalleryWidth = list.ActualWidth;
            ToggleFilters(true);
            Require(filters.IsVisible && Descendants<TextBox>(filters).Contains(search) && Math.Abs(list.ActualWidth - narrowGalleryWidth) < 1 &&
                filters.TranslatePoint(new Point(filters.ActualWidth, 0), list).X <= list.ActualWidth + 1,
                "The narrow filter panel should open over the right edge without shrinking the gallery");
            search.Text = "grass"; Pump();
            var widthInput = Get<TextBox>(browser, "filterWidth");
            widthInput.Text = "31"; Pump();
            Require(list.Items.Count == 0, "The sidebar no longer applies combined filters");
            ToggleFilters(false);
            Require(!filters.IsVisible && list.Items.Count == 0, "Hiding the panel cleared the active filters");
            ToggleFilters(true);
            Get<Button>(browser, "resetFiltersButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(search.Text.Length == 0 && widthInput.Text.Length == 0 && list.Items.Count >= 2, "The sidebar reset did not clear restored filters");
            Capture(window, Path.Combine(root, "workspace-narrow-filters.png"));
            ToggleFilters(false);

            void ToggleFilters(bool open)
            {
                filtersToggle.IsChecked = open;
                filtersToggle.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
                Pump();
            }

            Wait(window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == asset.Id))); Pump();
            Require(Get<System.Windows.Controls.ScrollViewer>(window, "workspaceHost").Visibility == Visibility.Visible, "Asset opens existing editor");
            var layout = Get<Grid>(window, "rootLayout");
            double editorHeight = layout.RowDefinitions[1].ActualHeight;
            NavigationTest.Click(window, "saveDocumentMenuItem"); Pump();
            var notification = ProgressUiChecks.Cards(Get<NotificationDisplay>(window, "notificationDisplay")).Last();
            Require(Get<TextBlock>(window, "statusLabel").Text == Tessera.Desktop.Localization.Text("ProjectSaved") &&
                notification.IsVisible && notification.Parent is StackPanel { VerticalAlignment: VerticalAlignment.Top } &&
                Get<TextBlock>(notification, "heading").Text == Tessera.Desktop.Localization.Text("Done") &&
                layout.RowDefinitions.Count == 2 && layout.RowDefinitions[1].ActualHeight == editorHeight,
                "Saving a project did not show a success notification over the workspace");
            Require(Get<TextBlock>(window, "fileLabel").Text == "grass-renamed.png" &&
                Get<ListBox>(window, "generationStrip").Items.OfType<HistorySourceRoot>().Single().Label == "grass-renamed.png",
                "The editor did not show the renamed source when reopened");
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
            WindowTestAccess.Invoke(window, "NavigateSourceFolder", BindingFlags.NonPublic | BindingFlags.Instance, [null, false]); Pump();
            Require(SourceIds().SetEquals([stone.Id]), "Project root navigation did not show root images");
            sources.SelectedItem = sources.Items.OfType<SourceEntry>().Single(s => s.Id == stone.Id); Pump();
            Require(SourceIds().SetEquals([stone.Id]), "Switching filmstrip images did not preserve project-root scope");
            Wait(Open(asset.Id));
            string scopedInput = Path.Combine(root, "scoped.png");
            using (var image = new Bitmap(12, 12)) { using var draw = Graphics.FromImage(image); draw.Clear(Color.Gold); image.Save(scopedInput); }
            Wait((Task)WindowTestAccess.Invoke(window, "AddImagesAsync", BindingFlags.Instance | BindingFlags.NonPublic, [new[] { scopedInput }])!);
            var scoped = window.Workspace.State.Assets.Single(a => a.Name == Path.GetFileName(scopedInput));
            Require(SourceIds().SetEquals([asset.Id, scoped.Id]), "Project import refreshes the scoped filmstrip after linking");
            Wait(Open(scoped.Id)); Remove(scoped.Id);
            Require(sources.SelectedItem is SourceEntry neighbor && SourceIds().Contains(neighbor.Id), "Deleting a source selects a neighbor inside the project");
            Wait(Open(stone.Id)); Remove(stone.Id);
            Require(sources.SelectedItem is SourceEntry remaining && remaining.Id == asset.Id && images.Load().Count == 2, "Deleting a project neighbor keeps unrelated global sources intact");
            Wait(Open(asset.Id));
            Get<IntegerInput>(window, "widthInput").Value = 4; Get<IntegerInput>(window, "heightInput").Value = 4;
            Get<Button>(window, "scaleOnlyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => !(bool)WindowTestAccess.GetField(window, "_processing", BindingFlags.NonPublic | BindingFlags.Instance)!);
            Require(source.Id == asset.Id && images.Load().Single(s => s.Id == asset.Id).Generations.Count == 3, "Docked action adds persistent shared history");
            window.Width = 1440; Pump(); Capture(window, Path.Combine(root, "workspace-editor.png"));
            var editedSource = (SourceEntry)WindowTestAccess.GetField(window, "_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(window.ProcessingBasis == 1 && sources.SelectedItem is null &&
                Get<ListBox>(window, "generationStrip").SelectedItem is GenerationEntry,
                "Processing must select exactly the result while retaining its active source document");
            Wait((Task)WindowTestAccess.Invoke(window, "AddGenerationAsSourceAsync", BindingFlags.Instance | BindingFlags.NonPublic, [editedSource, editedSource.Generations.Last()])!);
            var derived = sources.SelectedItem as SourceEntry
                ?? throw new Exception("Result-to-source drop did not select the newly created source");
            Require(window.ProcessingBasis == 0, "Result-to-source drop retained result processing selection");
            Require(SourceIds().SetEquals([asset.Id, derived.Id]) && window.Workspace.State.Links.Any(l => l.AssetId == derived.Id && l.ProjectId == projectA.Id && l.FolderId == grass.Id), "Result-to-source drop stays in the current project and folder");
            Remove(derived.Id);

            Wait(Action("background", window.Workspace.State.Assets.Single(a => a.Id == asset.Id)));
            var background = Get<BackgroundWorkspace>(window, "backgroundWorkspace"); Wait(background.RefreshPreviewAsync());
            var applyBackground = (Button)typeof(BackgroundWorkspace).GetField("_workspaceApplyButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(background)!;
            applyBackground.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(images.Load().Single(s => s.Id == asset.Id).Generations.Count == 4, "Background result returns directly to shared image history");

            var firstResult = images.Load().Single(s => s.Id == asset.Id).Generations[0];
            var history = Get<ListBox>(window, "generationStrip");
            history.SelectedItem = history.Items.OfType<GenerationEntry>().Single(g => g.Id == firstResult.Id); Pump();
            Require(window.ProcessingBasis == 1 && ((SourceEntry)WindowTestAccess.GetField(window, "_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!).SelectedGeneration?.Id == firstResult.Id,
                "Independent copy fixture did not select the older result");
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
            File.WriteAllBytes(animationPath, Tessera.Aseprite.Tests.Fixtures.File(2, 2, [new Tessera.Aseprite.Tests.Fixtures.Frame(100, Tessera.Aseprite.Tests.Fixtures.Layer(), Tessera.Aseprite.Tests.Fixtures.Cel(2, 2, [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255]))]));
            Wait(window.ImportWorkspaceFilesAsync([boardPath, animationPath]));
            Require(window.Workspace.State.Assets.Single(a => a.Kind == EWorkspaceAssetKind.Board && a.Name == Path.GetFileName(boardPath)).Thumbnail is not null, "Board thumbnail is available before opening the document");
            var animation = window.Workspace.State.Assets.Single(a => a.Kind == EWorkspaceAssetKind.Animation);
            Wait(window.OpenWorkspaceAssetAsync(animation));
            var aseprite = Get<AsepriteWorkspace>(window, "asepriteWorkspace");
            Require(Get<MenuItem>(window, "importAsepriteMenuItem").IsEnabled && Equals(Get<MenuItem>(window, "importAsepriteMenuItem").Header, "Импорт Aseprite…"), "File menu does not expose Aseprite import");
            Require(Get<MenuItem>(window, "saveDocumentMenuItem").IsEnabled && Equals(Get<MenuItem>(window, "saveMenuItem").Header, "PNG…") && Get<MenuItem>(window, "exportIcoMenuItem").Visibility == Visibility.Collapsed, "File menu follows the active animation and hides image-only exports");
            Require(aseprite.CurrentSheetPath is not null && animation.Thumbnail is not null, "Animation source is retained and its converted preview enters the catalog");
            Get<ComboBox>(aseprite, "sheetLayout").SelectedIndex = 2; Get<IntegerInput>(aseprite, "sheetColumns").Value = 1; Wait(aseprite.RebuildAsync());
            WindowTestAccess.Invoke(window, "ShowHome", BindingFlags.Instance | BindingFlags.NonPublic, null); Pump();
            Require(animation.SheetLayout == 2 && animation.SheetColumns == 1 && aseprite.CurrentSheetPath is null,
                "Animation layout or the editor lifetime did not follow the active asset");
            string animationOutput = Path.Combine(root, "animation-export"); Directory.CreateDirectory(animationOutput); Wait(window.OpenWorkspaceAssetAsync(animation));
            Get<CheckBox>(aseprite, "sheetIncludeMetadata").IsChecked = true; Pump();
            Require(Equals(Get<MenuItem>(window, "saveMenuItem").Header, "PNG + JSON…"), "File menu did not follow the JSON option");
            Wait(aseprite.SaveAsync(animationOutput, false));
            Require(Directory.EnumerateFiles(animationOutput, "*.png").Any() && Directory.EnumerateFiles(animationOutput, "*.json").Any(), "Animation exports PNG and JSON");

            Get<CheckBox>(aseprite, "sheetIncludeMetadata").IsChecked = false; Pump();
            string batchPath = Path.Combine(root, "jump.aseprite"); File.Copy(animationPath, batchPath);
            aseprite.Clear();
            Wait(aseprite.WorkspaceImport!([animationPath, batchPath]));
            var batch = Get<ListBox>(aseprite, "sheetStrip").Items.Cast<AsepriteWorkspace.SheetEntry>().ToArray();
            Require(batch.Length == 2 && batch.All(e => e.Ready) && batch.Select(e => e.InputPath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([animationPath, batchPath]), "Workspace import did not keep both animations in the lower strip");
            Require(window.Workspace.State.Assets.Count(a => a.Kind == EWorkspaceAssetKind.Animation) == 3, "Batch import did not add both animations to the catalog");
            string pngOnlyOutput = Path.Combine(root, "batch-png"); Wait(aseprite.SaveAsync(pngOnlyOutput, true));
            Require(Directory.GetFiles(pngOnlyOutput, "*.png").Length == 2 && Directory.GetFiles(pngOnlyOutput, "*.json").Length == 0, "PNG-only batch export wrote metadata or missed a file");

            var board = window.Workspace.State.Assets.Single(a => a.Kind == EWorkspaceAssetKind.Board && a.Name == Path.GetFileName(boardPath));
            Wait(window.OpenWorkspaceAssetAsync(board)); var boardEditor = Get<MoodboardWorkspace>(window, "moodboardWorkspace");
            Require(Get<MenuItem>(window, "saveDocumentMenuItem").IsEnabled && !Get<MenuItem>(window, "materialsToolsMenu").IsEnabled &&
                Get<MenuItem>(window, "macrosMenuItem").Visibility == Visibility.Collapsed, "Board file commands are available while image tools are disabled");
            Wait(boardEditor.AddFilesAsync([second])); Wait(boardEditor.SaveWorkspaceAsync());
            Require(File.ReadAllBytes(boardPath).SequenceEqual(boardOriginal) && board.Thumbnail is not null, "Board edits and thumbnail save to owned document, preserving external original");
            var boardReload = new MoodboardWorkspace(); boardReload.LoadFromFile(window.Workspace.ContentPath(board));
            Require(Get<ListBox>(boardReload, "layerList").Items.Count == 2, "Board keeps independently editable layers after saving");

            var hover = new Button(); Tessera.WpfStyles.Motion.HoverMotion.SetIsEnabled(hover, true); hover.Background = System.Windows.Media.Brushes.Black; hover.BorderBrush = System.Windows.Media.Brushes.White;
            hover.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
            Require(hover.Background is SolidColorBrush { IsFrozen: false }, "Frozen theme brushes remain safe for hover motion");

            WindowTestAccess.Invoke(window, "ShowHome", BindingFlags.Instance | BindingFlags.NonPublic, null); Pump();
            Require(!boardEditor.HasBoard, "Leaving the board kept its layers loaded");
            Task Action(string name, WorkspaceAsset selectedAsset) => (Task)WindowTestAccess.Invoke(window, "WorkspaceActionAsync", BindingFlags.Instance | BindingFlags.NonPublic, [name, selectedAsset])!;
            Task Open(Guid id) => window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single(a => a.Id == id));
            HashSet<Guid> SourceIds() => sources.Items.OfType<SourceEntry>().Select(s => s.Id).ToHashSet();
            void Remove(Guid id)
            {
                var selected = sources.Items.OfType<SourceEntry>().Single(s => s.Id == id);
                WindowTestAccess.Invoke(window, "RemoveSource", BindingFlags.Instance | BindingFlags.NonPublic, [selected]); Pump();
            }
        }
        finally { window.Close(); Pump(); }
        var reopened = new WorkspaceCatalog(libraryRoot);
        Require(reopened.State.ProjectId == projectA.Id && reopened.State.SelectedAsset is not null, "Navigation survives restart");
        Console.WriteLine("  Workspace artifacts: " + root);
        Console.WriteLine("  Workspace storage, project source scope, shared/copy history, WPF layouts, background roundtrip, boards and animation passed");
    }

    private static T Get<T>(FrameworkElement element, string name) => (T)element.FindControl(name);

    private static void VerifyPagedCatalog(string root)
    {
        string library = Path.Combine(root, "paged-catalog");
        Directory.CreateDirectory(library);
        string manifest = Path.Combine(library, "workspace.json");
        string pages = Path.Combine(library, ".workspace-data");
        var project = new WorkspaceProject { Name = "Project" };
        var state = new WorkspaceState { View = "project", ProjectId = project.Id };
        state.Projects.Add(project);
        for (int q = 0; q < WorkspaceStorage.PageSize * 2 + 1; q++)
            state.Assets.Add(new WorkspaceAsset { Name = "Asset " + q });
        var first = state.Assets[0];
        state.Links.Add(new WorkspaceLink { AssetId = first.Id, ProjectId = project.Id });
        long catalogBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state).Length;
        new WorkspaceStorage(library).Save(state);
        var catalog = new WorkspaceCatalog(library);
        var reopened = new WorkspaceCatalog(library);
        Require(reopened.State.Assets.Select(asset => asset.Id).SequenceEqual(state.Assets.Select(asset => asset.Id)) &&
            reopened.State.ProjectId == project.Id && reopened.State.Links.Single().AssetId == first.Id,
            "Catalog roundtrip lost assets, order, links or view settings");
        var initialPages = Directory.GetFiles(pages, "*.json").ToHashSet();
        Require(initialPages.Count == 5 && new FileInfo(manifest).Length < catalogBytes / 10 &&
            initialPages.All(path => new FileInfo(path).Length < catalogBytes / 2),
            "Catalog was not split into smaller metadata pages");

        catalog.State.Assets[WorkspaceStorage.PageSize].Name = "Edited";
        using (File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { catalog.Save(); throw new InvalidOperationException("Catalog commit ignored a locked manifest"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Require(new WorkspaceCatalog(library).State.Assets[WorkspaceStorage.PageSize].Name != "Edited" &&
            initialPages.All(File.Exists), "A failed save published metadata or removed live pages");
        catalog.Save();
        var savedPages = Directory.GetFiles(pages, "*.json").ToHashSet();
        Require(savedPages.Count == initialPages.Count && savedPages.Except(initialPages).Count() == 1 &&
            new WorkspaceCatalog(library).State.Assets[WorkspaceStorage.PageSize].Name == "Edited",
            "A single edit did not replace exactly one metadata page");
        try { reopened.Save(); throw new InvalidOperationException("A stale catalog overwrote the latest changes"); }
        catch (IOException) { }
    }
    private static void VerifyImageViewportInput()
    {
        var content = new Border { Width = 900, Height = 700 };
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        var input = new ImageViewportInput(scroll);
        var window = new Window { Width = 320, Height = 240, ShowInTaskbar = false, Content = scroll };
        try
        {
            window.Show(); Pump();
            scroll.ScrollToVerticalOffset(60); Pump();
            Require(!input.ScrollWheel(-120, ModifierKeys.None), "Ordinary wheel was intercepted before zoom");
            Require(input.ScrollWheel(-120, ModifierKeys.Alt) && input.ScrollWheel(-30, ModifierKeys.Alt) &&
                input.ScrollWheel(-90, ModifierKeys.Control), "Modified wheel did not handle rapid full and partial deltas");
            Pump();
            Require(scroll.HorizontalOffset == 150 && scroll.VerticalOffset == 150, "Alt and Ctrl did not scroll on their own axes");
            Require(input.ScrollWheel(120, ModifierKeys.Alt) && input.ScrollWheel(90, ModifierKeys.Control), "Modified wheel did not scroll back");
            Pump();
            Require(scroll.HorizontalOffset == 30 && scroll.VerticalOffset == 60, "Modified wheel used the wrong scroll direction");
            Require(ImageViewportInput.IsZoomGesture(ModifierKeys.None) && !ImageViewportInput.IsZoomGesture(ModifierKeys.Control) &&
                !ImageViewportInput.IsZoomGesture(ModifierKeys.Alt) && !ImageViewportInput.IsZoomGesture(ModifierKeys.Shift),
                "Zoom accepted a modified wheel gesture");

            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            content.RaiseEvent(down);
            Require(down.Handled && scroll.IsMouseCaptured, "Middle click did not capture the image viewport");
            input.DragTo(Mouse.GetPosition(scroll) - new Vector(40, 30)); Pump();
            Require(scroll.HorizontalOffset == 70 && scroll.VerticalOffset == 90, "Middle drag did not follow both axes");
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseUpEvent };
            content.RaiseEvent(up);
            Require(up.Handled && !scroll.IsMouseCaptured, "Middle release left the viewport captured");
            input.DragTo(new System.Windows.Point()); Pump();
            Require(scroll.HorizontalOffset == 70 && scroll.VerticalOffset == 90, "Image kept moving after middle release");
        }
        finally { window.Close(); Pump(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (int q = 0; q < VisualTreeHelper.GetChildrenCount(element); q++)
        {
            var child = VisualTreeHelper.GetChild(element, q);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void PressF2(ListBox list) => list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(list)!, Environment.TickCount, Key.F2) { RoutedEvent = Keyboard.KeyDownEvent });

    private static void VerifyRenameDialog(WorkspaceBrowser browser, Action open, string currentName, string name, Key close)
    {
        var grid = Get<Grid>(browser, "browserGrid");
        int previousCount = grid.Children.Count;
        string? initial = null;
        browser.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (grid.Children.Count == previousCount) return;
            var overlay = (Grid)grid.Children[previousCount];
            var panel = (StackPanel)((Border)overlay.Children[0]).Child;
            var input = ((StackPanel)panel.Children[1]).Children.OfType<TextBox>().Single();
            initial = input.Text;
            input.Text = name;
            overlay.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(browser)!,
                Environment.TickCount, close) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }));
        open(); Pump();
        Require(initial == currentName, "Rename dialog did not open with the current name");
    }

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
