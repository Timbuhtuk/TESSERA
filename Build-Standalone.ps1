param(
    [switch]$SkipChecks,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory = 'artifacts',
    [string]$ReleaseRepository
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$output = Join-Path $artifactsRoot 'standalone'
$cliOutput = Join-Path $artifactsRoot 'cli'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$python = (Get-Command python -ErrorAction Stop).Source
$pluginOutput = Join-Path $artifactsRoot 'chatgpt-plugin'

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: exit $LASTEXITCODE" }
}

Push-Location -LiteralPath $projectRoot
try {
    & (Join-Path $projectRoot 'Prepare-ReleaseNotes.ps1') -Version $Version -OutputDirectory $artifactsRoot -Repository $ReleaseRepository
    $notesFile = Join-Path $artifactsRoot 'ReleaseNotes.json'
    Invoke-Dotnet build Pixelizator.sln -c Release "-p:Version=$Version" "-p:ReleaseNotesFile=$notesFile" --nologo
    if (!$SkipChecks) {
        & (Join-Path $projectRoot 'tests/ReleaseNotes.Checks.ps1')
        Invoke-Dotnet run --project PixelArtAseprite.Tests -c Release --no-build
        Invoke-Dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --release-notes --tool-improvements --navigation --workspace --localization --ui --invariants
        Invoke-Dotnet run --project Pixelizator.Cli.Tests -c Release --no-build
    }
    Invoke-Dotnet publish DomainColorTest -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" "-p:ReleaseNotesFile=$notesFile" "-p:PublishDir=$output/" --nologo
    Invoke-Dotnet publish Pixelizator.Cli -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" "-p:PublishDir=$cliOutput/" --nologo
    $guide = @'
TESSERA / WINDOWS x64

Run Tessera.exe. No .NET installation or internet connection is required.
You can move the executable to any folder.

Tessera prepares pixel art and icons. It began as a way to correct uneven
pixel grids and inconsistent pixel sizes in AI-generated images.
Its name refers to an individual piece of a mosaic.

Open images from File > Open, with Ctrl+O or drag and drop.
The library organizes shared assets in projects and nested folders, with
tags, search, filters and result histories. Right-click projects/folders
for organization commands. The title bar contains File, Image, Tools and
View, with More on narrow windows. Settings selects theme and RU/EN.
The editor can change size and colors separately or together, with crisp
pixel enlargement. It also aligns grids and reduces cells to one pixel.
The interface includes bundled Cascadia Code and Science Gothic fonts.
Size fields accept dimensions above 4K, subject to available memory and codec limits.
Image and Tools open processing settings beside the previews. Select
Source or a saved result as the input. Info shows dimensions, color counts and palette
colors as a clickable list. Replacement opens with that color selected.
Choose any new color with the hue strip and color field, an image swatch,
or HEX. The original stays intact.
Zoom and preview background affect viewing only.
File > Save exports one result; Save all results exports the current history.

Filters offers monochrome conversion and ASCII art. Save ASCII as UTF-8 TXT
or render an opaque black-and-white image into the history. Choose a preset
character set or enter your own. Keep the input size or use one character
per pixel for a larger image. Input and render inversion are independent.

Open Aseprite conversion from Tools. Drop multiple files
to prepare sprite sheets automatically; save one result or the entire batch
as PNG + JSON. Existing output files receive distinct names.
Visible RGBA image layers are composited in order; hidden layers and groups
are excluded. Visible layers require Normal blending and full layer/cel opacity.

Open ICO creation from Tools: choose an image and several sizes,
inspect the previews, then save them together in one icon file.
You can also use a source or result from the editor. Optional solid-color
background removal supports color selection and adjustable tolerance.
The separate background workspace saves full-size transparent PNG files.
Hover over From editor to choose any library source or saved result.
It does not segment objects from complex photographic backgrounds.
Choose Russian or English in Settings; language changes apply on restart.

Image board lets you arrange images on a canvas with a solid background.
Move, rotate, scale and reorder layers individually or as a selection.
Save an editable .tboard project with embedded images, or export a PNG.
Boards can be retained as editable documents in the workspace. Save before closing.

Tile inspector repeats an image on X/Y and shows a seam heatmap. Compare
two equal-size assets in both directions, paint pixels, undo/redo edits,
and smooth the active tile or its borders. Save embedded images and edits
as a .ttiles collection, or export an edited asset as PNG. Open directly
with Tessera.exe --tiles [image-or-collection].
Hold Alt to temporarily show the heatmap while its switch is off; release
Alt to restore the selected view. Seam statistics sit below the preview.
Choose a neighboring tile from its thumbnail. Hover for its name; the
self-comparison thumbnail shows the active tile and a loop marker.
Ctrl + mouse wheel zooms toward the cursor, like the main editor.
The zoom selector offers Fit and fixed scales from 6.25% to 6400%.

Import a tileset atlas by tile dimensions or a columns/rows grid. Preview
grid suggestions, set margins and gaps, and optionally skip transparent
cells. Each slice becomes an editable asset with its original pixels and
alpha. Save slices in a .ttiles collection. Atlas imports are snapshots;
later changes to the source atlas do not reload imported tiles.

Open Height / Normal / AO maps from Tools while editing an image. Generate
maps from the selected source or result, paint on each map and inspect the
lighting by moving the preview light. Height edits update Normal and AO
while retaining their manual edits. Save editable .tmaps projects or export
the maps as transparent PNGs; Normal supports DirectX and OpenGL.

Your library is in WorkspaceData/Library beside Tessera.exe.
On first launch, images and histories from %LOCALAPPDATA%\Tessera\Library
are copied there. The original library stays intact. Keep WorkspaceData
when moving the app to another folder or computer.

Tessera checks GitHub Releases for updates at startup. Use Settings > General > Updates
to check manually and install a newer version. Your library remains in place.
Updates: https://github.com/Timbuhtuk/TESSERA/releases/latest

Each new version shows its patch notes on first launch. Read the bundled
release history offline in Settings > Patch notes.
'@
    [IO.File]::WriteAllText((Join-Path $output 'README.txt'), $guide, [Text.UTF8Encoding]::new($true))
    $archive = Join-Path $artifactsRoot 'Tessera-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $output 'Tessera.exe'),(Join-Path $output 'README.txt'),(Join-Path $output 'Assets/Fonts/Cascadia-Code-OFL.txt'),(Join-Path $output 'Assets/Fonts/Science-Gothic-OFL.txt') -DestinationPath $archive -Force
    $cliArchive = Join-Path $artifactsRoot 'Tessera-cli-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $cliOutput 'tessera.exe') -DestinationPath $cliArchive -Force
    & $python (Join-Path $projectRoot 'plugins/package_tessera.py') --output-directory $pluginOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tessera plugin packaging failed' }
    if (!$SkipChecks) {
        & $python (Join-Path $projectRoot 'plugins/test_tessera.py') --cli (Join-Path $cliOutput 'tessera.exe') --gui (Join-Path $output 'Tessera.exe') --packages $pluginOutput
        if ($LASTEXITCODE -ne 0) { throw 'Tessera plugin integration checks failed' }
    }
    $hash = Get-FileHash -LiteralPath (Join-Path $output 'Tessera.exe') -Algorithm SHA256
    [IO.File]::WriteAllText((Join-Path $artifactsRoot 'Tessera-win-x64.sha256'), "$($hash.Hash)  Tessera.exe`n")
    $pluginArchive = Join-Path $pluginOutput 'Tessera-chatgpt-plugin.zip'
    $skillArchive = Join-Path $pluginOutput 'Tessera-skill.zip'
    $checksums = foreach ($file in @((Join-Path $output 'Tessera.exe'), $archive, $cliArchive, $pluginArchive, $skillArchive)) {
        $checksum = Get-FileHash -LiteralPath $file -Algorithm SHA256
        "$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
    }
    [IO.File]::WriteAllText((Join-Path $artifactsRoot 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n")
    Write-Output "EXE: $(Join-Path $output 'Tessera.exe')"
    Write-Output "CLI: $(Join-Path $cliOutput 'tessera.exe')"
    Write-Output "ZIP: $archive"
    Write-Output "CLI ZIP: $cliArchive"
    Write-Output "PLUGIN: $pluginArchive"
    Write-Output "SKILL: $skillArchive"
}
finally { Pop-Location }
