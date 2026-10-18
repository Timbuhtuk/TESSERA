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

## Tile seam inspector

On the home screen, select **Проверить тайл…** under **Проверка тайлинга**. This opens an independent window. Drop several images into it or use **Добавить тайлы…** / Ctrl+O. PNG, BMP, GIF, JPEG and TIFF are supported; animated and multipage files use the first frame. The tool never modifies imported source files.

The collection on the left keeps separate assets and thumbnails. Select the active **A**, then choose **B** under **Сосед B** to compare different images. The neighbor list includes assets with exactly the same dimensions; images are never stretched to make them fit. **Сам с собой** restores the original single-asset mode. With B selected, copies alternate A/B along each enabled axis. Both **A→B** and **B→A** are measured, because matching one direction does not establish the other. Pair summaries show Δ as mean/maximum and the number/percentage above the threshold.

Enable **X** for horizontal repetition, **Y** for vertical repetition, or both. Each enabled axis repeats 3, 5 or 7 times. **Вписать** fits the preview, while 100–3200% zoom preserves hard pixel edges and enables scrolling. Checkerboard, white and dark backgrounds make transparency visible.

**Тепловая карта краёв** overlays the opposite edges of every copy. The algorithm compares left/right pixels at each row and top/bottom pixels at each column using Euclidean OKLab distance multiplied by 100. Transparent pixels are composited in linear sRGB over both black and white; the larger distance is retained. Invisible RGB does not create false errors. The metric follows [Björn Ottosson's OKLab conversion](https://bottosson.github.io/posts/oklab/).

Below the preview, the summary for each active axis shows the mean, maximum and number/percentage of pixel pairs above the threshold. Green marks values at or below the threshold; yellow/red indicate increasing differences. Red begins at four times the threshold (at threshold zero, red begins at Δ=1). Opacity and stripe width affect the overlay only. Disable it to judge the original repeated image. Hold **Alt** to temporarily show the overlay; releasing Alt restores the checkbox setting. The switch tooltip also explains this shortcut. This temporary view is not saved with the project. Hover over a tile to inspect the edge difference for its row and column.

Use **Карандаш**, **Ластик**, or **Пипетка** above the preview for local edits. A stroke changes the A or B copy where it begins, updates all copies and recalculates the heatmap. Moving across a seam during a stroke does not paint another asset. Choose a 1, 2 or 4 pixel square brush and a HEX color (`#RRGGBB` or `#AARRGGBB`); **Цвет…** opens the existing color picker. The eyedropper reads the original pixel, excluding the heatmap. **↶ / ↷** and Ctrl+Z / Ctrl+Y undo or redo a complete stroke or smoothing operation. The hint names the asset targeted by undo; selecting an asset selects its edit history. Text fields retain their normal editing shortcuts.

**Сгладить цвета A** runs the existing local 3×3 color algorithm on active A, with 1–100 passes. **Только края** retains the result only in the border strip whose width is set by the heatmap stripe control. The algorithm preserves dimensions and alpha; it operates inside the selected asset and does not blend it with B or guarantee matching seams. Undo restores the previous pixels in one step.

**Сохранить проект…** stores the entire collection, embedded edited images, selected pair and viewing settings in a portable `.ttiles` file. **Коллекция…** reopens it even if the original files have moved. Ctrl+S saves the current project (or opens the save dialog for a new one). The title's `*` indicates unsaved project changes; an asset's `*` indicates local changes relative to its imported source. **PNG…** exports active A to a separate PNG. **Убрать** removes an asset from the collection and leaves its original file intact. Unsaved work is checked before closing or replacing a collection.

**Следить за файлом** watches imported files, including B and inactive collection assets, after an external editor saves or replaces them. Changes are debounced and transient read failures retried. Assets with local edits are protected from automatic replacement, including after saving/reopening a project. **Перечитать файл** reloads active A manually and asks before replacing local edits. A failed load keeps the previous preview. Equal edge colors do not guarantee a seamless pattern: inspect the repeated texture for rhythm, gradients and shapes as well.

The desktop executable also accepts `--tiles [image-or-collection-path]` to open this tool directly.

### Tileset import

Use **Импорт тайлсета…** below the asset collection to open an atlas. Load a file or drop one into the import window. **Размер тайла, px** specifies cell width/height; **Столбцы и строки** specifies the grid and calculates the cell dimensions. For example, a 96×192 image with 32×32 tiles yields 3 columns × 6 rows, or 18 assets. The **Сетка 3 × 6** shortcut performs the reverse calculation.

Loading an atlas proposes a grid automatically. **Автоподбор сетки** ranks common square tile sizes and selected rectangular sizes using image dimensions and color/alpha changes at candidate boundaries. It offers alternatives rather than claiming that image dimensions uniquely determine the correct grid. Confirm the outlines in the preview before importing. The usual 96×192 atlas is proposed as 32×32 cells in a 3×6 grid, including flat/ambiguous images.

**Отступы и промежутки** supports symmetric horizontal/vertical outer margins and separate gaps between columns and rows. Dimensions must produce whole cells and cover the atlas exactly after accounting for these fields; invalid settings disable import. Up to 4096 cells can be imported at once. **Пропускать пустые тайлы** optionally excludes fully transparent cells; by default all positions are kept.

**Добавить тайлы в коллекцию** creates independent editable assets in row-major order, named with their original row/column coordinates. Pixel colors and alpha are copied without resizing. The collection embeds these images and retains the original atlas path for source protection. Import is a snapshot: use the import window again to reslice an externally edited atlas. Export and project saving cannot overwrite that source atlas.

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

The converter combines all visible RGBA image layers in Aseprite order. Hidden layers and layers inside hidden groups do not appear in the preview or exported PNG. Unsupported features produce an error rather than an incomplete export; see the [supported subset](../PixelArtAseprite/README.md). Animation imports are temporary session data, so save results before closing the app.

## Image boards

Open **Image board** from the home screen and choose a canvas size from 128 to 16384 pixels per dimension. Add or drop images to create separate layers. Choose a solid background color, move layers on the canvas, rotate or scale them, and change their stacking order. Use Ctrl or Shift to select several layers and transform them together. Zoom with Ctrl+mouse wheel or the zoom selector; use **Fit** to see the whole board.

**Save board** writes a `.tboard` project containing the images, background color, layer order and transforms. Open it later to continue editing without needing the original image files. Boards are saved explicitly, separately from the image library. **Export PNG** produces a flattened image with the selected opaque background; export does not replace saving the editable project. Large imports, project saves and PNG exports run in the background, and editor previews use reduced-size copies while export uses the original image data.

## Storage and updates

The library is saved automatically in `%LOCALAPPDATA%\Tessera\Library`. On the first launch after updating, Tessera copies existing images and result histories from `%LOCALAPPDATA%\Pixelizator\Library` into the new library. The old directory remains as a backup and is not read again after a successful migration. To move the library to another computer, copy the Tessera library directory separately.

Tessera checks the latest GitHub release in the background at startup. Use **Check for updates** in the bottom bar or File menu to check manually. When a newer version is available, select **Update to v…** and confirm. Tessera downloads the standalone executable, verifies its SHA-256 digest, closes, replaces the executable and restarts. Your image library stays in `%LOCALAPPDATA%\Tessera\Library`. The automatic install is available when running the published `Tessera.exe` from a writable folder; development builds open the release page instead. If a check fails or you are offline, the current app keeps working.

For more detail, see [grid alignment](../PixelArtAlignment/README.md), [ICO export](../PixelArtDownscale/ICO.md) and the [CLI reference](CLI.md).

## Standalone background removal

Open **Remove background** from the home screen, drop or open an image, and compare the source with the live transparent preview. Choose global color removal or edge-connected removal, automatic edge color, white or black, and a 0–100% tolerance. Save a transparent PNG without changing the source. Hover over **From editor** to inspect and choose any saved source or result by thumbnail. The ICO workflow keeps its optional background removal setting.
