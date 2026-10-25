using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PixelArtDownscale;

namespace PixelArtWorkflows;

public static class ProcessingOperations
{
    public const string Profile = "image.profile";
    public const string Resize = "image.resize";
    public const string Trim = "image.trim";
    public const string Colors = "image.colors";
    public const string Cleanup = "image.cleanup";
    public const string Align = "grid.align";
    public const string AlignLocal = "grid.align.local";
    public const string AlignLocalSnapped = "grid.align.local.snapped";
    public const string Reduce = "grid.reduce";
    public const string Monochrome = "filter.monochrome";
    public const string Ascii = "filter.ascii";
    public const string ReplaceColor = "color.replace";
    public const string RemoveBackground = "background.remove";
    public const string Macro = "macro.apply";
}

/// <summary>Semantic parameters only. Thread count, paths and editor appearance are not part of a recipe.</summary>
public sealed class ProcessingStep
{
    public string OperationId { get; init; } = "";
    public DownscaleOptions? Downscale { get; init; }
    public int? CellSize { get; init; }
    public bool UsePreviousAlignment { get; init; }
    public AsciiSettings? Ascii { get; init; }
    public ColorReplacementSettings? ColorReplacement { get; init; }
    public BackgroundSettings? Background { get; init; }
}

public sealed class AsciiSettings
{
    public string Characters { get; init; } = ImageFilters.DefaultAsciiRamp;
    public bool KeepSourceSize { get; init; } = true;
    public bool InvertSource { get; init; }
    public bool InvertColors { get; init; }
}

public sealed class ColorReplacementSettings
{
    public int FromArgb { get; init; }
    public int ToArgb { get; init; }
}

public sealed class BackgroundSettings
{
    public BackgroundRemovalMode Mode { get; init; }
    public int Tolerance { get; init; } = 8;
    /// <summary>Null requests a new automatic estimate on each input.</summary>
    public int? ColorArgb { get; init; }
}

public sealed class ProcessingRecipe
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<ProcessingStep> Steps { get; init; } = [];
}

public sealed class MacroDefinition
{
    public int Version { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public IReadOnlyList<ProcessingStep> Steps { get; init; } = [];
}

public static class WorkflowJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(DownscaleOptions))
            {
                var threads = info.Properties.FirstOrDefault(p => p.Name.Equals("threadCount", StringComparison.OrdinalIgnoreCase));
                if (threads is not null) info.Properties.Remove(threads);
            }
        });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static T Snapshot<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
