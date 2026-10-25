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
            $bridgeFlag $straightFlag $mismatchFlag $diagonalFlag $oppositionFlag --passes $Passes
        if ($LASTEXITCODE -ne 0) { throw "Tessera failed on $($file.FullName): $line" }
        [System.IO.File]::AppendAllText($report, $line + [Environment]::NewLine)
        Write-Output "$kind $($file.Name)"
    }
}

Write-Output "Completed 100 Tessera runs; report: $report"
