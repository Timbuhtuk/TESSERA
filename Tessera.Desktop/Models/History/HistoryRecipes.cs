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
        // TESSERA_LEGACY Recover the local step for a history record written before AppliedRecipe.
        recipe = entry.AppliedRecipe ?? RestoreLegacy(entry);
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
            // TESSERA_LEGACY Recover an old chain whose generation records predate saved recipes.
            var operation = current.AppliedRecipe ?? RestoreLegacy(current);
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

    // TESSERA_LEGACY Rebuild recipes from exact operation labels, flags and persisted settings in pre-recipe history.
    // Logs and captions are presentation, never an executable format.
    private static ProcessingRecipe? RestoreLegacy(GenerationEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.OperationId)) return null;
        string? kind = LegacyOperationId(entry);
        if (kind is null) return null;
        // An old auto-align result records DetectGrid. Its reducer records only the resolved size;
        // interpreting that size literally is the only unambiguous legacy behavior.
        return Single(kind switch
        {
            ProcessingOperations.Align => new ProcessingStep { OperationId = kind, CellSize = entry.Settings.DetectGrid ? null : entry.CellSize },
            ProcessingOperations.Reduce => new ProcessingStep { OperationId = kind, CellSize = entry.CellSize },
            ProcessingOperations.Monochrome => new ProcessingStep { OperationId = kind },
            _ => new ProcessingStep { OperationId = kind, Downscale = entry.Settings.Downscale }
        });
    }

    // TESSERA_LEGACY Map historical RU/EN operation labels and the alignment/reduction flags to current IDs.
    internal static string? LegacyOperationId(GenerationEntry entry) =>
        entry.IsAlignment ? ProcessingOperations.Align : entry.IsGridReduction ? ProcessingOperations.Reduce : entry.Operation switch
        {
            "" => ProcessingOperations.Profile,
            "Размер" or "Увеличение" or "Size" or "Upscale" => ProcessingOperations.Resize,
            "Очистка цветов" or "Cleanup Colors" => ProcessingOperations.Cleanup,
            "Квантование" or "Палитра" or "Цвет" or "Quantization" or "Palette" or "Color" => ProcessingOperations.Colors,
            "Монохром" or "Monochrome" => ProcessingOperations.Monochrome,
            _ => null
        };

    // TESSERA_LEGACY Use raw pre-recipe JSON field presence to reject incomplete replay instead of accepting deserialization defaults.
    public static void ValidateLegacyRecord(GenerationEntry entry, string path)
    {
        if (entry.RecipeSnapshot is not null || entry.AppliedRecipe is not null || !string.IsNullOrEmpty(entry.RecipeUnavailableReason)) return;
        var restored = RestoreLegacy(entry);
        if (restored is null) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement Find(JsonElement value, string name)
            => value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
        bool Has(JsonElement value, string name) => Find(value, name).ValueKind != JsonValueKind.Undefined;
        string kind = restored.Steps[0].OperationId;
        var settings = Find(document.RootElement, "Settings"); var downscale = Find(settings, "Downscale");
        var required = new List<string>();
        if (kind is ProcessingOperations.Profile or ProcessingOperations.Resize)
            required.AddRange(["TargetWidth", "TargetHeight", "SpriteMode", "AlphaThreshold", "CropHorizontal", "CropVertical", "BlockMode", "ManualCriteria"]);
        if (kind is ProcessingOperations.Profile or ProcessingOperations.Colors)
            required.AddRange(["Palette", "PaletteStep", "EnableDithering", "QuantizationColors", "Quantization", "UseColorWeights"]);
        if (kind == ProcessingOperations.Colors) required.Add("IndependentColorMode");
        if (kind == ProcessingOperations.Cleanup) required.AddRange(["Cleanup", "LocalColorPasses"]);
        string? missing = required.FirstOrDefault(name => !Has(downscale, name));
        if (missing is null && kind == ProcessingOperations.Cleanup)
        {
            var cleanup = Find(downscale, "Cleanup");
            missing = new[] { "ColorTolerance", "Strength", "DetailProtection", "EdgeProtection" }.FirstOrDefault(name => !Has(cleanup, name));
        }
        if (kind == ProcessingOperations.Align && !Has(settings, "DetectGrid")) missing = "DetectGrid";
        if (kind == ProcessingOperations.Reduce && !Has(document.RootElement, "CellSize")) missing = "CellSize";
        if (missing is not null) entry.RecipeUnavailableReason = Localization.Format("LegacyResultHasNoSavedValueFormat", missing);
    }
}
