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
- `DomainColorTest/Lib/Tessera.WpfStyles.dll`: checked-in shared WPF theme and fonts. Desktop and UI checks reference this assembly; keep it in the repository so clean CI builds can resolve it. Font license texts remain under `DomainColorTest/Assets/Fonts` and ship in the desktop archive.
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

`TileInspectorWindow.Zoom.cs` handles Ctrl+wheel with the main editor's 6.25–6400% scale steps, using the current fitted scale for the first wheel step. The deferred scroll adjustment retains the source point under the cursor and ignores stale zoom requests. Unmodified wheel input remains available for scrolling. Collection settings use an optional `ZoomScale` value and retain the original `Zoom` index for compatibility; old projects keep their original Fit/100–3200% zoom. WPF checks cover cursor anchoring, limits, multi-notch/small deltas, Fit transitions and old/new project zoom round trips.

Holding either Alt key temporarily enables the heatmap without changing the saved checkbox state or marking the project dirty. Preview key-up and window deactivation restore the selected mode; only the preview is invalidated, so holding Alt does not rerun edge analysis. Seam statistics are below the preview, while drawing/smoothing stays above it. The WPF checks exercise rendered press/release behavior, repeated keydown, both Alt keys, focus loss, persistent visibility, project settings and panel order at normal/minimum sizes.

`DomainColorTest/TileInspectorWindow.xaml(.cs)` owns the independent inspector window, collection selection, file loading and debounced directory watchers. Its `Collection` partial handles projects, export and unsaved changes; its `Painting` partial handles strokes, histories and smoothing. `MainWindow.Tiles.cs` opens one inspector per main window; `--tiles [image-path]` starts it directly. `TilePreview.cs` draws repeated A/B images and direction-specific heat strips without allocating a full repeated bitmap. Image decoding and smoothing run in the background; edge analysis updates synchronously in O(width + height) after edits. Stale loads and external saves cannot overwrite local edits.

`PixelArtDownscale/TileSeamAnalyzer.cs` compares four straight-alpha BGRA32 edge strips in O(width + height). It uses 100 × Euclidean OKLab distance after linear-sRGB compositing over black and white, keeping the larger difference. `Horizontal[y]` joins left/right at row y; `Vertical[x]` joins top/bottom at column x. The configurable threshold classifies edge differences and does not claim to certify visual seamlessness.

`TileAsset.cs` keeps BGRA pixels, a WPF bitmap, edit state and bounded per-asset undo/redo snapshots (up to 20 entries / 64 MiB, retaining at least one). `TileAsset.Compare(A, B)` compares A.right/B.left and A.bottom/B.top; reverse joins are computed independently. The checkerboard arrangement alternates A/B. Pair candidates require equal dimensions; incompatible assets remain available in the collection. `TileAssetProcessing.Smooth` delegates to `IndependentImageProcessor.ApplyNeighborColors`, preserving its color selection and alpha behavior, then optionally restores pixels outside the selected edge strip. It does not synthesize colors across asset boundaries.

`TileCollectionFile.cs` stores version-1 `.ttiles` ZIP projects containing `collection.json` and embedded PNGs. It reads and validates all images before replacing session state. Project and PNG writes use adjacent temporary files and atomic replacement; exports reject imported source paths. Embedded local-edit protection survives reopening. Original source paths serve only for optional watching/reloading; portable project images remain usable without originals.

Build the tests, then run `dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --tiles`. This checks known distances, alpha, axis mapping, the home entry, repetition modes, rendered heatmap toggling/opacity/direction, fit and fixed zoom, the minimum layout, failed loads, file writes, atomic replacement and disabling the watcher. Collection checks cover pair selection, pencil/eraser/eyedropper, stroke isolation, undo/redo, local-edit protection, portable project round trips, failed project loads, export protection and smoothing parity with the shared algorithm. Rendered captures are written under `artifacts/wpf/verification/tiles-*`. These checks also run with `--ui`.

### Tileset slicing

`PixelArtDownscale/TileSetSlicer.cs` validates tile-size/grid-count conversions, exact coverage, symmetric margins and gutters, and enumerates row-major cell rectangles. Automatic suggestions rank valid common-size layouts with a geometric prior and sampled color/alpha changes at candidate cut lines. This is a proposal, not unique grid detection; ambiguous images retain alternatives and require confirmation.

`TileSetImportWindow.xaml(.cs)` provides manual/automatic layout controls and an outlined pixel preview in `TileSetPreview.cs`. The `TileInspectorWindow.TileSet.cs` partial adds the confirmed batch to the collection. Each slice keeps an optional `AtlasSourcePath` instead of an ordinary single-image watcher path. Version-1 `.ttiles` manifests accept this optional field, retain embedded pixels, and protect the original atlas from PNG/project overwrite. Existing projects without the field remain supported.

`TileSetChecks.cs` runs with `--tiles` and `--ui`: 96×192 → 3×6 × 32×32, rectangular cells, margins/gutters, malformed grids, ambiguous automatic suggestions, byte-exact crops including alpha, optional empty-cell filtering, minimum dialog layout, collection integration, project round trips and source protection.

## Compatibility

Tessera was previously named Pixelizator. The repository is `Timbuhtuk/TESSERA`; historical solution and namespace names remain unchanged. Startup prepares the portable `WorkspaceData/Library` beside the executable. It copies existing GUID image/history directories from `%LOCALAPPDATA%\Tessera\Library` once. `LibraryLocation` first handles older `%LOCALAPPDATA%\Pixelizator\Library` entries without deleting their originals. Migration markers prevent later launches from overwriting workspace edits.

Published executables are now `Tessera.exe` and `tessera.exe`. Archive names are `Tessera-win-x64.zip` and `Tessera-cli-win-x64.zip`. Update external scripts that call the old executable name.

## Automated releases

The [Build and release workflow](../.github/workflows/release.yml) runs on pushes to `main`, including merged pull requests. Pull requests targeting `main` run the build and checks without publishing a release.

The Windows job builds and tests the solution, publishes both executables, packages them and smoke-tests the standalone CLI. A separate job with `contents: write` publishes the four release assets:

- `Tessera.exe`
- `Tessera-win-x64.zip`
- `Tessera-cli-win-x64.zip`
- `SHA256SUMS.txt`

Versions follow `v1.0.N`, where `N` is the workflow run number; the same version is embedded in the executables. Pull request runs can leave gaps in the sequence. Release descriptions are in English and include GitHub-generated change notes.

The release stays a draft until all assets are uploaded. Rerunning a completed publication preserves the existing release; an interrupted draft can resume. An old build is skipped if `main` already points to a newer commit.

Manual publication is available through **Actions → Build and release → Run workflow**, selecting `main`. The workflow uses the built-in `GITHUB_TOKEN`; no additional secret is required. The latest-release link follows the most recent published release.

The desktop app checks the latest public GitHub release on startup and on demand. It reads the `Tessera.exe` asset digest from the release API, falling back to `SHA256SUMS.txt`, then verifies the downloaded file before replacing the standalone executable after exit. The running app never embeds a GitHub token. Auto-install requires a writable executable directory; `WorkspaceData` remains in place.

See also the [desktop guide](USAGE.md) and [CLI reference](CLI.md).
# Workspace experiment

`WorkspaceCatalog` and `WorkspaceBrowser` connect the image history and processors. App startup enables the workspace with portable storage and updates; `MainWindow(libraryDirectory)` remains available for isolated UI regression tests. `--workspace-library <path>` selects an explicit test library. `Build-Standalone.ps1` includes `--navigation --workspace --localization --ui --invariants` in its required checks. See the [workspace guide](WORKSPACE_PROTOTYPE.md).

The tool-improvement checks run with `PixelArtAlignment.Tests --tool-improvements`. They cover palette dithering and comparison hints, preview geometry/theme colors, replacement masks, manual background edits, cleanup/ASCII quality and project-scoped tile history. Native desktop interaction remains a separate QA gate; a build or in-process render alone does not establish it.

## Deferred ideas

- ⭐ Generate a reusable processing macro from a selected result: recover the ordered tools and their settings, then apply the same recipe to other source images. Recorded for consideration; implementation has not been approved.
