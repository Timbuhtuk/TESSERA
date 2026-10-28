param(
    [string]$CliDll = (Join-Path $PSScriptRoot '..\..\Pixelizator.Cli\bin\Release\net8.0-windows\tessera.dll'),
    [string]$CorpusRoot = (Join-Path $PSScriptRoot 'corpus'),
    [double]$DarkLightness = 5,
    [double]$MinGain = 20,
    [double]$Consensus = 60,
    [int]$AlphaClearance = 1,
    [int]$MaxCardinalDark = 2,
    [int]$TwoCardinalDark5 = 11,
    [double]$TwoCardinalComponent = 40,
    [int]$TwoCardinalBrightPattern = 0,
    [double]$ThreeNeighborComponent = 10,
    [int]$ThreeNeighborDark5 = 9,
    [int]$ProtectedContinuation = 3,
    [double]$ProtectedComponent = 20,
    [double]$ProtectedMinimum = 15,
    [double]$RepeatSimilarity = 80,
    [int]$RepeatedPatchRadius = 3,
    [int]$RepeatedPatchDifferences = 2,
    [int]$LargePatchRadius = 3,
    [int]$LargePatchBright = 2,
    [double]$LargePatchRatio = 1,
    [int]$SmallPatchRadius = 1,
    [double]$SmallPatchComponent = 6,
    [int]$SmallPatchMaxCardinal = 1,
    [int]$SmallPatchBright = 10,
    [double]$SmallPatchRatio = 3,
    [switch]$NoDiagonalBridge,
    [double]$DiagonalBridgeConsensus = 90,
    [switch]$NoStraightRun,
    [int]$MinimumStraightRun = 8,
    [int]$StraightRunRadius = 15,
    [int]$StraightRunDark5 = 9,
    [double]$StraightRunConsensus = 50,
    [int]$IsolatedPatchRadius = 2,
    [int]$IsolatedPatchBright = 3,
    [double]$IsolatedPatchRatio = 2,
    [double]$IsolatedPatchConsensus = 50,
    [switch]$NoRepeatedMismatch,
    [switch]$NoDiagonalGradient,
    [double]$DiagonalColorTolerance = 8,
    [int]$DiagonalBright = 7,
    [double]$DiagonalBrightRatio = 2,
    [double]$DiagonalGradientRatio = 1.22,
    [int]$DiagonalGradientRadius = 3,
    [double]$DiagonalSimilarColor = 24,
    [int]$DiagonalSimilarNeighbors = 5,
    [switch]$NoDiagonalOpposition,
    [int]$DiagonalOppositionRadius = 5,
    [double]$DiagonalMinimumOpposition = 80,
    [double]$DiagonalMinimumAgreement = 20,
    [switch]$NoTaperedRun,
    [int]$TaperedDark5Min = 7,
    [int]$TaperedDark5Max = 10,
    [int]$TaperedRadius = 20,
    [int]$TaperedInsideLength = 8,
    [int]$TaperedMinShrink = 6,
    [int]$TaperedBright = 4,
    [double]$TaperedBrightRatio = 1.5,
    [switch]$NoSparsePatch,
    [int]$SparsePatchMaxDark5 = 5,
    [int]$SparsePatchMaxCardinal = 1,
    [int]$SparsePatchBright = 20,
    [double]$SparsePatchRatio = 5,
    [switch]$NoSecondarySparse,
    [int]$SecondarySparseDark5 = 8,
    [double]$SecondarySparseComponent = 50,
    [switch]$NoSoftColorConsensus,
    [double]$SoftConsensus = 50,
    [double]$SoftColorSpread = 40,
    [int]$SoftMaxDarkNeighbors = 2,
    [int]$SoftMaxDark5 = 6,
    [switch]$NoCoherentGradient,
    [int]$CoherentDark5 = 10,
    [int]$CoherentRadius = 5,
    [double]$CoherentColorTolerance = 24,
    [int]$CoherentMinimumNeighbors = 10,
    [double]$CoherentMagnitudeRatio = 1.2,
    [double]$CoherentMinimumAgreement = 90,
    [double]$CoherentMinimumAlignment = 90,
    [switch]$NoDivergentGradient,
    [int]$DivergentDark5 = 7,
    [int]$DivergentRadius = 5,
    [double]$DivergentColorTolerance = 24,
    [int]$DivergentMinimumNeighbors = 10,
    [double]$DivergentMagnitudeRatio = 1.4,
    [double]$DivergentMaximumAgreement = 15,
    [switch]$NoPatchGradient,
    [int]$PatchGradientDark5Minimum = 8,
    [int]$PatchGradientDark5Maximum = 10,
    [int]$PatchGradientBright = 6,
    [double]$PatchGradientBrightRatio = 3,
    [int]$PatchGradientRadius = 5,
    [double]$PatchGradientColorTolerance = 24,
    [int]$PatchGradientMinimumNeighbors = 10,
    [double]$PatchGradientMagnitudeMinimum = 1.18,
    [double]$PatchGradientMagnitudeMaximum = 1.4,
    [double]$PatchGradientMaximumAgreement = 50,
    [switch]$NoMainSparse,
    [int]$MainSparseDark5 = 6,
    [int]$MainSparseBright = 8,
    [double]$MainSparseBrightRatio = 5,
    [int]$MainSparseGradientRadius = 5,
    [double]$MainSparseColorTolerance = 24,
    [int]$MainSparseMinimumNeighbors = 10,
    [double]$MainSparseMaximumMagnitude = 1,
    [switch]$NoBrightCorner,
    [int]$BrightCornerMaximumDark = 2,
    [int]$BrightCornerMaximumCardinal = 1,
    [int]$BrightCornerDark5 = 6,
    [double]$BrightCornerComponent = 50,
    [int]$BrightCornerRun = 3,
    [double]$BrightCornerColorTolerance = 8,
    [switch]$NoVerticalShade,
    [int]$VerticalMediumDark5 = 10,
    [double]$VerticalMediumComponentMinimum = 25,
    [double]$VerticalMediumComponentMaximum = 50,
    [double]$VerticalMediumColorSpread = 30,
    [int]$VerticalWideDark5 = 11,
    [double]$VerticalWideComponentMinimum = 50,
    [double]$VerticalWideColorSpreadMinimum = 50,
    [double]$VerticalWideColorSpreadMaximum = 80,
    [int]$VerticalRunRadius = 4,
    [double]$VerticalColorTolerance = 8,
    [int]$ConflictRadius = 2,
    [switch]$RequireCommonGradient,
    [int]$CommonGradientRadius = 5,
    [double]$CommonColorTolerance = 24,
    [int]$CommonMinimumNeighbors = 5,
    [double]$CommonMinimumMagnitude = 0,
    [double]$CommonMagnitudeRatio = 1.2,
    [double]$CommonMinimumAgreement = 20,
    [double]$CommonMinimumOpposition = 80,
    [switch]$RequireColorPatch,
    [switch]$NoColorPatch,
    [double]$ColorPatchTolerance = 15,
    [int]$ColorPatchMinimumBright = 1,
    [double]$ColorPatchRatio = 3,
    [int]$ColorPatchMaxDifferences = 0,
    [switch]$ColorPatchEnergyRescue,
    [double]$ColorPatchEnergyDrop = 80,
    [switch]$ThreeNeighborEnergyRescue,
    [switch]$NoThreeNeighborEnergyRescue,
    [double]$ThreeNeighborEnergyDrop = 40,
    [int]$ThreeNeighborDistinctColors = 4,
    [double]$ThreeNeighborFlatBright = 2.1,
    [double]$ThreeNeighborTexturedBright = 0,
    [switch]$TexturedGradientRescue,
    [switch]$NoTexturedGradientRescue,
    [int]$TexturedGradientColors = 4,
    [double]$TexturedGradientEnergy = 58,
    [double]$TexturedGradientBright = 0.1,
    [int]$TexturedGradientRadius = 3,
    [double]$TexturedGradientTolerance = 24,
    [int]$TexturedGradientNeighbors = 5,
    [double]$TexturedGradientOpposition = 20,
    [switch]$SingleDiagonalGradientRescue,
    [switch]$NoSingleDiagonalGradientRescue,
    [int]$SingleDiagonalColors = 4,
    [double]$SingleDiagonalLightness = 2.5,
    [double]$SingleDiagonalEnergy = 10,
    [double]$SingleDiagonalBright = 0.01,
    [int]$SingleDiagonalRadius = 3,
    [double]$SingleDiagonalTolerance = 24,
    [int]$SingleDiagonalNeighbors = 5,
    [double]$SingleDiagonalOpposition = 20,
    [switch]$ThreeGradientRescue,
    [switch]$NoThreeGradientRescue,
    [int]$ThreeGradientColors = 4,
    [double]$ThreeGradientLightness = 2.5,
    [double]$ThreeGradientEnergy = 15,
    [double]$ThreeGradientBright = 0.1,
    [int]$ThreeGradientRadius = 3,
    [double]$ThreeGradientTolerance = 24,
    [int]$ThreeGradientNeighbors = 5,
    [double]$ThreeGradientOpposition = 50,
    [switch]$ColorPatchWeightedRescue,
    [switch]$NoColorPatchWeightedRescue,
    [int]$WeightedPatchDifferences = 2,
    [double]$WeightedPatchSigma = 5,
    [double]$WeightedPatchMinimumBright = 1,
    [double]$WeightedPatchRatio = 5,
    [switch]$ExtendedWeightedPatch,
    [switch]$NoExtendedWeightedPatch,
    [double]$ExtendedPatchSigma = 6,
    [double]$ExtendedPatchBright = 1,
    [double]$ExtendedPatchRatio = 5,
    [int]$ExtendedPatchColors = 4,
    [double]$ExtendedPatchLightness = 2.5,
    [int]$ExtendedPatchCardinal = 1,
    [double]$ExtendedPatchEnergy = 40,
    [int]$ExtendedPatchRadius = 3,
    [double]$ExtendedPatchTolerance = 24,
    [int]$ExtendedPatchNeighbors = 5,
    [double]$ExtendedPatchOpposition = 50,
    [switch]$SymmetricPatchRescue,
    [switch]$NoSymmetricPatchRescue,
    [switch]$SymmetricPatchFirstPass,
    [switch]$SymmetricPatchAllPasses,
    [int]$SymmetricPatchColors = 4,
    [double]$SymmetricPatchLightness = 2.5,
    [int]$SymmetricPatchCardinal = 1,
    [double]$SymmetricPatchSigma = 5,
    [double]$SymmetricPatchBright = 0.5,
    [double]$SymmetricPatchDark = 0.05,
    [double]$SymmetricPatchRatio = 5,
    [double]$SymmetricPatchEnergy = 60,
    [int]$SymmetricPatchRadius = 3,
    [double]$SymmetricPatchTolerance = 24,
    [int]$SymmetricPatchNeighbors = 5,
    [double]$SymmetricPatchOpposition = 50,
    [switch]$HighContrastRescue = $true,
    [switch]$NoHighContrastRescue,
    [int]$HighContrastMaxDark = 2,
    [double]$HighContrastGain = 45,
    [double]$HighContrastEnergy = 50,
    [int]$HighContrastRadius = 3,
    [double]$HighContrastTolerance = 24,
    [int]$HighContrastNeighbors = 5,
    [double]$HighContrastOpposition = 50,
    [switch]$ContrastConsensus = $true,
    [switch]$NoContrastConsensus,
    [double]$ContrastConsensusMin = 40,
    [double]$ContrastConsensusGain = 60,
    [int]$ContrastConsensusMaxDark = 3,
    [switch]$RepeatedDarkRescue = $true,
    [switch]$NoRepeatedDarkRescue,
    [switch]$RepeatedDarkAllPasses,
    [int]$RepeatedDarkProtectRadius = 1,
    [int]$RepeatedDarkMin = 4,
    [int]$RepeatedDarkMax = 8,
    [int]$RepeatedDarkNeighborsMin = 1,
    [int]$RepeatedDarkNeighborsMax = 2,
    [double]$RepeatedDarkLightness = 2.5,
    [double]$RepeatedDarkGain = 40,
    [double]$RepeatedDarkEnergy = 50,
    [switch]$RepeatedBridgeRescue = $true,
    [switch]$NoRepeatedBridgeRescue,
    [double]$RepeatedBridgeGain = 60,
    [double]$RepeatedBridgeEnergy = 45,
    [int]$RepeatedBridgeRadius = 3,
    [double]$RepeatedBridgeTolerance = 24,
    [int]$RepeatedBridgeNeighbors = 5,
    [double]$RepeatedBridgeOpposition = 70,
    [switch]$SparseDarkPaletteRescue = $true,
    [switch]$NoSparseDarkPaletteRescue,
    [double]$SparseDarkPaletteTolerance = 0.5,
    [int]$SparseDarkPaletteMax = 5,
    [double]$SparseDarkPaletteGain = 20,
    [double]$SparseDarkPaletteEnergy = 0,
    [switch]$SparseDarkPaletteExtended = $true,
    [switch]$NoSparseDarkPaletteExtended,
    [int]$SparseDarkPaletteExtendedMax = 10,
    [double]$SparseDarkPaletteExtendedConsensus = 50,
    [double]$SparseDarkPaletteExtendedTolerance = 0.5,
    [double]$SparseDarkPaletteExtendedGain = 20,
    [double]$SparseDarkPaletteExtendedEnergy = 0,
    [switch]$SparsePaletteExclusive = $true,
    [switch]$NoSparsePaletteExclusive,
    [double]$SparsePaletteExclusiveTolerance = 0.5,
    [int]$SparsePaletteExclusiveMax = 2,
    [double]$SparsePaletteExclusiveGain = 20,
    [switch]$ExemplarRescue = $true,
    [switch]$NoExemplarRescue,
    [int]$ExemplarNearDistance = 4,
    [int]$ExemplarNearMax = 40,
    [int]$ExemplarEarlyNearMax = 0,
    [double]$ExemplarRatio = 20,
    [int]$ExemplarBrightChannel = 25,
    [int]$ExemplarDarkChannel = 15,
    [int]$ExemplarExclusion = 3,
    [switch]$NoExemplarMaterialVeto,
    [int]$ExemplarMaterialError = 20000,
    [double]$ExemplarMaterialSource = 100,
    [double]$ExemplarMaterialBright = 40,
    [double]$ExemplarMaterialDrop = 20,
    [double]$ExemplarMaterialAgreementDistance = 20,
    [int]$ExemplarMaterialAgreement = 2,
    [int]$ExemplarMaterialNeighbors = 10,
    [switch]$NoLocalMaterialHueVeto,
    [double]$LocalMaterialChroma = 2,
    [double]$LocalMaterialNeighborChroma = 4,
    [double]$LocalMaterialNeighborLightness = 12,
    [double]$LocalMaterialHueAgreement = 0.85,
    [int]$LocalMaterialNeighbors = 3,
    [switch]$GroupedPaletteRescue = $true,
    [switch]$NoGroupedPaletteRescue,
    [int]$GroupedPaletteChannel = 10,
    [int]$GroupedPaletteMin = 4,
    [int]$GroupedPaletteMax = 8,
    [int]$GroupedPaletteNearDistance = 4,
    [int]$GroupedPaletteNearMax = 160,
    [int]$GroupedPaletteHighNear = 40,
    [int]$GroupedPalettePriorNear = 80,
    [int]$GroupedPalettePriorMin = 2,
    [int]$GroupedPaletteSupport = 4,
    [int]$GroupedPaletteBright = 5,
    [int]$GroupedPaletteDark = 3,
    [double]$GroupedPaletteAverage = 5,
    [double]$GroupedPaletteHighAverage = 4,
    [double]$GroupedPaletteSpacing = 8,
    [double]$GroupedPaletteRatio = 1.5,
    [double]$GroupedPaletteHighRatio = 1,
    [double]$GroupedPaletteGain = 20,
    [switch]$NoConfirmedGroupExemplar,
    [int]$ConfirmedGroupPrior = 3,
    [double]$ConfirmedGroupRatio = 0.4,
    [double]$ConfirmedGroupGain = 20,
    [int]$ConfirmedGroupDirectError = 5000,
    [int]$ConfirmedGroupFlatColors = 256,
    [int]$ConfirmedGroupProtectRadius = 1,
    [switch]$NoConfirmedGroupCardinal,
    [switch]$ConfirmedGroupCardinalMaterialVeto,
    [int]$ConfirmedGroupCardinalChannel = 40,
    [int]$ConfirmedGroupCardinalDark = 1,
    [int]$ConfirmedGroupCardinalExtraDark = 2,
    [double]$ConfirmedGroupCardinalExtraRatio = 0.1,
    [int]$ConfirmedGroupCardinalExtraError = 5000,
    [int]$ConfirmedGroupCardinalBright = 3,
    [int]$ConfirmedGroupCardinalStrong = 2,
    [int]$ConfirmedGroupCardinalExemplarError = 10000,
    [double]$ConfirmedGroupCardinalColorDistance = 40,
    [switch]$NoConfirmedGroupGradient,
    [int]$ConfirmedGroupGradientPrior = 1,
    [int]$ConfirmedGroupGradientRadius = 5,
    [double]$ConfirmedGroupGradientColor = 24,
    [int]$ConfirmedGroupGradientNeighbors = 5,
    [double]$ConfirmedGroupGradientRatio = 1.05,
    [double]$ConfirmedGroupGradientAgreement = 0.08,
    [double]$ConfirmedGroupGradientOpposition = 0.9,
    [int]$ConfirmedGroupGradientError = 50000,
    [int]$ConfirmedGroupGradientCloseRadius = 3,
    [double]$ConfirmedGroupGradientCloseRatio = 1.1,
    [double]$ConfirmedGroupGradientRayColor = 15,
    [switch]$NoConfirmedGroupSquare,
    [int]$ConfirmedGroupSquarePrior = 1,
    [double]$ConfirmedGroupSquareColor = 15,
    [double]$ConfirmedGroupSquareGain = 20,
    [switch]$NoConfirmedGroupLateCardinal,
    [int]$ConfirmedGroupLatePrior = 3,
    [double]$ConfirmedGroupLateRatio = 0.2,
    [int]$ConfirmedGroupLateError = 25000,
    [switch]$NoRichPaletteMaterialVeto,
    [int]$RichPaletteMinColors = 513,
    [int]$RichPaletteDarkChannel = 20,
    [double]$RichPaletteNeighborColor = 5,
    [int]$RichPaletteSuspiciousMin = 4,
    [int]$RichPaletteSuspiciousMax = 8,
    [switch]$NoChromaticExemplar,
    [double]$ChromaticExemplarNeutral = 5,
    [double]$ChromaticExemplarColor = 20,
    [int]$ChromaticExemplarError = 50000,
    [double]$ChromaticExemplarRatio = 0.4,
    [switch]$NoNeutralExemplar,
    [double]$NeutralExemplarProposed = 5,
    [double]$NeutralExemplarSample = 5,
    [double]$NeutralExemplarDrop = 40,
    [int]$NeutralExemplarError = 10000,
    [double]$NeutralExemplarRatio = 0.3,
    [switch]$NoNeutralExemplarBlend,
    [int]$NeutralBlendMinColors = 513,
    [double]$NeutralBlendProposed = 5,
    [double]$NeutralBlendSample = 10,
    [double]$NeutralBlendDrop = 40,
    [int]$NeutralBlendError = 50000,
    [double]$NeutralBlendRatio = 1,
    [double]$NeutralBlendWeight = 0.5,
    [switch]$SparsePaletteConsensus = $true,
    [switch]$NoSparsePaletteConsensus,
    [double]$SparsePaletteConsensusTolerance = 0.5,
    [int]$SparsePaletteConsensusMax = 0,
    [double]$SparsePaletteConsensusGain = 20,
    [double]$SparsePaletteConsensusEnergy = 30,
    [switch]$SparsePaletteThreeNeighbor = $true,
    [switch]$NoSparsePaletteThreeNeighbor,
    [double]$SparsePaletteThreeTolerance = 0.5,
    [int]$SparsePaletteThreeMax = 5,
    [double]$SparsePaletteThreeGain = 20,
    [double]$SparsePaletteThreeEnergy = 30,
    [switch]$CandidateAudit,
    [int]$Passes = 1
)

$ErrorActionPreference = 'Stop'
if ($RequireColorPatch -and $NoColorPatch) { throw 'Укажите только один из -RequireColorPatch и -NoColorPatch.' }
if ($ColorPatchWeightedRescue -and $NoColorPatchWeightedRescue) { throw 'Укажите только один из -ColorPatchWeightedRescue и -NoColorPatchWeightedRescue.' }
if ($ThreeNeighborEnergyRescue -and $NoThreeNeighborEnergyRescue) { throw 'Укажите только один из -ThreeNeighborEnergyRescue и -NoThreeNeighborEnergyRescue.' }
if ($TexturedGradientRescue -and $NoTexturedGradientRescue) { throw 'Укажите только один из -TexturedGradientRescue и -NoTexturedGradientRescue.' }
if ($SingleDiagonalGradientRescue -and $NoSingleDiagonalGradientRescue) { throw 'Укажите только один из -SingleDiagonalGradientRescue и -NoSingleDiagonalGradientRescue.' }
if ($ThreeGradientRescue -and $NoThreeGradientRescue) { throw 'Укажите только один из -ThreeGradientRescue и -NoThreeGradientRescue.' }
if ($ExtendedWeightedPatch -and $NoExtendedWeightedPatch) { throw 'Укажите только один из -ExtendedWeightedPatch и -NoExtendedWeightedPatch.' }
if ($SymmetricPatchRescue -and $NoSymmetricPatchRescue) { throw 'Укажите только один из -SymmetricPatchRescue и -NoSymmetricPatchRescue.' }
if ($SymmetricPatchFirstPass -and $SymmetricPatchAllPasses) { throw 'Укажите только один из -SymmetricPatchFirstPass и -SymmetricPatchAllPasses.' }
$cli = (Resolve-Path -LiteralPath $CliDll).Path
$corpus = (Resolve-Path -LiteralPath $CorpusRoot).Path
$report = Join-Path $corpus 'cli-results.jsonl'
[System.IO.File]::WriteAllText($report, '')
$bridgeFlag = if ($NoDiagonalBridge) { @('--no-diagonal-bridge') } else { @() }
$straightFlag = if ($NoStraightRun) { @('--no-straight-run') } else { @() }
$mismatchFlag = if ($NoRepeatedMismatch) { @('--no-repeated-mismatch') } else { @() }
$diagonalFlag = if ($NoDiagonalGradient) { @('--no-diagonal-gradient') } else { @() }
$oppositionFlag = if ($NoDiagonalOpposition) { @('--no-diagonal-opposition') } else { @() }
$taperedFlag = if ($NoTaperedRun) { @('--no-tapered-run') } else { @() }
$sparseFlag = if ($NoSparsePatch) { @('--no-sparse-patch') } else { @() }
$secondarySparseFlag = if ($NoSecondarySparse) { @('--no-secondary-sparse') } else { @() }
$softColorFlag = if ($NoSoftColorConsensus) { @('--no-soft-color-consensus') } else { @() }
$coherentFlag = if ($NoCoherentGradient) { @('--no-coherent-gradient') } else { @() }
$divergentFlag = if ($NoDivergentGradient) { @('--no-divergent-gradient') } else { @() }
$patchGradientFlag = if ($NoPatchGradient) { @('--no-patch-gradient') } else { @() }
$mainSparseFlag = if ($NoMainSparse) { @('--no-main-sparse') } else { @() }
$brightCornerFlag = if ($NoBrightCorner) { @('--no-bright-corner') } else { @() }
$verticalShadeFlag = if ($NoVerticalShade) { @('--no-vertical-shade') } else { @() }
$commonFlag = if ($RequireCommonGradient) { @('--require-common-gradient') } else { @() }
$colorPatchFlag = if ($NoColorPatch) { @('--no-color-patch') } else { @('--require-color-patch') }
$energyRescueFlag = if ($ColorPatchEnergyRescue) { @('--color-patch-energy-rescue') } else { @() }
$threeNeighborEnergyRescueFlag = if ($NoThreeNeighborEnergyRescue) { @('--no-three-neighbor-energy-rescue') } else { @('--three-neighbor-energy-rescue') }
$texturedGradientRescueFlag = if ($NoTexturedGradientRescue) { @('--no-textured-gradient-rescue') } else { @('--textured-gradient-rescue') }
$singleDiagonalFlag = if ($NoSingleDiagonalGradientRescue) { @('--no-single-diagonal-gradient-rescue') } else { @('--single-diagonal-gradient-rescue') }
$threeGradientFlag = if ($NoThreeGradientRescue) { @('--no-three-gradient-rescue') } else { @('--three-gradient-rescue') }
$weightedRescueFlag = if ($NoColorPatchWeightedRescue) { @('--no-color-patch-weighted-rescue') } else { @('--color-patch-weighted-rescue') }
$extendedWeightedFlag = if ($NoExtendedWeightedPatch) { @('--no-extended-weighted-patch') } else { @('--extended-weighted-patch') }
$symmetricPatchFlag = if ($NoSymmetricPatchRescue) { @('--no-symmetric-patch-rescue') } else { @('--symmetric-patch-rescue') }
$symmetricFirstPassFlag = if ($SymmetricPatchAllPasses) { @('--symmetric-patch-all-passes') } else { @('--symmetric-patch-first-pass') }
$highContrastFlag = if ($NoHighContrastRescue -or !$HighContrastRescue) { @('--no-high-contrast-rescue') } else { @('--high-contrast-rescue') }
$contrastConsensusFlag = if ($NoContrastConsensus -or !$ContrastConsensus) { @('--no-contrast-consensus') } else { @('--contrast-consensus') }
$repeatedDarkFlag = if ($NoRepeatedDarkRescue -or !$RepeatedDarkRescue) { @('--no-repeated-dark-rescue') } else { @('--repeated-dark-rescue') }
$repeatedDarkPassFlag = if ($RepeatedDarkAllPasses) { @('--repeated-dark-all-passes') } else { @('--repeated-dark-first-pass') }
$repeatedBridgeFlag = if ($NoRepeatedBridgeRescue -or !$RepeatedBridgeRescue) { @('--no-repeated-bridge-rescue') } else { @('--repeated-bridge-rescue') }
$sparseDarkPaletteFlag = if ($NoSparseDarkPaletteRescue -or !$SparseDarkPaletteRescue) { @('--no-sparse-dark-palette-rescue') } else { @('--sparse-dark-palette-rescue') }
$sparseDarkPaletteExtendedFlag = if ($NoSparseDarkPaletteExtended -or !$SparseDarkPaletteExtended) { @('--no-sparse-dark-palette-extended') } else { @('--sparse-dark-palette-extended') }
$sparsePaletteExclusiveFlag = if ($NoSparsePaletteExclusive -or !$SparsePaletteExclusive) { @('--no-sparse-palette-exclusive') } else { @('--sparse-palette-exclusive') }
$exemplarFlag = if ($NoExemplarRescue -or !$ExemplarRescue) { @('--no-exemplar-rescue') } else { @('--exemplar-rescue') }
$exemplarMaterialFlag = if ($NoExemplarMaterialVeto) { @('--no-exemplar-material-veto') } else { @('--exemplar-material-veto') }
$localMaterialHueFlag = if ($NoLocalMaterialHueVeto) { @('--no-local-material-hue-veto') } else { @('--local-material-hue-veto') }
$groupedPaletteFlag = if ($NoGroupedPaletteRescue -or !$GroupedPaletteRescue) { @('--no-grouped-palette-rescue') } else { @('--grouped-palette-rescue') }
$confirmedGroupFlag = if ($NoConfirmedGroupExemplar) { @('--no-confirmed-group-exemplar') } else { @('--confirmed-group-exemplar') }
$confirmedGroupCardinalFlag = if ($NoConfirmedGroupCardinal) { @('--no-confirmed-group-cardinal') } else { @('--confirmed-group-cardinal') }
$confirmedGroupCardinalMaterialFlag = if ($ConfirmedGroupCardinalMaterialVeto) { @('--confirmed-group-cardinal-material-veto') } else { @('--no-confirmed-group-cardinal-material-veto') }
$confirmedGroupGradientFlag = if ($NoConfirmedGroupGradient) { @('--no-confirmed-group-gradient') } else { @('--confirmed-group-gradient') }
$confirmedGroupSquareFlag = if ($NoConfirmedGroupSquare) { @('--no-confirmed-group-square') } else { @('--confirmed-group-square') }
$confirmedGroupLateFlag = if ($NoConfirmedGroupLateCardinal) { @('--no-confirmed-group-late-cardinal') } else { @('--confirmed-group-late-cardinal') }
$richPaletteFlag = if ($NoRichPaletteMaterialVeto) { @('--no-rich-palette-material-veto') } else { @('--rich-palette-material-veto') }
$chromaticExemplarFlag = if ($NoChromaticExemplar) { @('--no-chromatic-exemplar') } else { @('--chromatic-exemplar') }
$neutralExemplarFlag = if ($NoNeutralExemplar) { @('--no-neutral-exemplar') } else { @('--neutral-exemplar') }
$neutralBlendFlag = if ($NoNeutralExemplarBlend) { @('--no-neutral-exemplar-blend') } else { @('--neutral-exemplar-blend') }
$sparsePaletteConsensusFlag = if ($NoSparsePaletteConsensus -or !$SparsePaletteConsensus) { @('--no-sparse-palette-consensus') } else { @('--sparse-palette-consensus') }
$sparsePaletteThreeFlag = if ($NoSparsePaletteThreeNeighbor -or !$SparsePaletteThreeNeighbor) { @('--no-sparse-palette-three-neighbor') } else { @('--sparse-palette-three-neighbor') }

foreach ($kind in @('clean', 'noisy'))
{
    $inputDir = Join-Path $corpus $kind
    $outputDir = Join-Path $corpus "processed-$kind"
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    $files = Get-ChildItem -LiteralPath $inputDir -File -Filter '*.png' | Sort-Object Name
    foreach ($file in $files)
    {
        $target = Join-Path $outputDir $file.Name
        $auditArgs = if ($CandidateAudit) {
            @('--candidate-audit', (Join-Path $corpus "candidate-audit/$kind/$($file.BaseName).csv"))
        } else { @() }
        $line = & dotnet $cli clean-dark-contours $file.FullName -o $target --json --overwrite `
            --dark-lightness $DarkLightness `
            --min-lightness-gain $MinGain --min-bright-consensus $Consensus `
            --alpha-clearance $AlphaClearance --max-cardinal-dark $MaxCardinalDark `
            --two-cardinal-dark5 $TwoCardinalDark5 --two-cardinal-component $TwoCardinalComponent `
            --two-cardinal-bright-pattern $TwoCardinalBrightPattern `
            --three-neighbor-component $ThreeNeighborComponent --three-neighbor-dark5 $ThreeNeighborDark5 `
            --three-neighbor-continuation $ProtectedContinuation `
            --three-neighbor-protected-component $ProtectedComponent `
            --three-neighbor-protected-min $ProtectedMinimum `
            --repeated-component-similarity $RepeatSimilarity `
            --repeated-patch-radius $RepeatedPatchRadius `
            --repeated-patch-differences $RepeatedPatchDifferences `
            --large-patch-radius $LargePatchRadius --large-patch-bright $LargePatchBright `
            --large-patch-ratio $LargePatchRatio --small-patch-radius $SmallPatchRadius `
            --small-patch-component $SmallPatchComponent `
            --small-patch-max-cardinal $SmallPatchMaxCardinal --small-patch-bright $SmallPatchBright `
            --small-patch-ratio $SmallPatchRatio --diagonal-bridge-consensus $DiagonalBridgeConsensus `
            --min-straight-run $MinimumStraightRun --straight-run-radius $StraightRunRadius `
            --straight-run-dark5 $StraightRunDark5 --straight-run-consensus $StraightRunConsensus `
            --isolated-patch-radius $IsolatedPatchRadius --isolated-patch-bright $IsolatedPatchBright `
            --isolated-patch-ratio $IsolatedPatchRatio `
            --isolated-patch-consensus $IsolatedPatchConsensus `
            --diagonal-color-tolerance $DiagonalColorTolerance --diagonal-bright $DiagonalBright `
            --diagonal-bright-ratio $DiagonalBrightRatio --diagonal-gradient-ratio $DiagonalGradientRatio `
            --diagonal-gradient-radius $DiagonalGradientRadius --diagonal-similar-color $DiagonalSimilarColor `
            --diagonal-similar-neighbors $DiagonalSimilarNeighbors `
            --diagonal-opposition-radius $DiagonalOppositionRadius `
            --diagonal-min-opposition $DiagonalMinimumOpposition `
            --diagonal-min-agreement $DiagonalMinimumAgreement `
            --tapered-dark5-min $TaperedDark5Min --tapered-dark5-max $TaperedDark5Max `
            --tapered-run-radius $TaperedRadius --tapered-inside-length $TaperedInsideLength `
            --tapered-min-shrink $TaperedMinShrink --tapered-bright $TaperedBright `
            --tapered-bright-ratio $TaperedBrightRatio `
            --sparse-patch-max-dark5 $SparsePatchMaxDark5 `
            --sparse-patch-max-cardinal $SparsePatchMaxCardinal `
            --sparse-patch-bright $SparsePatchBright --sparse-patch-ratio $SparsePatchRatio `
            --secondary-sparse-dark5 $SecondarySparseDark5 `
            --secondary-sparse-component $SecondarySparseComponent `
            --soft-consensus $SoftConsensus --soft-color-spread $SoftColorSpread `
            --soft-max-dark-neighbors $SoftMaxDarkNeighbors --soft-max-dark5 $SoftMaxDark5 `
            --coherent-dark5 $CoherentDark5 --coherent-radius $CoherentRadius `
            --coherent-color-tolerance $CoherentColorTolerance `
            --coherent-min-neighbors $CoherentMinimumNeighbors `
            --coherent-magnitude-ratio $CoherentMagnitudeRatio `
            --coherent-min-agreement $CoherentMinimumAgreement `
            --coherent-min-alignment $CoherentMinimumAlignment `
            --divergent-dark5 $DivergentDark5 --divergent-radius $DivergentRadius `
            --divergent-color-tolerance $DivergentColorTolerance `
            --divergent-min-neighbors $DivergentMinimumNeighbors `
            --divergent-magnitude-ratio $DivergentMagnitudeRatio `
            --divergent-max-agreement $DivergentMaximumAgreement `
            --patch-gradient-dark5-min $PatchGradientDark5Minimum `
            --patch-gradient-dark5-max $PatchGradientDark5Maximum `
            --patch-gradient-bright $PatchGradientBright `
            --patch-gradient-ratio $PatchGradientBrightRatio `
            --patch-gradient-radius $PatchGradientRadius `
            --patch-gradient-color-tolerance $PatchGradientColorTolerance `
            --patch-gradient-min-neighbors $PatchGradientMinimumNeighbors `
            --patch-gradient-magnitude-min $PatchGradientMagnitudeMinimum `
            --patch-gradient-magnitude-max $PatchGradientMagnitudeMaximum `
            --patch-gradient-max-agreement $PatchGradientMaximumAgreement `
            --main-sparse-dark5 $MainSparseDark5 --main-sparse-bright $MainSparseBright `
            --main-sparse-ratio $MainSparseBrightRatio `
            --main-sparse-gradient-radius $MainSparseGradientRadius `
            --main-sparse-color-tolerance $MainSparseColorTolerance `
            --main-sparse-min-neighbors $MainSparseMinimumNeighbors `
            --main-sparse-max-magnitude $MainSparseMaximumMagnitude `
            --bright-corner-max-dark $BrightCornerMaximumDark `
            --bright-corner-max-cardinal $BrightCornerMaximumCardinal `
            --bright-corner-dark5 $BrightCornerDark5 `
            --bright-corner-component $BrightCornerComponent `
            --bright-corner-run $BrightCornerRun `
            --bright-corner-color-tolerance $BrightCornerColorTolerance `
            --vertical-medium-dark5 $VerticalMediumDark5 `
            --vertical-medium-component-min $VerticalMediumComponentMinimum `
            --vertical-medium-component-max $VerticalMediumComponentMaximum `
            --vertical-medium-color-spread $VerticalMediumColorSpread `
            --vertical-wide-dark5 $VerticalWideDark5 `
            --vertical-wide-component-min $VerticalWideComponentMinimum `
            --vertical-wide-color-spread-min $VerticalWideColorSpreadMinimum `
            --vertical-wide-color-spread-max $VerticalWideColorSpreadMaximum `
            --vertical-run-radius $VerticalRunRadius `
            --vertical-color-tolerance $VerticalColorTolerance `
            --conflict-radius $ConflictRadius `
            --common-gradient-radius $CommonGradientRadius --common-color-tolerance $CommonColorTolerance `
            --common-min-neighbors $CommonMinimumNeighbors --common-min-magnitude $CommonMinimumMagnitude `
            --common-magnitude-ratio $CommonMagnitudeRatio --common-min-agreement $CommonMinimumAgreement `
            --common-min-opposition $CommonMinimumOpposition `
            --color-patch-tolerance $ColorPatchTolerance --color-patch-min-bright $ColorPatchMinimumBright `
            --color-patch-ratio $ColorPatchRatio --color-patch-max-differences $ColorPatchMaxDifferences `
            --color-patch-energy-drop $ColorPatchEnergyDrop `
            --three-neighbor-energy-drop $ThreeNeighborEnergyDrop `
            --three-neighbor-distinct-colors $ThreeNeighborDistinctColors `
            --three-neighbor-flat-bright $ThreeNeighborFlatBright `
            --three-neighbor-textured-bright $ThreeNeighborTexturedBright `
            --textured-gradient-colors $TexturedGradientColors --textured-gradient-energy $TexturedGradientEnergy `
            --textured-gradient-bright $TexturedGradientBright --textured-gradient-radius $TexturedGradientRadius `
            --textured-gradient-tolerance $TexturedGradientTolerance --textured-gradient-neighbors $TexturedGradientNeighbors `
            --textured-gradient-opposition $TexturedGradientOpposition `
            --single-diagonal-colors $SingleDiagonalColors --single-diagonal-lightness $SingleDiagonalLightness `
            --single-diagonal-energy $SingleDiagonalEnergy `
            --single-diagonal-bright $SingleDiagonalBright --single-diagonal-radius $SingleDiagonalRadius `
            --single-diagonal-tolerance $SingleDiagonalTolerance --single-diagonal-neighbors $SingleDiagonalNeighbors `
            --single-diagonal-opposition $SingleDiagonalOpposition `
            --three-gradient-colors $ThreeGradientColors --three-gradient-lightness $ThreeGradientLightness `
            --three-gradient-energy $ThreeGradientEnergy --three-gradient-bright $ThreeGradientBright `
            --three-gradient-radius $ThreeGradientRadius --three-gradient-tolerance $ThreeGradientTolerance `
            --three-gradient-neighbors $ThreeGradientNeighbors --three-gradient-opposition $ThreeGradientOpposition `
            --weighted-patch-differences $WeightedPatchDifferences --weighted-patch-sigma $WeightedPatchSigma `
            --weighted-patch-min-bright $WeightedPatchMinimumBright --weighted-patch-ratio $WeightedPatchRatio `
            --extended-patch-sigma $ExtendedPatchSigma --extended-patch-bright $ExtendedPatchBright `
            --extended-patch-ratio $ExtendedPatchRatio --extended-patch-colors $ExtendedPatchColors `
            --extended-patch-lightness $ExtendedPatchLightness --extended-patch-cardinal $ExtendedPatchCardinal `
            --extended-patch-energy $ExtendedPatchEnergy --extended-patch-radius $ExtendedPatchRadius `
            --extended-patch-tolerance $ExtendedPatchTolerance --extended-patch-neighbors $ExtendedPatchNeighbors `
            --extended-patch-opposition $ExtendedPatchOpposition `
            --symmetric-patch-colors $SymmetricPatchColors --symmetric-patch-lightness $SymmetricPatchLightness `
            --symmetric-patch-cardinal $SymmetricPatchCardinal --symmetric-patch-sigma $SymmetricPatchSigma `
            --symmetric-patch-bright $SymmetricPatchBright --symmetric-patch-dark $SymmetricPatchDark `
            --symmetric-patch-ratio $SymmetricPatchRatio --symmetric-patch-energy $SymmetricPatchEnergy `
            --symmetric-patch-radius $SymmetricPatchRadius --symmetric-patch-tolerance $SymmetricPatchTolerance `
            --symmetric-patch-neighbors $SymmetricPatchNeighbors --symmetric-patch-opposition $SymmetricPatchOpposition `
            --high-contrast-max-dark $HighContrastMaxDark `
            --high-contrast-gain $HighContrastGain --high-contrast-energy $HighContrastEnergy `
            --high-contrast-radius $HighContrastRadius --high-contrast-tolerance $HighContrastTolerance `
            --high-contrast-neighbors $HighContrastNeighbors --high-contrast-opposition $HighContrastOpposition `
            --contrast-consensus-min $ContrastConsensusMin --contrast-consensus-gain $ContrastConsensusGain `
            --contrast-consensus-max-dark $ContrastConsensusMaxDark `
            --repeated-dark-min $RepeatedDarkMin --repeated-dark-max $RepeatedDarkMax `
            --repeated-dark-neighbors-min $RepeatedDarkNeighborsMin `
            --repeated-dark-neighbors-max $RepeatedDarkNeighborsMax `
            --repeated-dark-lightness $RepeatedDarkLightness `
            --repeated-dark-gain $RepeatedDarkGain --repeated-dark-energy $RepeatedDarkEnergy `
            --repeated-dark-protect-radius $RepeatedDarkProtectRadius `
            --repeated-bridge-gain $RepeatedBridgeGain --repeated-bridge-energy $RepeatedBridgeEnergy `
            --repeated-bridge-radius $RepeatedBridgeRadius --repeated-bridge-tolerance $RepeatedBridgeTolerance `
            --repeated-bridge-neighbors $RepeatedBridgeNeighbors --repeated-bridge-opposition $RepeatedBridgeOpposition `
            --sparse-dark-palette-tolerance $SparseDarkPaletteTolerance --sparse-dark-palette-max $SparseDarkPaletteMax `
            --sparse-dark-palette-gain $SparseDarkPaletteGain --sparse-dark-palette-energy $SparseDarkPaletteEnergy `
            --sparse-dark-palette-extended-max $SparseDarkPaletteExtendedMax `
            --sparse-dark-palette-extended-consensus $SparseDarkPaletteExtendedConsensus `
            --sparse-dark-palette-extended-tolerance $SparseDarkPaletteExtendedTolerance `
            --sparse-dark-palette-extended-gain $SparseDarkPaletteExtendedGain `
            --sparse-dark-palette-extended-energy $SparseDarkPaletteExtendedEnergy `
            --sparse-palette-exclusive-tolerance $SparsePaletteExclusiveTolerance --sparse-palette-exclusive-max $SparsePaletteExclusiveMax `
            --sparse-palette-exclusive-gain $SparsePaletteExclusiveGain `
            --exemplar-near-distance $ExemplarNearDistance --exemplar-near-max $ExemplarNearMax `
            --exemplar-early-near-max $ExemplarEarlyNearMax --exemplar-ratio $ExemplarRatio `
            --exemplar-bright-channel $ExemplarBrightChannel --exemplar-dark-channel $ExemplarDarkChannel `
            --exemplar-exclusion $ExemplarExclusion `
            --exemplar-material-error $ExemplarMaterialError --exemplar-material-source $ExemplarMaterialSource `
            --exemplar-material-bright $ExemplarMaterialBright --exemplar-material-drop $ExemplarMaterialDrop `
            --exemplar-material-agreement-distance $ExemplarMaterialAgreementDistance `
            --exemplar-material-agreement $ExemplarMaterialAgreement --exemplar-material-neighbors $ExemplarMaterialNeighbors `
            --local-material-chroma $LocalMaterialChroma --local-material-neighbor-chroma $LocalMaterialNeighborChroma `
            --local-material-neighbor-lightness $LocalMaterialNeighborLightness `
            --local-material-hue-agreement $LocalMaterialHueAgreement --local-material-neighbors $LocalMaterialNeighbors `
            --grouped-palette-channel $GroupedPaletteChannel --grouped-palette-min $GroupedPaletteMin `
            --grouped-palette-max $GroupedPaletteMax --grouped-palette-near-distance $GroupedPaletteNearDistance `
            --grouped-palette-near-max $GroupedPaletteNearMax --grouped-palette-support $GroupedPaletteSupport `
            --grouped-palette-high-near $GroupedPaletteHighNear --grouped-palette-high-ratio $GroupedPaletteHighRatio `
            --grouped-palette-prior-near $GroupedPalettePriorNear --grouped-palette-prior-min $GroupedPalettePriorMin `
            --grouped-palette-bright $GroupedPaletteBright --grouped-palette-dark $GroupedPaletteDark `
            --grouped-palette-average $GroupedPaletteAverage --grouped-palette-high-average $GroupedPaletteHighAverage `
            --grouped-palette-spacing $GroupedPaletteSpacing `
            --grouped-palette-ratio $GroupedPaletteRatio --grouped-palette-gain $GroupedPaletteGain `
            --confirmed-group-prior $ConfirmedGroupPrior --confirmed-group-ratio $ConfirmedGroupRatio `
            --confirmed-group-gain $ConfirmedGroupGain --confirmed-group-direct-error $ConfirmedGroupDirectError `
            --confirmed-group-flat-colors $ConfirmedGroupFlatColors `
            --confirmed-group-protect-radius $ConfirmedGroupProtectRadius `
            --confirmed-group-cardinal-channel $ConfirmedGroupCardinalChannel `
            --confirmed-group-cardinal-dark $ConfirmedGroupCardinalDark `
            --confirmed-group-cardinal-extra-dark $ConfirmedGroupCardinalExtraDark `
            --confirmed-group-cardinal-extra-ratio $ConfirmedGroupCardinalExtraRatio `
            --confirmed-group-cardinal-extra-error $ConfirmedGroupCardinalExtraError `
            --confirmed-group-cardinal-bright $ConfirmedGroupCardinalBright `
            --confirmed-group-cardinal-strong $ConfirmedGroupCardinalStrong `
            --confirmed-group-cardinal-exemplar-error $ConfirmedGroupCardinalExemplarError `
            --confirmed-group-cardinal-color-distance $ConfirmedGroupCardinalColorDistance `
            --confirmed-group-gradient-prior $ConfirmedGroupGradientPrior `
            --confirmed-group-gradient-radius $ConfirmedGroupGradientRadius `
            --confirmed-group-gradient-color $ConfirmedGroupGradientColor `
            --confirmed-group-gradient-neighbors $ConfirmedGroupGradientNeighbors `
            --confirmed-group-gradient-ratio $ConfirmedGroupGradientRatio `
            --confirmed-group-gradient-agreement $ConfirmedGroupGradientAgreement `
            --confirmed-group-gradient-opposition $ConfirmedGroupGradientOpposition `
            --confirmed-group-gradient-error $ConfirmedGroupGradientError `
            --confirmed-group-gradient-close-radius $ConfirmedGroupGradientCloseRadius `
            --confirmed-group-gradient-close-ratio $ConfirmedGroupGradientCloseRatio `
            --confirmed-group-gradient-ray-color $ConfirmedGroupGradientRayColor `
            --confirmed-group-square-prior $ConfirmedGroupSquarePrior `
            --confirmed-group-square-color $ConfirmedGroupSquareColor `
            --confirmed-group-square-gain $ConfirmedGroupSquareGain `
            --confirmed-group-late-prior $ConfirmedGroupLatePrior `
            --confirmed-group-late-ratio $ConfirmedGroupLateRatio `
            --confirmed-group-late-error $ConfirmedGroupLateError `
            --rich-palette-min-colors $RichPaletteMinColors `
            --rich-palette-dark-channel $RichPaletteDarkChannel `
            --rich-palette-neighbor-color $RichPaletteNeighborColor `
            --rich-palette-suspicious-min $RichPaletteSuspiciousMin `
            --rich-palette-suspicious-max $RichPaletteSuspiciousMax `
            --chromatic-exemplar-neutral $ChromaticExemplarNeutral `
            --chromatic-exemplar-color $ChromaticExemplarColor `
            --chromatic-exemplar-error $ChromaticExemplarError `
            --chromatic-exemplar-ratio $ChromaticExemplarRatio `
            --neutral-exemplar-proposed $NeutralExemplarProposed `
            --neutral-exemplar-sample $NeutralExemplarSample `
            --neutral-exemplar-drop $NeutralExemplarDrop `
            --neutral-exemplar-error $NeutralExemplarError `
            --neutral-exemplar-ratio $NeutralExemplarRatio `
            --neutral-blend-min-colors $NeutralBlendMinColors `
            --neutral-blend-proposed $NeutralBlendProposed `
            --neutral-blend-sample $NeutralBlendSample `
            --neutral-blend-drop $NeutralBlendDrop `
            --neutral-blend-error $NeutralBlendError `
            --neutral-blend-ratio $NeutralBlendRatio `
            --neutral-blend-weight $NeutralBlendWeight `
            --sparse-palette-consensus-tolerance $SparsePaletteConsensusTolerance --sparse-palette-consensus-max $SparsePaletteConsensusMax `
            --sparse-palette-consensus-gain $SparsePaletteConsensusGain --sparse-palette-consensus-energy $SparsePaletteConsensusEnergy `
            --sparse-palette-three-tolerance $SparsePaletteThreeTolerance --sparse-palette-three-max $SparsePaletteThreeMax `
            --sparse-palette-three-gain $SparsePaletteThreeGain --sparse-palette-three-energy $SparsePaletteThreeEnergy `
            $bridgeFlag $straightFlag $mismatchFlag $diagonalFlag $oppositionFlag $taperedFlag $sparseFlag $secondarySparseFlag $softColorFlag $coherentFlag $divergentFlag $patchGradientFlag $mainSparseFlag $brightCornerFlag $verticalShadeFlag $commonFlag $colorPatchFlag $energyRescueFlag $threeNeighborEnergyRescueFlag $weightedRescueFlag $extendedWeightedFlag $symmetricPatchFlag $symmetricFirstPassFlag $texturedGradientRescueFlag $singleDiagonalFlag $threeGradientFlag $highContrastFlag $contrastConsensusFlag $repeatedDarkFlag $repeatedDarkPassFlag $repeatedBridgeFlag $sparseDarkPaletteFlag $sparseDarkPaletteExtendedFlag $sparsePaletteExclusiveFlag $exemplarFlag $exemplarMaterialFlag $localMaterialHueFlag $groupedPaletteFlag $confirmedGroupFlag $confirmedGroupCardinalFlag $confirmedGroupCardinalMaterialFlag $confirmedGroupGradientFlag $confirmedGroupSquareFlag $confirmedGroupLateFlag $richPaletteFlag $chromaticExemplarFlag $neutralExemplarFlag $neutralBlendFlag $sparsePaletteConsensusFlag $sparsePaletteThreeFlag $auditArgs --passes $Passes
        if ($LASTEXITCODE -ne 0) { throw "Tessera failed on $($file.FullName): $line" }
        [System.IO.File]::AppendAllText($report, $line + [Environment]::NewLine)
        Write-Output "$kind $($file.Name)"
    }
}

Write-Output "Completed 100 Tessera runs; report: $report"
