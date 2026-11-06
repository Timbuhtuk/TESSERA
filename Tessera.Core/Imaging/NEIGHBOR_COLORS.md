# Local 3×3 color processing

`IndependentImageProcessor.ApplyNeighborColors(source, options)` is a separate
operation that preserves dimensions and alpha. It uses existing image colors
without quantization, a color-count limit, preset palettes or dithering.

Each pass compares a pixel with visible colors in its 3×3 neighborhood. The
transition cost considers LAB color distance and the number of neighbors with
each color. Optional `DownscaleOptions.LocalColorCriteria` adds preferences for
brightness, contrast, saturation and edges. These preferences select existing
colors; they do not recolor the finished image.

Each completed pass becomes the input for the next. A pixel keeps its color if
no candidate improves the cost. Processing stops early if a pass changes nothing.
`DownscaleOptions.LocalColorPasses` accepts 1–100 passes and defaults to 1.

The editor exposes this operation in **Smoothing**, with its own criteria and pass
count. It uses the selected source or result as input and saves a new history
entry. The pass count and local criteria are saved and restored with the other
`DownscaleOptions`, independently of the **Size** and **Palette** controls.
