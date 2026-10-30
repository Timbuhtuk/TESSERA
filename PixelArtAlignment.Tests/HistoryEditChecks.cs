using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class HistoryEditChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); VerifyStoreFailures(); VerifyGridInsertion(); Verify(); HistoryWorkspaceEditChecks.Run(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("History edit checks timed out.");
        if (failure is not null) throw failure;
    }

    private static void VerifyStoreFailures()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "history-store-" + Guid.NewGuid().ToString("N")));
        var source = new SourceEntry();
        string folder = Path.Combine(root, source.Id.ToString("N"), "generations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string image = Path.Combine(folder, "result.png");
        File.WriteAllText(image, "before");
        string branch = Path.Combine(root, "editor-history.json"); File.WriteAllText(branch, "{}");
        var store = new HistoryUndoStore(root, 64);
        store.Begin(source, new()).Commit();
        File.WriteAllText(image, "current");
        using (var locked = new FileStream(branch, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool failed = false;
            try { store.Undo(); } catch (IOException) { failed = true; }
            Require(failed && File.ReadAllText(image) == "current" && store.CanUndo,
                "A branch-write failure partially changed history or consumed its undo action.");
        }
        File.SetAttributes(branch, FileAttributes.ReadOnly);
        try
        {
            bool failed = false;
            try { store.Undo(); } catch (UnauthorizedAccessException) { failed = true; }
            Require(failed && File.ReadAllText(image) == "current" && store.CanUndo,
                "A late branch-publication failure did not restore the original directory and undo action.");
        }
        finally { File.SetAttributes(branch, FileAttributes.Normal); }
        store.Undo(); Require(File.ReadAllText(image) == "before", "Undo could not retry after a transient failure.");
        store.Begin(source, new()).Commit(); File.WriteAllText(image, "current");
        string blob = Directory.EnumerateFiles(Path.Combine(root, ".history-undo", "objects")).Single();
        File.WriteAllText(blob, "corrupted");
        bool rejected = false;
        try { store.Undo(); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && File.ReadAllText(image) == "current" && store.CanUndo,
            "A damaged snapshot overwrote valid history instead of failing before publication.");
        File.WriteAllText(blob, "before"); store.Undo();

        store.Begin(source, new()); File.WriteAllText(image, "unfinished");
        string extra = Path.Combine(Path.GetDirectoryName(folder)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extra); File.WriteAllText(Path.Combine(extra, "result.png"), "uncommitted");
        new HistoryUndoStore(root, 64).RecoverPending();
        Require(File.ReadAllText(image) == "before" && !Directory.Exists(extra), "Pending transaction did not recover its exact history.");
        new HistoryUndoStore(root, 64).RecoverPending();
        for (int q = 0; q < 66; q++) { store.Begin(source, new()).Commit(); File.WriteAllText(image, q.ToString()); }
        Require(Directory.EnumerateDirectories(Path.Combine(root, ".history-undo", "actions")).Count() == 64, "Undo capacity exceeded 64.");
        store.SetCapacity(1);
        Require(Directory.EnumerateDirectories(Path.Combine(root, ".history-undo", "actions")).Count() == 1, "Undo capacity did not shrink to one.");
        store.Undo(); Require(File.ReadAllText(image) == "64" && !store.CanUndo, "Capacity pruning removed the latest action.");
        Console.WriteLine("  History store: locked branch writes, corrupt snapshots, retries, recovery and 1–64 bounds passed.");
    }

    private static void VerifyGridInsertion()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "history-grid-edit-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "source.png");
        using var image = new Bitmap(16, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            image.SetPixel(x, y, (x / 4 + y / 4) % 2 == 0 ? Color.Red : Color.Blue);
        image.Save(input);
        string libraryPath = Path.Combine(root, "library"); var library = new ImageLibrary(libraryPath); var source = library.Import(input, []);
        var parent = library.SaveGeneration(source, image, null, new GenerationEntry
        {
            Operation = "Align 2", OperationId = ProcessingOperations.Align, IsAlignment = true, CellSize = 2,
            AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Align, CellSize = 2 })
        }); source.Generations.Add(parent);
        using var reduced = new WorkflowExecutor().Execute(image, HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Reduce, CellSize = 2 }));
        var child = library.SaveGeneration(source, reduced, null, new GenerationEntry
        {
            Operation = "Reduce inherited grid", OperationId = ProcessingOperations.Reduce, IsGridReduction = true, CellSize = 2,
            ParentGenerationId = parent.Id, AppliedRecipe = HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Reduce, UsePreviousAlignment = true })
        }); source.Generations.Add(child);
        var window = new MainWindow(libraryPath) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump(); var active = Get<SourceEntry>(window, "_activeSource");
            Invoke(window, "SelectGeneration", active.Generations.Single(g => g.Id == parent.Id));
            Get<CheckBox>(window, "insertHistoryInput").IsChecked = true;
            var align = new ProcessingStep { OperationId = ProcessingOperations.Align, CellSize = 4 };
            using var insertedImage = new WorkflowExecutor().Execute(image, HistoryRecipes.Single(align));
            var inserted = library.SaveGeneration(active, insertedImage, null, new GenerationEntry
            {
                Operation = "Align 4", OperationId = ProcessingOperations.Align, IsAlignment = true, CellSize = 4,
                ParentGenerationId = parent.Id, AppliedRecipe = HistoryRecipes.Single(align)
            });
            Require((bool)Invoke(window, "CommitGeneration", active, inserted, true)!, "Grid insertion failed.");
            var actual = active.Generations.Single(g => g.Id == child.Id);
            using var expected = new WorkflowExecutor().Execute(insertedImage, HistoryRecipes.Single(new ProcessingStep { OperationId = ProcessingOperations.Reduce, CellSize = 4 }));
            using var pixels = ImageLibrary.ReadBitmap(library.ResultPath(active, actual));
            Same(expected, pixels);
            Require(actual.CellSize == 4, "Recomputed grid metadata did not follow the inserted alignment.");
            using var replay = new WorkflowExecutor().Execute(image, actual.RecipeSnapshot!); Same(expected, replay);
        }
        finally { window.Close(); Pump(); }
    }

    private static void Verify()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "history-edits-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string libraryPath = Path.Combine(root, "library");
        string appearancePath = Path.Combine(root, "appearance.json");
        new AppearanceSettings { HistoryUndoDepth = 64 }.Save(appearancePath);
        Require(AppearanceSettings.Load(appearancePath).HistoryUndoDepth == 64, "Maximum undo depth did not persist.");
        new AppearanceSettings { HistoryUndoDepth = 1 }.Save(appearancePath);
        Require(AppearanceSettings.Load(appearancePath).HistoryUndoDepth == 1, "Minimum undo depth did not persist.");
        using var fixture = new Bitmap(12, 12);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
            fixture.SetPixel(x, y, Color.FromArgb(255, x * 17, y * 17, (x + y) * 9));
        string path = Path.Combine(root, "input.png"); fixture.Save(path);
        var library = new ImageLibrary(libraryPath);
        var source = library.Import(path, []);
        ProcessingStep resize = new() { OperationId = ProcessingOperations.Resize,
            Downscale = new DownscaleOptions { TargetWidth = 9, TargetHeight = 9, SpriteMode = true } };
        ProcessingStep mono = new() { OperationId = ProcessingOperations.Monochrome };
        ProcessingStep sharpen = new() { OperationId = ProcessingOperations.Sharpen,
            Sharpen = new SharpenSettings { Strength = 100, Threshold = 4 } };
        using var parentImage = new WorkflowExecutor().Execute(fixture, HistoryRecipes.Single(resize));
        var parent = Save(parentImage, resize, null);
        using var childImage = new WorkflowExecutor().Execute(parentImage, HistoryRecipes.Single(sharpen));
        var child = Save(childImage, sharpen, parent);
        using var siblingImage = new WorkflowExecutor().Execute(parentImage, HistoryRecipes.Single(mono));
        var sibling = Save(siblingImage, mono, parent);
        using var otherImage = new WorkflowExecutor().Execute(fixture, HistoryRecipes.Single(mono));
        var other = Save(otherImage, mono, null);
        using var otherLeafImage = new WorkflowExecutor().Execute(otherImage, HistoryRecipes.Single(resize));
        var otherLeaf = Save(otherLeafImage, resize, other);

        MainWindow window = Open();
        try
        {
            var active = Get<SourceEntry>(window, "_activeSource");
            var anchor = active.Generations.Single(g => g.Id == parent.Id);
            Invoke(window, "SelectGeneration", anchor);
            Get<CheckBox>(window, "insertHistoryInput").IsChecked = true;
            using var input = ImageLibrary.ReadBitmap(library.ResultPath(active, anchor));
            using var insertedImage = new WorkflowExecutor().Execute(input, HistoryRecipes.Single(mono));
            var inserted = library.SaveGeneration(active, insertedImage, null, new GenerationEntry
            { Operation = "Монохром", Caption = "Вставка", OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono), ParentGenerationId = anchor.Id });
            Require((bool)Invoke(window, "CommitGeneration", active, inserted, true)!, "Insertion failed.");
            Require(Get<CheckBox>(window, "insertHistoryInput").IsChecked != true, "One-shot insertion stayed enabled.");
            Require(active.Generations.Single(g => g.Id == child.Id).ParentGenerationId == inserted.Id &&
                active.Generations.Single(g => g.Id == sibling.Id).ParentGenerationId == inserted.Id, "Both descendants were not reparented.");
            using var expectedChild = new WorkflowExecutor().Execute(insertedImage, HistoryRecipes.Single(sharpen));
            using var actualChild = ImageLibrary.ReadBitmap(library.ResultPath(active, active.Generations.Single(g => g.Id == child.Id)));
            Same(expectedChild, actualChild);
            window.Close(); window = Open();
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.Any(g => g.Id == inserted.Id), "Insertion did not persist.");
            Undo(window);
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.All(g => g.Id != inserted.Id) && active.Generations.Single(g => g.Id == child.Id).ParentGenerationId == parent.Id,
                "Persistent undo did not restore insertion.");
            using var restoredChild = ImageLibrary.ReadBitmap(library.ResultPath(active, active.Generations.Single(g => g.Id == child.Id)));
            Same(childImage, restoredChild);

            Invoke(window, "SelectGeneration", active.Generations.Single(g => g.Id == parent.Id));
            Get<CheckBox>(window, "insertHistoryInput").IsChecked = true;
            var resizeStep = new ProcessingStep { OperationId = ProcessingOperations.Resize,
                Downscale = new DownscaleOptions { TargetWidth = 6, TargetHeight = 6, SpriteMode = true } };
            using var smaller = new WorkflowExecutor().Execute(parentImage, HistoryRecipes.Single(resizeStep));
            var resizeInsert = library.SaveGeneration(active, smaller, null, new GenerationEntry
            {
                Operation = "Inserted resize", OperationId = ProcessingOperations.Resize, ParentGenerationId = parent.Id,
                AppliedRecipe = HistoryRecipes.Single(resizeStep), Settings = new EditorSettings { Downscale = resizeStep.Downscale! }
            });
            Require((bool)Invoke(window, "CommitGeneration", active, resizeInsert, true)!, "Resize insertion failed.");
            var resizedChild = active.Generations.Single(g => g.Id == child.Id);
            Require(resizedChild.Width == 6 && resizedChild.Height == 6 && resizedChild.Settings.Downscale.TargetWidth == 6 && resizedChild.Settings.Downscale.TargetHeight == 6,
                "Recomputed non-resize cards retained stale cumulative dimensions.");
            Invoke(window, "SelectGeneration", resizedChild);
            Require(Get<IntegerInput>(window, "widthInput").Value == 6 && Get<IntegerInput>(window, "heightInput").Value == 6,
                "Selecting a recomputed card did not restore its updated size settings.");
            Undo(window); active = Get<SourceEntry>(window, "_activeSource");

            Invoke(window, "MergeHistoryBranches", active.Generations.Single(g => g.Id == otherLeaf.Id), active.Generations.Single(g => g.Id == child.Id));
            Require(active.Generations.Count == 7 && active.Generations.Any(g => g.Id == otherLeaf.Id), "Merge did not add two nodes or altered source branch.");
            var mergedLeaf = active.SelectedGeneration!;
            var mergedRoot = active.Generations.Single(g => g.Id == mergedLeaf.ParentGenerationId);
            Require(mergedRoot.ParentGenerationId == child.Id, "Merge path did not attach to target tail.");
            Undo(window);
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.Count == 5, "Merge was not one undo action.");

            var failingChild = active.Generations.Single(g => g.Id == child.Id);
            failingChild.AppliedRecipe = null; failingChild.RecipeSnapshot = null;
            failingChild.RecipeUnavailableReason = "Проверочная причина";
            Invoke(window, "SelectGeneration", active.Generations.Single(g => g.Id == parent.Id));
            Get<CheckBox>(window, "insertHistoryInput").IsChecked = true;
            var failedInsert = library.SaveGeneration(active, siblingImage, null, new GenerationEntry
            { Operation = "Монохром", OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono), ParentGenerationId = parent.Id });
            string? modalMessage = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var modal = window.OwnedWindows.OfType<TesseraDialog>().LastOrDefault();
                if (modal is null) return;
                modalMessage = ((TextBlock)modal.FindName("dialogMessage")).Text;
                modal.Close();
            }));
            Require(!(bool)Invoke(window, "CommitGeneration", active, failedInsert, true)!, "Unreplayable descendant was silently accepted.");
            active = Get<SourceEntry>(window, "_activeSource");
            Require(modalMessage is not null && modalMessage.Contains("filter.sharpen") && modalMessage.Contains("Проверочная причина"),
                "Recomputation failure lacked a modal with the failed operation and cause.");
            Require(active.Generations.Count == 5 && active.Generations.All(g => g.Id != failedInsert.Id) &&
                active.Generations.Single(g => g.Id == child.Id).ParentGenerationId == parent.Id &&
                Get<CheckBox>(window, "insertHistoryInput").IsChecked == true, "Failed insertion was not rolled back atomically.");
            Get<CheckBox>(window, "insertHistoryInput").IsChecked = false;

            using var macroImage = new WorkflowExecutor().Execute(fixture, HistoryRecipes.Single(mono));
            var macro = library.SaveGeneration(active, macroImage, null, new GenerationEntry
            { Operation = "Макрос", Caption = "Макрос", OperationId = ProcessingOperations.Macro,
                AppliedRecipe = HistoryRecipes.Single(mono), ParentGenerationId = parent.Id });
            Require((bool)Invoke(window, "CommitGeneration", active, macro, true)!, "Macro result publication failed.");
            Invoke(window, "RemoveGenerationWithUndo", macro, false);
            Require(active.Generations.All(g => g.Id != macro.Id), "Macro node stayed visible after deletion.");
            window.Close(); window = Open();
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.All(g => g.Id != macro.Id) && library.Load().Single().Generations.All(g => g.Id != macro.Id),
                "Deleted macro node returned after restart.");
            Undo(window);
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.Any(g => g.Id == macro.Id), "Deleted macro node was not restorable with Ctrl+Z history.");
            Invoke(window, "RemoveGenerationWithUndo", active.Generations.Single(g => g.Id == parent.Id), true);
            Require(active.Generations.Count == 2 && active.Generations.All(g => g.Id == other.Id || g.Id == otherLeaf.Id),
                "Shift-delete did not remove the selected node and its visual descendants.");
            Undo(window);
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.Count == 6 && active.Generations.Any(g => g.Id == macro.Id),
                "Shift-delete was not restored as one undo action.");
        }
        finally { window.Close(); }

        GenerationEntry Save(Bitmap image, ProcessingStep step, GenerationEntry? basis)
        {
            var entry = library.SaveGeneration(source, image, null, new GenerationEntry
            { Operation = step.OperationId, Caption = step.OperationId, OperationId = step.OperationId,
                ParentGenerationId = basis?.Id, AppliedRecipe = HistoryRecipes.Single(step) });
            source.Generations.Add(entry); return entry;
        }
        MainWindow Open()
        {
            var result = new MainWindow(libraryPath) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.Show(); Pump(); return result;
        }
    }

    private static void Undo(MainWindow window)
    {
        var store = Get<HistoryUndoStore>(window, "_historyUndo");
        var snapshot = store.Undo() ?? throw new Exception("Undo buffer empty.");
        Invoke(window, "RestoreHistorySource", snapshot);
    }

    private static object? Invoke(MainWindow window, string name, params object[] args)
        => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static T Get<T>(MainWindow window, string name)
        => (T)(typeof(MainWindow).GetField(name, Private)?.GetValue(window) ?? window.FindName(name))!;
    private static void Same(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Image size changed unexpectedly.");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Pixel differs at {x}, {y}.");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
