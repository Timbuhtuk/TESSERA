param(
    [switch]$SkipChecks,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$output = Join-Path $artifactsRoot 'standalone'
$cliOutput = Join-Path $artifactsRoot 'cli'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: exit $LASTEXITCODE" }
}

Push-Location -LiteralPath $projectRoot
try {
    Invoke-Dotnet build Pixelizator.sln -c Release "-p:Version=$Version" --nologo
    if (!$SkipChecks) {
        Invoke-Dotnet run --project PixelArtAseprite.Tests -c Release --no-build
        Invoke-Dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --ui --invariants
        Invoke-Dotnet run --project Pixelizator.Cli.Tests -c Release --no-build
    }
    Invoke-Dotnet publish DomainColorTest -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" "-p:PublishDir=$output/" --nologo
    Invoke-Dotnet publish Pixelizator.Cli -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" "-p:PublishDir=$cliOutput/" --nologo
    $guide = @'
TESSERA / WINDOWS x64

Run Tessera.exe. No .NET installation or internet connection is required.
You can move the executable to any folder.

Tessera prepares pixel art and icons. It began as a way to correct uneven
pixel grids and inconsistent pixel sizes in AI-generated images.
Its name refers to an individual piece of a mosaic.

Open images from File > Open, with Ctrl+O or drag and drop.
The home screen keeps your source library and result histories.
The editor can change size and colors separately or together, with crisp
pixel enlargement. It also aligns grids and reduces cells to one pixel.
The toolbar opens Presets, Size, Palette, Smoothing, Grid and Info.
On narrow windows, find them in the Processing menu. Select Source or a
saved result as the input. Info shows dimensions, color counts and palette
colors as a clickable list. Replacement opens with that color selected.
Choose any new color with the hue strip and color field, an image swatch,
or HEX. The original stays intact.
Zoom and preview background affect viewing only.
File > Save exports one result; Save all results exports the current history.

The centred animation banner opens Aseprite conversion. Drop multiple files
to prepare sprite sheets automatically; save one result or the entire batch
as PNG + JSON. Existing output files receive distinct names.

The icon banner opens ICO creation: choose an image and several sizes,
inspect the previews, then save them together in one icon file.
You can also use a source or result from the editor. Optional solid-color
background removal supports color selection and adjustable tolerance.
The separate background workspace saves full-size transparent PNG files.
Hover over From editor to choose any library source or saved result.
It does not segment objects from complex photographic backgrounds.
The application interface currently uses Russian labels.

Your library is at %LOCALAPPDATA%\Tessera\Library.
On first launch, existing Pixelizator images and histories are copied there.
The old library remains as a backup. Copy the Tessera folder separately
when moving your library to another computer.

Tessera checks GitHub Releases for updates at startup. Use the bottom-bar button
to check manually and install a newer version. Your library remains in place.
Updates: https://github.com/Timbuhtuk/TESSERA/releases/latest
'@
    [IO.File]::WriteAllText((Join-Path $output 'README.txt'), $guide, [Text.UTF8Encoding]::new($true))
    $archive = Join-Path $artifactsRoot 'Tessera-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $output 'Tessera.exe'),(Join-Path $output 'README.txt') -DestinationPath $archive -Force
    $cliArchive = Join-Path $artifactsRoot 'Tessera-cli-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $cliOutput 'tessera.exe') -DestinationPath $cliArchive -Force
    $hash = Get-FileHash -LiteralPath (Join-Path $output 'Tessera.exe') -Algorithm SHA256
    [IO.File]::WriteAllText((Join-Path $artifactsRoot 'Tessera-win-x64.sha256'), "$($hash.Hash)  Tessera.exe`n")
    $checksums = foreach ($file in @((Join-Path $output 'Tessera.exe'), $archive, $cliArchive)) {
        $checksum = Get-FileHash -LiteralPath $file -Algorithm SHA256
        "$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
    }
    [IO.File]::WriteAllText((Join-Path $artifactsRoot 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n")
    Write-Output "EXE: $(Join-Path $output 'Tessera.exe')"
    Write-Output "CLI: $(Join-Path $cliOutput 'tessera.exe')"
    Write-Output "ZIP: $archive"
    Write-Output "CLI ZIP: $cliArchive"
}
finally { Pop-Location }
