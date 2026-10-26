using System.Drawing;
using System.Drawing.Imaging;

namespace PixelArtDownscale;

public static class DarkContourNoiseCleaner
{
    public sealed record Options(double DarkLightness, int MaximumDarkNeighbors,
        double BrightColorTolerance, double MinimumLightnessGain, int AlphaClearance = 1,
        double MinimumBrightConsensus = 0.6, int MaximumCardinalDarkNeighbors = 2,
        int ThreeNeighborMinimumDark5 = 9, double ThreeNeighborMinimumComponentRatio = 0.1,
        double CardinalColorWeight = 3, bool UseOppositeBrightPair = true,
        int MinimumPairExtension = 1, double PairColorTolerance = 8,
        int TwoCardinalMinimumDark5 = 11, double TwoCardinalMinimumComponentRatio = 0.4,
        int ThreeNeighborProtectedContinuation = 3, double ThreeNeighborProtectedComponentRatio = 0.2,
        double ThreeNeighborProtectedMinimumComponentRatio = 0.15,
        double RepeatedComponentSimilarity = 0.8, int RepeatedPatchRadius = 3,
        int RepeatedPatchMaximumDifferences = 2, int LargePatchRadius = 3,
        int LargePatchMinimumBrightMatches = 2, double LargePatchBrightRatio = 1,
        int SmallPatchRadius = 1, double SmallPatchMaximumComponentRatio = 0.06,
        int SmallPatchMinimumBrightMatches = 10, double SmallPatchBrightRatio = 3,
        bool AllowDiagonalBridge = true, double DiagonalBridgeMinimumConsensus = 0.9,
        bool UseStraightRun = true, int MinimumStraightRun = 8, int StraightRunSearchRadius = 15,
        int StraightRunMinimumDark5ForTwoNeighbors = 9, double StraightRunMinimumConsensus = 0.5,
        int IsolatedPatchRadius = 2, int IsolatedPatchMinimumBrightMatches = 3,
        double IsolatedPatchBrightRatio = 2, double IsolatedPatchMinimumConsensus = 0.5,
        bool UseRepeatedMismatch = true,
        bool UseDiagonalGradientEvidence = true, double DiagonalColorTolerance = 8,
        int DiagonalMinimumBrightMatches = 7, double DiagonalBrightRatio = 2,
        double DiagonalMinimumGradientRatio = 1.22, int DiagonalGradientRadius = 3,
        double DiagonalSimilarColorTolerance = 24, int DiagonalMinimumSimilarNeighbors = 5,
        bool UseDiagonalOppositionEvidence = true, int DiagonalOppositionRadius = 5,
        double DiagonalMinimumOpposition = 0.8, double DiagonalMinimumAgreement = 0.2,
        int SmallPatchMaximumCardinalDarkNeighbors = 1,
        bool UseTaperedRun = true, int TaperedRunMinimumDark5 = 7,
        int TaperedRunMaximumDark5 = 10, int TaperedRunSearchRadius = 20,
        int TaperedRunMinimumInsideLength = 8, int TaperedRunMinimumShrink = 6,
        int TaperedRunMinimumBrightMatches = 4, double TaperedRunBrightRatio = 1.5,
        bool UseSparsePatchEvidence = true, int SparsePatchMaximumDark5 = 5,
        int SparsePatchMaximumCardinalDarkNeighbors = 1,
        int SparsePatchMinimumBrightMatches = 20, double SparsePatchBrightRatio = 5,
        int CandidateConflictRadius = 2,
        bool RequireCommonGradient = false, int CommonGradientRadius = 5,
        double CommonColorTolerance = 24, int CommonMinimumSimilarNeighbors = 5,
        double CommonMinimumMagnitude = 0, double CommonMinimumMagnitudeRatio = 1.2,
        double CommonMinimumAgreement = 0.2, double CommonMinimumOpposition = 0.8,
        bool UseSecondarySparsePatch = true, int SecondarySparseMaximumDark5 = 8,
        double SecondarySparseMaximumComponentRatio = 0.5,
        bool UseSoftColorConsensus = true, double SoftMinimumConsensus = 0.5,
        double SoftMaximumColorSpread = 40, int SoftMaximumDarkNeighbors = 2,
        int SoftMaximumDark5 = 6,
        bool UseCoherentGradientRescue = true, int CoherentGradientMinimumDark5 = 10,
        int CoherentGradientRadius = 5, double CoherentGradientColorTolerance = 24,
        int CoherentGradientMinimumNeighbors = 10, double CoherentGradientMagnitudeRatio = 1.2,
        double CoherentGradientMinimumAgreement = 0.9,
        double CoherentGradientMinimumAlignment = 0.9,
        bool UseDivergentGradientRescue = true, int DivergentGradientDark5 = 7,
        int DivergentGradientRadius = 5, double DivergentGradientColorTolerance = 24,
        int DivergentGradientMinimumNeighbors = 10, double DivergentGradientMagnitudeRatio = 1.4,
        double DivergentGradientMaximumAgreement = 0.15,
        bool UsePatchGradientRescue = true, int PatchGradientMinimumDark5 = 8,
        int PatchGradientMaximumDark5 = 10, int PatchGradientMinimumBrightMatches = 6,
        double PatchGradientBrightRatio = 3, int PatchGradientRadius = 5,
        double PatchGradientColorTolerance = 24, int PatchGradientMinimumNeighbors = 10,
        double PatchGradientMagnitudeRatio = 1.18, double PatchGradientMaximumMagnitudeRatio = 1.4,
        double PatchGradientMaximumAgreement = 0.5,
        bool UseMainSparsePatch = true, int MainSparseMaximumDark5 = 6,
        int MainSparseMinimumBrightMatches = 8, double MainSparseBrightRatio = 5,
        int MainSparseGradientRadius = 5, double MainSparseColorTolerance = 24,
        int MainSparseMinimumSimilarNeighbors = 10, double MainSparseMaximumMagnitudeRatio = 1,
        bool UseBrightCornerContinuation = true, int BrightCornerMaximumDarkNeighbors = 2,
        int BrightCornerMaximumCardinalNeighbors = 1, int BrightCornerMaximumDark5 = 6,
        double BrightCornerMaximumComponentRatio = 0.5, int BrightCornerMinimumRun = 3,
        double BrightCornerColorTolerance = 8,
        bool UseVerticalShadeContinuation = true, int VerticalMediumDark5 = 10,
        double VerticalMediumMinimumComponentRatio = 0.25,
        double VerticalMediumMaximumComponentRatio = 0.5,
        double VerticalMediumMaximumColorSpread = 30, int VerticalWideDark5 = 11,
        double VerticalWideMinimumComponentRatio = 0.5,
        double VerticalWideMinimumColorSpread = 50, double VerticalWideMaximumColorSpread = 80,
        int VerticalShadeSearchRadius = 4, double VerticalShadeColorTolerance = 8)
    {
        public int TwoCardinalMinimumBrightPatternMatches { get; init; }
        public bool RequireColorMatchedPatch { get; init; }
        public double ColorMatchedPatchTolerance { get; init; } = 15;
        public int ColorMatchedPatchMinimumBrightMatches { get; init; } = 1;
        public double ColorMatchedPatchBrightRatio { get; init; } = 3;
        public int ColorMatchedPatchMaximumPatternDifferences { get; init; }
        public bool UseColorPatchEnergyRescue { get; init; }
        public double ColorPatchEnergyRescueMinimumDrop { get; init; } = 0.8;
        public bool UseThreeNeighborEnergyRescue { get; init; }
        public double ThreeNeighborEnergyRescueMinimumDrop { get; init; } = 0.4;
        public int ThreeNeighborEnergyDistinctColors { get; init; } = 4;
        public double ThreeNeighborEnergyFlatMinimumBrightWeight { get; init; } = 2.1;
        public double ThreeNeighborEnergyTexturedMinimumBrightWeight { get; init; }
        public bool UseTexturedGradientRescue { get; init; }
        public int TexturedGradientMinimumDistinctColors { get; init; } = 4;
        public double TexturedGradientMinimumEnergyDrop { get; init; } = 0.58;
        public double TexturedGradientMinimumBrightWeight { get; init; } = 0.1;
        public int TexturedGradientRadius { get; init; } = 3;
        public double TexturedGradientColorTolerance { get; init; } = 24;
        public int TexturedGradientMinimumSimilarNeighbors { get; init; } = 5;
        public double TexturedGradientMinimumOpposition { get; init; } = 0.2;
        public bool UseSingleDiagonalGradientRescue { get; init; }
        public int SingleDiagonalMinimumDistinctColors { get; init; } = 4;
        public double SingleDiagonalMaximumLightness { get; init; } = 2.5;
        public double SingleDiagonalMinimumEnergyDrop { get; init; } = 0.1;
        public double SingleDiagonalMinimumBrightWeight { get; init; } = 0.01;
        public int SingleDiagonalGradientRadius { get; init; } = 3;
        public double SingleDiagonalColorTolerance { get; init; } = 24;
        public int SingleDiagonalMinimumSimilarNeighbors { get; init; } = 5;
        public double SingleDiagonalMinimumOpposition { get; init; } = 0.2;
        public bool UseThreeNeighborGradientRescue { get; init; }
        public int ThreeNeighborGradientMinimumDistinctColors { get; init; } = 4;
        public double ThreeNeighborGradientMaximumLightness { get; init; } = 2.5;
        public double ThreeNeighborGradientMinimumEnergyDrop { get; init; } = 0.15;
        public double ThreeNeighborGradientMinimumBrightWeight { get; init; } = 0.1;
        public int ThreeNeighborGradientRadius { get; init; } = 3;
        public double ThreeNeighborGradientColorTolerance { get; init; } = 24;
        public int ThreeNeighborGradientMinimumSimilarNeighbors { get; init; } = 5;
        public double ThreeNeighborGradientMinimumOpposition { get; init; } = 0.5;
        public bool UseWeightedColorPatchRescue { get; init; }
        public int WeightedColorPatchMaximumPatternDifferences { get; init; } = 2;
        public double WeightedColorPatchSigma { get; init; } = 5;
        public double WeightedColorPatchMinimumBrightWeight { get; init; } = 1;
        public double WeightedColorPatchBrightRatio { get; init; } = 5;
        public bool UseExtendedWeightedColorPatchRescue { get; init; }
        public double ExtendedWeightedPatchSigma { get; init; } = 6;
        public double ExtendedWeightedPatchMinimumBrightWeight { get; init; } = 1;
        public double ExtendedWeightedPatchBrightRatio { get; init; } = 5;
        public int ExtendedWeightedPatchTexturedDistinctColors { get; init; } = 4;
        public double ExtendedWeightedPatchMaximumLightness { get; init; } = 2.5;
        public int ExtendedWeightedPatchMinimumCardinalDarkNeighbors { get; init; } = 1;
        public double ExtendedWeightedPatchMinimumEnergyDrop { get; init; } = 0.4;
        public int ExtendedWeightedPatchGradientRadius { get; init; } = 3;
        public double ExtendedWeightedPatchColorTolerance { get; init; } = 24;
        public int ExtendedWeightedPatchMinimumSimilarNeighbors { get; init; } = 5;
        public double ExtendedWeightedPatchMinimumOpposition { get; init; } = 0.5;
        public bool UseSymmetricColorPatchRescue { get; init; }
        public int SymmetricPatchMinimumDistinctColors { get; init; } = 4;
        public double SymmetricPatchMaximumLightness { get; init; } = 2.5;
        public int SymmetricPatchMinimumCardinalDarkNeighbors { get; init; } = 1;
        public double SymmetricPatchSigma { get; init; } = 5;
        public double SymmetricPatchMinimumBrightWeight { get; init; } = 0.5;
        public double SymmetricPatchMaximumDarkWeight { get; init; } = 0.05;
        public double SymmetricPatchBrightRatio { get; init; } = 5;
        public double SymmetricPatchMinimumEnergyDrop { get; init; } = 0.6;
        public int SymmetricPatchGradientRadius { get; init; } = 3;
        public double SymmetricPatchColorTolerance { get; init; } = 24;
        public int SymmetricPatchMinimumSimilarNeighbors { get; init; } = 5;
        public double SymmetricPatchMinimumOpposition { get; init; } = 0.5;
        public bool UseHighContrastGradientRescue { get; init; }
        public int HighContrastMaximumDarkNeighbors { get; init; } = 2;
        public double HighContrastMinimumLightnessGain { get; init; } = 45;
        public double HighContrastMinimumEnergyDrop { get; init; } = 0.5;
        public int HighContrastGradientRadius { get; init; } = 3;
        public double HighContrastColorTolerance { get; init; } = 24;
        public int HighContrastMinimumSimilarNeighbors { get; init; } = 5;
        public double HighContrastMinimumOpposition { get; init; } = 0.5;
        public bool UseHighContrastConsensus { get; init; }
        public double HighContrastConsensusThreshold { get; init; } = 0.4;
        public double HighContrastConsensusMinimumGain { get; init; } = 60;
        public int HighContrastConsensusMaximumDarkNeighbors { get; init; } = 3;
        public bool UseRepeatedDarkColorRescue { get; init; }
        public int RepeatedDarkColorMinimumOccurrences { get; init; } = 4;
        public int RepeatedDarkColorMaximumOccurrences { get; init; } = 8;
        public int RepeatedDarkColorMinimumDarkNeighbors { get; init; } = 1;
        public int RepeatedDarkColorMaximumDarkNeighbors { get; init; } = 2;
        public double RepeatedDarkColorMaximumLightness { get; init; } = 2.5;
        public double RepeatedDarkColorMinimumLightnessGain { get; init; } = 40;
        public double RepeatedDarkColorMinimumEnergyDrop { get; init; } = 0.5;
        public bool UseRepeatedDiagonalBridgeRescue { get; init; } = true;
        public double RepeatedBridgeMinimumLightnessGain { get; init; } = 60;
        public double RepeatedBridgeMinimumEnergyDrop { get; init; } = 0.45;
        public int RepeatedBridgeGradientRadius { get; init; } = 3;
        public double RepeatedBridgeColorTolerance { get; init; } = 24;
        public int RepeatedBridgeMinimumSimilarNeighbors { get; init; } = 5;
        public double RepeatedBridgeMinimumOpposition { get; init; } = 0.7;
    }

    public sealed record Result(Bitmap Image, int ChangedPixels);
    private readonly record struct NeighborColor(Color Color, double Weight);
    private readonly record struct PatchCounts(int Bright, int Dark);
    private readonly record struct ColorPatchSample(bool Dark, (double L, double A, double B)[] Ring);
    private static readonly int[][] PatchSymmetries =
    [
        [0, 1, 2, 3, 4, 5, 6, 7],
        [5, 3, 0, 6, 1, 7, 4, 2],
        [7, 6, 5, 4, 3, 2, 1, 0],
        [2, 4, 7, 1, 6, 0, 3, 5],
        [2, 1, 0, 4, 3, 7, 6, 5],
        [7, 4, 2, 6, 1, 5, 3, 0],
        [5, 6, 7, 3, 4, 0, 1, 2],
        [0, 3, 5, 1, 6, 2, 4, 7]
    ];
    private readonly record struct RepeatEvidence(bool DarkPeer, bool BrightPeer);
    private readonly record struct Proposal(int Index, Color Color, int DarkNeighbors,
        int CardinalDarkNeighbors, int Dark5Support, double Consensus, double LightnessGain,
        double LocalGradientRatio, bool RepeatedDarkRescue);
    private readonly record struct Gradient(double X, double Y)
    {
        public double Length => Math.Sqrt(X * X + Y * Y);
    }
    private readonly record struct GradientEvidence(double MagnitudeRatio, double Agreement,
        double Alignment);
    private sealed record DarkComponent(int[] Pixels, int MinX, int MinY, int MaxX, int MaxY);

    public static Result Clean(Bitmap source, Options options, Action<int, int, string>? audit = null,
        bool[]? protectedPixels = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(options.DarkLightness) || options.DarkLightness is < 0 or > 100 ||
            options.MaximumDarkNeighbors is < 0 or > 8 ||
            options.MaximumCardinalDarkNeighbors is < 0 or > 4 ||
            options.TwoCardinalMinimumBrightPatternMatches is < 0 or > 10000 ||
            !double.IsFinite(options.ColorMatchedPatchTolerance) ||
            options.ColorMatchedPatchTolerance is < 0 or > 100 ||
            options.ColorMatchedPatchMinimumBrightMatches is < 0 or > 10000 ||
            options.ColorMatchedPatchMaximumPatternDifferences is < 0 or > 2 ||
            !double.IsFinite(options.ColorPatchEnergyRescueMinimumDrop) ||
            options.ColorPatchEnergyRescueMinimumDrop is < 0 or > 1 ||
            !double.IsFinite(options.ThreeNeighborEnergyRescueMinimumDrop) ||
            options.ThreeNeighborEnergyRescueMinimumDrop is < 0 or > 1 ||
            options.ThreeNeighborEnergyDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.ThreeNeighborEnergyFlatMinimumBrightWeight) ||
            options.ThreeNeighborEnergyFlatMinimumBrightWeight is < 0 or > 10000 ||
            !double.IsFinite(options.ThreeNeighborEnergyTexturedMinimumBrightWeight) ||
            options.ThreeNeighborEnergyTexturedMinimumBrightWeight is < 0 or > 10000 ||
            options.TexturedGradientMinimumDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.TexturedGradientMinimumEnergyDrop) ||
            options.TexturedGradientMinimumEnergyDrop is < 0 or > 1 ||
            !double.IsFinite(options.TexturedGradientMinimumBrightWeight) ||
            options.TexturedGradientMinimumBrightWeight is < 0 or > 10000 ||
            options.TexturedGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.TexturedGradientColorTolerance) ||
            options.TexturedGradientColorTolerance is < 0 or > 100 ||
            options.TexturedGradientMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.TexturedGradientMinimumOpposition) ||
            options.TexturedGradientMinimumOpposition is < 0 or > 1 ||
            options.SingleDiagonalMinimumDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.SingleDiagonalMaximumLightness) ||
            options.SingleDiagonalMaximumLightness is < 0 or > 100 ||
            !double.IsFinite(options.SingleDiagonalMinimumEnergyDrop) ||
            options.SingleDiagonalMinimumEnergyDrop is < 0 or > 1 ||
            !double.IsFinite(options.SingleDiagonalMinimumBrightWeight) ||
            options.SingleDiagonalMinimumBrightWeight is < 0 or > 10000 ||
            options.SingleDiagonalGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.SingleDiagonalColorTolerance) ||
            options.SingleDiagonalColorTolerance is < 0 or > 100 ||
            options.SingleDiagonalMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.SingleDiagonalMinimumOpposition) ||
            options.SingleDiagonalMinimumOpposition is < 0 or > 1 ||
            options.ThreeNeighborGradientMinimumDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.ThreeNeighborGradientMaximumLightness) ||
            options.ThreeNeighborGradientMaximumLightness is < 0 or > 100 ||
            !double.IsFinite(options.ThreeNeighborGradientMinimumEnergyDrop) ||
            options.ThreeNeighborGradientMinimumEnergyDrop is < 0 or > 1 ||
            !double.IsFinite(options.ThreeNeighborGradientMinimumBrightWeight) ||
            options.ThreeNeighborGradientMinimumBrightWeight is < 0 or > 10000 ||
            options.ThreeNeighborGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.ThreeNeighborGradientColorTolerance) ||
            options.ThreeNeighborGradientColorTolerance is < 0 or > 100 ||
            options.ThreeNeighborGradientMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.ThreeNeighborGradientMinimumOpposition) ||
            options.ThreeNeighborGradientMinimumOpposition is < 0 or > 1 ||
            options.WeightedColorPatchMaximumPatternDifferences is < 0 or > 2 ||
            !double.IsFinite(options.WeightedColorPatchSigma) ||
            options.WeightedColorPatchSigma is <= 0 or > 100 ||
            !double.IsFinite(options.WeightedColorPatchMinimumBrightWeight) ||
            options.WeightedColorPatchMinimumBrightWeight is < 0 or > 10000 ||
            !double.IsFinite(options.WeightedColorPatchBrightRatio) ||
            options.WeightedColorPatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.ExtendedWeightedPatchSigma) ||
            options.ExtendedWeightedPatchSigma is <= 0 or > 100 ||
            !double.IsFinite(options.ExtendedWeightedPatchMinimumBrightWeight) ||
            options.ExtendedWeightedPatchMinimumBrightWeight is < 0 or > 10000 ||
            !double.IsFinite(options.ExtendedWeightedPatchBrightRatio) ||
            options.ExtendedWeightedPatchBrightRatio is < 0 or > 100 ||
            options.ExtendedWeightedPatchTexturedDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.ExtendedWeightedPatchMaximumLightness) ||
            options.ExtendedWeightedPatchMaximumLightness is < 0 or > 100 ||
            options.ExtendedWeightedPatchMinimumCardinalDarkNeighbors is < 0 or > 4 ||
            !double.IsFinite(options.ExtendedWeightedPatchMinimumEnergyDrop) ||
            options.ExtendedWeightedPatchMinimumEnergyDrop is < 0 or > 1 ||
            options.ExtendedWeightedPatchGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.ExtendedWeightedPatchColorTolerance) ||
            options.ExtendedWeightedPatchColorTolerance is < 0 or > 100 ||
            options.ExtendedWeightedPatchMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.ExtendedWeightedPatchMinimumOpposition) ||
            options.ExtendedWeightedPatchMinimumOpposition is < 0 or > 1 ||
            options.SymmetricPatchMinimumDistinctColors is < 1 or > 8 ||
            !double.IsFinite(options.SymmetricPatchMaximumLightness) ||
            options.SymmetricPatchMaximumLightness is < 0 or > 100 ||
            options.SymmetricPatchMinimumCardinalDarkNeighbors is < 0 or > 4 ||
            !double.IsFinite(options.SymmetricPatchSigma) ||
            options.SymmetricPatchSigma is <= 0 or > 100 ||
            !double.IsFinite(options.SymmetricPatchMinimumBrightWeight) ||
            options.SymmetricPatchMinimumBrightWeight is < 0 or > 10000 ||
            !double.IsFinite(options.SymmetricPatchMaximumDarkWeight) ||
            options.SymmetricPatchMaximumDarkWeight is < 0 or > 10000 ||
            !double.IsFinite(options.SymmetricPatchBrightRatio) ||
            options.SymmetricPatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.SymmetricPatchMinimumEnergyDrop) ||
            options.SymmetricPatchMinimumEnergyDrop is < 0 or > 1 ||
            options.SymmetricPatchGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.SymmetricPatchColorTolerance) ||
            options.SymmetricPatchColorTolerance is < 0 or > 100 ||
            options.SymmetricPatchMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.SymmetricPatchMinimumOpposition) ||
            options.SymmetricPatchMinimumOpposition is < 0 or > 1 ||
            !double.IsFinite(options.HighContrastMinimumLightnessGain) ||
            options.HighContrastMinimumLightnessGain is < 0 or > 100 ||
            options.HighContrastMaximumDarkNeighbors is < 0 or > 8 ||
            !double.IsFinite(options.HighContrastMinimumEnergyDrop) ||
            options.HighContrastMinimumEnergyDrop is < 0 or > 1 ||
            options.HighContrastGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.HighContrastColorTolerance) ||
            options.HighContrastColorTolerance is < 0 or > 100 ||
            options.HighContrastMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.HighContrastMinimumOpposition) ||
            options.HighContrastMinimumOpposition is < 0 or > 1 ||
            !double.IsFinite(options.HighContrastConsensusThreshold) ||
            options.HighContrastConsensusThreshold is < 0 or > 1 ||
            !double.IsFinite(options.HighContrastConsensusMinimumGain) ||
            options.HighContrastConsensusMinimumGain is < 0 or > 100 ||
            options.HighContrastConsensusMaximumDarkNeighbors is < 0 or > 8 ||
            options.RepeatedDarkColorMinimumOccurrences is < 1 or > 1000000 ||
            options.RepeatedDarkColorMaximumOccurrences is < 1 or > 1000000 ||
            options.RepeatedDarkColorMinimumOccurrences > options.RepeatedDarkColorMaximumOccurrences ||
            options.RepeatedDarkColorMinimumDarkNeighbors is < 0 or > 8 ||
            options.RepeatedDarkColorMaximumDarkNeighbors is < 0 or > 8 ||
            options.RepeatedDarkColorMinimumDarkNeighbors > options.RepeatedDarkColorMaximumDarkNeighbors ||
            !double.IsFinite(options.RepeatedDarkColorMaximumLightness) ||
            options.RepeatedDarkColorMaximumLightness is < 0 or > 100 ||
            !double.IsFinite(options.RepeatedDarkColorMinimumLightnessGain) ||
            options.RepeatedDarkColorMinimumLightnessGain is < 0 or > 100 ||
            !double.IsFinite(options.RepeatedDarkColorMinimumEnergyDrop) ||
            options.RepeatedDarkColorMinimumEnergyDrop is < 0 or > 1 ||
            !double.IsFinite(options.RepeatedBridgeMinimumLightnessGain) ||
            options.RepeatedBridgeMinimumLightnessGain is < 0 or > 100 ||
            !double.IsFinite(options.RepeatedBridgeMinimumEnergyDrop) ||
            options.RepeatedBridgeMinimumEnergyDrop is < 0 or > 1 ||
            options.RepeatedBridgeGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.RepeatedBridgeColorTolerance) ||
            options.RepeatedBridgeColorTolerance is < 0 or > 100 ||
            options.RepeatedBridgeMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.RepeatedBridgeMinimumOpposition) ||
            options.RepeatedBridgeMinimumOpposition is < 0 or > 1 ||
            !double.IsFinite(options.ColorMatchedPatchBrightRatio) ||
            options.ColorMatchedPatchBrightRatio is < 0 or > 100 ||
            options.ThreeNeighborMinimumDark5 is < 0 or > 24 ||
            options.TwoCardinalMinimumDark5 is < 0 or > 24 ||
            !double.IsFinite(options.ThreeNeighborMinimumComponentRatio) ||
            options.ThreeNeighborMinimumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.TwoCardinalMinimumComponentRatio) ||
            options.TwoCardinalMinimumComponentRatio is < 0 or > 1 ||
            options.ThreeNeighborProtectedContinuation is < 0 or > 8 ||
            !double.IsFinite(options.ThreeNeighborProtectedComponentRatio) ||
            options.ThreeNeighborProtectedComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.ThreeNeighborProtectedMinimumComponentRatio) ||
            options.ThreeNeighborProtectedMinimumComponentRatio is < 0 or > 1 ||
            options.ThreeNeighborProtectedMinimumComponentRatio > options.ThreeNeighborProtectedComponentRatio ||
            !double.IsFinite(options.RepeatedComponentSimilarity) ||
            options.RepeatedComponentSimilarity is < 0 or > 1 ||
            options.RepeatedPatchRadius is < 0 or > 4 ||
            options.RepeatedPatchMaximumDifferences is < 0 or > 80 ||
            options.LargePatchRadius is < 0 or > 4 ||
            options.SmallPatchRadius is < 0 or > 4 ||
            options.LargePatchMinimumBrightMatches is < 0 or > 10000 ||
            options.SmallPatchMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.LargePatchBrightRatio) || options.LargePatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.SmallPatchBrightRatio) || options.SmallPatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.SmallPatchMaximumComponentRatio) ||
            options.SmallPatchMaximumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.DiagonalBridgeMinimumConsensus) ||
            options.DiagonalBridgeMinimumConsensus is < 0 or > 1 ||
            options.MinimumStraightRun is < 1 or > 101 ||
            options.StraightRunSearchRadius is < 1 or > 50 ||
            options.StraightRunMinimumDark5ForTwoNeighbors is < 0 or > 24 ||
            !double.IsFinite(options.StraightRunMinimumConsensus) ||
            options.StraightRunMinimumConsensus is < 0 or > 1 ||
            options.IsolatedPatchRadius is < 0 or > 4 ||
            options.IsolatedPatchMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.IsolatedPatchBrightRatio) ||
            options.IsolatedPatchBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.IsolatedPatchMinimumConsensus) ||
            options.IsolatedPatchMinimumConsensus is < 0 or > 1 ||
            !double.IsFinite(options.DiagonalColorTolerance) || options.DiagonalColorTolerance is < 0 or > 100 ||
            options.DiagonalMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.DiagonalBrightRatio) || options.DiagonalBrightRatio is < 0 or > 100 ||
            !double.IsFinite(options.DiagonalMinimumGradientRatio) ||
            options.DiagonalMinimumGradientRatio is < 1 or > 10 ||
            options.DiagonalGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.DiagonalSimilarColorTolerance) ||
            options.DiagonalSimilarColorTolerance is < 0 or > 100 ||
            options.DiagonalMinimumSimilarNeighbors is < 1 or > 1000 ||
            options.SmallPatchMaximumCardinalDarkNeighbors is < 0 or > 4 ||
            options.TaperedRunMinimumDark5 is < 0 or > 24 ||
            options.TaperedRunMaximumDark5 is < 0 or > 24 ||
            options.TaperedRunMinimumDark5 > options.TaperedRunMaximumDark5 ||
            options.TaperedRunSearchRadius is < 1 or > 50 ||
            options.TaperedRunMinimumInsideLength is < 1 or > 101 ||
            options.TaperedRunMinimumShrink is < 1 or > 100 ||
            options.TaperedRunMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.TaperedRunBrightRatio) ||
            options.TaperedRunBrightRatio is < 0 or > 100 ||
            options.SparsePatchMaximumDark5 is < 0 or > 24 ||
            options.SparsePatchMaximumCardinalDarkNeighbors is < 0 or > 4 ||
            options.SparsePatchMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.SparsePatchBrightRatio) ||
            options.SparsePatchBrightRatio is < 0 or > 100 ||
            options.CandidateConflictRadius is < 0 or > 4 ||
            options.CommonGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.CommonColorTolerance) || options.CommonColorTolerance is < 0 or > 100 ||
            options.CommonMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.CommonMinimumMagnitude) || options.CommonMinimumMagnitude is < 0 or > 100000 ||
            !double.IsFinite(options.CommonMinimumMagnitudeRatio) || options.CommonMinimumMagnitudeRatio is < 0 or > 10 ||
            !double.IsFinite(options.CommonMinimumAgreement) || options.CommonMinimumAgreement is < 0 or > 1 ||
            !double.IsFinite(options.CommonMinimumOpposition) || options.CommonMinimumOpposition is < 0 or > 1 ||
            options.SecondarySparseMaximumDark5 is < 0 or > 24 ||
            !double.IsFinite(options.SecondarySparseMaximumComponentRatio) ||
            options.SecondarySparseMaximumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.SoftMinimumConsensus) || options.SoftMinimumConsensus is < 0 or > 1 ||
            !double.IsFinite(options.SoftMaximumColorSpread) || options.SoftMaximumColorSpread is < 0 or > 100 ||
            options.SoftMaximumDarkNeighbors is < 0 or > 8 ||
            options.SoftMaximumDark5 is < 0 or > 24 ||
            options.CoherentGradientMinimumDark5 is < 0 or > 24 ||
            options.CoherentGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.CoherentGradientColorTolerance) ||
            options.CoherentGradientColorTolerance is < 0 or > 100 ||
            options.CoherentGradientMinimumNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.CoherentGradientMagnitudeRatio) ||
            options.CoherentGradientMagnitudeRatio is < 0 or > 10 ||
            !double.IsFinite(options.CoherentGradientMinimumAgreement) ||
            options.CoherentGradientMinimumAgreement is < 0 or > 1 ||
            !double.IsFinite(options.CoherentGradientMinimumAlignment) ||
            options.CoherentGradientMinimumAlignment is < 0 or > 1 ||
            options.DivergentGradientDark5 is < 0 or > 24 ||
            options.DivergentGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.DivergentGradientColorTolerance) ||
            options.DivergentGradientColorTolerance is < 0 or > 100 ||
            options.DivergentGradientMinimumNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.DivergentGradientMagnitudeRatio) ||
            options.DivergentGradientMagnitudeRatio is < 0 or > 10 ||
            !double.IsFinite(options.DivergentGradientMaximumAgreement) ||
            options.DivergentGradientMaximumAgreement is < 0 or > 1 ||
            options.PatchGradientMinimumDark5 is < 0 or > 24 ||
            options.PatchGradientMaximumDark5 is < 0 or > 24 ||
            options.PatchGradientMinimumDark5 > options.PatchGradientMaximumDark5 ||
            options.PatchGradientMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.PatchGradientBrightRatio) ||
            options.PatchGradientBrightRatio is < 0 or > 100 ||
            options.PatchGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.PatchGradientColorTolerance) ||
            options.PatchGradientColorTolerance is < 0 or > 100 ||
            options.PatchGradientMinimumNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.PatchGradientMagnitudeRatio) ||
            options.PatchGradientMagnitudeRatio is < 0 or > 10 ||
            !double.IsFinite(options.PatchGradientMaximumMagnitudeRatio) ||
            options.PatchGradientMaximumMagnitudeRatio is < 0 or > 10 ||
            options.PatchGradientMaximumMagnitudeRatio < options.PatchGradientMagnitudeRatio ||
            !double.IsFinite(options.PatchGradientMaximumAgreement) ||
            options.PatchGradientMaximumAgreement is < 0 or > 1 ||
            options.MainSparseMaximumDark5 is < 0 or > 24 ||
            options.MainSparseMinimumBrightMatches is < 0 or > 10000 ||
            !double.IsFinite(options.MainSparseBrightRatio) ||
            options.MainSparseBrightRatio is < 0 or > 100 ||
            options.MainSparseGradientRadius is < 1 or > 15 ||
            !double.IsFinite(options.MainSparseColorTolerance) ||
            options.MainSparseColorTolerance is < 0 or > 100 ||
            options.MainSparseMinimumSimilarNeighbors is < 1 or > 1000 ||
            !double.IsFinite(options.MainSparseMaximumMagnitudeRatio) ||
            options.MainSparseMaximumMagnitudeRatio is < 0 or > 10 ||
            options.BrightCornerMaximumDarkNeighbors is < 0 or > 8 ||
            options.BrightCornerMaximumCardinalNeighbors is < 0 or > 4 ||
            options.BrightCornerMaximumDark5 is < 0 or > 24 ||
            !double.IsFinite(options.BrightCornerMaximumComponentRatio) ||
            options.BrightCornerMaximumComponentRatio is < 0 or > 1 ||
            options.BrightCornerMinimumRun is < 1 or > 15 ||
            !double.IsFinite(options.BrightCornerColorTolerance) ||
            options.BrightCornerColorTolerance is < 0 or > 100 ||
            options.VerticalMediumDark5 is < 0 or > 24 ||
            !double.IsFinite(options.VerticalMediumMinimumComponentRatio) ||
            options.VerticalMediumMinimumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.VerticalMediumMaximumComponentRatio) ||
            options.VerticalMediumMaximumComponentRatio is < 0 or > 1 ||
            options.VerticalMediumMinimumComponentRatio > options.VerticalMediumMaximumComponentRatio ||
            !double.IsFinite(options.VerticalMediumMaximumColorSpread) ||
            options.VerticalMediumMaximumColorSpread is < 0 or > 100 ||
            options.VerticalWideDark5 is < 0 or > 24 ||
            !double.IsFinite(options.VerticalWideMinimumComponentRatio) ||
            options.VerticalWideMinimumComponentRatio is < 0 or > 1 ||
            !double.IsFinite(options.VerticalWideMinimumColorSpread) ||
            options.VerticalWideMinimumColorSpread is < 0 or > 100 ||
            !double.IsFinite(options.VerticalWideMaximumColorSpread) ||
            options.VerticalWideMaximumColorSpread is < 0 or > 100 ||
            options.VerticalWideMinimumColorSpread > options.VerticalWideMaximumColorSpread ||
            options.VerticalShadeSearchRadius is < 1 or > 15 ||
            !double.IsFinite(options.VerticalShadeColorTolerance) ||
            options.VerticalShadeColorTolerance is < 0 or > 100 ||
            options.DiagonalOppositionRadius is < 1 or > 15 ||
            !double.IsFinite(options.DiagonalMinimumOpposition) ||
            options.DiagonalMinimumOpposition is < 0 or > 1 ||
            !double.IsFinite(options.DiagonalMinimumAgreement) ||
            options.DiagonalMinimumAgreement is < 0 or > 1 ||
            !double.IsFinite(options.CardinalColorWeight) || options.CardinalColorWeight is < 1 or > 10 ||
            options.MinimumPairExtension is < 0 or > 2 ||
            !double.IsFinite(options.PairColorTolerance) || options.PairColorTolerance is < 0 or > 100 ||
            !double.IsFinite(options.BrightColorTolerance) || options.BrightColorTolerance is < 0 or > 100 ||
            !double.IsFinite(options.MinimumLightnessGain) || options.MinimumLightnessGain is < 0 or > 100 ||
            options.AlphaClearance is < 1 or > 15 ||
            !double.IsFinite(options.MinimumBrightConsensus) || options.MinimumBrightConsensus is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Некорректные параметры очистки тёмных контуров.");
        int width = source.Width, height = source.Height;
        if (protectedPixels is not null && protectedPixels.Length != width * height)
            throw new ArgumentException("Размер карты защищённых пикселей не совпадает с изображением.", nameof(protectedPixels));
        var original = new Color[width * height];
        var dark = new bool[original.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color color = source.GetPixel(x, y);
                original[index] = color;
                dark[index] = color.A > 0 && Lab(color).L < options.DarkLightness;
            }
        var darkColorOccurrences = new Dictionary<int, int>();
        if (options.UseRepeatedDarkColorRescue)
            for (int q = 0; q < original.Length; q++)
            {
                if (!dark[q]) continue;
                int color = original[q].ToArgb();
                darkColorOccurrences.TryGetValue(color, out int count);
                darkColorOccurrences[color] = count + 1;
            }
        var (componentSizes, componentIds, components) = FindDarkComponents(dark, width, height);
        int largestComponent = componentSizes.Max();
        int[] cardinalComponentSizes = DarkComponentSizes(dark, width, height, false);
        int largestCardinalComponent = cardinalComponentSizes.Max();
        Dictionary<UInt128, PatchCounts> patchCounts = options.LargePatchRadius > 0 ?
            BuildPatchCounts(original, dark, width, height, options.LargePatchRadius) : new();
        Dictionary<UInt128, PatchCounts> shortPatchCounts = options.SmallPatchRadius > 0 &&
            options.SmallPatchMaximumComponentRatio > 0 ?
            BuildPatchCounts(original, dark, width, height, options.SmallPatchRadius) : new();
        Dictionary<UInt128, PatchCounts> isolatedPatchCounts = options.IsolatedPatchRadius > 0 ?
            BuildPatchCounts(original, dark, width, height, options.IsolatedPatchRadius) : new();
        Dictionary<UInt128, PatchCounts> diagonalPatchCounts =
            options.UseDiagonalGradientEvidence || options.UseTaperedRun ||
            options.UseSparsePatchEvidence || options.UsePatchGradientRescue ||
            options.UseMainSparsePatch ||
            options.TwoCardinalMinimumBrightPatternMatches > 0 ?
            BuildPatchCounts(original, dark, width, height, 1) : new();
        Dictionary<UInt128, List<ColorPatchSample>> colorPatchIndex =
            options.RequireColorMatchedPatch ? BuildColorPatchIndex(original, dark, width, height,
                options.ColorMatchedPatchMaximumPatternDifferences) : new();
        Dictionary<UInt128, List<ColorPatchSample>> weightedPatchIndex =
            options.RequireColorMatchedPatch &&
            (options.UseWeightedColorPatchRescue || options.UseExtendedWeightedColorPatchRescue ||
             options.UseThreeNeighborEnergyRescue ||
             options.UseTexturedGradientRescue || options.UseSingleDiagonalGradientRescue ||
             options.UseThreeNeighborGradientRescue) ?
                BuildColorPatchIndex(original, dark, width, height,
                    options.WeightedColorPatchMaximumPatternDifferences) : new();
        Dictionary<UInt128, List<ColorPatchSample>> symmetricPatchIndex =
            options.RequireColorMatchedPatch && options.UseSymmetricColorPatchRescue ?
                BuildColorPatchIndex(original, dark, width, height,
                    options.WeightedColorPatchMaximumPatternDifferences, true) : new();

        var output = (Color[])original.Clone();
        var proposals = new List<Proposal>();
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int index = y * width + x;
                if (!dark[index]) continue;
                if (protectedPixels is not null && protectedPixels[index])
                { audit?.Invoke(x, y, "inter-pass-protection"); continue; }
                if (!HasAlphaClearance(original, width, height, x, y, options.AlphaClearance))
                { audit?.Invoke(x, y, "alpha-clearance"); continue; }
                var surrounding = new List<NeighborColor>(8);
                int darkNeighbors = 0, cardinalDarkNeighbors = 0;
                bool allVisible = true;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int neighborIndex = (y + dy) * width + x + dx;
                        Color neighbor = original[neighborIndex];
                        if (neighbor.A == 0) { allVisible = false; continue; }
                        if (dark[neighborIndex])
                        {
                            darkNeighbors++;
                            if (dx == 0 || dy == 0) cardinalDarkNeighbors++;
                        }
                        else surrounding.Add(new NeighborColor(neighbor,
                            dx == 0 || dy == 0 ? options.CardinalColorWeight : 1));
                    }
                if (!allVisible || darkNeighbors > options.MaximumDarkNeighbors ||
                    cardinalDarkNeighbors > options.MaximumCardinalDarkNeighbors ||
                    surrounding.Count < 5)
                { audit?.Invoke(x, y, "dark-neighbors"); continue; }
                bool repeatedDarkColor = options.UseRepeatedDarkColorRescue &&
                    Lab(original[index]).L <= options.RepeatedDarkColorMaximumLightness &&
                    darkNeighbors >= options.RepeatedDarkColorMinimumDarkNeighbors &&
                    darkNeighbors <= options.RepeatedDarkColorMaximumDarkNeighbors &&
                    darkColorOccurrences.TryGetValue(original[index].ToArgb(), out int occurrences) &&
                    occurrences >= options.RepeatedDarkColorMinimumOccurrences &&
                    occurrences <= options.RepeatedDarkColorMaximumOccurrences;
                if (cardinalDarkNeighbors == 2 &&
                    options.TwoCardinalMinimumBrightPatternMatches > 0)
                {
                    UInt128 pattern = PatchPattern(original, dark, width, x, y, 1);
                    if (!diagonalPatchCounts.TryGetValue(pattern, out PatchCounts matches) ||
                        matches.Bright < options.TwoCardinalMinimumBrightPatternMatches)
                    { audit?.Invoke(x, y, "two-cardinal-pattern"); continue; }
                }
                bool colorPatchMatch = !options.RequireColorMatchedPatch ||
                    HasColorMatchedPatch(original, dark, width, x, y, colorPatchIndex,
                        options.ColorMatchedPatchTolerance,
                        options.ColorMatchedPatchMinimumBrightMatches,
                        options.ColorMatchedPatchBrightRatio);
                if (!colorPatchMatch && options.UseWeightedColorPatchRescue)
                    colorPatchMatch = HasWeightedColorPatch(original, dark, width, x, y,
                        weightedPatchIndex, options.WeightedColorPatchSigma,
                        options.WeightedColorPatchMinimumBrightWeight,
                        options.WeightedColorPatchBrightRatio);
                bool extendedWeightedPatch = !colorPatchMatch &&
                    options.UseExtendedWeightedColorPatchRescue &&
                    HasWeightedColorPatch(original, dark, width, x, y,
                        weightedPatchIndex, options.ExtendedWeightedPatchSigma,
                        options.ExtendedWeightedPatchMinimumBrightWeight,
                        options.ExtendedWeightedPatchBrightRatio);
                bool extendedTexturedPatch = extendedWeightedPatch &&
                    DistinctRingColors(original, width, x, y) >=
                        options.ExtendedWeightedPatchTexturedDistinctColors;
                if (extendedWeightedPatch && !extendedTexturedPatch) colorPatchMatch = true;
                bool symmetricPatchRescue = !colorPatchMatch &&
                    options.UseSymmetricColorPatchRescue &&
                    cardinalDarkNeighbors >= options.SymmetricPatchMinimumCardinalDarkNeighbors &&
                    Lab(original[index]).L <= options.SymmetricPatchMaximumLightness &&
                    DistinctRingColors(original, width, x, y) >=
                        options.SymmetricPatchMinimumDistinctColors &&
                    HasSymmetricWeightedColorPatch(original, dark, width, x, y, symmetricPatchIndex,
                        options.SymmetricPatchSigma, options.SymmetricPatchMinimumBrightWeight,
                        options.SymmetricPatchMaximumDarkWeight,
                        options.SymmetricPatchBrightRatio);
                bool threeNeighborEnergyRescue = options.UseThreeNeighborEnergyRescue &&
                    darkNeighbors == 3 && cardinalDarkNeighbors == 1;
                bool texturedGradientRescue = options.UseTexturedGradientRescue &&
                    darkNeighbors == 2 && cardinalDarkNeighbors == 1 &&
                    DistinctRingColors(original, width, x, y) >=
                        options.TexturedGradientMinimumDistinctColors;
                bool singleDiagonalRescue = options.UseSingleDiagonalGradientRescue &&
                    darkNeighbors == 1 && cardinalDarkNeighbors == 0 &&
                    Lab(original[index]).L <= options.SingleDiagonalMaximumLightness &&
                    DistinctRingColors(original, width, x, y) >=
                        options.SingleDiagonalMinimumDistinctColors;
                bool threeNeighborGradientRescue = options.UseThreeNeighborGradientRescue &&
                    darkNeighbors == 3 && cardinalDarkNeighbors == 1 &&
                    Lab(original[index]).L <= options.ThreeNeighborGradientMaximumLightness &&
                    DistinctRingColors(original, width, x, y) >=
                        options.ThreeNeighborGradientMinimumDistinctColors;
                bool noOtherPatchEntry = !colorPatchMatch && !options.UseColorPatchEnergyRescue &&
                    !threeNeighborEnergyRescue && !texturedGradientRescue &&
                    !singleDiagonalRescue && !threeNeighborGradientRescue &&
                    !extendedTexturedPatch && !symmetricPatchRescue;
                bool repeatedDarkEntry = noOtherPatchEntry && repeatedDarkColor;
                if (noOtherPatchEntry && !repeatedDarkColor)
                { audit?.Invoke(x, y, "color-patch"); continue; }
                int ringComponents = DarkRingComponents(dark, width, x, y);
                bool diagonalBridge = options.AllowDiagonalBridge && ringComponents == 2 &&
                    darkNeighbors == 2 && cardinalDarkNeighbors == 0 && cardinalComponentSizes[index] == 1;
                if (ringComponents > 1 && !diagonalBridge)
                { audit?.Invoke(x, y, "dark-ring-components"); continue; }
                int dark5Support = Dark5Support(dark, width, height, x, y);
                bool straightSpur = options.UseStraightRun && cardinalDarkNeighbors == 1 &&
                    (darkNeighbors == 3 || darkNeighbors == 2 &&
                        dark5Support >= options.StraightRunMinimumDark5ForTwoNeighbors) &&
                    LongStraightRun(dark, width, height, x, y, options.StraightRunSearchRadius) >=
                        options.MinimumStraightRun;
                bool patchSupport = options.LargePatchRadius > 0 &&
                    HasBrightPatchMajority(original, dark, width, height, x, y, patchCounts,
                        options.LargePatchRadius, options.LargePatchMinimumBrightMatches,
                        options.LargePatchBrightRatio);
                bool isolatedEvidence = options.IsolatedPatchRadius > 0 && darkNeighbors <= 1 &&
                    cardinalDarkNeighbors == 0 &&
                    HasBrightPatchMajority(original, dark, width, height, x, y,
                        isolatedPatchCounts, options.IsolatedPatchRadius,
                        options.IsolatedPatchMinimumBrightMatches, options.IsolatedPatchBrightRatio);
                if (!patchSupport && options.SmallPatchRadius > 0 &&
                    componentSizes[index] < largestComponent * options.SmallPatchMaximumComponentRatio &&
                    cardinalDarkNeighbors <= options.SmallPatchMaximumCardinalDarkNeighbors)
                    patchSupport = HasBrightPatchMajority(original, dark, width, height, x, y,
                        shortPatchCounts, options.SmallPatchRadius,
                        options.SmallPatchMinimumBrightMatches, options.SmallPatchBrightRatio);
                if (!patchSupport && options.UseSparsePatchEvidence &&
                    dark5Support <= options.SparsePatchMaximumDark5 &&
                    cardinalDarkNeighbors <= options.SparsePatchMaximumCardinalDarkNeighbors)
                    patchSupport = HasBrightPatchMajority(original, dark, width, height, x, y,
                        diagonalPatchCounts, 1, options.SparsePatchMinimumBrightMatches,
                        options.SparsePatchBrightRatio);
                if (!patchSupport && options.UseSecondarySparsePatch &&
                    dark5Support > options.SparsePatchMaximumDark5 &&
                    dark5Support <= options.SecondarySparseMaximumDark5 &&
                    cardinalDarkNeighbors <= options.SparsePatchMaximumCardinalDarkNeighbors &&
                    cardinalComponentSizes[index] < largestCardinalComponent *
                        options.SecondarySparseMaximumComponentRatio)
                    patchSupport = HasBrightPatchMajority(original, dark, width, height, x, y,
                        diagonalPatchCounts, 1, options.SparsePatchMinimumBrightMatches,
                        options.SparsePatchBrightRatio);
                if (!patchSupport && options.UseMainSparsePatch &&
                    dark5Support <= options.MainSparseMaximumDark5 &&
                    cardinalDarkNeighbors <= options.SparsePatchMaximumCardinalDarkNeighbors &&
                    componentSizes[index] >= largestComponent * options.SecondarySparseMaximumComponentRatio &&
                    TryMeasureSimilarGradients(original, width, height, x, y,
                        options.MainSparseGradientRadius, options.MainSparseColorTolerance,
                        options.MainSparseMinimumSimilarNeighbors, out GradientEvidence sparseEvidence) &&
                    sparseEvidence.MagnitudeRatio <= options.MainSparseMaximumMagnitudeRatio)
                    patchSupport = HasBrightPatchMajority(original, dark, width, height, x, y,
                        diagonalPatchCounts, 1, options.MainSparseMinimumBrightMatches,
                        options.MainSparseBrightRatio);
                bool mediumComponent = darkNeighbors == 3 &&
                    componentSizes[index] >= largestComponent * options.ThreeNeighborProtectedMinimumComponentRatio &&
                    componentSizes[index] < largestComponent * options.ThreeNeighborProtectedComponentRatio;
                RepeatEvidence repeated = mediumComponent ?
                    CompareRepeatedComponent(original, dark, componentIds, components, width, height, x, y,
                        options.RepeatedComponentSimilarity, options.RepeatedPatchRadius,
                        options.RepeatedPatchMaximumDifferences) : default;
                bool repeatedMismatch = options.UseRepeatedMismatch && repeated.BrightPeer && !repeated.DarkPeer;
                Color diagonalColor = default;
                bool diagonalEvidence = options.UseDiagonalGradientEvidence && darkNeighbors == 3 &&
                    cardinalDarkNeighbors == 2 &&
                    TryDiagonalBrightPair(original, dark, width, height, x, y,
                        options.DiagonalColorTolerance, out diagonalColor) &&
                    HasBrightPatchMajority(original, dark, width, height, x, y, diagonalPatchCounts,
                        1, options.DiagonalMinimumBrightMatches, options.DiagonalBrightRatio) &&
                    (HasStrongDiagonalGradient(original, dark, width, height, x, y, options) ||
                     options.UseDiagonalOppositionEvidence &&
                     HasOpposedDiagonalGradient(original, dark, width, height, x, y, options));
                bool taperedRun = options.UseTaperedRun && darkNeighbors == 3 &&
                    cardinalDarkNeighbors == 2 && dark5Support >= options.TaperedRunMinimumDark5 &&
                    dark5Support <= options.TaperedRunMaximumDark5 &&
                    HasBrightPatchMajority(original, dark, width, height, x, y, diagonalPatchCounts,
                        1, options.TaperedRunMinimumBrightMatches, options.TaperedRunBrightRatio) &&
                    HasTaperedRun(dark, width, height, x, y, options);
                bool divergentGradientRescue = darkNeighbors == 3 && cardinalDarkNeighbors == 2 &&
                    options.UseDivergentGradientRescue &&
                    dark5Support == options.DivergentGradientDark5 &&
                    componentSizes[index] >= largestComponent * options.TwoCardinalMinimumComponentRatio &&
                    cardinalComponentSizes[index] >= largestCardinalComponent *
                        options.TwoCardinalMinimumComponentRatio &&
                    HasDivergentStrongGradient(original, width, height, x, y, options);
                bool patchGradientRescue = darkNeighbors == 3 && cardinalDarkNeighbors == 2 &&
                    options.UsePatchGradientRescue &&
                    dark5Support >= options.PatchGradientMinimumDark5 &&
                    dark5Support <= options.PatchGradientMaximumDark5 &&
                    componentSizes[index] >= largestComponent * options.TwoCardinalMinimumComponentRatio &&
                    cardinalComponentSizes[index] >= largestCardinalComponent *
                        options.TwoCardinalMinimumComponentRatio &&
                    HasBrightPatchMajority(original, dark, width, height, x, y, diagonalPatchCounts,
                        1, options.PatchGradientMinimumBrightMatches, options.PatchGradientBrightRatio) &&
                    HasPatchGradientEvidence(original, width, height, x, y, options);
                if (darkNeighbors == 3 &&
                    (dark5Support < options.ThreeNeighborMinimumDark5 ||
                     componentSizes[index] < largestComponent * options.ThreeNeighborMinimumComponentRatio) &&
                    !patchSupport && !straightSpur && !repeatedMismatch && !diagonalEvidence &&
                    !taperedRun && !divergentGradientRescue && !patchGradientRescue)
                { audit?.Invoke(x, y, "three-neighbor-support"); continue; }
                if (darkNeighbors == 3 && options.ThreeNeighborProtectedContinuation > 0 &&
                    DarkContinuation(dark, width, height, x, y) >= options.ThreeNeighborProtectedContinuation &&
                    repeated.DarkPeer)
                { audit?.Invoke(x, y, "repeated-dark-continuation"); continue; }
                bool coherentGradientRescue = cardinalDarkNeighbors == 2 &&
                    options.UseCoherentGradientRescue &&
                    dark5Support >= options.CoherentGradientMinimumDark5 &&
                    dark5Support < options.TwoCardinalMinimumDark5 &&
                    !patchSupport && !repeatedMismatch && !diagonalEvidence && !taperedRun &&
                    componentSizes[index] >= largestComponent * options.TwoCardinalMinimumComponentRatio &&
                    cardinalComponentSizes[index] >= largestCardinalComponent *
                        options.TwoCardinalMinimumComponentRatio &&
                    HasCoherentStrongGradient(original, width, height, x, y, options);
                if (cardinalDarkNeighbors == 2 &&
                    (dark5Support < options.TwoCardinalMinimumDark5 ||
                     componentSizes[index] < largestComponent * options.TwoCardinalMinimumComponentRatio ||
                     cardinalComponentSizes[index] < largestCardinalComponent *
                         options.TwoCardinalMinimumComponentRatio) && !patchSupport && !repeatedMismatch &&
                    !diagonalEvidence && !taperedRun && !coherentGradientRescue &&
                    !divergentGradientRescue && !patchGradientRescue)
                { audit?.Invoke(x, y, "two-cardinal-support"); continue; }

                Color pairColor = default;
                bool hasPair = options.UseOppositeBrightPair && TryOppositeBrightPair(original, dark,
                    width, height, x, y, options.PairColorTolerance, options.BrightColorTolerance,
                    options.MinimumPairExtension,
                    out pairColor);
                Color cornerColor = default;
                bool brightCorner = options.UseBrightCornerContinuation &&
                    darkNeighbors <= options.BrightCornerMaximumDarkNeighbors &&
                    cardinalDarkNeighbors <= options.BrightCornerMaximumCardinalNeighbors &&
                    dark5Support <= options.BrightCornerMaximumDark5 &&
                    componentSizes[index] < largestComponent *
                        options.BrightCornerMaximumComponentRatio &&
                    TryBrightCornerContinuation(original, dark, width, height, x, y,
                        options.BrightCornerMinimumRun, options.BrightCornerColorTolerance,
                        out cornerColor);
                Color replacement = diagonalEvidence ? diagonalColor : hasPair ? pairColor :
                    brightCorner ? cornerColor : BrightRepresentative(surrounding, options.BrightColorTolerance);
                int matching = surrounding.Count(item => DistanceSquared(Lab(item.Color), Lab(replacement)) <=
                    options.BrightColorTolerance * options.BrightColorTolerance);
                double colorConsensus = matching / (double)surrounding.Count;
                bool softColorConsensus = options.UseSoftColorConsensus &&
                    darkNeighbors <= options.SoftMaximumDarkNeighbors &&
                    cardinalDarkNeighbors <= 1 && dark5Support <= options.SoftMaximumDark5 &&
                    colorConsensus >= options.SoftMinimumConsensus &&
                    MaximumColorSpreadSquared(surrounding) <=
                        options.SoftMaximumColorSpread * options.SoftMaximumColorSpread;
                bool highContrastConsensus = options.UseHighContrastConsensus &&
                    darkNeighbors <= options.HighContrastConsensusMaximumDarkNeighbors &&
                    colorConsensus >= options.HighContrastConsensusThreshold &&
                    Lab(replacement).L - Lab(original[index]).L >=
                        options.HighContrastConsensusMinimumGain;
                if (!hasPair && !diagonalEvidence && !brightCorner &&
                    colorConsensus < options.MinimumBrightConsensus && !softColorConsensus &&
                    !highContrastConsensus &&
                    !(straightSpur && colorConsensus >=
                        options.StraightRunMinimumConsensus) &&
                    !(isolatedEvidence && colorConsensus >=
                        options.IsolatedPatchMinimumConsensus))
                { audit?.Invoke(x, y, "color-consensus"); continue; }
                bool repeatedBridgeRescue = diagonalBridge && repeatedDarkColor &&
                    options.UseRepeatedDiagonalBridgeRescue &&
                    Lab(replacement).L - Lab(original[index]).L >=
                        options.RepeatedBridgeMinimumLightnessGain &&
                    HasColorPatchEnergyRescue(original, width, x, y, replacement,
                        options.RepeatedBridgeMinimumEnergyDrop) &&
                    HasOpposedReplacementGradient(original, width, height, x, y,
                        replacement, options.RepeatedBridgeGradientRadius,
                        options.RepeatedBridgeColorTolerance,
                        options.RepeatedBridgeMinimumSimilarNeighbors,
                        options.RepeatedBridgeMinimumOpposition);
                if (diagonalBridge && colorConsensus <
                    options.DiagonalBridgeMinimumConsensus && !repeatedBridgeRescue)
                { audit?.Invoke(x, y, "diagonal-bridge-consensus"); continue; }
                double colorSpread = MaximumColorSpreadSquared(surrounding);
                bool mediumShadeContinuation = darkNeighbors == 3 && cardinalDarkNeighbors == 1 &&
                    dark5Support == options.VerticalMediumDark5 &&
                    componentSizes[index] >= largestComponent *
                        options.VerticalMediumMinimumComponentRatio &&
                    componentSizes[index] < largestComponent *
                        options.VerticalMediumMaximumComponentRatio &&
                    colorSpread <= options.VerticalMediumMaximumColorSpread *
                        options.VerticalMediumMaximumColorSpread;
                bool wideShadeContinuation = darkNeighbors == 3 && cardinalDarkNeighbors == 2 &&
                    dark5Support == options.VerticalWideDark5 &&
                    componentSizes[index] >= largestComponent *
                        options.VerticalWideMinimumComponentRatio &&
                    colorSpread >= options.VerticalWideMinimumColorSpread *
                        options.VerticalWideMinimumColorSpread &&
                    colorSpread <= options.VerticalWideMaximumColorSpread *
                        options.VerticalWideMaximumColorSpread;
                if (options.UseVerticalShadeContinuation &&
                    (mediumShadeContinuation || wideShadeContinuation) &&
                    TryVerticalShadeContinuation(original, dark, width, height, x, y,
                        options.VerticalShadeSearchRadius, options.VerticalShadeColorTolerance,
                        out Color verticalColor))
                    replacement = verticalColor;
                bool repeatedDarkOnly = repeatedDarkEntry || repeatedBridgeRescue;
                if (!colorPatchMatch)
                {
                    bool globalEnergyRescue = options.UseColorPatchEnergyRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.ColorPatchEnergyRescueMinimumDrop);
                    bool localEnergyRescue = false;
                    if (threeNeighborEnergyRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.ThreeNeighborEnergyRescueMinimumDrop))
                    {
                        int colors = DistinctRingColors(original, width, x, y);
                        double minimumBrightWeight = colors >= options.ThreeNeighborEnergyDistinctColors ?
                            options.ThreeNeighborEnergyTexturedMinimumBrightWeight :
                            options.ThreeNeighborEnergyFlatMinimumBrightWeight;
                        localEnergyRescue = WeightedBrightPatchWeight(original, dark, width, x, y,
                            weightedPatchIndex, options.WeightedColorPatchSigma) >= minimumBrightWeight;
                    }
                    bool texturedRescue = texturedGradientRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.TexturedGradientMinimumEnergyDrop) &&
                        WeightedBrightPatchWeight(original, dark, width, x, y,
                            weightedPatchIndex, options.WeightedColorPatchSigma) >=
                            options.TexturedGradientMinimumBrightWeight &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.TexturedGradientRadius,
                            options.TexturedGradientColorTolerance,
                            options.TexturedGradientMinimumSimilarNeighbors,
                            options.TexturedGradientMinimumOpposition);
                    bool singleDiagonalGradientRescue = singleDiagonalRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.SingleDiagonalMinimumEnergyDrop) &&
                        WeightedBrightPatchWeight(original, dark, width, x, y,
                            weightedPatchIndex, options.WeightedColorPatchSigma) >=
                            options.SingleDiagonalMinimumBrightWeight &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.SingleDiagonalGradientRadius,
                            options.SingleDiagonalColorTolerance,
                            options.SingleDiagonalMinimumSimilarNeighbors,
                            options.SingleDiagonalMinimumOpposition);
                    bool threeNeighborGradientAllowed = threeNeighborGradientRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.ThreeNeighborGradientMinimumEnergyDrop) &&
                        WeightedBrightPatchWeight(original, dark, width, x, y,
                            weightedPatchIndex, options.WeightedColorPatchSigma) >=
                            options.ThreeNeighborGradientMinimumBrightWeight &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.ThreeNeighborGradientRadius,
                            options.ThreeNeighborGradientColorTolerance,
                            options.ThreeNeighborGradientMinimumSimilarNeighbors,
                            options.ThreeNeighborGradientMinimumOpposition);
                    bool extendedTexturedAllowed = extendedTexturedPatch &&
                        cardinalDarkNeighbors >=
                            options.ExtendedWeightedPatchMinimumCardinalDarkNeighbors &&
                        Lab(original[index]).L <= options.ExtendedWeightedPatchMaximumLightness &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.ExtendedWeightedPatchMinimumEnergyDrop) &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.ExtendedWeightedPatchGradientRadius,
                            options.ExtendedWeightedPatchColorTolerance,
                            options.ExtendedWeightedPatchMinimumSimilarNeighbors,
                            options.ExtendedWeightedPatchMinimumOpposition);
                    bool symmetricPatchAllowed = symmetricPatchRescue &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.SymmetricPatchMinimumEnergyDrop) &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.SymmetricPatchGradientRadius,
                            options.SymmetricPatchColorTolerance,
                            options.SymmetricPatchMinimumSimilarNeighbors,
                            options.SymmetricPatchMinimumOpposition);
                    bool highContrastRescue = options.UseHighContrastGradientRescue &&
                        darkNeighbors <= options.HighContrastMaximumDarkNeighbors &&
                        Lab(replacement).L - Lab(original[index]).L >=
                            options.HighContrastMinimumLightnessGain &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.HighContrastMinimumEnergyDrop) &&
                        HasOpposedReplacementGradient(original, width, height, x, y,
                            replacement, options.HighContrastGradientRadius,
                            options.HighContrastColorTolerance,
                            options.HighContrastMinimumSimilarNeighbors,
                            options.HighContrastMinimumOpposition);
                    bool repeatedDarkRescue = repeatedDarkColor &&
                        Lab(replacement).L - Lab(original[index]).L >=
                            options.RepeatedDarkColorMinimumLightnessGain &&
                        HasColorPatchEnergyRescue(original, width, x, y, replacement,
                            options.RepeatedDarkColorMinimumEnergyDrop);
                    repeatedDarkOnly |= repeatedDarkRescue && !globalEnergyRescue && !localEnergyRescue &&
                        !texturedRescue && !singleDiagonalGradientRescue &&
                        !threeNeighborGradientAllowed && !extendedTexturedAllowed &&
                        !symmetricPatchAllowed && !highContrastRescue;
                    if (!globalEnergyRescue && !localEnergyRescue && !texturedRescue &&
                        !singleDiagonalGradientRescue && !threeNeighborGradientAllowed &&
                        !extendedTexturedAllowed && !symmetricPatchAllowed && !highContrastRescue &&
                        !repeatedDarkRescue)
                    { audit?.Invoke(x, y, "color-rescue-evidence"); continue; }
                }
                if (options.RequireCommonGradient &&
                    !HasOpposedCommonGradient(original, width, height, x, y, options))
                { audit?.Invoke(x, y, "common-gradient"); continue; }
                double lightnessGain = Lab(replacement).L - Lab(original[index]).L;
                if (lightnessGain < options.MinimumLightnessGain)
                { audit?.Invoke(x, y, "lightness-gain"); continue; }
                Color restored = Color.FromArgb(original[index].A, replacement.R, replacement.G, replacement.B);
                double localGradientRatio = TryMeasureSimilarGradients(original, width, height, x, y,
                    1, options.CommonColorTolerance, 1, out GradientEvidence localEvidence) ?
                    localEvidence.MagnitudeRatio : 0;
                proposals.Add(new Proposal(index, restored, darkNeighbors, cardinalDarkNeighbors,
                    dark5Support, colorConsensus, lightnessGain, localGradientRatio, repeatedDarkOnly));
            }

        var accepted = new List<Proposal>();
        foreach (Proposal proposal in proposals.OrderBy(item => item.DarkNeighbors)
                     .ThenBy(item => item.CardinalDarkNeighbors)
                     .ThenByDescending(item => item.LocalGradientRatio)
                     .ThenBy(item => item.Dark5Support)
                     .ThenByDescending(item => item.Consensus)
                     .ThenByDescending(item => item.LightnessGain))
        {
            int x = proposal.Index % width, y = proposal.Index / width;
            if (options.CandidateConflictRadius > 0 && accepted.Any(item =>
                    Math.Abs(item.Index % width - x) <= options.CandidateConflictRadius &&
                    Math.Abs(item.Index / width - y) <= options.CandidateConflictRadius))
            { audit?.Invoke(x, y, "proposal-conflict"); continue; }
            accepted.Add(proposal);
            output[proposal.Index] = proposal.Color;
            audit?.Invoke(x, y, proposal.RepeatedDarkRescue ? "accepted-repeated-dark" : "accepted");
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) result.SetPixel(x, y, output[y * width + x]);
        return new Result(result, accepted.Count);
    }

    private static bool HasAlphaClearance(Color[] original, int width, int height, int x, int y, int radius)
    {
        if (x - radius < 0 || y - radius < 0 || x + radius >= width || y + radius >= height)
            return false;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (original[(y + dy) * width + x + dx].A == 0) return false;
        return true;
    }

    private static int Dark5Support(bool[] dark, int width, int height, int x, int y)
    {
        int count = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && dark[ny * width + nx]) count++;
            }
        return count;
    }

    private static int DarkContinuation(bool[] dark, int width, int height, int x, int y)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nearX = x + dx, nearY = y + dy;
                int farX = x + 2 * dx, farY = y + 2 * dy;
                if (farX < 0 || farY < 0 || farX >= width || farY >= height) continue;
                if (dark[nearY * width + nearX] && dark[farY * width + farX]) count++;
            }
        return count;
    }

    private static int LongStraightRun(bool[] dark, int width, int height, int x, int y, int maximumStep)
    {
        for (int q = 0; q < 4; q++)
        {
            int dx = q == 0 ? 1 : q == 1 ? -1 : 0;
            int dy = q == 2 ? 1 : q == 3 ? -1 : 0;
            int nx = x + dx, ny = y + dy;
            if (!dark[ny * width + nx]) continue;
            int axisX = dy != 0 ? 1 : 0, axisY = dx != 0 ? 1 : 0;
            int length = 1;
            for (int side = -1; side <= 1; side += 2)
                for (int step = 1; step <= maximumStep; step++)
                {
                    int px = nx + side * axisX * step, py = ny + side * axisY * step;
                    if (px < 0 || py < 0 || px >= width || py >= height || !dark[py * width + px]) break;
                    length++;
                }
            return length;
        }
        return 0;
    }

    private static bool HasTaperedRun(bool[] dark, int width, int height, int x, int y, Options options)
    {
        for (int q = 0; q < 4; q++)
        {
            int dx = q == 0 ? 1 : q == 1 ? -1 : 0;
            int dy = q == 2 ? 1 : q == 3 ? -1 : 0;
            int neighborX = x + dx, neighborY = y + dy;
            if (!dark[neighborY * width + neighborX]) continue;
            int axisX = Math.Abs(dy), axisY = Math.Abs(dx);
            int inside = DarkRunLength(dark, width, height, neighborX, neighborY,
                axisX, axisY, options.TaperedRunSearchRadius);
            if (inside < options.TaperedRunMinimumInsideLength) continue;
            int outer = DarkRunLength(dark, width, height, x, y,
                axisX, axisY, options.TaperedRunSearchRadius);
            int other = DarkRunLength(dark, width, height, x + 2 * dx, y + 2 * dy,
                axisX, axisY, options.TaperedRunSearchRadius);
            if (other - outer >= options.TaperedRunMinimumShrink) return true;
        }
        return false;
    }

    private static int DarkRunLength(bool[] dark, int width, int height, int x, int y,
        int axisX, int axisY, int maximumStep)
    {
        if (x < 0 || y < 0 || x >= width || y >= height || !dark[y * width + x]) return 0;
        int length = 1;
        for (int side = -1; side <= 1; side += 2)
            for (int step = 1; step <= maximumStep; step++)
            {
                int px = x + side * axisX * step, py = y + side * axisY * step;
                if (px < 0 || py < 0 || px >= width || py >= height || !dark[py * width + px]) break;
                length++;
            }
        return length;
    }

    private static Dictionary<UInt128, PatchCounts> BuildPatchCounts(Color[] colors, bool[] dark,
        int width, int height, int radius)
    {
        var counts = new Dictionary<UInt128, PatchCounts>();
        for (int y = radius; y < height - radius; y++)
            for (int x = radius; x < width - radius; x++)
            {
                int index = y * width + x;
                if (colors[index].A == 0) continue;
                UInt128 key = PatchPattern(colors, dark, width, x, y, radius);
                counts.TryGetValue(key, out PatchCounts current);
                counts[key] = dark[index] ? current with { Dark = current.Dark + 1 } :
                    current with { Bright = current.Bright + 1 };
            }
        return counts;
    }

    private static bool HasBrightPatchMajority(Color[] colors, bool[] dark, int width, int height,
        int x, int y, Dictionary<UInt128, PatchCounts> counts, int radius, int minimumBright,
        double darkMultiplier = 1)
    {
        if (x < radius || y < radius || x + radius >= width || y + radius >= height) return false;
        UInt128 key = PatchPattern(colors, dark, width, x, y, radius);
        return counts.TryGetValue(key, out PatchCounts match) &&
            match.Bright >= minimumBright && match.Bright > match.Dark * darkMultiplier;
    }

    private static Dictionary<UInt128, List<ColorPatchSample>> BuildColorPatchIndex(
        Color[] colors, bool[] dark, int width, int height, int maximumDifferences,
        bool includeSymmetricCandidates = false)
    {
        var candidatePatterns = new HashSet<UInt128>();
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
                if (dark[y * width + x])
                {
                    UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
                    candidatePatterns.Add(pattern);
                    if (includeSymmetricCandidates)
                        for (int q = 1; q < PatchSymmetries.Length; q++)
                            candidatePatterns.Add(TransformPatchPattern(pattern, PatchSymmetries[q]));
                }

        var exact = new Dictionary<UInt128, List<ColorPatchSample>>();
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int pixel = y * width + x;
                if (colors[pixel].A == 0) continue;
                UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
                if (maximumDifferences == 0 && !candidatePatterns.Contains(pattern)) continue;
                if (!exact.TryGetValue(pattern, out List<ColorPatchSample>? matches))
                {
                    matches = new List<ColorPatchSample>();
                    exact.Add(pattern, matches);
                }
                matches.Add(new ColorPatchSample(dark[pixel], ColorPatchRing(colors, width, x, y)));
            }
        if (maximumDifferences == 0) return exact;

        var index = new Dictionary<UInt128, List<ColorPatchSample>>();
        foreach (UInt128 candidate in candidatePatterns)
        {
            var matches = new List<ColorPatchSample>();
            if (exact.TryGetValue(candidate, out List<ColorPatchSample>? same))
                matches.AddRange(same);
            UInt128 rest = candidate, place = 1;
            for (int q = 0; q < 8; q++)
            {
                int digit = (int)(rest % 3);
                rest /= 3;
                UInt128 nextRest = rest, nextPlace = place * 3;
                for (int replacement = 0; replacement < 3; replacement++)
                {
                    if (replacement == digit) continue;
                    UInt128 oneDifference = ReplacePatchDigit(candidate, digit, replacement, place);
                    if (exact.TryGetValue(oneDifference, out List<ColorPatchSample>? one))
                        matches.AddRange(one);
                    if (maximumDifferences < 2) continue;
                    UInt128 laterRest = nextRest, laterPlace = nextPlace;
                    for (int e = q + 1; e < 8; e++)
                    {
                        int laterDigit = (int)(laterRest % 3);
                        laterRest /= 3;
                        for (int laterReplacement = 0; laterReplacement < 3; laterReplacement++)
                        {
                            if (laterReplacement == laterDigit) continue;
                            UInt128 twoDifferences = ReplacePatchDigit(oneDifference,
                                laterDigit, laterReplacement, laterPlace);
                            if (exact.TryGetValue(twoDifferences, out List<ColorPatchSample>? two))
                                matches.AddRange(two);
                        }
                        laterPlace *= 3;
                    }
                }
                place = nextPlace;
            }
            if (matches.Count > 0) index.Add(candidate, matches);
        }
        return index;
    }

    private static UInt128 ReplacePatchDigit(UInt128 pattern, int oldDigit, int newDigit, UInt128 place)
    {
        return newDigit > oldDigit ? pattern + (uint)(newDigit - oldDigit) * place :
            pattern - (uint)(oldDigit - newDigit) * place;
    }

    private static UInt128 TransformPatchPattern(UInt128 pattern, int[] permutation)
    {
        Span<int> digits = stackalloc int[8];
        for (int q = 7; q >= 0; q--)
        {
            digits[q] = (int)(pattern % 3);
            pattern /= 3;
        }
        UInt128 transformed = 0;
        for (int q = 0; q < 8; q++)
            transformed = transformed * 3 + (uint)digits[permutation[q]];
        return transformed;
    }

    private static bool HasColorMatchedPatch(Color[] colors, bool[] dark, int width, int x, int y,
        Dictionary<UInt128, List<ColorPatchSample>> index, double tolerance,
        int minimumBright, double brightRatio)
    {
        UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
        if (!index.TryGetValue(pattern, out List<ColorPatchSample>? matches)) return false;
        var ring = ColorPatchRing(colors, width, x, y);
        double toleranceSquared = 8 * tolerance * tolerance;
        int bright = 0, darkMatches = 0;
        foreach (ColorPatchSample sample in matches)
        {
            double difference = 0;
            for (int q = 0; q < ring.Length; q++)
            {
                difference += DistanceSquared(ring[q], sample.Ring[q]);
                if (difference > toleranceSquared) break;
            }
            if (difference > toleranceSquared) continue;
            if (sample.Dark) darkMatches++;
            else bright++;
        }
        return bright >= minimumBright && bright > darkMatches * brightRatio;
    }

    private static bool HasColorPatchEnergyRescue(Color[] colors, int width, int x, int y,
        Color replacement, double minimumDrop)
    {
        var before = Lab(colors[y * width + x]);
        var after = Lab(replacement);
        double originalEnergy = 0, replacementEnergy = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var neighbor = Lab(colors[(y + dy) * width + x + dx]);
                double weight = 1.0 / (dx * dx + dy * dy);
                originalEnergy += weight * DistanceSquared(before, neighbor);
                replacementEnergy += weight * DistanceSquared(after, neighbor);
            }
        return originalEnergy > 1e-9 &&
            (originalEnergy - replacementEnergy) / originalEnergy > minimumDrop;
    }

    private static bool HasWeightedColorPatch(Color[] colors, bool[] dark, int width, int x, int y,
        Dictionary<UInt128, List<ColorPatchSample>> index, double sigma,
        double minimumBright, double brightRatio)
    {
        UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
        if (!index.TryGetValue(pattern, out List<ColorPatchSample>? matches)) return false;
        var ring = ColorPatchRing(colors, width, x, y);
        double scale = 16 * sigma * sigma;
        double bright = 0, darkWeight = 0;
        foreach (ColorPatchSample sample in matches)
        {
            double difference = 0;
            for (int q = 0; q < ring.Length; q++)
                difference += DistanceSquared(ring[q], sample.Ring[q]);
            double weight = Math.Exp(-difference / scale);
            if (sample.Dark) darkWeight += weight;
            else bright += weight;
        }
        return bright >= minimumBright && bright > darkWeight * brightRatio;
    }

    private static bool HasSymmetricWeightedColorPatch(Color[] colors, bool[] dark, int width, int x, int y,
        Dictionary<UInt128, List<ColorPatchSample>> index, double sigma,
        double minimumBright, double maximumDark, double brightRatio)
    {
        UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
        var ring = ColorPatchRing(colors, width, x, y);
        double scale = 16 * sigma * sigma;
        for (int orientation = 1; orientation < PatchSymmetries.Length; orientation++)
        {
            int[] permutation = PatchSymmetries[orientation];
            UInt128 transformedPattern = TransformPatchPattern(pattern, permutation);
            if (!index.TryGetValue(transformedPattern, out List<ColorPatchSample>? matches)) continue;
            double bright = 0, darkWeight = 0;
            foreach (ColorPatchSample sample in matches)
            {
                double difference = 0;
                for (int q = 0; q < ring.Length; q++)
                    difference += DistanceSquared(ring[permutation[q]], sample.Ring[q]);
                double weight = Math.Exp(-difference / scale);
                if (sample.Dark) darkWeight += weight;
                else bright += weight;
            }
            if (bright >= minimumBright && darkWeight <= maximumDark &&
                bright > darkWeight * brightRatio) return true;
        }
        return false;
    }

    private static double WeightedBrightPatchWeight(Color[] colors, bool[] dark, int width, int x, int y,
        Dictionary<UInt128, List<ColorPatchSample>> index, double sigma)
    {
        UInt128 pattern = PatchPattern(colors, dark, width, x, y, 1);
        if (!index.TryGetValue(pattern, out List<ColorPatchSample>? matches)) return 0;
        var ring = ColorPatchRing(colors, width, x, y);
        double scale = 16 * sigma * sigma, bright = 0;
        foreach (ColorPatchSample sample in matches)
        {
            if (sample.Dark) continue;
            double difference = 0;
            for (int q = 0; q < ring.Length; q++)
                difference += DistanceSquared(ring[q], sample.Ring[q]);
            bright += Math.Exp(-difference / scale);
        }
        return bright;
    }

    private static int DistinctRingColors(Color[] colors, int width, int x, int y)
    {
        Span<int> distinct = stackalloc int[8];
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int color = colors[(y + dy) * width + x + dx].ToArgb();
                bool found = false;
                for (int q = 0; q < count; q++)
                    if (distinct[q] == color) { found = true; break; }
                if (!found) distinct[count++] = color;
            }
        return count;
    }

    private static (double L, double A, double B)[] ColorPatchRing(Color[] colors, int width, int x, int y)
    {
        var ring = new (double L, double A, double B)[8];
        int q = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    ring[q++] = Lab(colors[(y + dy) * width + x + dx]);
        return ring;
    }

    private static UInt128 PatchPattern(Color[] colors, bool[] dark, int width, int x, int y, int radius)
    {
        UInt128 key = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int index = (y + dy) * width + x + dx;
                int value = colors[index].A == 0 ? 0 : dark[index] ? 1 : 2;
                key = key * 3 + (uint)value;
            }
        return key;
    }

    private static int[] DarkComponentSizes(bool[] dark, int width, int height, bool includeDiagonals = true)
    {
        var sizes = new int[dark.Length];
        var queue = new int[dark.Length];
        for (int q = 0; q < dark.Length; q++)
        {
            if (!dark[q] || sizes[q] != 0) continue;
            int head = 0, count = 0;
            queue[count++] = q; sizes[q] = -1;
            while (head < count)
            {
                int index = queue[head++], x = index % width, y = index / width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!includeDiagonals && Math.Abs(dx) + Math.Abs(dy) != 1) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (!dark[neighbor] || sizes[neighbor] != 0) continue;
                        sizes[neighbor] = -1;
                        queue[count++] = neighbor;
                    }
            }
            for (int e = 0; e < count; e++) sizes[queue[e]] = count;
        }
        return sizes;
    }

    private static (int[] Sizes, int[] Ids, List<DarkComponent> Components) FindDarkComponents(
        bool[] dark, int width, int height)
    {
        var sizes = new int[dark.Length];
        var ids = new int[dark.Length];
        Array.Fill(ids, -1);
        var components = new List<DarkComponent>();
        var queue = new int[dark.Length];
        for (int q = 0; q < dark.Length; q++)
        {
            if (!dark[q] || ids[q] >= 0) continue;
            int id = components.Count, head = 0, count = 0;
            int minX = width, minY = height, maxX = 0, maxY = 0;
            queue[count++] = q;
            ids[q] = id;
            while (head < count)
            {
                int index = queue[head++], x = index % width, y = index / width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (!dark[neighbor] || ids[neighbor] >= 0) continue;
                        ids[neighbor] = id;
                        queue[count++] = neighbor;
                    }
            }
            int[] pixels = queue[..count].ToArray();
            foreach (int pixel in pixels) sizes[pixel] = count;
            components.Add(new DarkComponent(pixels, minX, minY, maxX, maxY));
        }
        return (sizes, ids, components);
    }

    private static RepeatEvidence CompareRepeatedComponent(Color[] colors, bool[] dark, int[] ids,
        List<DarkComponent> components,
        int width, int height, int x, int y, double similarity, int patchRadius, int maximumPatchDifferences)
    {
        int sourceId = ids[y * width + x];
        DarkComponent source = components[sourceId];
        bool darkPeer = false, brightPeer = false;
        for (int id = 0; id < components.Count; id++)
        {
            if (id == sourceId) continue;
            DarkComponent peer = components[id];
            if (Math.Abs(source.Pixels.Length - peer.Pixels.Length) > source.Pixels.Length * (1 - similarity) ||
                Math.Abs((source.MaxX - source.MinX) - (peer.MaxX - peer.MinX)) > 2 ||
                Math.Abs((source.MaxY - source.MinY) - (peer.MaxY - peer.MinY)) > 2) continue;
            int startDx = peer.MinX - source.MinX, startDy = peer.MinY - source.MinY;
            for (int dy = startDy - 1; dy <= startDy + 1; dy++)
                for (int dx = startDx - 1; dx <= startDx + 1; dx++)
                {
                    int counterpartX = x + dx, counterpartY = y + dy;
                    if (counterpartX < 0 || counterpartY < 0 || counterpartX >= width || counterpartY >= height ||
                        colors[counterpartY * width + counterpartX].A != colors[y * width + x].A) continue;
                    if (patchRadius > 0 &&
                        (x < patchRadius || y < patchRadius || x + patchRadius >= width ||
                         y + patchRadius >= height || counterpartX < patchRadius ||
                         counterpartY < patchRadius || counterpartX + patchRadius >= width ||
                         counterpartY + patchRadius >= height ||
                         PatchDifference(colors, dark, width, x, y, counterpartX, counterpartY,
                             patchRadius) > maximumPatchDifferences)) continue;
                    int overlap = 0;
                    foreach (int pixel in source.Pixels)
                    {
                        int nx = pixel % width + dx, ny = pixel / width + dy;
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height && ids[ny * width + nx] == id)
                            overlap++;
                    }
                    if (overlap < Math.Max(source.Pixels.Length, peer.Pixels.Length) * similarity) continue;
                    if (ids[counterpartY * width + counterpartX] == id) darkPeer = true;
                    else if (!dark[counterpartY * width + counterpartX]) brightPeer = true;
                }
        }
        return new RepeatEvidence(darkPeer, brightPeer);
    }

    private static int PatchDifference(Color[] colors, bool[] dark, int width,
        int x, int y, int otherX, int otherY, int radius)
    {
        int differences = 0;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int a = (y + dy) * width + x + dx;
                int b = (otherY + dy) * width + otherX + dx;
                int classA = colors[a].A == 0 ? 0 : dark[a] ? 1 : 2;
                int classB = colors[b].A == 0 ? 0 : dark[b] ? 1 : 2;
                if (classA != classB) differences++;
            }
        return differences;
    }

    private static bool TryBrightCornerContinuation(Color[] original, bool[] dark,
        int width, int height, int x, int y, int minimumRun, double tolerance, out Color color)
    {
        color = default;
        double toleranceSquared = tolerance * tolerance;
        foreach (int dy in new[] { -1, 1 })
            foreach (int dx in new[] { -1, 1 })
            {
                if (x + dx * minimumRun < 0 || x + dx * minimumRun >= width ||
                    y + dy * minimumRun < 0 || y + dy * minimumRun >= height) continue;
                int first = y * width + x + dx;
                Color candidate = original[first];
                if (candidate.A == 0 || dark[first]) continue;
                int corner = (y + dy) * width + x + dx;
                if (original[corner].A != candidate.A || dark[corner] ||
                    DistanceSquared(Lab(original[corner]), Lab(candidate)) > toleranceSquared) continue;
                bool continuous = true;
                for (int distance = 1; distance <= minimumRun; distance++)
                {
                    int horizontal = y * width + x + dx * distance;
                    int vertical = (y + dy * distance) * width + x;
                    if (original[horizontal].A != candidate.A || dark[horizontal] ||
                        DistanceSquared(Lab(original[horizontal]), Lab(candidate)) > toleranceSquared ||
                        original[vertical].A != candidate.A || dark[vertical] ||
                        DistanceSquared(Lab(original[vertical]), Lab(candidate)) > toleranceSquared)
                    {
                        continuous = false;
                        break;
                    }
                }
                if (!continuous) continue;
                color = candidate;
                return true;
            }
        return false;
    }

    private static bool TryVerticalShadeContinuation(Color[] original, bool[] dark,
        int width, int height, int x, int y, int searchRadius, double tolerance, out Color color)
    {
        color = default;
        int bestRun = 1;
        double bestLightness = double.NegativeInfinity;
        foreach (int direction in new[] { -1, 1 })
        {
            int firstY = y + direction;
            if (firstY < 0 || firstY >= height) continue;
            int first = firstY * width + x;
            Color candidate = original[first];
            if (candidate.A == 0 || dark[first]) continue;
            int run = 0;
            for (int distance = 1; distance <= searchRadius; distance++)
            {
                int py = y + direction * distance;
                if (py < 0 || py >= height) break;
                int index = py * width + x;
                if (original[index].A != candidate.A || dark[index] ||
                    DistanceSquared(Lab(original[index]), Lab(candidate)) > tolerance * tolerance) break;
                run++;
            }
            double lightness = Lab(candidate).L;
            if (run > bestRun || run == bestRun && lightness > bestLightness)
            {
                bestRun = run;
                bestLightness = lightness;
                color = candidate;
            }
        }
        return bestRun >= 2;
    }

    private static bool TryOppositeBrightPair(Color[] original, bool[] dark, int width, int height,
        int x, int y, double pairTolerance, double extensionTolerance, int minimumExtension, out Color color)
    {
        bool TryAxis(int dx, int dy, out Color candidate, out int extension)
        {
            candidate = default;
            extension = 0;
            int first = (y - dy) * width + x - dx, second = (y + dy) * width + x + dx;
            Color a = original[first], b = original[second];
            if (a.A == 0 || b.A != a.A || dark[first] || dark[second] ||
                DistanceSquared(Lab(a), Lab(b)) > pairTolerance * pairTolerance) return false;
            candidate = a;
            for (int side = -1; side <= 1; side += 2)
            {
                int nx = x + side * dx * 2, ny = y + side * dy * 2;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int index = ny * width + nx;
                Color outer = original[index];
                if (outer.A == a.A && !dark[index] &&
                    DistanceSquared(Lab(outer), Lab(candidate)) <= extensionTolerance * extensionTolerance) extension++;
            }
            return extension >= minimumExtension;
        }

        bool horizontal = TryAxis(1, 0, out Color h, out int hs);
        bool vertical = TryAxis(0, 1, out Color v, out int vs);
        color = default;
        if (!horizontal && !vertical) return false;
        if (horizontal && vertical && hs == vs &&
            DistanceSquared(Lab(h), Lab(v)) > pairTolerance * pairTolerance) return false;
        color = horizontal && (!vertical || hs >= vs) ? h : v;
        return true;
    }

    private static bool TryDiagonalBrightPair(Color[] original, bool[] dark, int width, int height,
        int x, int y, double tolerance, out Color color)
    {
        bool TryAxis(int dx, int dy, out Color candidate)
        {
            candidate = default;
            if (x - 2 < 0 || y - 2 < 0 || x + 2 >= width || y + 2 >= height) return false;
            int first = (y - dy) * width + x - dx;
            int second = (y + dy) * width + x + dx;
            Color a = original[first], b = original[second];
            if (a.A == 0 || b.A != a.A || dark[first] || dark[second] ||
                DistanceSquared(Lab(a), Lab(b)) > tolerance * tolerance) return false;
            foreach (int side in new[] { -1, 1 })
            {
                int index = (y + side * dy * 2) * width + x + side * dx * 2;
                Color outer = original[index];
                if (outer.A != a.A || dark[index] ||
                    DistanceSquared(Lab(outer), Lab(a)) > tolerance * tolerance) return false;
            }
            candidate = a;
            return true;
        }

        bool falling = TryAxis(1, 1, out Color a);
        bool rising = TryAxis(1, -1, out Color b);
        color = default;
        if (!falling && !rising) return false;
        if (falling && rising && DistanceSquared(Lab(a), Lab(b)) > tolerance * tolerance) return false;
        color = falling ? a : b;
        return true;
    }

    private static bool HasStrongDiagonalGradient(Color[] original, bool[] dark, int width, int height,
        int x, int y, Options options)
    {
        double center = GradientAt(original, width, height, x, y, -1, -1).Length;
        if (center <= 1e-9) return false;
        Color color = original[y * width + x];
        double tolerance = options.DiagonalSimilarColorTolerance * options.DiagonalSimilarColorTolerance;
        double total = 0;
        int count = 0, radius = options.DiagonalGradientRadius;
        for (int py = Math.Max(1, y - radius); py <= Math.Min(height - 2, y + radius); py++)
            for (int px = Math.Max(1, x - radius); px <= Math.Min(width - 2, x + radius); px++)
            {
                if (px == x && py == y) continue;
                int index = py * width + px;
                if (!dark[index] || original[index].A != color.A ||
                    DistanceSquared(Lab(original[index]), Lab(color)) > tolerance) continue;
                double length = GradientAt(original, width, height, px, py, x, y).Length;
                if (length <= 1e-9) continue;
                total += length;
                count++;
            }
        return count >= options.DiagonalMinimumSimilarNeighbors &&
            center >= total / count * options.DiagonalMinimumGradientRatio;
    }

    private static bool HasOpposedDiagonalGradient(Color[] original, bool[] dark, int width, int height,
        int x, int y, Options options)
    {
        Gradient center = GradientAt(original, width, height, x, y, -1, -1);
        if (center.Length <= 1e-9) return false;
        Color color = original[y * width + x];
        double tolerance = options.DiagonalSimilarColorTolerance * options.DiagonalSimilarColorTolerance;
        double commonX = 0, commonY = 0;
        int count = 0, radius = options.DiagonalOppositionRadius;
        for (int py = Math.Max(1, y - radius); py <= Math.Min(height - 2, y + radius); py++)
            for (int px = Math.Max(1, x - radius); px <= Math.Min(width - 2, x + radius); px++)
            {
                if (px == x && py == y) continue;
                int index = py * width + px;
                if (!dark[index] || original[index].A != color.A ||
                    DistanceSquared(Lab(original[index]), Lab(color)) > tolerance) continue;
                Gradient other = GradientAt(original, width, height, px, py, x, y);
                if (other.Length <= 1e-9) continue;
                commonX += other.X / other.Length;
                commonY += other.Y / other.Length;
                count++;
            }
        if (count < options.DiagonalMinimumSimilarNeighbors) return false;
        double commonLength = Math.Sqrt(commonX * commonX + commonY * commonY);
        if (commonLength / count < options.DiagonalMinimumAgreement) return false;
        double opposition = -(center.X * commonX + center.Y * commonY) /
            (center.Length * commonLength);
        return opposition >= options.DiagonalMinimumOpposition;
    }

    private static bool HasCoherentStrongGradient(Color[] original, int width, int height,
        int x, int y, Options options)
    {
        return TryMeasureSimilarGradients(original, width, height, x, y,
            options.CoherentGradientRadius, options.CoherentGradientColorTolerance,
            options.CoherentGradientMinimumNeighbors, out GradientEvidence evidence) &&
            evidence.MagnitudeRatio >= options.CoherentGradientMagnitudeRatio &&
            evidence.Agreement >= options.CoherentGradientMinimumAgreement &&
            evidence.Alignment >= options.CoherentGradientMinimumAlignment;
    }

    private static bool HasPatchGradientEvidence(Color[] original, int width, int height,
        int x, int y, Options options)
    {
        return TryMeasureSimilarGradients(original, width, height, x, y,
            options.PatchGradientRadius, options.PatchGradientColorTolerance,
            options.PatchGradientMinimumNeighbors, out GradientEvidence evidence) &&
            evidence.MagnitudeRatio >= options.PatchGradientMagnitudeRatio &&
            evidence.MagnitudeRatio <= options.PatchGradientMaximumMagnitudeRatio &&
            evidence.Agreement <= options.PatchGradientMaximumAgreement;
    }

    private static bool HasDivergentStrongGradient(Color[] original, int width, int height,
        int x, int y, Options options)
    {
        return TryMeasureSimilarGradients(original, width, height, x, y,
            options.DivergentGradientRadius, options.DivergentGradientColorTolerance,
            options.DivergentGradientMinimumNeighbors, out GradientEvidence evidence) &&
            evidence.MagnitudeRatio >= options.DivergentGradientMagnitudeRatio &&
            evidence.Agreement <= options.DivergentGradientMaximumAgreement;
    }

    private static bool TryMeasureSimilarGradients(Color[] original, int width, int height,
        int x, int y, int radius, double colorTolerance, int minimumNeighbors,
        out GradientEvidence evidence)
    {
        evidence = default;
        Gradient center = GradientAt(original, width, height, x, y, -1, -1);
        double centerLength = center.Length;
        if (centerLength <= 1e-9) return false;

        Color color = original[y * width + x];
        var centerLab = Lab(color);
        double toleranceSquared = colorTolerance * colorTolerance;
        double unitX = 0, unitY = 0, magnitudeTotal = 0;
        int count = 0;
        for (int py = Math.Max(1, y - radius); py <= Math.Min(height - 2, y + radius); py++)
            for (int px = Math.Max(1, x - radius); px <= Math.Min(width - 2, x + radius); px++)
            {
                if (px == x && py == y) continue;
                Color peer = original[py * width + px];
                if (peer.A != color.A || DistanceSquared(centerLab, Lab(peer)) > toleranceSquared) continue;
                Gradient other = GradientAt(original, width, height, px, py, x, y);
                double length = other.Length;
                if (length <= 1e-9) continue;
                unitX += other.X / length;
                unitY += other.Y / length;
                magnitudeTotal += length;
                count++;
            }

        if (count < minimumNeighbors || magnitudeTotal <= 1e-9) return false;
        double commonLength = Math.Sqrt(unitX * unitX + unitY * unitY);
        double alignment = commonLength > 1e-9 ?
            (center.X * unitX + center.Y * unitY) / (centerLength * commonLength) : 0;
        evidence = new GradientEvidence(centerLength / (magnitudeTotal / count),
            commonLength / count, alignment);
        return true;
    }

    private static bool HasOpposedCommonGradient(Color[] original, int width, int height,
        int x, int y, Options options)
    {
        Gradient center = GradientAt(original, width, height, x, y, -1, -1);
        double centerLength = center.Length;
        if (centerLength <= 1e-9 || centerLength < options.CommonMinimumMagnitude) return false;

        Color color = original[y * width + x];
        var centerLab = Lab(color);
        double toleranceSquared = options.CommonColorTolerance * options.CommonColorTolerance;
        double commonX = 0, commonY = 0, unitX = 0, unitY = 0, magnitudeTotal = 0;
        int count = 0, radius = options.CommonGradientRadius;
        for (int py = Math.Max(1, y - radius); py <= Math.Min(height - 2, y + radius); py++)
            for (int px = Math.Max(1, x - radius); px <= Math.Min(width - 2, x + radius); px++)
            {
                if (px == x && py == y) continue;
                int index = py * width + px;
                Color peer = original[index];
                if (peer.A != color.A || DistanceSquared(centerLab, Lab(peer)) > toleranceSquared) continue;
                Gradient other = GradientAt(original, width, height, px, py, x, y);
                double length = other.Length;
                if (length <= 1e-9) continue;
                commonX += other.X;
                commonY += other.Y;
                unitX += other.X / length;
                unitY += other.Y / length;
                magnitudeTotal += length;
                count++;
            }

        if (count < options.CommonMinimumSimilarNeighbors ||
            centerLength < magnitudeTotal / count * options.CommonMinimumMagnitudeRatio) return false;
        double commonLength = Math.Sqrt(commonX * commonX + commonY * commonY);
        if (commonLength <= 1e-9 ||
            Math.Sqrt(unitX * unitX + unitY * unitY) / count < options.CommonMinimumAgreement) return false;
        double opposition = -(center.X * commonX + center.Y * commonY) / (centerLength * commonLength);
        return opposition >= options.CommonMinimumOpposition;
    }

    private static bool HasOpposedReplacementGradient(Color[] original, int width, int height,
        int x, int y, Color replacement, int radius, double colorTolerance,
        int minimumNeighbors, double minimumOpposition)
    {
        Gradient center = GradientAt(original, width, height, x, y, -1, -1);
        double centerLength = center.Length;
        if (centerLength <= 1e-9) return false;

        Color originalColor = original[y * width + x];
        var replacementLab = Lab(replacement);
        double toleranceSquared = colorTolerance * colorTolerance;
        double commonX = 0, commonY = 0;
        int count = 0;
        for (int py = Math.Max(1, y - radius); py <= Math.Min(height - 2, y + radius); py++)
            for (int px = Math.Max(1, x - radius); px <= Math.Min(width - 2, x + radius); px++)
            {
                if (px == x && py == y) continue;
                Color peer = original[py * width + px];
                if (peer.A != originalColor.A ||
                    DistanceSquared(replacementLab, Lab(peer)) > toleranceSquared) continue;
                Gradient other = GradientAt(original, width, height, px, py, x, y);
                if (other.Length <= 1e-9) continue;
                commonX += other.X;
                commonY += other.Y;
                count++;
            }

        double commonLength = Math.Sqrt(commonX * commonX + commonY * commonY);
        if (count < minimumNeighbors || commonLength <= 1e-9) return false;
        double opposition = -(center.X * commonX + center.Y * commonY) /
            (centerLength * commonLength);
        return opposition >= minimumOpposition;
    }

    private static Gradient GradientAt(Color[] original, int width, int height, int x, int y,
        int excludeX, int excludeY)
    {
        Color color = original[y * width + x];
        var lab = Lab(color);
        double gx = 0, gy = 0, wx = 0, wy = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || x + dx == excludeX && y + dy == excludeY) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                Color other = original[ny * width + nx];
                if (other.A != color.A) continue;
                double weight = 1.0 / (dx * dx + dy * dy);
                double difference = DistanceSquared(lab, Lab(other));
                gx += dx * weight * difference;
                gy += dy * weight * difference;
                wx += Math.Abs(dx) * weight;
                wy += Math.Abs(dy) * weight;
            }
        return new Gradient(wx > 0 ? gx / wx : 0, wy > 0 ? gy / wy : 0);
    }

    private static Color BrightRepresentative(List<NeighborColor> colors, double tolerance)
    {
        int selected = 0;
        double best = double.NegativeInfinity;
        for (int q = 0; q < colors.Count; q++)
        {
            var candidate = Lab(colors[q].Color);
            double support = 0;
            foreach (NeighborColor color in colors)
            {
                var lab = Lab(color.Color);
                double distance = Math.Sqrt((candidate.L - lab.L) * (candidate.L - lab.L) +
                    (candidate.A - lab.A) * (candidate.A - lab.A) +
                    (candidate.B - lab.B) * (candidate.B - lab.B));
                support += color.Weight * Math.Max(0, 1 - distance / Math.Max(tolerance, 1));
            }
            if (support > best) { best = support; selected = q; }
        }
        return colors[selected].Color;
    }

    private static double MaximumColorSpreadSquared(List<NeighborColor> colors)
    {
        double maximum = 0;
        for (int q = 0; q < colors.Count; q++)
            for (int e = q + 1; e < colors.Count; e++)
                maximum = Math.Max(maximum, DistanceSquared(Lab(colors[q].Color), Lab(colors[e].Color)));
        return maximum;
    }

    private static int DarkRingComponents(bool[] dark, int width, int x, int y)
    {
        Span<bool> present = stackalloc bool[9], seen = stackalloc bool[9];
        present.Clear();
        seen.Clear();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0)
                    present[(dy + 1) * 3 + dx + 1] = dark[(y + dy) * width + x + dx];
        int components = 0;
        Span<int> queue = stackalloc int[8];
        for (int q = 0; q < 9; q++)
        {
            if (!present[q] || seen[q]) continue;
            components++;
            int head = 0, count = 0;
            queue[count++] = q; seen[q] = true;
            while (head < count)
            {
                int pixel = queue[head++], py = pixel / 3, px = pixel % 3;
                for (int e = Math.Max(0, py - 1); e <= Math.Min(2, py + 1); e++)
                    for (int f = Math.Max(0, px - 1); f <= Math.Min(2, px + 1); f++)
                    {
                        int neighbor = e * 3 + f;
                        if (neighbor == 4 || !present[neighbor] || seen[neighbor]) continue;
                        seen[neighbor] = true;
                        queue[count++] = neighbor;
                    }
            }
        }
        return components;
    }

    private static (double L, double A, double B) Lab(Color color)
        => ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF);

    private static double DistanceSquared((double L, double A, double B) a, (double L, double A, double B) b)
    {
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
    }
}
