using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using PixelArtWorkflows;
using PixelArtDownscale;
using Bitmap = System.Drawing.Bitmap;

namespace DomainColorTest;

public partial class MainWindow
{
    private readonly HistoryUndoStore _historyUndo;

    private void HistoryUndoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Z || Keyboard.Modifiers != ModifierKeys.Control || _processing) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        e.Handled = true;
        try
        {
            var snapshot = _historyUndo.Undo();
            if (snapshot is null) return;
            RestoreHistorySource(snapshot);
            statusLabel.Text = Localization.Text("Последнее изменение истории отменено.");
        }
        catch (Exception ex) { ShowHistoryFailure(Localization.Text("Отмена изменения"), ex); }
    }

    private bool CommitGeneration(SourceEntry source, GenerationEntry generation, bool select = true)
    {
        bool insert = insertHistoryInput.IsChecked == true && source == _activeSource &&
            ProcessingBasis == 1 && source.SelectedGeneration?.Id == generation.ParentGenerationId;
        HistoryUndoStore.HistoryUndoTransaction? transaction = null;
        try
        {
            transaction = _historyUndo.Begin(source, BranchFor(source), generation.Id);
            if (insert) RecomputeAfterInsertion(source, source.SelectedGeneration!, generation);
            source.Generations.Add(generation);
            if (select && source == _activeSource) SelectGeneration(generation);
            else source.SelectedGeneration = generation;
            transaction.Commit();
            if (insertHistoryInput.IsChecked == true) insertHistoryInput.IsChecked = false;
            return true;
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                try { RestoreHistorySource(transaction.Rollback()); }
                catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            }
            else _library.DeleteUnpublishedGeneration(source, generation);
            ShowHistoryFailure(insert ? Localization.Text("Вставка и пересчёт цепочки") : Localization.Text("Сохранение результата"), ex);
            return false;
        }
    }

    private BranchPresentation BranchFor(SourceEntry source)
    {
        if (!_trayState.Branches.TryGetValue(source.Id, out var branch))
            _trayState.Branches[source.Id] = branch = new();
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
                if (!visited.Add(old.Id)) throw new InvalidDataException(Localization.Text("Циклическая связь между результатами."));
                if (!HistoryRecipes.TryGetLocal(source, old, out var local, out string reason))
                    throw new InvalidOperationException($"{old.Label} ({old.OperationId ?? old.Operation}): {reason}");
                var parent = old.ParentGenerationId == anchor.Id ? inserted : source.Generations.First(g => g.Id == old.ParentGenerationId);
                using var basis = staged.TryGetValue(parent.Id, out var ready)
                    ? (Bitmap)ready.Image.Clone() : ImageLibrary.ReadBitmap(_library.ResultPath(source, parent));
                var actualParent = staged.TryGetValue(parent.Id, out var stagedBasis) ? stagedBasis.Record : parent;
                ProcessingRecipe executable = ExecutableRecipe(local!, actualParent.IsGlobalAlignment ? actualParent.CellSize : old.CellSize);
                Bitmap result; Bitmap? crop = null;
                int? resolvedCell = null;
                try
                {
                    if (old.HasCrop && executable.Steps.Count == 1 && executable.Steps[0].OperationId == ProcessingOperations.Profile)
                    {
                        var processed = new PixelArtDownscaler().Process(basis, executable.Steps[0].Downscale!);
                        result = processed.Downscaled; crop = processed.CroppedSource;
                    }
                    else result = new WorkflowExecutor().Execute(basis, executable,
                        stepCompleted: (step, _) => resolvedCell = step.CellSize);
                }
                catch (Exception ex) { throw new InvalidOperationException($"{old.Label} ({old.OperationId ?? old.Operation}): {ex.Message}", ex); }
                var record = JsonSerializer.Deserialize<GenerationEntry>(JsonSerializer.Serialize(old))!;
                record.Settings = RecomputedSettings(actualParent.Settings, local!, resolvedCell);
                if (resolvedCell is { } cell) record.Settings.CellSize = record.CellSize = cell;
                if (old.ParentGenerationId == anchor.Id) record.ParentGenerationId = inserted.Id;
                var parentSnapshot = parent.Id == inserted.Id ? inserted.RecipeSnapshot :
                    staged.TryGetValue(parent.Id, out var stagedParent) ? stagedParent.Record.RecipeSnapshot : parent.RecipeSnapshot;
                record.RecipeSnapshot = parentSnapshot is null ? null :
                    new ProcessingRecipe { Steps = parentSnapshot.Steps.Concat(local!.Steps).ToArray() };
                record.RecipeUnavailableReason = record.RecipeSnapshot is null ? Localization.Text("После ручной вставки полный рецепт этой цепочки недоступен.") : null;
                staged.Add(old.Id, (record, result, crop));
                foreach (var child in children.GetValueOrDefault(old.Id) ?? []) pending.Enqueue(child);
            }
            foreach (var old in source.Generations.Where(g => staged.ContainsKey(g.Id)))
            {
                var pair = staged[old.Id];
                try { _library.ReplaceGeneration(source, pair.Record, pair.Image, pair.Crop); }
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

    private static EditorSettings RecomputedSettings(EditorSettings parent, ProcessingRecipe recipe, int? cellSize)
    {
        var settings = WorkflowJson.Snapshot(parent);
        foreach (var step in recipe.Steps)
        {
            int width = settings.Downscale.TargetWidth, height = settings.Downscale.TargetHeight;
            settings = MacroStepSettings(settings, step, cellSize);
            if (step.OperationId is not (ProcessingOperations.Resize or ProcessingOperations.Profile))
            {
                var options = JsonSerializer.SerializeToNode(settings.Downscale)!;
                options[nameof(DownscaleOptions.TargetWidth)] = width;
                options[nameof(DownscaleOptions.TargetHeight)] = height;
                settings.Downscale = options.Deserialize<DownscaleOptions>()!;
            }
        }
        return settings;
    }

    private static void CopyRecomputedEntry(GenerationEntry target, GenerationEntry record)
    {
        target.ParentGenerationId = record.ParentGenerationId;
        target.Width = record.Width; target.Height = record.Height;
        target.CellSize = record.CellSize; target.Settings = record.Settings; target.HasCrop = record.HasCrop;
        target.RecipeSnapshot = record.RecipeSnapshot;
        target.RecipeUnavailableReason = record.RecipeUnavailableReason;
        target.Thumbnail = record.Thumbnail;
    }

    private void RemoveGenerationWithUndo(GenerationEntry generation, bool removeTail)
    {
        var source = _sources.FirstOrDefault(s => s.Generations.Contains(generation));
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
            try { RestoreHistorySource(transaction.Rollback()); }
            catch (Exception rollback) { ex = new AggregateException(ex, rollback); }
            ShowHistoryFailure(Localization.Text("Удаление результатов"), ex);
        }
    }

    private void MergeHistoryBranches(GenerationEntry sourceLeaf, GenerationEntry targetLeaf)
    {
        var source = _sources.FirstOrDefault(s => s.Generations.Contains(sourceLeaf) && s.Generations.Contains(targetLeaf));
        if (source is null) return;
        var layout = HistoryBranchLayout.Build(source.Generations.ToArray(), BranchFor(source));
        try
        {
            if (layout.RootOf(sourceLeaf.Id) == layout.RootOf(targetLeaf.Id))
                throw new InvalidOperationException(Localization.Text("Выберите хвост другой ветки."));
            if (source.Generations.Any(g => layout.DisplayParents[g.Id] == sourceLeaf.Id))
                throw new InvalidOperationException(Localization.Text("Перетаскиваемая ветка разветвляется дальше. Выберите конечную ноду нужного пути."));
            if (source.Generations.Any(g => layout.DisplayParents[g.Id] == targetLeaf.Id))
                throw new InvalidOperationException(Localization.Text("Для объединения выберите конечную ноду целевой ветки."));
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
                using var targetImage = ImageLibrary.ReadBitmap(_library.ResultPath(source, targetLeaf));
                Bitmap basis = targetImage;
                foreach (var original in path)
                {
                    if (!HistoryRecipes.TryGetLocal(source, original, out var local, out string reason))
                        throw new InvalidOperationException($"{original.Label} ({original.OperationId ?? original.Operation}): {reason}");
                    Bitmap result;
                    int? resolvedCell = null;
                    try { result = new WorkflowExecutor().Execute(basis, ExecutableRecipe(local!, parent.IsGlobalAlignment ? parent.CellSize : original.CellSize),
                        stepCompleted: (step, _) => resolvedCell = step.CellSize); }
                    catch (Exception ex) { throw new InvalidOperationException($"{original.Label} ({original.OperationId ?? original.Operation}): {ex.Message}", ex); }
                    var copy = JsonSerializer.Deserialize<GenerationEntry>(JsonSerializer.Serialize(original))!;
                    copy.Settings = RecomputedSettings(parent.Settings, local!, resolvedCell);
                    copy.Id = Guid.NewGuid(); copy.CreatedAt = DateTimeOffset.Now;
                    if (resolvedCell is { } cell) copy.Settings.CellSize = copy.CellSize = cell;
                    copy.ParentGenerationId = parent.Id; copy.AppliedRecipe = local;
                    copy.RecipeSnapshot = parent.RecipeSnapshot is null ? null :
                        new ProcessingRecipe { Steps = parent.RecipeSnapshot.Steps.Concat(local!.Steps).ToArray() };
                    copy.RecipeUnavailableReason = copy.RecipeSnapshot is null ? Localization.Text("Полный рецепт цепочки недоступен.") : null;
                    staged.Add((copy, result));
                    parent = copy; basis = result;
                }
                foreach (var pair in staged)
                {
                    _library.SaveGeneration(source, pair.Image, null, pair.Entry);
                    source.Generations.Add(pair.Entry);
                }
                if (staged.Count > 0 && source == _activeSource) SelectGeneration(staged[^1].Entry);
                transaction.Commit();
                statusLabel.Text = Localization.Format("Ветки объединены. Добавлено результатов: {0}.", staged.Count);
            }
            catch
            {
                RestoreHistorySource(transaction.Rollback());
                throw;
            }
            finally { foreach (var pair in staged) pair.Image.Dispose(); }
        }
        catch (Exception ex) { ShowHistoryFailure(Localization.Text("Объединение веток"), ex); }
    }

    private void RestoreHistorySource(HistorySnapshot snapshot)
    {
        var source = _sources.FirstOrDefault(s => s.Id == snapshot.SourceId);
        if (source is null) return;
        var loaded = _library.Load().FirstOrDefault(s => s.Id == snapshot.SourceId)
            ?? throw new InvalidDataException(Localization.Text("Не удалось восстановить исходник истории."));
        _trayState.Branches[snapshot.SourceId] = snapshot.Branch;
        _restoringHistory = true;
        try
        {
            source.Generations.Clear();
            foreach (var generation in loaded.Generations) source.Generations.Add(generation);
            source.SelectedGeneration = source.Generations.FirstOrDefault(g => g.Id == snapshot.SelectedGenerationId);
            if (source == _activeSource) { ClearDisplayedResult(); RefreshHistoryLayout(); }
        }
        finally { _restoringHistory = false; }
        if (source == _activeSource)
        {
            if (source.SelectedGeneration is { } selected) SelectGeneration(selected);
            else { ProcessingBasis = 0; SyncProcessingSelection(); UpdateSourcePreview(); UpdateSizeHint(); }
        }
        else SelectSource(source);
        SaveTrayState();
    }

    private void ShowHistoryFailure(string action, Exception ex)
    {
        string message = $"{action}: {ex.Message}";
        statusLabel.Text = message;
        TesseraDialog.Show(this, message, Localization.Text("История результатов"), MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
