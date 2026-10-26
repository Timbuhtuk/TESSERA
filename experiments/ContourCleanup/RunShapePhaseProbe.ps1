param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot 'artifacts/fimoz-user-samples/pipeline-smooth-2x4'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts/shape-research/phase-probe'),
    [int]$CellSize = 8,
    [int[]]$Offsets = @(0, 4)
)

Add-Type -AssemblyName System.Drawing
$cli = Join-Path $PSScriptRoot '../../Pixelizator.Cli/bin/Release/net8.0-windows/tessera.dll'
if (!(Test-Path -LiteralPath $cli)) { throw "Сначала соберите Pixelizator.Cli в Release: $cli" }

foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Filter '*-smooth.png') {
    $stem = $file.BaseName -replace '-smooth$', ''
    foreach ($dy in $Offsets) {
        foreach ($dx in $Offsets) {
            if ($dx -lt 0 -or $dy -lt 0 -or $dx -ge $CellSize -or $dy -ge $CellSize) {
                throw 'Смещение должно быть от 0 до CellSize - 1.'
            }

            $phaseRoot = Join-Path $OutputRoot ('phase-{0:D2}-{1:D2}' -f $dx, $dy)
            New-Item -ItemType Directory -Path $phaseRoot -Force | Out-Null
            $shiftedPath = Join-Path $phaseRoot ($stem + '-shifted.png')
            $alignedPath = Join-Path $phaseRoot ($stem + '-aligned.png')
            $reducedPath = Join-Path $phaseRoot ($stem + '-reduced.png')

            $source = [System.Drawing.Bitmap]::new($file.FullName)
            $shifted = [System.Drawing.Bitmap]::new($source.Width, $source.Height,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($shifted)
            try {
                $graphics.Clear($source.GetPixel(0, 0))
                $graphics.DrawImageUnscaled($source, $dx, $dy)
                $shifted.Save($shiftedPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $graphics.Dispose()
                $shifted.Dispose()
                $source.Dispose()
            }

            & dotnet $cli align $shiftedPath --cell-size $CellSize -o $alignedPath
            if ($LASTEXITCODE -ne 0) { throw "Не удалось выровнять $shiftedPath" }
            & dotnet $cli reduce-grid $alignedPath --cell-size $CellSize -o $reducedPath
            if ($LASTEXITCODE -ne 0) { throw "Не удалось сжать $alignedPath" }
        }
    }
}
