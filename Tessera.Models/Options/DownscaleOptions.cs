namespace Tessera.Models;

public sealed class ManualBlockCriteria
{
    public double TargetBrightness { get; init; } = 0.5;
    public double TargetContrast { get; init; } = 0.5;
    public double TargetSaturation { get; init; } = 0.5;
    public double TargetEdge { get; init; } = 0.5;

    public double BrightnessImportance { get; init; } = 1.0;
    public double ContrastImportance { get; init; } = 1.0;
    public double SaturationImportance { get; init; } = 1.0;
    public double EdgeImportance { get; init; } = 1.0;
}

public sealed class DownscaleOptions
{
    public const int MaxQuantizationColors = 4096;
    public const int MaxLocalColorPasses = 100;

    public bool SpriteMode { get; init; }
    public int AlphaThreshold { get; init; } = 50;
    public int TargetWidth { get; init; } = 48;
    public int TargetHeight { get; init; } = 48;
    public ECropHorizontalAlignment CropHorizontal { get; init; } = ECropHorizontalAlignment.Center;
    public ECropVerticalAlignment CropVertical { get; init; } = ECropVerticalAlignment.Center;
    public EPaletteKind Palette { get; init; } = EPaletteKind.DB16;
    public int PaletteStep { get; init; } = 32;
    public bool EnableDithering { get; init; }
    public int QuantizationColors { get; init; } = 64;
    public EIndependentColorMode IndependentColorMode { get; init; } = EIndependentColorMode.Combined;
    public int LocalColorPasses { get; init; } = 1;
    public ManualBlockCriteria? LocalColorCriteria { get; init; }
    public ColorCleanupOptions? Cleanup { get; init; }
    public bool UseColorWeights { get; init; }
    public EQuantizationMethod Quantization { get; init; } = EQuantizationMethod.KMeansLab;
    public int ThreadCount { get; init; } = Environment.ProcessorCount;
    public EBlockSelectionMode BlockMode { get; init; } = EBlockSelectionMode.Automatic;
    public ManualBlockCriteria? ManualCriteria { get; init; }
}
