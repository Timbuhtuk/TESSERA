namespace Tessera.Models;

public sealed class GridAlignmentResult : IDisposable
{
    /// <summary>Aligned full-size canvas. Edge cells can be clipped to preserve the canvas.</summary>
    public required Bitmap Aligned { get; init; }
    public required int CellSize { get; init; }
    public EGridAlignmentMode Mode { get; init; }
    public int LocalRegionCount { get; init; }
    public bool SnapToGlobalGrid { get; init; }
    public int ShiftedRegionCount { get; init; }
    public int ConflictingCellCount { get; init; }
    /// <summary>Detector evidence, not a reconstruction accuracy percentage.</summary>
    public required double DetectionConfidence { get; init; }
    public required bool WasDetected { get; init; }
    public required double ElapsedSeconds { get; init; }
    public void Dispose() => Aligned.Dispose();
}
