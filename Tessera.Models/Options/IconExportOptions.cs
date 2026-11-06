namespace Tessera.Models;

public sealed class IconExportOptions
{
    public IReadOnlyList<int> Sizes { get; init; } = new[] { 16, 24, 32, 48, 64, 128, 256 };
    public EIconResizeMode ResizeMode { get; init; } = EIconResizeMode.Smooth;
    public bool RemoveBackground { get; init; }
    public EBackgroundRemovalMode BackgroundRemovalMode { get; init; } = EBackgroundRemovalMode.GlobalColor;
    public int BackgroundTolerance { get; init; } = 8;
    public System.Drawing.Color? BackgroundColor { get; init; }
    public EIconFitMode FitMode { get; init; } = EIconFitMode.Contain;
}
