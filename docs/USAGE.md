# Using Tessera

Download `Tessera.exe` or `Tessera-win-x64.zip` from [GitHub Releases](https://github.com/Timbuhtuk/TESSERA/releases/latest). These Windows x64 builds run offline without installing .NET. Earlier releases used the Pixelizator name.

The application interface currently uses Russian labels. This guide describes the actions in English.

## Library and editor

Open images with the Open button, Ctrl+O or drag and drop. The home screen shows your library, image dimensions and result counts. Open a card to edit its source. The back arrow returns to the library; the layout adapts to narrower windows.

The editor has three starting profiles:

| Profile | Intended use | Initial settings |
| --- | --- | --- |
| Background | Broad color areas and composition | K-Means LAB, automatic block selection, RGB step 16 |
| Scene | Shapes, lighting and color transitions | K-Means LAB, automatic block selection, RGB step 16 |
| Details | Sprites, outlines and transparency | Median Cut, no additional palette, full image, 75% alpha coverage |

The editor can run size and color work independently. Choose **Source** or **Selected result** as the input, then use **Change size** to downscale without changing source colors, or **Apply colors** to quantize and use a palette without changing dimensions or transparency. You can chain them in either order; each operation adds a separate result to the history. **Size + color** keeps the original combined workflow and starts from the source. Grid alignment is another separate operation that preserves canvas dimensions and selects original colors. Reducing grid cells to one pixel compresses an aligned result without quantizing it again.

Previews preserve aspect ratio and enlarge pixels without smoothing. Ctrl+mouse wheel changes zoom; source and result scrolling stay synchronized. Choose a checkerboard, dark or light preview background.

The upper filmstrip contains results for the current source; the lower filmstrip contains sources. Drag a result to the source filmstrip to process it independently. Removing a library item leaves external originals and exported files intact. Save exports the selected result; use PNG for transparency.

## Icons

The icon banner opens a separate ICO workspace. Open or drop an image, or hover over **From editor** to choose any saved source or result from its thumbnail. Select several standard sizes from 16 to 256 pixels, or add a custom size from 1 to 256. Choose the resizing and fitting modes, inspect the previews and save all selected sizes into one ICO file.

Optional background removal makes a solid color transparent. It can infer the color from the edges or use white or black; tolerance ranges from 0 to 100%. Choose global removal to remove matching colors everywhere, or edge-connected removal to preserve enclosed matching areas inside the foreground. An open passage to the border can still expose an area to removal. The preview and saved ICO show the same transparency, and the original stays unchanged. Both modes remove a solid color rather than segmenting a complex photographic background.

Ctrl+O opens an image for the currently active workspace.

## Aseprite animations

The animation banner opens a separate workspace. Open or drop several `.ase` or `.aseprite` files to convert them automatically into PNG sprite sheets with JSON frame timings. Choose horizontal, vertical or grid layout and adjust columns and transparent gaps. Save one ready result or the whole batch; conflicting output names receive numeric suffixes. A failed file does not stop other files in the batch.

The current converter supports RGBA files with one visible ordinary layer and normal blending. Unsupported features produce an error rather than an incomplete export; see the [supported subset](../PixelArtAseprite/README.md). Animation imports are temporary session data, so save results before closing the app.

## Storage and updates

The library is saved automatically in `%LOCALAPPDATA%\Pixelizator\Library`. This historical path is retained by Tessera to preserve existing images and histories. To move the library to another computer, copy that directory separately.

Download the new executable to update the application. GitHub releases are generated automatically, but the installed application does not replace itself.

For more detail, see [grid alignment](../PixelArtAlignment/README.md), [ICO export](../PixelArtDownscale/ICO.md) and the [CLI reference](CLI.md).

## Standalone background removal

Open **Remove background** from the home screen, drop or open an image, and compare the source with the live transparent preview. Choose global color removal or edge-connected removal, automatic edge color, white or black, and a 0–100% tolerance. Save a transparent PNG without changing the source. Hover over **From editor** to inspect and choose any saved source or result by thumbnail. The ICO workflow keeps its optional background removal setting.
