using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Tessera.Core.Workflows;
using Tessera.Core.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    private readonly ImageEditorPage _editor;
    private MainWindow Host => _editor.Host;

    internal EditorHistory(ImageEditorPage editor, string libraryDirectory)
    {
        _editor = editor;
        _trayStore = new HistoryTrayStore(Host._library.DirectoryPath);
        _historyUndo = new HistoryUndoStore(libraryDirectory, Host._appearanceSettings.HistoryUndoDepth);
        _historyUndo.RecoverPending();
    }

    private readonly HistoryUndoStore _historyUndo;

    internal void Initialize()
    {
        Host.PreviewKeyDown += HistoryUndoKeyDown;
        _editor.insertHistoryInput.Content = Localization.Text("InsertAndRecalculate");
        _editor.insertHistoryInput.ToolTip = Localization.Text("InsertAndRecalculateHint");
        InitializeHistoryTrays();
        InitializeMacros();
    }

    internal void SetUndoCapacity(int capacity) => _historyUndo.SetCapacity(capacity);


    internal void HistoryUndoKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || _editor._processing) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (_editor.Atlas.TryUndoOrRedo(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Z) return;
        e.Handled = true;
        try
        {
            var snapshot = _historyUndo.Undo();
            if (snapshot is null) return;
            RestoreHistorySource(snapshot);
            Host.Notify(Localization.Text("LastHistoryChangeUndone"), NotificationKind.Success);
        }
        catch (Exception ex) { ShowHistoryFailure(Localization.Text("UndoChange"), ex); }
    }

    internal bool CommitGeneration(SourceEntry source, GenerationEntry generation, bool select = true)
    {
        bool insert = _editor.insertHistoryInput.IsChecked == true && source == _editor._activeSource &&
            _editor.ProcessingBasis == 1 && source.SelectedGeneration?.Id == generation.ParentGenerationId;
        HistoryUndoStore.HistoryUndoTransaction? transaction = null;
        try
        {
            transaction = _historyUndo.Begin(source, BranchFor(source), generation.Id);
            if (insert) RecomputeAfterInsertion(source, source.SelectedGeneration!, generation);
            source.Generations.Add(generation);
            if (select && source == _editor._activeSource) _editor.SelectGeneration(generation);
            else source.SelectedGeneration = generation;
            transaction.Commit();
            if (_editor.insertHistoryInput.IsChecked == true) _editor.insertHistoryInput.IsChecked = false;
            Host.CompleteProcessingProgress();
            return true;
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            if (transaction is not null)
            {
                try { RestoreHistorySource(transaction.Rollback()); }
                catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            }
            else Host._library.DeleteUnpublishedGeneration(source, generation);
            ShowHistoryFailure(insert ? Localization.Text("InsertAndRecomputeChain") : Localization.Text("SaveResultLabel"), ex);
            return false;
        }
    }

    private BranchPresentation BranchFor(SourceEntry source)
    {
        if (_loadedBranchSource == source.Id && _loadedBranch is { } cached) return cached;
        BranchPresentation branch;
        try { branch = _trayStore.ReadBranch(source.Id); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Host.Notify(Localization.Text("CouldNotRestoreTrayLayoutPrefix") + ex.Message, NotificationKind.Warning);
            branch = new();
        }
        if (source == _trayHistorySource)
        {
            _loadedBranchSource = source.Id;
            _loadedBranch = branch;
        }
        return branch;
    }

    private void RecomputeAfterInsertion(SourceEntry source, GenerationEntry anchor, GenerationEntry inserted)
    {
        var children = source.Generations.Where(g => g.ParentGenerationId is not null)
            .GroupBy(g => g.ParentGenerationId!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var pending = new Queue<GenerationEntry>(children.GetValueOrDefault(anchor.Id) ?? []);
        var visited = new HashSet<Guid>();
        var staged = new Dictionary<Guid, (GenerationEntry Record, Bitmap Image, Bitmap? Crop)>();
        try
        {
            while (pending.TryDequeue(out var old))
            {
                if (!visited.Add(old.Id)) throw new InvalidDataException(Localization.Text("HistoryResultCycle"));
                if (!HistoryRecipes.TryGetLocal(source, old, out var local, out string reason))
                    throw new InvalidOperationException($"{old.Label} ({old.OperationId ?? old.Operation}): {reason}");
                var parent = old.ParentGenerationId == anchor.Id ? inserted : source.Generations.First(g => g.Id == old.ParentGenerationId);
                using var basis = staged.TryGetValue(parent.Id, out var ready)
                    ? (Bitmap)ready.Image.Clone() : ImageLibrary.ReadBitmap(Host._library.ResultPath(source, parent));
                ProcessingRecipe executable = ExecutableRecipe(local!, old.CellSize);
                Bitmap result; Bitmap? crop = null;
                try
                {
                    if (old.HasCrop && executable.Steps.Count == 1 && executable.Steps[0].OperationId == ProcessingOperations.Profile)
                    {
                        var processed = new PixelArtDownscaler().Process(basis, executable.Steps[0].Downscale!);
                        result = processed.Downscaled; crop = processed.CroppedSource;
                    }
                    else result = new WorkflowExecutor().Execute(basis, executable);
                }
                catch (Exception ex) { throw new InvalidOperationException($"{old.Label} ({old.OperationId ?? old.Operation}): {ex.Message}", ex); }
                var record = JsonSerializer.Deserialize<GenerationEntry>(JsonSerializer.Serialize(old))!;
                if (old.ParentGenerationId == anchor.Id) record.ParentGenerationId = inserted.Id;
                var parentSnapshot = parent.Id == inserted.Id ? inserted.RecipeSnapshot :
                    staged.TryGetValue(parent.Id, out var stagedParent) ? stagedParent.Record.RecipeSnapshot : parent.RecipeSnapshot;
                record.RecipeSnapshot = parentSnapshot is null ? null :
                    new ProcessingRecipe { Steps = parentSnapshot.Steps.Concat(local!.Steps).ToArray() };
                if (record.RecipeSnapshot is null) record.RecipeUnavailableReason = Localization.Text("FullRecipeThisChainUnavailableAfter");
                staged.Add(old.Id, (record, result, crop));
                foreach (var child in children.GetValueOrDefault(old.Id) ?? []) pending.Enqueue(child);
            }
            foreach (var old in source.Generations.Where(g => staged.ContainsKey(g.Id)))
            {
                var pair = staged[old.Id];
                try { Host._library.ReplaceGeneration(source, pair.Record, pair.Image, pair.Crop); }
                catch (Exception ex) { throw new InvalidOperationException($"{old.Label} ({old.OperationId ?? old.Operation}): {ex.Message}", ex); }
                CopyRecomputedEntry(old, pair.Record);
            }
            RefreshHistoryLayout();
        }
        finally { foreach (var pair in staged.Values) { pair.Image.Dispose(); pair.Crop?.Dispose(); } }
    }

    private static ProcessingRecipe ExecutableRecipe(ProcessingRecipe local, int cellSize)
    {
        bool aligned = false;
        var steps = new List<ProcessingStep>();
        foreach (var step in local.Steps)
        {
            steps.Add(step.UsePreviousAlignment && !aligned
                ? new ProcessingStep { OperationId = step.OperationId, CellSize = cellSize } : step);
            if (step.OperationId is ProcessingOperations.Align or ProcessingOperations.AlignLocalSnapped) aligned = true;
            else if (step.OperationId == ProcessingOperations.AlignLocal) aligned = false;
        }
        return new ProcessingRecipe { Steps = steps };
    }

    private static void CopyRecomputedEntry(GenerationEntry target, GenerationEntry record)
    {
        target.ParentGenerationId = record.ParentGenerationId;
        target.Width = record.Width; target.Height = record.Height;
        target.RecipeSnapshot = record.RecipeSnapshot;
        target.RecipeUnavailableReason = record.RecipeUnavailableReason;
        target.ThumbnailFile = record.ThumbnailFile;
    }

    private void BakeHistoryResult(GenerationEntry generation)
    {
        if (_editor._processing || _editor._activeSource is not { } source || !source.Generations.Contains(generation)) return;
        _dragCandidate = null;
        var branch = BranchFor(source);
        HistoryUndoStore.HistoryUndoTransaction? transaction = null;
        try
        {
            var entries = source.Generations.ToDictionary(entry => entry.Id);
            var removed = new HashSet<Guid> { generation.Id };
            var current = generation;
            while (current.ParentGenerationId is { } parentId && entries.TryGetValue(parentId, out var parent))
            {
                if (!removed.Add(parentId))
                    throw new InvalidDataException(Localization.Text("HistoryResultCycle"));
                current = parent;
            }

            transaction = _historyUndo.Begin(source, branch, includeSourceImage: true);
            Host._library.ReplaceSourceImage(source, generation);
            RebaseBakedDescendants(source, generation);
            foreach (var entry in source.Generations.Where(entry => !removed.Contains(entry.Id) &&
                entry.ParentGenerationId is { } parent && removed.Contains(parent)))
            {
                // Keep the recipe from the original branch before unlinking its removed parent.
                HistoryRecipes.Capture(source, entry);
                if (entry.AppliedRecipe is null && HistoryRecipes.TryGetLocal(source, entry, out var local, out _))
                    entry.AppliedRecipe = local;
                entry.ParentGenerationId = null;
                Host._library.SaveGenerationRecord(source, entry);
            }

            foreach (Guid id in removed) Host._library.DeleteGenerationFiles(source, entries[id]);
            branch.Roots.RemoveAll(removed.Contains);
            branch.Detached.ExceptWith(removed);
            _trayStore.SaveBranch(source.Id, branch);
            source.SelectedGeneration = null;

            bool restoring = _restoringHistory, syncing = Host._workspaceSyncing;
            _restoringHistory = Host._workspaceSyncing = true;
            try
            {
                foreach (Guid id in removed) source.Generations.Remove(entries[id]);
            }
            finally { _restoringHistory = restoring; Host._workspaceSyncing = syncing; }

            source.DraftSettings = _editor.Tools.CaptureSettings();
            _editor.OpenSourceImage(source);
            _editor.generationStrip.ScrollIntoView(_historySourceRoot);
            WriteTrayState();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                try { RestoreHistorySource(transaction.Rollback()); }
                catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            }
            ShowHistoryFailure(Localization.Text("Bake"), ex);
            return;
        }
        Host.SynchronizeWorkspace(source);
        Host.Notify(Localization.Text("ResultBakedIntoTheSource"), NotificationKind.Success);
    }

    private void RebaseBakedDescendants(SourceEntry source, GenerationEntry baked)
    {
        var children = source.Generations.ToLookup(entry => entry.ParentGenerationId);
        var pending = new Queue<GenerationEntry>(children[baked.Id]);
        var updates = new List<(GenerationEntry Entry, ProcessingRecipe? Recipe, string Reason)>();
        while (pending.TryDequeue(out var entry))
        {
            HistoryRecipes.TryGetLocal(source, entry, out var recipe, out string reason);
            updates.Add((entry, recipe, reason));
            foreach (var child in children[entry.Id]) pending.Enqueue(child);
        }

        foreach (var (entry, recipe, reason) in updates)
        {
            bool newRoot = entry.ParentGenerationId == baked.Id;
            if (newRoot) entry.ParentGenerationId = null;
            entry.AppliedRecipe = newRoot && recipe is not null ? ExecutableRecipe(recipe, entry.CellSize) : recipe;
            entry.RecipeSnapshot = null;
            entry.RecipeUnavailableReason = recipe is null ? reason : null;
            HistoryRecipes.Capture(source, entry);
            Host._library.SaveGenerationRecord(source, entry);
        }
    }

    private void RemoveGenerationWithUndo(GenerationEntry generation, bool removeTail)
    {
        var source = Host._sources.FirstOrDefault(s => s.HasLoadedHistory && s.Generations.Contains(generation));
        if (source is null) return;
        var layout = HistoryBranchLayout.Build(source.Generations.ToArray(), BranchFor(source));
        var selected = new HashSet<Guid> { generation.Id };
        if (removeTail)
        {
            var queue = new Queue<Guid>(); queue.Enqueue(generation.Id);
            while (queue.TryDequeue(out Guid parent))
                foreach (var child in source.Generations.Where(g => layout.DisplayParents[g.Id] == parent))
                    if (selected.Add(child.Id)) queue.Enqueue(child.Id);
        }
        var depth = new Dictionary<Guid, int>();
        foreach (Guid id in selected)
        {
            int count = 0; Guid? at = layout.DisplayParents.GetValueOrDefault(id);
            while (at is { } parent && selected.Contains(parent)) { count++; at = layout.DisplayParents.GetValueOrDefault(parent); }
            depth[id] = count;
        }
        var transaction = _historyUndo.Begin(source, BranchFor(source));
        try
        {
            foreach (var entry in source.Generations.Where(g => selected.Contains(g.Id)).OrderByDescending(g => depth[g.Id]).ToArray())
                RemoveGeneration(entry);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Host._mainProgress?.Dispose();
            try { RestoreHistorySource(transaction.Rollback()); }
            catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            ShowHistoryFailure(Localization.Text("DeleteResults"), ex);
        }
    }

    private void MergeHistoryBranches(GenerationEntry sourceLeaf, GenerationEntry targetLeaf)
    {
        var source = Host._sources.FirstOrDefault(s => s.HasLoadedHistory && s.Generations.Contains(sourceLeaf) && s.Generations.Contains(targetLeaf));
        if (source is null) return;
        var layout = HistoryBranchLayout.Build(source.Generations.ToArray(), BranchFor(source));
        try
        {
            if (layout.RootOf(sourceLeaf.Id) == layout.RootOf(targetLeaf.Id))
                throw new InvalidOperationException(Localization.Text("SelectTheTailOfADifferent"));
            if (source.Generations.Any(g => layout.DisplayParents[g.Id] == sourceLeaf.Id))
                throw new InvalidOperationException(Localization.Text("TheSourceBranchForksFurther"));
            if (source.Generations.Any(g => layout.DisplayParents[g.Id] == targetLeaf.Id))
                throw new InvalidOperationException(Localization.Text("SelectTargetBranchSFinalNode"));
            var nodes = source.Generations.ToDictionary(g => g.Id);
            var path = new List<GenerationEntry>();
            Guid? at = sourceLeaf.Id;
            while (at is { } id)
            {
                path.Add(nodes[id]);
                at = layout.DisplayParents.GetValueOrDefault(id);
            }
            path.Reverse();
            var transaction = _historyUndo.Begin(source, BranchFor(source));
            var staged = new List<(GenerationEntry Entry, Bitmap Image)>();
            try
            {
                GenerationEntry parent = targetLeaf;
                using var targetImage = ImageLibrary.ReadBitmap(Host._library.ResultPath(source, targetLeaf));
                Bitmap basis = targetImage;
                foreach (var original in path)
                {
                    if (!HistoryRecipes.TryGetLocal(source, original, out var local, out string reason))
                        throw new InvalidOperationException($"{original.Label} ({original.OperationId ?? original.Operation}): {reason}");
                    Bitmap result;
                    try { result = new WorkflowExecutor().Execute(basis, ExecutableRecipe(local!, original.CellSize)); }
                    catch (Exception ex) { throw new InvalidOperationException($"{original.Label} ({original.OperationId ?? original.Operation}): {ex.Message}", ex); }
                    var copy = JsonSerializer.Deserialize<GenerationEntry>(JsonSerializer.Serialize(original))!;
                    copy.Id = Guid.NewGuid(); copy.CreatedAt = DateTimeOffset.Now;
                    copy.ParentGenerationId = parent.Id; copy.AppliedRecipe = local;
                    copy.RecipeSnapshot = parent.RecipeSnapshot is null ? null :
                        new ProcessingRecipe { Steps = parent.RecipeSnapshot.Steps.Concat(local!.Steps).ToArray() };
                    if (copy.RecipeSnapshot is null) copy.RecipeUnavailableReason = Localization.Text("FullRecipeThisChainUnavailable");
                    staged.Add((copy, result));
                    parent = copy; basis = result;
                }
                foreach (var pair in staged)
                {
                    Host._library.SaveGeneration(source, pair.Image, null, pair.Entry);
                    source.Generations.Add(pair.Entry);
                }
                if (staged.Count > 0 && source == _editor._activeSource) _editor.SelectGeneration(staged[^1].Entry);
                transaction.Commit();
                Host.Notify(Localization.Format("BranchesMergedFormat", staged.Count), NotificationKind.Success);
            }
            catch
            {
                RestoreHistorySource(transaction.Rollback());
                throw;
            }
            finally { foreach (var pair in staged) pair.Image.Dispose(); }
        }
        catch (Exception ex) { ShowHistoryFailure(Localization.Text("MergeBranches"), ex); }
    }

    private void RestoreHistorySource(HistorySnapshot snapshot)
    {
        var source = Host._sources.FirstOrDefault(s => s.Id == snapshot.SourceId);
        if (source is null) return;
        Host._library.Resources.Clear();
        if (Host._workspaceReady && source != _editor._activeSource && Host._workspaceCatalog?.State is { } state &&
            !Host.IsAssetInScope(source.Id, state.View, state.ProjectId))
        {
            if (snapshot.IncludesSourceImage && Host._library.LoadSource(source.Id, restoreHistory: false) is { } header)
                source.UpdateImageDetails(header.Width, header.Height, header.AlignedCellSize);
            source.UnloadHistory();
            source.RestoreSelection(snapshot.SelectedGenerationId);
            Host.SynchronizeWorkspace(source);
            return;
        }
        var loaded = Host._library.LoadSource(snapshot.SourceId)
            ?? throw new InvalidDataException(Localization.Text("RestoreHistorySourceFailed"));
        if (_loadedBranchSource == snapshot.SourceId) _loadedBranch = snapshot.Branch;
        bool syncing = Host._workspaceSyncing;
        if (snapshot.IncludesSourceImage) Host._workspaceSyncing = true;
        _restoringHistory = true;
        try
        {
            if (snapshot.IncludesSourceImage)
                source.UpdateImageDetails(loaded.Width, loaded.Height, loaded.AlignedCellSize);
            source.SelectedGeneration = null;
            source.Generations.Clear();
            foreach (var generation in loaded.Generations) source.Generations.Add(generation);
            source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == snapshot.SelectedGenerationId);
            if (source == _editor._activeSource) { _editor.ClearDisplayedResult(); RefreshHistoryLayout(); }
        }
        finally { _restoringHistory = false; Host._workspaceSyncing = syncing; }
        if (source == _editor._activeSource && snapshot.IncludesSourceImage)
        {
            _editor.OpenSourceImage(source);
            if (source.SelectedGeneration is not null) _editor.ProcessingBasis = 1;
        }
        else if (source == _editor._activeSource)
        {
            if (source.SelectedGeneration is { } selected) _editor.SelectGeneration(selected);
            else { _editor.ProcessingBasis = 0; _editor.SyncProcessingSelection(); _editor.Preview.UpdateSourcePreview(); _editor.Tools.UpdateSizeHint(); }
        }
        else _editor.SelectSource(source);
        if (snapshot.IncludesSourceImage) Host.SynchronizeWorkspace(source);
        SaveTrayState();
    }

    private void ShowHistoryFailure(string action, Exception ex)
    {
        string message = $"{action}: {ex.Message}";
        Host.Notify(message, NotificationKind.Error);
        TesseraDialogs.Show(Host, message, Localization.Text("ResultHistory"), MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
