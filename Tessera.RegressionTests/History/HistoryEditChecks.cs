using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Tessera.Core.Workflows;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Localization = Tessera.Desktop.Localization;

namespace Tessera.RegressionTests;

internal static class HistoryEditChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); Verify(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("History edit checks timed out.");
        if (failure is not null) throw failure;
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

            window.EnableWorkspacePrototype();
            Invoke(window, "SelectSource", active);
            var bakeTarget = active.Generations.Single(g => g.Id == child.Id);
            using var tailImage = new WorkflowExecutor().Execute(childImage, HistoryRecipes.Single(mono));
            var tail = library.SaveGeneration(active, tailImage, null, new GenerationEntry
            {
                OperationId = mono.OperationId, AppliedRecipe = HistoryRecipes.Single(mono), ParentGenerationId = bakeTarget.Id
            });
            Require((bool)Invoke(window, "CommitGeneration", active, tail, true)!, "Could not publish successor to bake target.");
            Invoke(window, "SelectGeneration", bakeTarget);
            var asset = window.Workspace!.State.Assets.Single(a => a.Id == active.Id);
            var project = window.Workspace.CreateProject("Bake test");
            window.Workspace.Link(asset.Id, project.Id, null);
            byte[] inputBytes = File.ReadAllBytes(path);
            byte[] bakedBytes = File.ReadAllBytes(library.ResultPath(active, bakeTarget));
            var menu = (ContextMenu)Invoke(window, "OpenHistoryContextMenu", bakeTarget, Get<VirtualizingTray>(window, "generationStrip"))!;
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Localization.Text("Bake")))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;
            Pump();

            Require(active.Label == source.Label && active.Id == source.Id && active.OriginalPath == path &&
                File.ReadAllBytes(path).SequenceEqual(inputBytes), "Bake changed the name, identity or imported file.");
            Require(File.ReadAllBytes(library.SourcePath(active)).SequenceEqual(bakedBytes), "Baked PNG does not match the chosen result.");
            Require(active.Generations.Count == 5 && active.Generations.All(g => g.Id != parent.Id && g.Id != child.Id) &&
                tail.ParentGenerationId is null && tail.RecipeSnapshot?.Steps.Count == 1 &&
                active.Generations.Single(g => g.Id == sibling.Id).ParentGenerationId is null &&
                active.Generations.Single(g => g.Id == otherLeaf.Id).ParentGenerationId == other.Id,
                "Bake removed a surviving result or left its parent or recipe inconsistent.");
            Require(active.SelectedGeneration is null && window.ProcessingBasis == 0 &&
                ReferenceEquals(asset.Thumbnail, active.Thumbnail) && asset.Width == childImage.Width &&
                window.Workspace.State.Links.Any(link => link.ProjectId == project.Id && link.AssetId == source.Id),
                "Editor or library did not switch to the baked source.");
            Same(childImage, Get<Bitmap>(window, "_sourceImage"));

            window.Close(); window = Open();
            window.EnableWorkspacePrototype();
            active = Get<System.Collections.ObjectModel.ObservableCollection<SourceEntry>>(window, "_sources").Single();
            asset = window.Workspace!.State.Assets.Single(a => a.Id == active.Id);
            Require(active.SelectedGeneration is null && active.Generations.Count == 5 &&
                ReferenceEquals(asset.Thumbnail, active.Thumbnail), "Baked library preview or history was lost after reopening.");
            Undo(window);
            active = Get<SourceEntry>(window, "_activeSource");
            Require(active.Generations.Count == 7 && active.SelectedGeneration?.Id == child.Id &&
                active.Width == fixture.Width && active.Height == fixture.Height &&
                active.Generations.Single(g => g.Id == tail.Id).ParentGenerationId == child.Id,
                "Undo did not restore the original dimensions, selection or result chain.");
            using var restored = ImageLibrary.ReadBitmap(library.SourcePath(active));
            Same(fixture, restored);
            Same(fixture, Get<Bitmap>(window, "_sourceImage"));
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
        => WindowTestAccess.Invoke(window, name, Private, args);
    private static T Get<T>(MainWindow window, string name)
        => (T)(WindowTestAccess.GetField(window, name, Private) ?? window.FindControl(name))!;
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
