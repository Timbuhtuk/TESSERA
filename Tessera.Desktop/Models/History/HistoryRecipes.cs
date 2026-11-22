using System.IO;
using System.Text.Json;
using Tessera.Core.Workflows;

namespace Tessera.Desktop;

/// <summary>A recipe belongs to a result, not to the continued existence of its ancestors.</summary>
internal static class HistoryRecipes
{
    public static ProcessingRecipe Single(ProcessingStep step) => new() { Steps = [step] };

    public static bool TryGetLocal(SourceEntry source, GenerationEntry entry, out ProcessingRecipe? recipe, out string reason)
    {
        recipe = entry.AppliedRecipe;
        reason = "";
        if (recipe is null && entry.RecipeSnapshot is { } snapshot)
        {
            ProcessingRecipe? parent = null;
            if (entry.ParentGenerationId is { } parentId)
                parent = source.Generations.FirstOrDefault(g => g.Id == parentId)?.RecipeSnapshot;
            int prefix = parent?.Steps.Count ?? 0;
            bool samePrefix = parent is null || snapshot.Steps.Take(prefix).Select(step => JsonSerializer.Serialize(step, WorkflowJson.Options))
                .SequenceEqual(parent.Steps.Select(step => JsonSerializer.Serialize(step, WorkflowJson.Options)));
            if (snapshot.Steps.Count > prefix && samePrefix)
                recipe = new ProcessingRecipe { Steps = snapshot.Steps.Skip(prefix).ToArray() };
        }
        if (recipe is null)
        {
            reason = entry.RecipeUnavailableReason is { } savedReason ? Localization.StoredText(savedReason)
                : Localization.Format("HistoryResultMissingOperationSettings", entry.Label);
            return false;
        }
        try
        {
            if (recipe.Steps.Any(step => step.UsePreviousAlignment))
            {
                bool aligned = false;
                var steps = new List<ProcessingStep>();
                foreach (var step in recipe.Steps)
                {
                    steps.Add(step.UsePreviousAlignment && !aligned
                        ? new ProcessingStep { OperationId = step.OperationId, CellSize = Math.Max(1, entry.CellSize) } : step);
                    if (step.OperationId is ProcessingOperations.Align or ProcessingOperations.AlignLocalSnapped) aligned = true;
                    else if (step.OperationId == ProcessingOperations.AlignLocal) aligned = false;
                }
                var normalized = new ProcessingRecipe { Steps = steps };
                WorkflowExecutor.Validate(normalized);
            }
            else WorkflowExecutor.Validate(recipe);
            return true;
        }
        catch (Exception ex) { reason = ex.Message; recipe = null; return false; }
    }

    public static void Capture(SourceEntry source, GenerationEntry entry)
    {
        if (entry.RecipeSnapshot is not null) return;
        if (TryBuild(source, entry, out var recipe, out string reason))
            entry.RecipeSnapshot = WorkflowJson.Snapshot(recipe!);
        else entry.RecipeUnavailableReason = reason;
        if (entry.AppliedRecipe is not null) entry.AppliedRecipe = WorkflowJson.Snapshot(entry.AppliedRecipe);
    }

    public static bool TryBuild(SourceEntry source, GenerationEntry entry, out ProcessingRecipe? recipe, out string reason)
    {
        recipe = null;
        reason = "";
        var chain = new List<ProcessingRecipe>();
        var visited = new HashSet<Guid>();
        var index = source.Generations.ToDictionary(g => g.Id);
        var current = entry;
        while (true)
        {
            if (!visited.Add(current.Id)) { reason = Localization.Text("ACycleWasFoundInProcessing"); return false; }
            if (current.RecipeSnapshot is { } saved)
            {
                if (saved.Version != 1) { reason = Localization.Text("UnknownSavedRecipeVersion"); return false; }
                chain.Add(saved);
                break;
            }
            if (!string.IsNullOrEmpty(current.RecipeUnavailableReason)) { reason = Localization.StoredText(current.RecipeUnavailableReason); return false; }
            var operation = current.AppliedRecipe;
            if (operation is null)
            {
                reason = Localization.Format("ResultThisOperationHasNoSavedFormat", current.Label);
                return false;
            }
            chain.Add(operation);
            if (current.ParentGenerationId is not { } parent) break;
            if (!index.TryGetValue(parent, out var found))
            {
                reason = Localization.Format("TheParentOfResultIsMissingFormat", current.Label);
                return false;
            }
            current = found;
        }
        chain.Reverse();
        recipe = new ProcessingRecipe { Steps = chain.SelectMany(c => c.Steps).ToArray() };
        try { WorkflowExecutor.Validate(recipe); return true; }
        catch (Exception ex) when (ex is WorkflowStepException or InvalidDataException or ArgumentException)
        { reason = ex.Message; recipe = null; return false; }
    }
}
