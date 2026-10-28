# TCR finalists in Tessera

The image editor exposes **Tools → Size and grid → TCR · reduce image** with three modes and a reduction factor selector from 2× through 64×. The default is 8×.

| Mode | Method |
| --- | --- |
| T3 / p44 | Alpha-weighted mean over cells with internal boundaries at `factor*q+floor(factor/2)` on each axis. At 8× these are the original `8*q+4` laboratory boundaries. |
| P5 / OFF | R0/C0 alpha-weighted mean over regular `factor × factor` cells, without a region map or FH segmentation. |
| 50 / 50 | Equal average of the two outputs: alpha is rounded `(a+b)/2`; sRGB channels are weighted by each output alpha; RGB is zero when output alpha is zero. |

The methods use the original RGBA pixels and do not reduce the palette. At the default 8×, a 1254×1254 original produces 157×157 pixels with the laboratory boundaries unchanged. Other factors produce `ceil(width/factor) × ceil(height/factor)` pixels. For T3 on a short final cell, an internal boundary is capped so the last cell remains nonempty. The user can process either an original or a selected result; each operation stores its mode and factor in history and can be replayed as a macro step. Older recipes without a factor replay at 8×.

## Provenance and validation

- T3/p44 source: TCR-Lab commit `ce3b649abf52f18e9e84db10f6c544c30a8fd25a`, `experiments/T3/Program.cs`.
- P5/OFF source: TCR-Lab commit `8151a7614f2ae3f83caf828b51126ae9d12f0ab8`, `src/tcr_lab/r0.py`. The OFF branch does not call FH or use a region map.
- Mixture source: TCR-Lab `scripts/preview_finalist_average.py` and `reports/gallery/previews/t3-p44_p5-off_50-50/manifest.json`.
- `dotnet run --project PixelArtAlignment.Tests -- --finalists T:\Timbuhtuk\source\repos\TCR-Lab` verifies original and reference PNG SHA-256, exact decoded RGBA for all 5 subjects × 3 modes at 8×, PNG save/reopen, other factors and image sizes, legacy recipe replay, and alpha edge cases.
- `dotnet run --project PixelArtAlignment.Tests -- --finalists-ui T:\Timbuhtuk\source\repos\TCR-Lab` opens the real WPF editor, selects all three modes on Bowman, checks 4× and 8× recipe replay, exports and reopens PNG, checks the selector range, and exercises 1× and 8× preview zoom.
- The 1× and integer 8× output sheets under ignored `artifacts/finalists-verification/` were inspected for all five subjects. A screenshot of the WPF tool is under ignored `artifacts/finalists-ui-verification/`.

The T3 PNG encoder happens to reproduce the laboratory file SHA in the checked case. P5 and mixture PNG encoders differ in file bytes from Pillow, while decoded RGBA matches exactly. File SHA is verified for laboratory originals and reference outputs; Tessera output correctness is determined by exact decoded RGBA and save/reopen checks.

The owner accepted T3/p44 and P5/OFF visually at 8×. Other factors are extrapolations of these methods and do not have matching laboratory reference images. T3/p44 separately had a laboratory safety **FAIL**; that verdict is unchanged by this integration. This tool does not include H1/H3/H5/H9/I002/P5-FH or other laboratory candidates. A Tessera release has not been made by these checks.
