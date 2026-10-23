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
| `--cell-size N` | Explicit square cell size, at least 2 and no larger than the smaller image dimension |
| `--max-cell-size N` | Upper bound for automatic detection: 2–256; default 32 |
| `--rigid` | Disable adaptation to local grid shifts |
| `--overwrite`, `--json` | Shared output replacement and reporting options |

Omit `--cell-size` for automatic detection. Set it explicitly for ambiguous grids. See the [algorithm and API](../PixelArtAlignment/README.md).

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

`--overwrite` and `--json` are supported. Without `--output`, the result is `<name>.ico` beside the input. Full command help is `tessera ico --help`. See the [ICO format and shared API](../PixelArtDownscale/ICO.md) for details, including background removal in the desktop workspace and library API.

## Aseprite sprite sheets

`tessera aseprite` converts one or more `.ase`/`.aseprite` files with the offline decoder used by the desktop workspace. Each input produces `<name>.png` and `<name>.json` in `--output-dir`; `--inspection` adds `<name>.inspection.json`. The default directory is `aseprite-export` beside the first input. Layout choices are `horizontal` (default), `vertical` and `grid`; `--columns` applies only to grid and accepts 1–256 (default 4). `--padding` accepts 0–128 pixels (default 0).

```powershell
.\artifacts\cli\tessera.exe aseprite walk.aseprite idle.aseprite --output-dir artifacts/sheets --layout grid --columns 4 --padding 2 --inspection --json
```

Files are processed in order. A failure in one file does not stop the rest, but the command exits with code 1 if any failed. Existing outputs and same-name collisions are reported as errors; the converter does not overwrite them. With `--json`, the report contains a success or error entry for every input. The decoder composites visible RGBA32 image layers and excludes hidden layers; see [Aseprite conversion details](../PixelArtAseprite/README.md) for the remaining format limits.

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
