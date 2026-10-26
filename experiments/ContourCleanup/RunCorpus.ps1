param(
    [string]$CliDll = (Join-Path $PSScriptRoot '..\..\Pixelizator.Cli\bin\Release\net8.0-windows\tessera.dll'),
    [string]$CorpusRoot = (Join-Path $PSScriptRoot 'corpus'),
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
    [int]$Passes = 1
)

$ErrorActionPreference = 'Stop'
if ($RequireColorPatch -and $NoColorPatch) { throw 'Укажите только один из -RequireColorPatch и -NoColorPatch.' }
if ($ColorPatchWeightedRescue -and $NoColorPatchWeightedRescue) { throw 'Укажите только один из -ColorPatchWeightedRescue и -NoColorPatchWeightedRescue.' }
if ($ThreeNeighborEnergyRescue -and $NoThreeNeighborEnergyRescue) { throw 'Укажите только один из -ThreeNeighborEnergyRescue и -NoThreeNeighborEnergyRescue.' }
if ($TexturedGradientRescue -and $NoTexturedGradientRescue) { throw 'Укажите только один из -TexturedGradientRescue и -NoTexturedGradientRescue.' }
if ($SingleDiagonalGradientRescue -and $NoSingleDiagonalGradientRescue) { throw 'Укажите только один из -SingleDiagonalGradientRescue и -NoSingleDiagonalGradientRescue.' }
if ($ThreeGradientRescue -and $NoThreeGradientRescue) { throw 'Укажите только один из -ThreeGradientRescue и -NoThreeGradientRescue.' }
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

foreach ($kind in @('clean', 'noisy'))
{
    $inputDir = Join-Path $corpus $kind
    $outputDir = Join-Path $corpus "processed-$kind"
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    $files = Get-ChildItem -LiteralPath $inputDir -File -Filter '*.png' | Sort-Object Name
    foreach ($file in $files)
    {
        $target = Join-Path $outputDir $file.Name
        $line = & dotnet $cli clean-dark-contours $file.FullName -o $target --json --overwrite `
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
            $bridgeFlag $straightFlag $mismatchFlag $diagonalFlag $oppositionFlag $taperedFlag $sparseFlag $secondarySparseFlag $softColorFlag $coherentFlag $divergentFlag $patchGradientFlag $mainSparseFlag $brightCornerFlag $verticalShadeFlag $commonFlag $colorPatchFlag $energyRescueFlag $threeNeighborEnergyRescueFlag $weightedRescueFlag $texturedGradientRescueFlag $singleDiagonalFlag $threeGradientFlag --passes $Passes
        if ($LASTEXITCODE -ne 0) { throw "Tessera failed on $($file.FullName): $line" }
        [System.IO.File]::AppendAllText($report, $line + [Environment]::NewLine)
        Write-Output "$kind $($file.Name)"
    }
}

Write-Output "Completed 100 Tessera runs; report: $report"
