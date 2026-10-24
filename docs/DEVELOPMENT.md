# Developing Tessera

## Requirements and build

Use Windows with .NET SDK 8 or later, Windows Desktop support and the .NET 8 Runtime for checks. The `dotnet` command must be on PATH. SDKs and package caches are not stored in the repository.

Run from the repository root:

```powershell
.\Build-Standalone.ps1
```

The script builds the solution, runs algorithm, WPF and CLI checks, publishes self-contained Windows x64 executables and creates the archives. Use `-Version 1.0.6` to set a build version. Use `-SkipChecks` only when the checks have already passed for the same code.

If Tessera is running from the usual output folder, publish to a separate directory without closing the application:

```powershell
.\Build-Standalone.ps1 -OutputDirectory artifacts/release-check -Version 1.0.6
```

Relative output paths are resolved from the repository root. Executables, archives and checksums all go under the selected directory. The default remains `artifacts` for local and GitHub builds. Tests still use their own temporary directories and ignored verification output.

Individual commands:

```powershell
dotnet build Pixelizator.sln -c Release
dotnet run --project PixelArtAseprite.Tests -c Release
dotnet run --project PixelArtAlignment.Tests -c Release -- --ui --invariants
dotnet run --project Pixelizator.Cli.Tests -c Release
dotnet publish DomainColorTest -c Release -p:PublishProfile=win-x64
dotnet publish Pixelizator.Cli -c Release -p:PublishProfile=win-x64
```

Checks generate their own images and temporary libraries. They do not use the user's library or external image collections. See the [alignment evaluation protocol](../PixelArtAlignment.Tests/README.md).

## Source layout

Historical project and namespace names are retained:

- `DomainColorTest`: WPF desktop application, including its embedded assets.
- `Tessera.WpfStyles` **0.1.0**: shared WPF theme, controls and fonts restored from [NuGet.org](https://www.nuget.org/packages/Tessera.WpfStyles/0.1.0) through `PackageReference` in the desktop and UI-check projects. Run `dotnet restore Pixelizator.sln` before building; no local style project or copied DLL is required. Theme/font pack URIs retain the `Tessera.WpfStyles` assembly name. Font license texts remain under `DomainColorTest/Assets/Fonts` and ship in the desktop archive. See [package migration verification](WPF_STYLES_NUGET_QA.md).
- `PixelArtDownscale`: downscaling, quantization, palettes, sprites and [ICO export](../PixelArtDownscale/ICO.md).
- `PixelArtAlignment`: [grid alignment and cell reduction](../PixelArtAlignment/README.md).
- `PixelArtAseprite`: offline [Aseprite decoding and sprite sheet export](../PixelArtAseprite/README.md).
- `PixelArtAseprite.Tests`: synthetic format, transparency, export, limits and cancellation checks.
- `Pixelizator.Cli`: [command-line interface](CLI.md) to the image algorithms.
- `PixelArtAlignment.Tests`: algorithms, editor, library and navigation checks.
- `Pixelizator.Cli.Tests`: CLI integration and image-processing checks.
- `plugins/tessera`: [local ChatGPT plugin and skill](CHATGPT.md) for all Tessera CLI commands and options; `.agents/plugins/marketplace.json` registers the repository marketplace.
- `artifacts`: local builds, test output and archives; ignored by Git.

The editable Tessera mark and its generation script are documented in the [asset README](../DomainColorTest/Assets/README.md).

## Tile seam inspector

Library PNG writes use .NET file streams rather than GDI+ filename APIs, which fail at legacy MAX_PATH lengths. The UI suite includes source/result/crop pixel and alpha round trips plus history reopening in a library whose root exceeds 270 characters. Editor regression tests fail on unexpected owned error dialogs instead of waiting invisibly for user input.

`TileInspectorWindow.Zoom.cs` handles Ctrl+wheel with the main editor's 6.25–6400% scale steps, using the current fitted scale for the first wheel step. The deferred scroll adjustment retains the source point under the cursor and ignores stale zoom requests. Unmodified wheel input remains available for scrolling. Collection settings use an optional `ZoomScale` value and retain the original `Zoom` index for compatibility; old projects keep their original Fit/100–3200% zoom. WPF checks cover cursor anchoring, limits, multi-notch/small deltas, Fit transitions and old/new project zoom round trips.

Holding either Alt key temporarily enables the heatmap without changing the saved checkbox state or marking the project dirty. Preview key-up and window deactivation restore the selected mode; only the preview is invalidated, so holding Alt does not rerun edge analysis. Seam statistics are below the preview, while drawing/smoothing stays above it. The WPF checks exercise rendered press/release behavior, repeated keydown, both Alt keys, focus loss, persistent visibility, project settings and panel order at normal/minimum sizes.

`DomainColorTest/TileInspectorWindow.xaml(.cs)` owns the independent inspector window, collection selection, file loading and debounced directory watchers. Its `Collection` partial handles projects, export and unsaved changes; its `Painting` partial handles strokes, histories and smoothing. `MainWindow.Tiles.cs` opens one inspector per main window; `--tiles [image-path]` starts it directly. `TilePreview.cs` draws repeated A/B images and direction-specific heat strips without allocating a full repeated bitmap. Image decoding and smoothing run in the background; edge analysis updates synchronously in O(width + height) after edits. Stale loads and external saves cannot overwrite local edits.

`PixelArtDownscale/TileSeamAnalyzer.cs` compares four straight-alpha BGRA32 edge strips in O(width + height). It uses 100 × Euclidean OKLab distance after linear-sRGB compositing over black and white, keeping the larger difference. `Horizontal[y]` joins left/right at row y; `Vertical[x]` joins top/bottom at column x. The configurable threshold classifies edge differences and does not claim to certify visual seamlessness.

`TileAsset.cs` keeps BGRA pixels, a WPF bitmap, edit state and bounded per-asset undo/redo snapshots (up to 20 entries / 64 MiB, retaining at least one). `TileAsset.Compare(A, B)` compares A.right/B.left and A.bottom/B.top; reverse joins are computed independently. The checkerboard arrangement alternates A/B. Pair candidates require equal dimensions; incompatible assets remain available in the collection. `TileAssetProcessing.Smooth` delegates to `IndependentImageProcessor.ApplyNeighborColors`, preserving its color selection and alpha behavior, then optionally restores pixels outside the selected edge strip. It does not synthesize colors across asset boundaries.

`TileCollectionFile.cs` stores version-1 `.ttiles` ZIP projects containing `collection.json` and embedded PNGs. It reads and validates all images before replacing session state. Project and PNG writes use adjacent temporary files and atomic replacement; exports reject imported source paths. Embedded local-edit protection survives reopening. Original source paths serve only for optional watching/reloading; portable project images remain usable without originals.

Build the tests, then run `dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --tiles`. This checks known distances, alpha, axis mapping, the home entry, repetition modes, rendered heatmap toggling/opacity/direction, fit and fixed zoom, the minimum layout, failed loads, file writes, atomic replacement and disabling the watcher. Collection checks cover pair selection, pencil/eraser/eyedropper, stroke isolation, undo/redo, local-edit protection, portable project round trips, failed project loads, export protection and smoothing parity with the shared algorithm. Rendered captures are written under `artifacts/wpf/verification/tiles-*`. These checks also run with `--ui`.

Painting and both fills use straight-alpha source-over, with color alpha multiplied by `PaintOpacity`. Brush dabs read the stroke's original snapshot, avoiding buildup within a stroke. Flood fill uses an iterative four-neighbor traversal over the pre-edit pixels; fully transparent pixels compare equal regardless of hidden RGB. It stays within one asset and creates one undo entry. Version-1 collections accept the optional `PaintOpacity` setting (default 100, finite range 0–100). `TilePaintChecks` and the inspector WPF checks cover blending over opaque/partial/zero alpha, overlapping dabs, separate strokes, boundaries, no-op history, A/B targeting, project compatibility and translated controls.

### Tileset slicing

`PixelArtDownscale/TileSetSlicer.cs` validates tile-size/grid-count conversions, exact coverage, symmetric margins and gutters, and enumerates row-major cell rectangles. Automatic suggestions rank valid common-size layouts with a geometric prior and sampled color/alpha changes at candidate cut lines. This is a proposal, not unique grid detection; ambiguous images retain alternatives and require confirmation.

`TileSetImportWindow.xaml(.cs)` provides manual/automatic layout controls and an outlined pixel preview in `TileSetPreview.cs`. The `TileInspectorWindow.TileSet.cs` partial adds the confirmed batch to the collection. Each slice keeps an optional `AtlasSourcePath` instead of an ordinary single-image watcher path. Version-1 `.ttiles` manifests accept this optional field, retain embedded pixels, and protect the original atlas from PNG/project overwrite. Existing projects without the field remain supported.

`TileSetChecks.cs` runs with `--tiles` and `--ui`: 96×192 → 3×6 × 32×32, rectangular cells, margins/gutters, malformed grids, ambiguous automatic suggestions, byte-exact crops including alpha, optional empty-cell filtering, minimum dialog layout, collection integration, project round trips and source protection.

## Compatibility

Tessera was previously named Pixelizator. The repository is `Timbuhtuk/TESSERA`; historical solution and namespace names remain unchanged. Startup prepares the portable `WorkspaceData/Library` beside the executable. It copies existing GUID image/history directories from `%LOCALAPPDATA%\Tessera\Library` once. `LibraryLocation` first handles older `%LOCALAPPDATA%\Pixelizator\Library` entries without deleting their originals. Migration markers prevent later launches from overwriting workspace edits.

Published executables are now `Tessera.exe` and `tessera.exe`. Archive names are `Tessera-win-x64.zip` and `Tessera-cli-win-x64.zip`. Update external scripts that call the old executable name.

## Automated releases

The [Build and release workflow](../.github/workflows/release.yml) runs on pushes to `main`, including merged pull requests. Pull requests targeting `main` run the build and checks without publishing a release.

The Windows job sets up .NET 8 and Python 3.13, builds and tests the solution, publishes both executables, and packages the app, CLI, plugin and skill. Integration checks run all 18 command/alias helps and actual image operations through the plugin launcher, inspect the archives, and reject the desktop executable without starting it. SHA256SUMS.txt covers the desktop executable and all four distributable ZIPs. A separate job with `contents: write` publishes the seven release assets:

- `Tessera.exe`
- `Tessera-win-x64.zip`
- `Tessera-cli-win-x64.zip`
- `SHA256SUMS.txt`
- `ReleaseNotes.json` (the versioned, bilingual archive embedded in the desktop executable)
- `Tessera-chatgpt-plugin.zip` (local plugin)
- `Tessera-skill.zip` (standalone skill)

Versions follow `v1.0.N`, where `N` is the workflow run number; the same version is embedded in the executables. Pull request runs can leave gaps in the sequence. Release descriptions are in English and use the same current change notes as the bundled desktop catalog.

### Patch notes for every release

Update the `next` entry in `release-notes/catalog.json` with a short title and change list in Russian and English whenever preparing a release. Do not replace the version with a workflow run number or copy the previous release's changes. `Prepare-ReleaseNotes.ps1` stamps the actual version at build time and emits `ReleaseNotes.json` and `ReleaseNotes.md`. `Build-Standalone.ps1 -ReleaseRepository Timbuhtuk/TESSERA` retrieves the published history, preserving user-facing entries from the latest release's `ReleaseNotes.json` asset. The checked-in catalog supplies reviewed, bilingual descriptions for older releases; raw GitHub release bodies are never imported. This requires full Git history and tags, which the workflow checks out. Local builds without `-ReleaseRepository` work offline with the checked-in baseline history.

Patch notes describe only affected tools and their expected behavior: additions, changes, removals, changed defaults and user-visible fixes. Name the tool and explain what the user can do or should expect differently. Do not include implementation details, tests, build/deployment information, commit logs or pull requests. See [Writing patch notes](../release-notes/README.md) for the editorial rules and examples.

Preparation compares the actual `next` title and notes with the last published tag. A new version with unchanged notes fails instead of substituting commit subjects or a rebuild description; changing only historical entries does not bypass the check. Rerunning the same version may reuse its notes. Missing curated history also fails instead of importing an unedited GitHub description. The generated Markdown contains only the release title and user-facing notes and is passed to `gh release create --notes-file`. `ReleaseNotesFile` embeds that same catalog in the tested desktop assembly and published executable. The app does not fetch notes or require an API token at runtime.

`ReleaseNotesStartup` opens the owned, non-modal notes window after the main window or direct tile inspector first renders. It records the version only after the notes themselves render. `ReleaseNotesState` atomically preserves all seen versions beside the appearance settings, tolerates corrupt state, and reports failed writes without blocking the app. The Settings patch-notes tab uses the same `ReleaseNotesView` and catalog as the startup window. Release text is rendered as plain text with simple headings and bullets; no HTML, scripts or external content is loaded.

Run `PixelArtAlignment.Tests --release-notes` for first-launch/restart state, upgrades/downgrades, offline history selection, RU/EN, three themes, minimum layouts and failed writes. `tests/ReleaseNotes.Checks.ps1` checks curated notes, reruns, rejection of unchanged notes and unreviewed history, API arrays, translated archives and exact GitHub/bundle text parity with isolated fixtures. Both are mandatory in `Build-Standalone.ps1`. Rendered WPF captures are written under `artifacts/wpf/patch-notes`; physical mouse/keyboard desktop interaction remains a separate check.

The release stays a draft until all assets are uploaded. Rerunning a completed publication preserves the existing release; an interrupted draft can resume. An old build is skipped if `main` already points to a newer commit.

Manual publication is available through **Actions → Build and release → Run workflow**, selecting `main`. The workflow uses the built-in `GITHUB_TOKEN`; no additional secret is required. The latest-release link follows the most recent published release.

The desktop app checks the latest public GitHub release on startup and on demand. It reads the `Tessera.exe` asset digest from the release API, falling back to `SHA256SUMS.txt`, then verifies the downloaded file before replacing the standalone executable after exit. The running app never embeds a GitHub token. Auto-install requires a writable executable directory; `WorkspaceData` remains in place.

See also the [desktop guide](USAGE.md) and [CLI reference](CLI.md).
# Workspace experiment

`WorkspaceCatalog` and `WorkspaceBrowser` connect the image history and processors. App startup enables the workspace with portable storage and updates; `MainWindow(libraryDirectory)` remains available for isolated UI regression tests. `--workspace-library <path>` selects an explicit test library. `Build-Standalone.ps1` includes `--navigation --workspace --localization --ui --invariants` in its required checks. See the [workspace guide](WORKSPACE_PROTOTYPE.md).

The tool-improvement checks run with `PixelArtAlignment.Tests --tool-improvements`. They cover palette dithering and comparison hints, preview geometry/theme colors, replacement masks, manual background edits, cleanup/ASCII quality and project-scoped tile history. Native desktop interaction remains a separate QA gate; a build or in-process render alone does not establish it.

## Material-map editor

The material-map editor is available under the image editor's Tools menu. `PixelArtDownscale/MaterialMapDocument.cs` owns luminance Height initialization, central-difference normalized normals, eight-direction/four-distance horizon AO and independent normal/AO masks. Canonical normals use +Y up; DirectX export flips only green. Color preview lighting runs in linear light and AO modulates its ambient term. Transparent neighbors are excluded and image edges do not wrap. This is a 2D height-field approximation, not geometry baking or AI depth estimation.

`MaterialMapFile` stores versioned `.tmaps` ZIP files with a JSON header and lossless binary source/layers, validates dimensions and finite layer values, and replaces saves atomically. Stroke/reset/settings history is capped at 30 snapshots or approximately 128 MiB, retaining at least one action. `MaterialMapsWindow` provides the three paint surfaces, per-map reset, throttled preview light, source overlay, PNG/project persistence and source-path protection. It snapshots the selected source/result and holds one session per source/result, so switching assets cannot redirect existing strokes.

`MaterialMapsWindow.Viewport` synchronizes a common source-space center and scale across the 2×2 grid, including the downsampled lighting image, whose logical dimensions remain full-size. Shared header rows keep all four scroll viewports equal. `WorkspaceState.MaterialSessions` stores source/result identities and stable IDs for the three generated image assets. The library keeps the editable `.tmaps` payload internally, while `ImageLibrary.StoreMaterialMap` atomically updates only generated image bases and their metadata. Imported originals and result histories retain their existing behavior. The workspace autosave timer waits for strokes and regeneration to finish; return, main-window save and close flush pending settings. Failed persistence leaves the editor open and dirty. New assets inherit the source's folder in the active project; existing map placements are retained.

Run `dotnet run --project PixelArtAlignment.Tests -c Release -- --material-maps` for the focused checks; they also run with `--ui` in the existing release pipeline. They exercise flat/gradient/cavity fields, dimensions and alpha, normal conventions, independent masks and resets, history, soft/interpolated strokes, project roundtrips, Tools entry, PNG source protection, wide/minimum 2×2 WPF renders, all four scroll origins, fit/zoom, sibling asset placement, idle/main-menu saving, locked-file failure/retry and layer recovery after reopening the library. Physical pointer interaction remains a separate desktop verification.

## Deferred ideas

- ⭐ Generate a reusable processing macro from a selected result: recover the ordered tools and their settings, then apply the same recipe to other source images. Recorded for consideration; implementation has not been approved.
