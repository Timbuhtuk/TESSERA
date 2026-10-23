# Material maps verification — 2026-10-23

Branch: `codex/material-maps`, based on `origin/main` at `d13a6da`. The original dirty checkout is preserved; implementation and validation use `artifacts/material-maps`.

## Implemented behavior

- Access from the active image asset editor through **Tools → Height / Normal / AO maps…**; source/result selection is respected. Each source/result retains its own open session.
- Automatic local generation at full source dimensions, with unchanged source alpha. All three maps have paint canvases beside each other; the lighting preview is below them.
- Direct Height/Normal/AO painting, square/soft brush, strength, size, grayscale value, normal hemisphere, eyedropper and eraser. Height recalculates both bases; Normal/AO manual layers survive independently.
- Independent and undoable Normal/AO layer reset, undo/redo, fit/explicit zoom, pixel grid and source overlay. Lighting can toggle Normal/AO, show the source and move with the mouse.
- Lossless portable `.tmaps` projects and individual/all-map PNG export. Source/library overwrite protection and unsaved-work guards. Russian/English UI and DirectX/OpenGL normal output.

## Passed

- Release solution build: **0 warnings, 0 errors**.
- WPF, processing invariants, navigation, workspace, localization and tool-improvement regression run: **77 PASS, 0 FAIL**. The seven material-map checks are included in `--ui` and thus the existing release pipeline.
- CLI: **67 passed, 0 failed**. Aseprite: **10 passed, 0 failed**.
- Bilingual next-release notes were added without changing historical entries. Release-note preparation fixtures and the WPF bundled-reader/history checks passed after the catalog change.
- Final focused material-map run after the Ctrl+S text-input handling adjustment: **12 PASS, 0 FAIL** (seven material-map checks plus five existing evaluator checks).
- Material cases: flat/gradient/cavity generation; 1-pixel axes, rectangular and >4096-pixel dimensions; 1024×1024 generation; normalized vectors and green-channel convention; transparent/partial alpha; independent layers after Height changes and resets; stroke interpolation, soft falloff and erasure; undo/redo; exact project roundtrip and invalid-file rejection.
- In-process WPF windows: Tools command availability and dispatch, session reuse, source/result isolation, all four canvases, Height-controller update, independent reset/undo, pending-slider save, project save, PNG dimensions/alpha and source overwrite rejection. Wide and 860×620 windows were rendered and visually inspected.
- On this machine, 1024×1024 initial generation plus a full Height regeneration took about **41 ms** in the final focused run; this is a local sample, not a cross-machine performance guarantee.

Commands and logs are under the isolated checkout's ignored `artifacts/`: `material-map-regression-desktop.log`, `material-map-cli.log`, `material-map-focused-final.log`, and `material-map-ui-*/wide.png` / `compact.png`.

## Limits and unperformed checks

- A first sandboxed full run exposed missing checkbox tooltips, which were added. Its existing ICO cursor test also failed because cursor positioning was unavailable. The subsequent desktop-enabled full run passed all 77 checks without modifying that test.
- Separate physical mouse/keyboard testing through Computer Use was **not completed**: launching the newly built EXE returned `Computer Use app approval timed out`, and no test app window opened. In-process WPF execution/renders are not claimed as this separate interaction check.
- The 82 pre-existing modified/untracked files recorded in the primary checkout's preservation manifest retained their SHA-256 hashes.
- Height starts from luminance and AO is a height-field approximation. Atlases are processed as whole images; automatic cell/animation inference and 3D geometry baking are outside this implementation. Large-image limits depend on memory and the image codec; lighting preview sampling is capped at 512 pixels on its longest axis, while maps/export remain full-size.
- No merge into `main`, release or deployment is performed by this feature task.
