using System.Reflection;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Tessera.WpfStyles.Themes;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class HistoryMacroChecks
{
    internal static readonly string Root = Path.GetFullPath(Path.Combine("artifacts", "history-macros-qa"));
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<string, Action> check)
    {
        check("Branch forest: siblings, promotion, deletion, cycles and 30,000 nodes", VerifyBranches);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Directory.CreateDirectory(Root);
                VerifyRecipes();
                VerifyEditor();
            }
            catch (Exception ex) { Console.WriteLine(ex); failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        check("History recipes, macro publication, folders, virtualization, both themes and restart", () =>
        {
            if (!thread.Join(TimeSpan.FromSeconds(55))) throw new Exception("History/macro WPF checks timed out.");
            if (failure is not null) throw failure;
        });
    }

    private static void VerifyBranches()
    {
        var a = new GenerationEntry(); var b = new GenerationEntry { ParentGenerationId = a.Id };
        var c = new GenerationEntry { ParentGenerationId = b.Id }; var d = new GenerationEntry { ParentGenerationId = b.Id };
        var e = new GenerationEntry { ParentGenerationId = d.Id }; var f = new GenerationEntry();
        GenerationEntry[] entries = [a, b, c, d, e, f];
        var state = new BranchPresentation(); var layout = HistoryBranchLayout.Build(entries, state);
        Require(layout.Bounds[a.Id].Y == layout.Bounds[b.Id].Y && layout.Bounds[b.Id].Y == layout.Bounds[c.Id].Y, "Continuation did not stay in its row.");
        Require(layout.Bounds[d.Id].Y > layout.Bounds[c.Id].Y && layout.Bounds[d.Id].Y == layout.Bounds[e.Id].Y, "Sibling branch was flattened.");
        layout.Promote(d.Id, state); layout = HistoryBranchLayout.Build(entries, state);
        Require(layout.Roots.SequenceEqual(new[] { a.Id, d.Id, f.Id }) && layout.Bounds[d.Id].X == layout.Bounds[a.Id].X && layout.Bounds[e.Id].X > layout.Bounds[d.Id].X, "Promotion lost subtree or root position.");
        Require(d.ParentGenerationId == b.Id && e.ParentGenerationId == d.Id && !string.IsNullOrWhiteSpace(d.PresentationHint), "Promotion rewrote provenance.");
        layout.Promote(d.Id, state, 0); layout = HistoryBranchLayout.Build(entries, state);
        Require(layout.Roots[0] == d.Id, "Explicit branch insertion ignored.");
        var restored = JsonSerializer.Deserialize<BranchPresentation>(JsonSerializer.Serialize(state))!;
        Require(HistoryBranchLayout.Build(entries, restored).Roots.SequenceEqual(layout.Roots), "Branch placement did not roundtrip.");
        layout = HistoryBranchLayout.Build(entries.Where(g => g != b).ToArray(), state);
        Require(layout.Bounds.Count == 5 && layout.Roots.Contains(c.Id), "Deleting parent hid surviving children.");
        a.ParentGenerationId = c.Id; c.ParentGenerationId = a.Id;
        layout = HistoryBranchLayout.Build(entries, new());
        Require(layout.Bounds.Count == 6 && entries.Any(g => g.PresentationHint.Contains("цикл")), "Cycle broke forest loading.");
        var large = new List<GenerationEntry>();
        for (int q = 0; q < 30000; q++) large.Add(new GenerationEntry { ParentGenerationId = q == 0 ? null : large[q - 1].Id });
        Require(HistoryBranchLayout.Build(large, new()).Bounds.Count == large.Count, "Long chain lost nodes or used recursive layout.");
    }

    private static void VerifyRecipes()
    {
        string directory = Path.Combine(Root, "recipes-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var fixture = Fixture(); string path = Path.Combine(directory, "input.png"); fixture.Save(path);
        var library = new ImageLibrary(Path.Combine(directory, "library")); var source = library.Import(path, []);
        var resize = new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new DownscaleOptions { TargetWidth = 17, TargetHeight = 11, SpriteMode = true } };
        var mono = new ProcessingStep { OperationId = ProcessingOperations.Monochrome };
        using var scaled = IndependentImageProcessor.Scale(fixture, resize.Downscale!);
        var parent = library.SaveGeneration(source, scaled, null, new GenerationEntry { OperationId = resize.OperationId, AppliedRecipe = HistoryRecipes.Single(resize) }); source.Generations.Add(parent);
        using var gray = ImageFilters.Monochrome(scaled);
        var child = library.SaveGeneration(source, gray, null, new GenerationEntry { ParentGenerationId = parent.Id, OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono) }); source.Generations.Add(child);
        Require(child.RecipeSnapshot?.Steps.Count == 2, "Full recipe was not snapshotted.");
        library.RemoveGeneration(source, parent); source.Generations.Remove(parent);
        source = library.Load().Single(); child = source.Generations.Single();
        Require(HistoryRecipes.TryBuild(source, child, out var recipe, out _), "Deleting ancestor invalidated an existing recipe.");
        using var replay = new WorkflowExecutor().Execute(fixture, recipe!); Equal(gray, replay);
        var store = new MacroStore(library.DirectoryPath); var macro = store.Save(new MacroDefinition { Name = "Точный размер", Steps = recipe!.Steps });
        Require(!File.ReadAllText(store.PathFor(macro.Id)).Contains("threadCount", StringComparison.OrdinalIgnoreCase), "Recipe leaked thread count.");
        bool duplicate = false; try { store.Save(new MacroDefinition { Name = "ТОЧНЫЙ РАЗМЕР", Steps = macro.Steps }); } catch (ArgumentException) { duplicate = true; }
        Require(duplicate, "Macro names were not unique.");
        store.Save(new MacroDefinition { Id = macro.Id, Name = "Переименован", Steps = macro.Steps }); store.Delete(macro.Id);
        var namedMacro = new MacroDefinition { Name = "Переносимый", Steps = macro.Steps };
        string namedPath = Path.Combine(store.DirectoryPath, "portable.json");
        File.WriteAllText(namedPath, JsonSerializer.Serialize(namedMacro, WorkflowJson.Options));
        store.Save(new MacroDefinition { Id = namedMacro.Id, Name = "Другое имя", Steps = namedMacro.Steps });
        Require(MacroStore.Read(namedPath).Name == "Другое имя", "Renaming a portable macro left a duplicate file.");
        store.Delete(namedMacro.Id); Require(!File.Exists(namedPath), "Deleting a portable macro left its definition.");
        File.WriteAllText(namedPath, "{\"name\":\"No version or ID\",\"steps\":[{\"operationId\":\"filter.monochrome\"}]}");
        Require(store.List().Count == 0 && store.LoadWarnings.Count == 1, "Unversioned macro received a random unstable ID.");
        File.Delete(namedPath);
        var macroResult = library.SaveGeneration(source, replay, null, new GenerationEntry { OperationId = ProcessingOperations.Macro, AppliedRecipe = recipe }); source.Generations.Add(macroResult);
        Require(HistoryRecipes.TryBuild(source, macroResult, out var flattened, out _) && flattened!.Steps.Count == 2, "Macro result depended on deleted definition.");
        var independent = library.ImportGeneration(source, child, [source]);
        var independentResult = library.SaveGeneration(independent, gray, null, new GenerationEntry { OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono) });
        Require(independentResult.RecipeSnapshot!.Steps.Count == 1, "Independent source traversed its origin history.");
        var incomplete = new GenerationEntry { Operation = "ASCII" };
        Require(!HistoryRecipes.TryBuild(source, incomplete, out _, out string reason) && reason.Contains("параметры"), "Incomplete legacy ASCII was silently guessed.");
        var manual = new GenerationEntry { RecipeUnavailableReason = "Ручной ластик" };
        Require(!HistoryRecipes.TryBuild(source, manual, out _, out reason) && reason.Contains("ластик"), "Manual step was silently skipped.");
        using var original = ImageLibrary.ReadBitmap(library.SourcePath(source)); Equal(fixture, original);
        var legacy = library.SaveGeneration(source, fixture, null, new GenerationEntry()); source.Generations.Add(legacy);
        string legacyPath = Path.Combine(Path.GetDirectoryName(library.ResultPath(source, legacy))!, "generation.json");
        var record = JsonNode.Parse(File.ReadAllText(legacyPath))!;
        record.AsObject().Remove("RecipeSnapshot"); record.AsObject().Remove("AppliedRecipe"); record.AsObject().Remove("OperationId");
        record["Settings"]!["Downscale"]!.AsObject().Remove("TargetWidth"); File.WriteAllText(legacyPath, record.ToJsonString());
        var incompleteDimensions = library.Load().Single(s => s.Id == source.Id).Generations.Single(g => g.Id == legacy.Id);
        Require(!HistoryRecipes.TryBuild(source, incompleteDimensions, out _, out reason) && reason.Contains("TargetWidth"), "Missing legacy dimensions were replaced by defaults.");
        var contours = new ContourCleanOptions { WindowSize = 5, MinimumAgreement = 0.45 };
        var contourStep = new ProcessingStep { OperationId = ProcessingOperations.ContourHeatmap, Contours = contours };
        using var contourMap = ContourNoiseCleaner.Analyze(fixture, contours).Heatmap;
        var contourEntry = library.SaveGeneration(source, contourMap, null, new GenerationEntry
        {
            OperationId = contourStep.OperationId, AppliedRecipe = HistoryRecipes.Single(contourStep)
        });
        source.Generations.Add(contourEntry);
        Require(HistoryRecipes.TryBuild(source, contourEntry, out var contourRecipe, out reason), reason);
        using var contourReplay = new WorkflowExecutor().Execute(fixture, contourRecipe!);
        Equal(contourMap, contourReplay);
        Require(contourRecipe!.Steps[0].Contours?.MinimumAgreement == 0.45,
            "Saved contour map lost its parameters.");
        Console.WriteLine("  Recipes: exact resize, alpha, ancestor deletion, independent source, macro rename/delete and legacy refusal passed.");
    }

    private static void VerifyEditor()
    {
        string directory = Path.Combine(Root, "editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string libraryPath = Path.Combine(directory, "library"); var library = new ImageLibrary(libraryPath);
        using var fixture = Fixture(); string path = Path.Combine(directory, "input.png"); fixture.Save(path);
        var source = library.Import(path, []);
        string rootPath = Path.Combine(directory, "root.png"); fixture.Save(rootPath);
        var rootSource = library.Import(rootPath, [source]);
        var mono = new ProcessingStep { OperationId = ProcessingOperations.Monochrome };
        GenerationEntry? parent = null;
        for (int q = 0; q < 80; q++)
        {
            var generation = library.SaveGeneration(source, fixture, null, new GenerationEntry { ParentGenerationId = q % 10 == 0 ? null : parent?.Id,
                OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono), Caption = "Ветка " + q });
            source.Generations.Add(generation); parent = generation;
        }
        // Logical folders intentionally do not match file paths. A shared asset has distinct project links.
        var catalog = new WorkspaceCatalog(libraryPath); catalog.SyncImages([source, rootSource]);
        var project = catalog.CreateProject("Проект A"); var otherProject = catalog.CreateProject("Проект Б");
        var folder = catalog.CreateFolder(project.Id, null, "Папка"); var nested = catalog.CreateFolder(project.Id, folder.Id, "Вложенная");
        catalog.CreateFolder(project.Id, null, "Пустая"); var otherFolder = catalog.CreateFolder(otherProject.Id, null, "Другая папка");
        catalog.Link(source.Id, project.Id, nested.Id); catalog.Link(source.Id, otherProject.Id, otherFolder.Id);
        catalog.Link(rootSource.Id, project.Id, null);
        catalog.State.View = "project"; catalog.State.ProjectId = project.Id; catalog.State.FolderId = nested.Id; catalog.Save();
        var store = new MacroStore(libraryPath);
        var macro = store.Save(new MacroDefinition { Name = "Один итог", Steps = [new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new DownscaleOptions { TargetWidth = 17, TargetHeight = 11, SpriteMode = true } }, mono] });
        var window = new MainWindow(libraryPath) { Width = 1280, Height = 850, ShowActivated = false, ShowInTaskbar = false, Left = -8000, Top = -8000 };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            var actualCatalog = window.Workspace!;
            Wait(window.OpenWorkspaceAssetAsync(actualCatalog.State.Assets.Single(a => a.Id == source.Id))); Pump();
            var strip = Get<VirtualizingTray>(window, "generationStrip"); var sources = Get<VirtualizingTray>(window, "sourceStrip");
            Require(sources.Items.OfType<SourceEntry>().Count() == 1 && Get<Guid?>(window, "_sourceTrayFolder") == nested.Id, "Opening source did not resolve its active project folder.");
            Require(Get<Border>(window, "sourceTray").Visibility == Visibility.Collapsed && Get<RowDefinition>(window, "sourceLibraryHeight").ActualHeight == 0,
                "Source library did not start collapsed below the preview.");
            Require(strip.Items[0] is HistorySourceRoot root && root.Source.Id == source.Id && strip.SelectedItem == root,
                "The active source is missing as the history root.");
            Capture(window, "editor-source-root.png");
            var active = Get<SourceEntry>(window, "_activeSource");
            var selected = active.Generations[4]; Invoke(window, "PromoteHistoryBranch", selected, (int?)null); Pump();
            Require(strip.CardBounds[5].X == strip.CardBounds[1].X && selected.ParentGenerationId is not null, "Editor promotion lost real parent.");
            Require(strip.CardBounds[0].X < strip.CardBounds[1].X && strip.Connections.Count > active.Generations.Count - 1,
                "Source root is not connected to the result branches.");
            var row = Get<RowDefinition>(window, "trayHeight"); row.Height = new GridLength(300); Invoke(window, "CommitTrayHeight"); Pump();
            var splitter = Get<GridSplitter>(window, "trayHeightSplitter"); double beforeDrag = row.ActualHeight;
            Require(splitter.ActualWidth >= Get<Grid>(window, "historyRow").ActualWidth - 2,
                "History height divider does not span the tree.");
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 30) { RoutedEvent = Thumb.DragDeltaEvent });
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 30, false) { RoutedEvent = Thumb.DragCompletedEvent }); Pump();
            Require(row.ActualHeight > beforeDrag + 20, "Shared splitter did not resize the tray row.");
            Get<Button>(window, "sourceLibraryToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Require(Get<Border>(window, "sourceTray").Visibility == Visibility.Visible && Get<RowDefinition>(window, "sourceLibraryHeight").ActualHeight > 0,
                "Source library did not expand below the preview.");
            Require(sources.Items.OfType<TrayFolderHeader>().Any(h => h.IsEmpty), "Expanded source tray hid empty folders.");
            Require(sources.Items.OfType<TrayFolderHeader>().Any(h => h.Label.Contains("Вложенная")), "Nested folder header missing.");
            sources.SelectedItem = sources.Items.OfType<SourceEntry>().Single(s => s.Id == rootSource.Id); Pump();
            Require(Get<SourceEntry>(window, "_activeSource").Id == rootSource.Id && strip.Items[0] is HistorySourceRoot otherRoot && otherRoot.Source.Id == rootSource.Id,
                "Choosing another asset in the lower library did not replace the history root.");
            sources.SelectedItem = sources.Items.OfType<SourceEntry>().Single(s => s.Id == source.Id); Pump();
            Require(Get<SourceEntry>(window, "_activeSource").Id == source.Id, "Lower library could not switch back to the original asset.");
            Invoke(window, "NavigateSourceFolder", folder.Id, false); Pump();
            Require(Get<SourceEntry>(window, "_activeSource").Id == source.Id, "Folder navigation switched preview.");
            Invoke(window, "NavigateSourceFolder", folder.Id, true); Pump();
            Require(!sources.Items.OfType<TrayFolderHeader>().Any(h => h.Label.Contains("Вложенная")), "Folder collapse did not hide descendants.");
            Invoke(window, "NavigateSourceFolder", folder.Id, true); Pump();
            window.Browser!.Navigate("project", otherProject.Id);
            Wait(window.OpenWorkspaceAssetAsync(actualCatalog.State.Assets.Single(a => a.Id == source.Id))); Pump();
            Require(Get<Guid?>(window, "_sourceTrayFolder") == otherFolder.Id && Get<TextBlock>(window, "sourceCaption").Text.Contains("Другая папка"), "Shared asset reused another project's folder link.");
            window.Browser.Navigate("project", project.Id, nested.Id);
            Wait(window.OpenWorkspaceAssetAsync(actualCatalog.State.Assets.Single(a => a.Id == source.Id))); Pump();
            var preview = Get<Grid>(window, "previewPair"); Require(preview.ActualHeight >= 240, "Trays consumed preview minimum.");
            strip.RestoreScroll(new Point(0, 0)); Pump();
            strip.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent }); Pump();
            double firstRow = Math.Min(strip.CardBounds.Select(bounds => bounds.Top - strip.CardBounds.Min(card => card.Top))
                .Where(offset => offset > .5).Min(), strip.Panel!.ExtentHeight - strip.Panel.ViewportHeight);
            Require(Math.Abs(strip.ScrollPosition.Y - firstRow) < 1,
                $"One wheel notch did not advance exactly one history card row: {strip.ScrollPosition.Y} vs {firstRow}, viewport={strip.Panel.ViewportHeight}, extent={strip.Panel.ExtentHeight}.");
            strip.RestoreScroll(new Point(0, 0)); Pump();
            strip.Panel.ScrollByCard(true, true); Pump();
            double firstColumn = Math.Min(strip.CardBounds.Select(bounds => bounds.Left - strip.CardBounds.Min(card => card.Left))
                .Where(offset => offset > .5).Min(), strip.Panel.ExtentWidth - strip.Panel.ViewportWidth);
            Require(Math.Abs(strip.ScrollPosition.X - firstColumn) < 1,
                "Horizontal history scroll did not advance one card column.");
            strip.RestoreScroll(new Point(500, 100)); Pump(); Require(strip.ScrollPosition.X > 0 && strip.ScrollPosition.Y > 0, "Both result scroll axes are not active.");
            int realized = Enumerable.Range(0, strip.Items.Count).Count(q => strip.ItemContainerGenerator.ContainerFromIndex(q) is not null);
            Require(realized < active.Generations.Count / 2, "History created containers for all cards.");
            var originalItems = sources.ItemsSource; var originalBounds = sources.CardBounds;
            var largeCatalog = Enumerable.Range(0, 20000).Select(q => new SourceEntry { DisplayName = "Asset " + q, Thumbnail = source.Thumbnail }).ToArray();
            sources.ItemsSource = largeCatalog;
            sources.SetLayout(largeCatalog.Select((_, q) => new Rect(q % 200 * 130, q / 200 * 78, 124, 68)).ToArray());
            Pump(); sources.RestoreScroll(new Point(10000, 2500)); Pump();
            int visibleSources = Enumerable.Range(0, sources.Items.Count).Count(q => sources.ItemContainerGenerator.ContainerFromIndex(q) is not null);
            Require(visibleSources < 100, "Large source catalog realized all thumbnails.");
            sources.ItemsSource = originalItems; sources.SetLayout(originalBounds); Pump();
            window.ProcessingBasis = 0; Get<ListBox>(window, "_macroList").SelectedItem = Get<ObservableCollection<MacroDefinition>>(window, "_macros").Single(m => m.Id == macro.Id);
            Require(Get<ListBox>(window, "_macroSteps").IsHitTestVisible && Get<ListBox>(window, "_macroSteps").Focusable,
                "Macro step list must support real scrolling and keyboard inspection.");
            int before = active.Generations.Count;
            Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!); Pump();
            Require(active.Generations.Count == before + 1 && active.Generations[^1].RecipeSnapshot?.Steps.Count == 2 && active.Generations[^1].ParentGenerationId is null, "Macro published intermediates or wrong parent.");
            Require(active.Generations[^1].Width == 17 && active.Generations[^1].Height == 11, "Macro did not repeat literal dimensions.");
            Require(strip.CardBounds[^1].IntersectsWith(new Rect(strip.ScrollPosition, new System.Windows.Size(strip.Panel!.ViewportWidth, strip.Panel.ViewportHeight))), "New result did not become visible.");
            strip.SelectedItem = strip.Items[0]; Pump();
            Require(window.ProcessingBasis == 0 && strip.SelectedItem is HistorySourceRoot,
                "Selecting the history root did not restore the original as processing input.");
            strip.SelectedItem = active.Generations[^1]; Pump();
            window.ProcessingBasis = 1;
            Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!); Pump();
            Require(active.Generations[^1].ParentGenerationId == active.Generations[^2].Id && active.Generations[^1].RecipeSnapshot?.Steps.Count == 4, "Macro-on-result lost expanded recipe or parent.");
            foreach (var theme in new[] { TesseraTheme.Dark, TesseraTheme.Light })
            {
                AppearanceResources.Apply(theme); Pump(); Capture(window, theme == TesseraTheme.Dark ? "editor-dark.png" : "editor-light.png");
            }
            window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
            Require(preview.ActualHeight >= 240 && Get<Grid>(window, "historyRow").ActualWidth >= 280, "Minimum window clipped trays or preview.");
            Invoke(window, "RememberTrayScroll"); Invoke(window, "SaveTrayState");
            var restored = JsonSerializer.Deserialize<HistoryTrayState>(File.ReadAllText(Path.Combine(libraryPath, "editor-history.json")))!;
            Require(restored.Branches[source.Id].Detached.Contains(selected.Id), "Detached layout was not saved.");
            var savedPosition = strip.ScrollPosition;
            window.Close(); Pump();
            var reopened = new MainWindow(libraryPath) { Width = window.Width, Height = window.Height, ShowActivated = false, ShowInTaskbar = false, Left = -8000, Top = -8000 };
            try
            {
                reopened.EnableWorkspacePrototype(); reopened.Show(); Pump();
                Wait(reopened.OpenWorkspaceAssetAsync(reopened.Workspace!.State.Assets.Single(a => a.Id == source.Id))); Pump(); Pump();
                var stateAfterRestart = Get<HistoryTrayState>(reopened, "_trayState");
                Require(stateAfterRestart.Branches[source.Id].Detached.Contains(selected.Id), "Branch order was lost after editor restart.");
                var afterRestart = Get<VirtualizingTray>(reopened, "generationStrip").ScrollPosition;
                double expectedHeight = Math.Min(restored.Height, Math.Max(120, Get<Grid>(reopened, "previewArea").ActualHeight - 246));
                Require(Math.Abs(Get<RowDefinition>(reopened, "trayHeight").ActualHeight - expectedHeight) < 2,
                    "Opening through the library reset the saved expanded tray height.");
                Require(Math.Abs(savedPosition.X - afterRestart.X) < 2 && Math.Abs(savedPosition.Y - afterRestart.Y) < 2,
                    $"Tray scroll did not restore: {savedPosition} -> {afterRestart}.");
            }
            finally { reopened.Close(); Pump(); }
            var nameDialog = new MacroNameDialog("Макрос", " ", n => n != "Занято");
            Require(!nameDialog.Confirm.IsEnabled, "Blank macro name accepted.");
            nameDialog.NameInput.Text = "Занято"; Require(!nameDialog.Confirm.IsEnabled, "Duplicate name accepted.");
            nameDialog.NameInput.Text = "Новый"; Require(nameDialog.Confirm.IsEnabled, "Unique name rejected.");
            Console.WriteLine($"  Editor: {before} cards, {realized} realized; 20,000 sources, {visibleSources} realized; two axes, nested/empty/collapsed/project folders, one final result and both themes passed.");
            File.WriteAllText(Path.Combine(Root, "native-library.txt"), libraryPath);
        }
        finally { if (window.IsVisible) window.Close(); Pump(); AppearanceResources.Apply(TesseraTheme.Dark); }
    }

    private static Bitmap Fixture()
    {
        var bitmap = new Bitmap(48, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, Color.FromArgb((x + y) % 5 == 0 ? 72 : 255, x * 5, y * 7, (x + y) * 3));
        return bitmap;
    }
    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Bitmap dimensions changed.");
        for (int y = 0; y < actual.Height; y++) for (int x = 0; x < actual.Width; x++)
            Require(actual.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb(), $"Pixel differs at {x},{y}.");
    }
    private static T Get<T>(MainWindow window, string name) => (T)(typeof(MainWindow).GetField(name, Private)?.GetValue(window) ?? window.FindName(name))!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, arguments);
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame); }
    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Editor operation timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void Capture(MainWindow window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Root, name)); encoder.Save(stream);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
