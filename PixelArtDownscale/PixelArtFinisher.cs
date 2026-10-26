using System.Drawing.Imaging;
using PixelArtAlignment;

namespace PixelArtDownscale;

public sealed class PixelArtFinishOptions
{
    public int CellSize { get; init; } = 8;
    public int SmoothPasses { get; init; } = 1;
    public int ColorCount { get; init; } = 128;
    public bool AdaptiveGrid { get; init; } = true;
}

/// <summary>Runs local color preparation, one grid reduction, and optional final palette reduction.</summary>
public static class PixelArtFinisher
{
    public static Bitmap Process(Bitmap source, PixelArtFinishOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.CellSize is < 2 or > 65535 || options.CellSize > Math.Min(source.Width, source.Height))
            throw new ArgumentOutOfRangeException(nameof(options.CellSize),
                "Размер ячейки должен быть от 2 до меньшей стороны изображения.");
        if (options.SmoothPasses is < 0 or > DownscaleOptions.MaxLocalColorPasses)
            throw new ArgumentOutOfRangeException(nameof(options.SmoothPasses));
        if (options.ColorCount is < 0 or > DownscaleOptions.MaxQuantizationColors)
            throw new ArgumentOutOfRangeException(nameof(options.ColorCount));

        using Bitmap prepared = options.SmoothPasses == 0 ?
            source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb) :
            IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions
            {
                LocalColorPasses = options.SmoothPasses,
                LocalColorCriteria = new ManualBlockCriteria()
            });

        using GridAlignmentResult aligned = new PixelGridAligner().Align(prepared, new GridAlignmentOptions
        {
            CellSize = options.CellSize,
            Adaptive = options.AdaptiveGrid
        });
        using Bitmap reduced = PixelGridReducer.Reduce(aligned.Aligned, options.CellSize);
        if (options.ColorCount == 0)
            return reduced.Clone(new Rectangle(0, 0, reduced.Width, reduced.Height), PixelFormat.Format32bppArgb);

        return IndependentImageProcessor.ApplyColors(reduced, new DownscaleOptions
        {
            Palette = PaletteKind.None,
            IndependentColorMode = IndependentColorMode.Quantization,
            Quantization = QuantizationMethod.KMeansLab,
            QuantizationColors = options.ColorCount
        });
    }
}
