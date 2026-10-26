param(
    [string]$CandidateRoot = (Join-Path $PSScriptRoot '../../../contour-black-lines/experiments/ContourCleanup/artifacts/shape-research/finish-cli'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts/shape-gradient'),
    [string]$Cli = (Join-Path $PSScriptRoot '../../../contour-black-lines/Pixelizator.Cli/bin/Release/net8.0-windows/tessera.dll')
)

Add-Type -AssemblyName System.Drawing
if (!(Test-Path -LiteralPath $Cli)) { throw "Сначала соберите CLI: $Cli" }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$results = @()
foreach ($source in Get-ChildItem -LiteralPath $CandidateRoot -Filter 'T_*_UNP.png') {
    $default = Join-Path $OutputRoot ($source.BaseName + '-gradient-default.png')
    $strict = Join-Path $OutputRoot ($source.BaseName + '-gradient-opposition.png')
    & dotnet $Cli clean-dark-contours $source.FullName -o $default --overwrite
    if ($LASTEXITCODE -ne 0) { throw "Не удалось обработать $($source.Name)" }
    & dotnet $Cli clean-dark-contours $source.FullName -o $strict --require-common-gradient --overwrite
    if ($LASTEXITCODE -ne 0) { throw "Не удалось обработать $($source.Name) с общим градиентом" }

    $original = [System.Drawing.Bitmap]::new($source.FullName)
    try {
        foreach ($variant in @(@('default', $default), @('opposition', $strict))) {
            $image = [System.Drawing.Bitmap]::new($variant[1])
            try {
                if ($image.Size -ne $original.Size) { throw 'Изменён размер изображения.' }
                $changed = 0
                $alphaChanged = 0
                for ($y = 0; $y -lt $image.Height; $y++) {
                    for ($x = 0; $x -lt $image.Width; $x++) {
                        $before = $original.GetPixel($x, $y)
                        $after = $image.GetPixel($x, $y)
                        if ($before.ToArgb() -ne $after.ToArgb()) { $changed++ }
                        if ($before.A -ne $after.A) { $alphaChanged++ }
                    }
                }
                $results += [pscustomobject]@{ file = $source.Name; variant = $variant[0]; changedPixels = $changed; alphaChangedPixels = $alphaChanged; width = $image.Width; height = $image.Height }
            }
            finally { $image.Dispose() }
        }
    }
    finally { $original.Dispose() }
}

$results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputRoot 'metrics.json') -Encoding utf8
$results | Format-Table -AutoSize
