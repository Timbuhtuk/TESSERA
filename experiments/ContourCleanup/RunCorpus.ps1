param(
    [string]$CliDll = (Join-Path $PSScriptRoot '..\..\Pixelizator.Cli\bin\Release\net8.0-windows\tessera.dll'),
    [string]$CorpusRoot = (Join-Path $PSScriptRoot 'corpus'),
    [double]$MinGain = 20,
    [double]$Consensus = 60,
    [int]$AlphaClearance = 1,
    [int]$MaxCardinalDark = 2,
    [int]$TwoCardinalDark5 = 11,
    [double]$TwoCardinalComponent = 40,
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
    [int]$Passes = 1
)

$ErrorActionPreference = 'Stop'
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
            $bridgeFlag $straightFlag $mismatchFlag $diagonalFlag $oppositionFlag $taperedFlag $sparseFlag $secondarySparseFlag $softColorFlag $coherentFlag $divergentFlag $patchGradientFlag $mainSparseFlag $brightCornerFlag $verticalShadeFlag $commonFlag --passes $Passes
        if ($LASTEXITCODE -ne 0) { throw "Tessera failed on $($file.FullName): $line" }
        [System.IO.File]::AppendAllText($report, $line + [Environment]::NewLine)
        Write-Output "$kind $($file.Name)"
    }
}

Write-Output "Completed 100 Tessera runs; report: $report"
