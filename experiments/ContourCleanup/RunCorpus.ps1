param(
    [string]$CliDll = (Join-Path $PSScriptRoot '..\..\Pixelizator.Cli\bin\Release\net8.0-windows\tessera.dll'),
    [string]$CorpusRoot = (Join-Path $PSScriptRoot 'corpus'),
    [double]$MinGain = 30,
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
    [double]$SmallPatchComponent = 5,
    [int]$SmallPatchBright = 10,
    [double]$SmallPatchRatio = 3,
    [switch]$NoDiagonalBridge,
    [double]$DiagonalBridgeConsensus = 90,
    [int]$Passes = 1
)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $CliDll).Path
$corpus = (Resolve-Path -LiteralPath $CorpusRoot).Path
$report = Join-Path $corpus 'cli-results.jsonl'
[System.IO.File]::WriteAllText($report, '')
$bridgeFlag = if ($NoDiagonalBridge) { @('--no-diagonal-bridge') } else { @() }

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
            --small-patch-component $SmallPatchComponent --small-patch-bright $SmallPatchBright `
            --small-patch-ratio $SmallPatchRatio --diagonal-bridge-consensus $DiagonalBridgeConsensus `
            $bridgeFlag --passes $Passes
        if ($LASTEXITCODE -ne 0) { throw "Tessera failed on $($file.FullName): $line" }
        [System.IO.File]::AppendAllText($report, $line + [Environment]::NewLine)
        Write-Output "$kind $($file.Name)"
    }
}

Write-Output "Completed 100 Tessera runs; report: $report"
