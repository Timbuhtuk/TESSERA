# Using Tessera

Download `Tessera.exe` or `Tessera-win-x64.zip` from [GitHub Releases](https://github.com/Timbuhtuk/TESSERA/releases/latest). These Windows x64 builds run offline without installing .NET. Earlier releases used the Pixelizator name.

The application interface currently uses Russian labels. This guide describes the actions in English.

The interface includes Cascadia Code and Science Gothic fonts. Icon, background-removal and Aseprite workspaces place settings beside the preview on wide windows; narrow windows place scrollable settings above the preview.

Image size fields accept dimensions above 4K. Available memory and image codec support determine the practical maximum, in both the desktop editor and CLI.

## Library and editor

Open images from **File → Open**, with Ctrl+O or by drag and drop. The home screen shows your library, image dimensions and result counts. Open a card to edit its source. The back arrow returns to the library; the layout adapts to narrower windows.

The editor keeps the image previews in the main window. Choose **Process: Source** or **Process: Result №…** as the input, then open **Presets**, **Size**, **Palette**, **Smoothing**, **Grid**, **Filters**, or **Info**. On narrow windows the tools appear under **Processing**. Zoom and preview background are separate viewing controls; they do not change exported images. **File → Save** exports the selected result, while **File → Save all results** exports every result for the current source to a chosen folder. The editor has three ready processing profiles:

| Profile | Intended use | Initial settings |
| --- | --- | --- |
| Background | Broad color areas and composition | K-Means LAB, automatic block selection, RGB step 16 |
| Scene | Shapes, lighting and color transitions | K-Means LAB, automatic block selection, RGB step 16 |
| Details | Sprites, outlines and transparency | Median Cut, no additional palette, full image, 75% alpha coverage |

The editor can run size and palette work independently. **Change size** enlarges or reduces the chosen input using its pixel colors. Enlargement copies pixels and their alpha without smoothing; reduction uses the selected frame, transparency and pixel selection settings under **Additional settings**. Those settings are disabled for enlargement. **Reduce palette** offers mutually exclusive **By color count** and **By palette** choices. Inactive controls do not affect the operation; dimensions and alpha stay unchanged. You can chain these operations in either order; each adds a separate result to the history.

Selecting a ready mode leaves the current controls unchanged. **Process** applies that mode's preset when clicked and runs the combined reduction on the chosen input; execution settings are under **Additional settings**. Use **Change size** to enlarge. **Smoothing** is its own 3×3 operation with 1–100 passes. It replaces a pixel only with a nearby visible color, keeps dimensions and alpha, and ignores the palette, color count, quantization, and dithering. It uses the same source/result choice as **Reduce palette**, with its own brightness, contrast, saturation, and edge controls. These controls and the pass count are saved with each result. Sliders in **Size** and **Smoothing** express which existing pixel color to prefer; they do not recolor the finished image and remain independent. Grid alignment is another separate operation that preserves canvas dimensions and selects original colors. Reducing grid cells to one pixel compresses an aligned result without quantizing it again.

**Info** shows dimensions and visible RGB color counts for the source and current result. The chosen input's colors appear as a list in first-appearance order when there are at most 1024. Click a color to open the replacement window; its palette starts with that color selected. Choose any new color with the hue strip and saturation/brightness square, pick an existing image color, or enter #RRGGBB; these controls stay synchronized. Then select **Replace color**. Larger images still show the full count and offer manual HEX replacement. This creates a saved result without changing the source, dimensions, or transparency.

Previews preserve aspect ratio and enlarge pixels without smoothing. Ctrl+mouse wheel changes zoom; source and result scrolling stay synchronized. Choose a checkerboard, dark or light preview background.

On wide windows, the result and source filmstrips sit above their previews; on narrow windows, the source filmstrip moves below them. Drag a result to the source filmstrip to process it independently. Removing a library item leaves external originals and exported files intact. Use PNG when saving transparency.

## Filters

**Filters** uses the source or result selected on the editor toolbar. **Monochrome** converts visible colors to grayscale, preserves dimensions and alpha, and saves a new result in the history.

For ASCII art, select **Standard**, **Detailed**, **Minimal** or **Blocks**, or edit the character sequence from dark to light. Editing switches the preset to **Custom**. **Save TXT** exports UTF-8 text; **Render image** adds an opaque black-and-white image to the history, which can then be exported through **File → Save**.

**Keep source size** is enabled by default: the character grid is calculated from the input dimensions and the rendered image keeps those dimensions. Disable it to map each pixel to one character, producing a larger render. **Invert source colors** reverses brightness before selecting characters; **Invert render colors** switches to black characters on a white background and does not affect TXT export. The original image remains intact.

## Icons

The icon banner opens a separate ICO workspace. Open or drop an image, or hover over **From editor** to choose any saved source or result from its thumbnail. Select several standard sizes from 16 to 256 pixels, or add a custom size from 1 to 256. Choose the resizing and fitting modes, inspect the previews and save all selected sizes into one ICO file.

Optional background removal makes a solid color transparent. It can infer the color from the edges or use white or black; tolerance ranges from 0 to 100%. Choose global removal to remove matching colors everywhere, or edge-connected removal to preserve enclosed matching areas inside the foreground. An open passage to the border can still expose an area to removal. The preview and saved ICO show the same transparency, and the original stays unchanged. Both modes remove a solid color rather than segmenting a complex photographic background.

Ctrl+O opens an image for the currently active workspace.

## Aseprite animations

The animation banner opens a separate workspace. Open or drop several `.ase` or `.aseprite` files to convert them automatically into PNG sprite sheets with JSON frame timings. Choose horizontal, vertical or grid layout and adjust columns and transparent gaps. Save one ready result or the whole batch; conflicting output names receive numeric suffixes. A failed file does not stop other files in the batch.

The current converter supports RGBA files with one visible ordinary layer and normal blending. Unsupported features produce an error rather than an incomplete export; see the [supported subset](../PixelArtAseprite/README.md). Animation imports are temporary session data, so save results before closing the app.

## Storage and updates

The library is saved automatically in `%LOCALAPPDATA%\Tessera\Library`. On the first launch after updating, Tessera copies existing images and result histories from `%LOCALAPPDATA%\Pixelizator\Library` into the new library. The old directory remains as a backup and is not read again after a successful migration. To move the library to another computer, copy the Tessera library directory separately.

Tessera checks the latest GitHub release in the background at startup. Use **Check for updates** in the bottom bar or File menu to check manually. When a newer version is available, select **Update to v…** and confirm. Tessera downloads the standalone executable, verifies its SHA-256 digest, closes, replaces the executable and restarts. Your image library stays in `%LOCALAPPDATA%\Tessera\Library`. The automatic install is available when running the published `Tessera.exe` from a writable folder; development builds open the release page instead. If a check fails or you are offline, the current app keeps working.

For more detail, see [grid alignment](../PixelArtAlignment/README.md), [ICO export](../PixelArtDownscale/ICO.md) and the [CLI reference](CLI.md).

## Standalone background removal

Open **Remove background** from the home screen, drop or open an image, and compare the source with the live transparent preview. Choose global color removal or edge-connected removal, automatic edge color, white or black, and a 0–100% tolerance. Save a transparent PNG without changing the source. Hover over **From editor** to inspect and choose any saved source or result by thumbnail. The ICO workflow keeps its optional background removal setting.
