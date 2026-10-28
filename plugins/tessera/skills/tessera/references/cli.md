# Tessera CLI reference

The standalone Windows x64 CLI is `artifacts/cli/tessera.exe` after publication, or inside `Tessera-cli-win-x64.zip` in [GitHub Releases](https://github.com/Timbuhtuk/TESSERA/releases/latest). It downscales images, applies size and color operations independently, smooths local colors, creates monochrome and ASCII output, inspects and replaces colors, aligns pixel grids, removes solid backgrounds, exports ICO files and converts Aseprite animations to PNG sprite sheets with JSON metadata. Raster commands accept PNG, JPEG, BMP, GIF and TIFF; the animation command accepts .ase and .aseprite.

Run these examples from the repository root after building; replace the sample input paths with your own:

```powershell
.\artifacts\cli\tessera.exe --help
.\artifacts\cli\tessera.exe input.png -o result.png --width 64 --height 64 --palette db32
.\artifacts\cli\tessera.exe align input.png -o aligned.png --cell-size 4
.\artifacts\cli\tessera.exe reduce-grid aligned.png -o pixels.png --cell-size 4
.\artifacts\cli\tessera.exe ico input.png -o application.ico --sizes 16,32,48,256 --resize nearest-neighbor
.\artifacts\cli\tessera.exe remove-background input.png -o transparent.png --mode edges --tolerance 8
.\artifacts\cli\tessera.exe resize input.png -o smaller.png --width 32 --height 32
.\artifacts\cli\tessera.exe colors smaller.png -o colored.png --palette db16
.\artifacts\cli\tessera.exe smooth-colors colored.png -o smoothed.png --passes 2
.\artifacts\cli\tessera.exe monochrome colored.png -o monochrome.png
.\artifacts\cli\tessera.exe ascii colored.png -o colored.txt
.\artifacts\cli\tessera.exe palette-info colored.png --json
.\artifacts\cli\tessera.exe replace-color colored.png -o recolored.png --from "#000000" --to "#FFFFFF"
.\artifacts\cli\tessera.exe aseprite walk.aseprite idle.aseprite --output-dir artifacts/sheets --layout grid --columns 4
```

The application interface and CLI messages currently use Russian. This reference describes the commands in English.

## Downscaling options

Without `--output`, the result is placed next to the source with the suffix `_downscaled.png`. Existing files require `--overwrite`; the source itself is protected.

| Option | Values and default |
| --- | --- |
| `<image>`, `-i`, `--input` | One input file; quote paths containing spaces |
| `-o`, `--output` | PNG, JPG/JPEG or BMP |
| `--width` | Positive integer; **48**; practical size depends on available memory and the image codec |
| `--height` | Positive integer; **48**; practical size depends on available memory and the image codec |
| `--sprite` | Full image without cropping, preserves partial alpha; PNG output only |
| `--alpha-threshold` | 1–100%; **50**; requires `--sprite` |
| `--crop-horizontal` | **center**, left, right |
| `--crop-vertical` | **center**, top, bottom |
| `--palette` | none, **db16**, db32, nes, gameboy, step |
| `--palette-step` | 1–255; **32**; requires `--palette step` |
| `--quantization` | median-cut, **kmeans-lab**, kmeans-linear |
| `--quantization-colors` | 1–4096; **64**; upper bound for the initial palette |
| `--color-weights` | Off by default; weight each color by its pixel count |
| `--dithering` | Off by default; available for DB16, DB32, NES and GameBoy |
| `--threads` | 1–logical processor count; defaults to all |
| `--block-mode` | **automatic**, manual |
| `--brightness` | 0–100; **50**; manual mode only |
| `--contrast` | 0–100; **50**; manual mode only |
| `--saturation` | 0–100; **50**; manual mode only |
| `--edge` | 0–100; **50**; manual mode only |
| `--cropped-output` | Export the cropped source |
| `--preview-output` | Export a nearest-neighbor enlarged preview |
| `--preview-scale` | 1–100; **8**; requires `--preview-output` |
| `--overwrite` | Allow replacing an existing output |
| `--json` | Write the processing report as JSON |

Numbers are integers. Mode names are case-insensitive and options accept `--width=48` syntax. For an input path starting with a hyphen, use `--input=PATH` or place it after `--`. Help is `--help` / `-h`; the version is `--version`. Running without arguments prints help.

The output must not be larger than the input. Cropping removes the remainder along each axis so that the source dimensions are divisible by the target dimensions; crop options select its position.

`--palette none` disables the final fixed-palette mapping, but initial quantization still runs. `--quantization-colors` limits that stage; the actual number of colors can be smaller. DB16, DB32, NES, GameBoy and RGB step mapping run later and may reduce it further.

Without `--color-weights`, unique colors have equal weight. With it, K-Means averages use pixel counts, and Median Cut uses counts both when splitting groups and when averaging them. K-Means initialization and iteration counts remain unchanged. Sprite weights include every pixel with nonzero alpha. Weighting can help large color regions but lose rare accents; it is separate from block representative selection.

Manual-mode values describe the desired properties of the **color selected within each block**, rather than applying global brightness or contrast filters. The CLI divides 0–100 values by 100, matching the application's sliders.

## Transparent sprites

`--sprite` divides the entire image into a target grid with fractional block boundaries and no cropping. Coverage is the area occupied by pixels with nonzero alpha, independently of their opacity. Color selection and quantization include every visible pixel without mixing in a black background. When coverage meets `--alpha-threshold`, the result keeps the selected pixel's alpha instead of forcing it to 0 or 255. Resize, cropping, quantization, palette mapping and dithering preserve partial alpha; save as PNG to retain it in the output file.

A 25% threshold keeps more of the edge; 75% narrows the silhouette. Sprite mode cannot be combined with cropping options or dithering. `--cropped-output` exports the full source. Set the target size manually; sprite downscaling does not automatically find the original pixel size or repack animation frames.

```powershell
.\artifacts\cli\tessera.exe sprite.png -o artifacts/sprite.png --sprite --width 256 --height 128 --alpha-threshold 50 --palette none --preview-output artifacts/sprite-preview.png --preview-scale 4
```

The image decoder is shared with the desktop application. Corrupt or unsupported inputs fail. PNG and BMP preserve output pixels losslessly; JPEG compression can change colors.

## Independent size and color operations

`tessera resize` changes pixel dimensions through the editor's `IndependentImageProcessor.Scale` algorithm. It selects colors already present in each source block; it does not run quantization or a palette. Supply positive `--width` and `--height` values (48 × 48 by default); Tessera does not impose a 4K cap, while available memory and the image codec determine the practical maximum. A larger target repeats exact source pixels with nearest-neighbor sampling, preserving colors and alpha without smoothing. If either dimension grows, nearest-neighbor sampling is used on both axes. For reduction, `--sprite` keeps the full frame and enables `--alpha-threshold` (1–100%, default 50); otherwise `--crop-horizontal` and `--crop-vertical` choose which remainder to discard. The block representative can be `automatic` or `manual` with `--brightness`, `--contrast`, `--saturation` and `--edge` (0–100).

`tessera colors` applies quantization and an optional palette to every pixel at the original dimensions. It preserves the input alpha values. It accepts `--palette`, `--palette-step`, `--quantization`, `--quantization-colors`, `--color-weights` and `--dithering`. Size and crop options are rejected.

Both commands take one image file, including a result saved by an earlier CLI or desktop operation. They save PNG to retain transparency. Defaults are `<name>_resized.png` and `<name>_colors.png` next to the input. Existing files require `--overwrite`; `--json` reports paths, dimensions, settings and elapsed time.

```powershell
.\artifacts\cli\tessera.exe resize sprite.png -o sprite-small.png --width 32 --height 32 --sprite --alpha-threshold 75
.\artifacts\cli\tessera.exe resize sprite-small.png -o sprite-large.png --width 256 --height 256
.\artifacts\cli\tessera.exe colors sprite-small.png -o sprite-colored.png --palette db32 --quantization-colors 32
```

## Current editor image tools

`tessera colors` additionally accepts `--color-mode combined|quantization|palette`. `combined` preserves the earlier CLI pipeline; `quantization` and `palette` run only the corresponding editor stage. Palette-only mode rejects quantization settings, and quantization-only mode rejects palette and dithering settings.

`tessera smooth-colors` (alias `smooth`) runs the editor's local 3×3 color cleanup without changing dimensions or alpha. `--passes` accepts 1–100. `--color-tolerance` accepts 0–40 (default 12); `--strength`, `--detail-protection` and `--edge-protection` accept 0–100 (defaults 65, 80 and 80). These match the current editor controls and accept decimal values with a dot. Zero tolerance or strength leaves the image unchanged.

`tessera monochrome` (alias `mono`) converts visible pixels to grayscale while preserving size and alpha. `tessera replace-color` replaces one exact visible RGB color with another using `--from #RRGGBB` and `--to #RRGGBB`; source alpha is retained. `tessera palette-info` counts visible RGB colors and lists them when the image contains at most 1024 colors.

`tessera ascii` saves text, a rendered PNG, or both. `--mode fit` chooses a character grid for the source dimensions and keeps those dimensions in rendered PNG; `--mode per-pixel` creates one character per source pixel. Choose `--preset standard|detailed|minimal|blocks` or supply `--characters`. `--invert-source` reverses brightness mapping and `--invert-render` draws black text on white.

```powershell
.\artifacts\cli\tessera.exe colors input.png -o quantized.png --color-mode quantization --quantization-colors 32
.\artifacts\cli\tessera.exe smooth-colors input.png -o smoothed.png --passes 2 --color-tolerance 18 --strength 70 --detail-protection 80 --edge-protection 80
.\artifacts\cli\tessera.exe monochrome input.png -o monochrome.png
.\artifacts\cli\tessera.exe replace-color input.png -o recolored.png --from "#112233" --to "#AABBCC"
.\artifacts\cli\tessera.exe palette-info input.png --json
.\artifacts\cli\tessera.exe ascii input.png --text-output image.txt --image-output image-ascii.png --preset detailed
```

All image-producing commands save PNG, reject source overwrite, require `--overwrite` for existing results and support `--json`. ASCII text uses UTF-8 without a byte-order mark.
## Grid alignment

`tessera align` preserves the canvas and chooses colors and alpha from the source. Output must be PNG and defaults to `<name>_aligned.png`.

| Option | Meaning |
| --- | --- |
| `--mode global\|local` | Shared grid or independent local grids; default `global` |
| `--cell-size N` | Explicit square cell size: global requires 2..min(width,height); local supports 2..64 |
| `--max-cell-size N` | Upper bound for automatic detection: 2–256; default 32 |
| `--snap-to-grid` | Local mode only: move restored regions to the shared canvas-origin grid; off by default |
| `--rigid` | Disable adaptation to local grid shifts |
| `--overwrite`, `--json` | Shared output replacement and reporting options |

Omit `--cell-size` for automatic detection. Set it explicitly for ambiguous grids. Local mode cleans nearby shades and restores each detected contrast region on its own grid origin, retaining independent figure positions. Small and ambiguous regions are preserved. Complex bends may remain partly distorted. Use `tessera align input.png -o local.png --mode local --cell-size 5`. The JSON report includes `mode` and `localRegionCount`. Add `--snap-to-grid` to allow rigid region shifts onto the shared grid: `tessera align input.png -o snapped.png --mode local --cell-size 5 --snap-to-grid`. These results support `reduce-grid`. Without this flag, local results do not imply one shared compactable grid. The JSON report also includes `snapToGlobalGrid`, `shiftedRegionCount` and `conflictingCellCount`. Snapping keeps the canvas size and fills vacated areas from the surrounding background; tiny details occupy a complete output cell. Adjacent regions may compete for a cell; unavoidable collisions retain the stronger contrast detail and are counted in the report. See the [algorithm and API](https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtAlignment/README.md).

## Reduce an aligned grid

`tessera reduce-grid` (alias `compact-grid`) applies the editor's “one pixel per grid cell” operation to a previously aligned image. `--cell-size` is required because CLI files do not carry the editor's history metadata; use the size reported by `tessera align` (or its JSON `cellSize`). Each uniform cell contributes one original ARGB pixel, including partial cells on the right and bottom edges. A nonuniform grid is rejected; no palette or quantization is applied.

```powershell
.\artifacts\cli\tessera.exe align input.png -o aligned.png --cell-size 4
.\artifacts\cli\tessera.exe reduce-grid aligned.png -o pixels.png --cell-size 4
```

The output is PNG, defaulting to `<name>_pixels.png`. `--overwrite` and `--json` work as in the other standalone commands. The input and output paths must differ.

## Background removal

`remove-background` (alias `remove-bg`) saves a full-size PNG with a solid background made transparent. It uses the same algorithm as the ICO workspace.

```powershell
.\artifacts\cli\tessera.exe remove-background input.png -o transparent.png --mode edges --background-color auto --tolerance 8
```

`--mode global` removes every matching pixel in the image. `--mode edges` starts from all four borders and preserves matching colors enclosed inside the foreground. The default mode is `global`.

`--background-color` accepts `auto`, `white`, `black` or an RGB color such as `"#35A7C8"`. Automatic detection chooses the predominant opaque border color; a mostly transparent border leaves the image unchanged. `--tolerance` accepts 0–100% and defaults to 8. Existing output requires `--overwrite`; `--json` reports the effective settings. Without `--output`, the result is `<name>_transparent.png` beside the input.

## ICO export

The `ico` command (alias `icon`) creates a multi-size icon directly from an image, without reapplying a palette or quantization.

```powershell
.\artifacts\cli\tessera.exe ico result.png -o application.ico --sizes 16,24,32,48,64,128,256 --resize nearest-neighbor --fit contain
```

`--sizes` accepts unique integer sizes from 1 to 256 separated by commas. The default set is 16, 24, 32, 48, 64, 128 and 256. `--resize` accepts `smooth` or `nearest-neighbor`; the latter avoids color blending for pixel art. `--fit contain` preserves aspect ratio with transparent padding, `cover` fills the frame with a centered crop, and `stretch` fills it by stretching.

`--overwrite` and `--json` are supported. Without `--output`, the result is `<name>.ico` beside the input. Full command help is `tessera ico --help`. See the [ICO format and shared API](https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtDownscale/ICO.md) for details, including background removal in the desktop workspace and library API.

## Aseprite sprite sheets

`tessera aseprite` converts one or more `.ase`/`.aseprite` files with the offline decoder used by the desktop workspace. Each input produces `<name>.png` and `<name>.json` in `--output-dir`; `--inspection` adds `<name>.inspection.json`. The default directory is `aseprite-export` beside the first input. Layout choices are `horizontal` (default), `vertical` and `grid`; `--columns` applies only to grid and accepts 1–256 (default 4). `--padding` accepts 0–128 pixels (default 0).

```powershell
.\artifacts\cli\tessera.exe aseprite walk.aseprite idle.aseprite --output-dir artifacts/sheets --layout grid --columns 4 --padding 2 --inspection --json
```

Files are processed in order. A failure in one file does not stop the rest, but the command exits with code 1 if any failed. Existing outputs and same-name collisions are reported as errors; the converter does not overwrite them. With `--json`, the report contains a success or error entry for every input. The decoder composites visible RGBA32 image layers and excludes hidden layers; see [Aseprite conversion details](https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtAseprite/README.md) for the remaining format limits.

## More examples

Quantize to at most 128 colors with frequency weighting and no fixed palette:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/weighted.png --quantization-colors 128 --color-weights --palette none
```

Omit `--color-weights` to disable weighting. Omit the color count or use 64 to restore its default.

Manual color selection with bottom-right cropping and an RGB step palette:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/manual.png --width 96 --height 64 --crop-horizontal right --crop-vertical bottom --palette step --palette-step 32 --quantization median-cut --block-mode manual --brightness 60 --contrast 70 --saturation 80 --edge 90 --threads 1
```

Export the crop and an 8x preview:

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/result.png --cropped-output artifacts/crop.png --preview-output artifacts/preview.png --preview-scale 8
```

## Reports and exit codes

```powershell
.\artifacts\cli\tessera.exe input.png -o artifacts/result.png --json > artifacts/report.json
if ($LASTEXITCODE -ne 0) { throw "Processing failed" }
$report = Get-Content artifacts/report.json -Raw | ConvertFrom-Json
$report.dominantColor.hex
```

For downscaling, the report includes absolute paths, source/crop/result dimensions, effective options, the dominant color and stage timings in seconds. `--json` keeps standard output JSON-only; errors go to standard error. The report is printed only after all requested outputs have been saved successfully.

| Exit code | Meaning |
| --- | --- |
| 0 | Success, help or version |
| 1 | Reading, processing or saving failed; output exists without `--overwrite` |
| 2 | Invalid or unknown arguments, incompatible options, duplicate paths, unsupported output extension or oversized target |


`tessera clean-contours` (Tessera 2.8+) detects gradient-direction candidates and performs a separate conservative replacement pass. `--window-size` accepts odd values 3–15 (default 7). Candidate selection exposes `--similar-color-distance` (0–100, 24), `--min-pixel-gradient` (0–20000, 120), `--min-neighbor-gradient` (0–20000, 80), `--min-similar-neighbors` (1–224, 2), `--min-agreement` and `--min-opposition` (0–100 percent, 35 and 10). Similarity is a LAB distance; gradient strengths use squared color differences. Detail protection is enabled unless `--no-detail-guard` is supplied. `--strictness cautious|normal|strong` controls the later replacement pass, not candidate selection. A candidate need not receive a replacement. `--candidates-output candidates.png` saves all detected locations in magenta on transparency. The cleaned PNG retains dimensions, every alpha value and source colors. Output paths cannot overwrite the input or each other; existing files require `--overwrite`. `--json` reports candidates and actual changes separately. The desktop tool creates a white-to-red analysis map rather than cleaning pixels.

```powershell
tessera clean-contours input.png -o result.png --window-size 3 --similar-color-distance 24 --min-pixel-gradient 120 --min-neighbor-gradient 80 --min-similar-neighbors 2 --min-agreement 35 --min-opposition 10 --candidates-output candidates.png --json
```

## Experimental connected palette

`connected-palette` reduces the visible RGB palette before grid downscaling, using connected color areas, spatial concentration and color difference to estimate the cost of removing a color. The result uses only colors from its input palette. It keeps width, height, pixel positions and every alpha value, and does not add dithering. A preliminary Median Cut or K-means pass is recommended for rich source images; this first implementation accepts up to 4096 visible RGB colors.

```powershell
tessera colors input.png -o intermediate.png --color-mode quantization --quantization median-cut --quantization-colors 1024
tessera connected-palette intermediate.png -o palette128.png --colors 128 --connectivity 70 --tile-size 32 --local-weight 40 --detail-weight 50 --context-weight 30 --space oklab --json
```

`--colors` accepts 1–4096 (default 128). `--connectivity`, `--local-weight`, `--detail-weight`, and `--context-weight` accept 0–100 percent (defaults 70, 40, 50, 30). `--tile-size` accepts 0 to disable local analysis, or 4–256 pixels (default 32). `--space` selects `oklab` (default) or `lab`. Each exact RGB color is analyzed in eight-connected components. The removal cost combines pixel count, component concentration, local tile concentration, and the distance to the nearest remaining color. Removed components can choose different retained colors according to their four-neighbor boundary. This is a test tool: a rare eye, speckle and highlight can remain ambiguous; inspect the result before grid compression. The desktop equivalent is **Tools → Color → Connected palette…**, with native preview and a saved result.
## Experimental structure probe

`structure-probe` finds small connected areas of similar colors and tests whether one neighboring color dominates their boundary. It can save a proposed cleaned image, a red candidate map, or both. It keeps the source dimensions and every alpha value. This is an experiment for already reduced images; it does not reconstruct a pixel grid or guarantee that a marked detail is noise.

```powershell
tessera structure-probe input.png -o structure.png --map-output candidates.png --json
tessera structure-probe input.png -o candidates.png --map-only --max-component 16 --minimum-confidence 80
```

Eight-neighbor connectivity and LAB similarity form areas. Four-neighbor boundary votes select an existing replacement color. `--similar-color` (0–100, default 12) and `--max-component` (1–4096, default 8) control area formation. `--neighbor-agreement` (0–100%, default 70), `--max-replacement-distance` (1–150, default 60), and `--minimum-confidence` (0–100%, default 65) control replacement. `--detail-distance` (0–100, default 25) protects high-contrast areas of one or two pixels. Transparent boundaries and thin lines are protected by default; `--allow-alpha-edge` and `--allow-lines` disable those guards. Red on the map means a candidate, including candidates retained by the guards or confidence threshold. `--overwrite` is required for existing files; input and output paths must differ. The equivalent desktop tool is **Tools → Color → Structure probe…** and previews either the proposed image or candidate map.

## Dark contour and grid speckle cleanup (2.9+)

`clean-dark-contours` is a CLI tool for isolated dark outliers near contours. It writes a separate PNG, retaining dimensions and every alpha value. Correction colors are estimates from neighboring material, rather than recovery of an unknown original. Review the output before using it as an asset.

```powershell
tessera clean-dark-contours input.png -o cleaned.png --passes 2 --json
tessera clean-dark-contours enlarged.png -o cleaned-grid.png --grid-cell-size 2 --grid-color-tolerance 0 --json
```

With a known pixel-grid step of 2–256 pixels, `--grid-cell-size N` selects the singleton mode instead of dark contour analysis. This mode compares eight neighbors and accepts a color only when a 2×2 square has three agreeing neighbors with the same alpha as the center. Fully transparent pixels, image borders and ambiguous colors remain intact. `--grid-color-tolerance` allows RGB distances from 0 to 442 (default 0), requires the grid mode and never relaxes the alpha check. The grid step is supplied manually; it is not detected automatically. The image must be at least as large as the supplied step.

`--passes` accepts 1–10 (default 1). Use `clean-dark-contours --help` for the individual contour thresholds and optional recovery rules. `--candidate-audit decisions.csv` records per-pass decisions, while `--json` returns the mode, dimensions, change count, per-pass counts and effective settings. Change counts count corrections across passes. The input, PNG output and audit file must have distinct paths; existing output or audit files require `--overwrite`. This command is independent of the desktop Contours analysis map and of `clean-contours`.
