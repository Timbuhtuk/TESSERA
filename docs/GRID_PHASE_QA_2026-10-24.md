# Grid phase recovery acceptance · 2026-10-24

Integrated from the three explicitly authorized files into an isolated checkout based on released `main` / v1.0.55 (`2fcf391da1f56b880505ca095821a1af8cc7609c`). The primary checkout's other work and the original diagnostic projects/images were not included in the commit.

The adaptive grid now follows neighboring boundaries without jumping a whole cell at phase wrapping or drift limits. Initial half-period ambiguity and incoherent background noise are handled before following the contour. Rigid alignment remains unchanged.

## Repeated integrated-code checks

- Release solution build: **0 warnings, 0 errors**.
- Existing invariants and **16/16** perfect-grid detections passed. Added coverage requires **10 exact reconstructions**, **3 narrow images** and **3 noisy contour masks**, preserving input bytes, dimensions, source RGBA samples and idempotence.
- CLI integration: **75 passed, 0 failed**. Release-note preparation checks passed. TEMP/TMP were kept inside the isolated checkout's ignored artifacts.
- Repeated the original **336 comparisons** against the unchanged baseline: **65 improvements, 0 regressions**. No acceptance thresholds were reduced.

| Dataset | Cases | Mean before | Mean after | Worst after | Original gate |
|---|---:|---:|---:|---:|---|
| Development | 96 | 95.9325 | 97.0903 | 76.4368 | PASS |
| Holdout | 96 | 97.1073 | 98.4864 | 84.6154 | PASS |
| Fresh | 96 | 96.5342 | 97.7861 | 76.6571 | PASS |
| Additional probe | 48 | 97.1562 | 98.2327 | 74.2133 | FAIL — accepted existing limits |

## Explicitly accepted existing limits

The strict combined diagnostic **returns exit code 1**, because the additional probe still contains two cases below the unchanged minimum of 75: `style=0`, `pitch=8`, `seed=101`, `distortionSeed=77`, distortions `unequal-widths` and `contamination`. Each scores **74.21328488797009 both before and after**. These are pre-existing errors in fine contour/marker reconstruction. The user viewed their comparisons and explicitly accepted release with these limitations; they are not counted as passes or removed from the report.

The normal release pipeline's invariant/processing checks and this extra diagnostic are separate. A successful release CI does not mean that all 336 diagnostic examples satisfy the quality floor.

## Real CLI image acceptance and scope

The integrated CLI is compared with the user-approved outputs: automatic cell size 5 equals explicit 5 px, two previously missing dark cells return, canvas/source RGBA are preserved, the original file hash stays unchanged, and rigid output matches the previous implementation. Three reference controls reconstruct exactly; all 12 noisy arrow variants retain their dark contour. Both known-limit images must remain identical to the accepted outputs. These checks are repeated on the downloaded published CLI before final delivery.

Native Windows UI was not exercised for this algorithm-only change. The release pipeline still runs its full automated WPF regression suite. Visual approval of the saved algorithm comparisons occurred in the originating task; it is distinct from desktop interaction.

Ignored integrated evidence: `artifacts/grid-phase-release/` (`build.log`, `invariants.log`, `cli.log`, `matrix-summary.json`, `matrix/matrix.json`, `cli-images/verification.json`). Original evidence remains under the primary checkout's `artifacts/pixelization-diagnosis-20261024/final/` and `known-limits-visual/`. The user's PNG and diagnostic projects are not release sources.
