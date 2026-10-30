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
    public const string Sharpen = "filter.sharpen";
    public const string Ascii = "filter.ascii";
    public const string ReplaceColor = "color.replace";
    public const string AdjustChannel = "color.channel.adjust";
    public const string RemoveBackground = "background.remove";
    public const string ContourHeatmap = "contours.heatmap";
    public const string CleanContours = "contours.clean";
    public const string ConnectedPalette = "connected-palette";
    public const string FinalistReduce = "image.finalist.reduce";
    public const string Macro = "macro.apply";
}

/// <summary>Semantic parameters only. Thread count, paths and editor appearance are not part of a recipe.</summary>
public sealed class ProcessingStep
{
    public string OperationId { get; init; } = "";
    public DownscaleOptions? Downscale { get; init; }
    public ResizeFactorSettings? ResizeFactor { get; init; }
    public int? CellSize { get; init; }
    public bool UsePreviousAlignment { get; init; }
    public AsciiSettings? Ascii { get; init; }
    public SharpenSettings? Sharpen { get; init; }
    public ColorReplacementSettings? ColorReplacement { get; init; }
    public ChannelAdjustmentOptions? ChannelAdjustment { get; init; }
    public BackgroundSettings? Background { get; init; }
    public ContourCleanOptions? Contours { get; init; }
    public ConnectivityPaletteOptions? ConnectedPalette { get; init; }
    public FinalistMode? FinalistMode { get; init; }
    public int? FinalistFactor { get; init; }
}

public sealed class ResizeFactorSettings
{
    public double Factor { get; init; }
    public bool SpriteMode { get; init; }
    public int AlphaThreshold { get; init; } = 50;
    public CropHorizontalAlignment CropHorizontal { get; init; }
    public CropVerticalAlignment CropVertical { get; init; }
    public BlockSelectionMode BlockMode { get; init; }
    public ManualBlockCriteria? ManualCriteria { get; init; }

    public static ResizeFactorSettings From(DownscaleOptions options, double factor) => new()
    {
        Factor = factor, SpriteMode = options.SpriteMode, AlphaThreshold = options.AlphaThreshold,
        CropHorizontal = options.CropHorizontal, CropVertical = options.CropVertical,
        BlockMode = options.BlockMode, ManualCriteria = options.ManualCriteria
    };

    public DownscaleOptions Resolve(int sourceWidth, int sourceHeight)
    {
        if (!double.IsFinite(Factor) || Factor <= 0) throw new InvalidDataException("Scale factor must be positive and finite.");
        double width = Math.Round(sourceWidth * Factor, MidpointRounding.AwayFromZero);
        double height = Math.Round(sourceHeight * Factor, MidpointRounding.AwayFromZero);
        if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException("Scaled dimensions exceed the supported range.");
        return ToOptions(Math.Max(1, (int)width), Math.Max(1, (int)height));
    }

    public DownscaleOptions ToOptions(int targetWidth, int targetHeight)
    {
        if (targetWidth < 1 || targetHeight < 1) throw new ArgumentOutOfRangeException(nameof(targetWidth));
        return new DownscaleOptions
        {
            TargetWidth = targetWidth, TargetHeight = targetHeight,
            SpriteMode = SpriteMode, AlphaThreshold = AlphaThreshold,
            CropHorizontal = CropHorizontal, CropVertical = CropVertical,
            BlockMode = BlockMode, ManualCriteria = ManualCriteria
        };
    }
}

public sealed class AsciiSettings
{
    public string Characters { get; init; } = ImageFilters.DefaultAsciiRamp;
    public bool KeepSourceSize { get; init; } = true;
    public bool InvertSource { get; init; }
    public bool InvertColors { get; init; }
    public AsciiColorMode ColorMode { get; init; }
    public int ForegroundArgb { get; init; } = -1;
    public int BackgroundArgb { get; init; } = unchecked((int)0xFF000000);
    public int Columns { get; init; }
    public bool Dither { get; init; }
}

public sealed class SharpenSettings
{
    public int Strength { get; init; } = 100;
    public int Threshold { get; init; } = 4;
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
