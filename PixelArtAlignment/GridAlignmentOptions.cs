namespace PixelArtAlignment;

public enum GridAlignmentMode
{
    Global,
    Local
}

public sealed class GridAlignmentOptions
{
    /// <summary>Square output cell in source pixels; null estimates it from transitions.</summary>
    public int? CellSize { get; init; }
    public GridAlignmentMode Mode { get; init; } = GridAlignmentMode.Global;
    /// <summary>Move local region grids onto the canvas-origin grid; local mode only.</summary>
    public bool SnapToGlobalGrid { get; init; }
    public bool Adaptive { get; init; } = true;
    public int MaxDetectedCellSize { get; init; } = 32;
}

public sealed class GridAlignmentResult : IDisposable
{
    /// <summary>Aligned full-size canvas. Edge cells can be clipped to preserve the canvas.</summary>
    public required Bitmap Aligned { get; init; }
    public required int CellSize { get; init; }
    public GridAlignmentMode Mode { get; init; }
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
