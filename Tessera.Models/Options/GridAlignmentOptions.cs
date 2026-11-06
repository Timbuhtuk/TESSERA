namespace Tessera.Models;

public sealed class GridAlignmentOptions
{
    /// <summary>Square output cell in source pixels; null estimates it from transitions.</summary>
    public int? CellSize { get; init; }
    public EGridAlignmentMode Mode { get; init; } = EGridAlignmentMode.Global;
    /// <summary>Move local region grids onto the canvas-origin grid; local mode only.</summary>
    public bool SnapToGlobalGrid { get; init; }
    public bool Adaptive { get; init; } = true;
    public int MaxDetectedCellSize { get; init; } = 32;
}
