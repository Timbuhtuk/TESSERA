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
dotnet build Tessera.sln -c Release
dotnet run --project Tessera.Aseprite.Tests -c Release
dotnet run --project Tessera.RegressionTests -c Release -- --ui --invariants
dotnet run --project Tessera.Cli.Tests -c Release
dotnet publish Tessera.Desktop -c Release -p:PublishProfile=win-x64
dotnet publish Tessera.Cli -c Release -p:PublishProfile=win-x64
```

Checks generate their own images and temporary libraries. They do not use the user's library or external image collections. See the [alignment evaluation protocol](../Tessera.RegressionTests/README.md).

`dotnet run --project Tessera.RegressionTests -c Release -- --channels` checks RGBA channel arithmetic, fractions, clamping, unchanged channels and sources, cancellation, recipe replay, docked/floating UI, RU/EN, PNG and persisted settings. Arithmetic checks also run with `--invariants`; UI checks also run with `--ui`.

## Source layout

The [architecture guide](ARCHITECTURE.md) defines ownership and dependency direction:

- `Tessera.Models`: shared processing options, result contracts and tile layouts. It has no project references.
- `Tessera.Core`: image algorithms, grid alignment and workflows under `Imaging`, `Alignment` and `Workflows`. It references only `Tessera.Models`.
- `Tessera.Aseprite`: offline [Aseprite decoding and sprite sheet export](../Tessera.Aseprite/README.md) on plain .NET 8.
- `Tessera.Rendering`: WPF image-board PNG rendering shared by desktop and CLI.
- `Tessera.Desktop`: WPF views, UI controllers and workspace state. `Views`, `Controllers` and `Models` separate rendering, action orchestration and stored state.
- `Tessera.Cli`: [command-line interface](CLI.md) to the same core services.
- `Tessera.Aseprite.Tests`, `Tessera.RegressionTests` and `Tessera.Cli.Tests`: format, algorithm, desktop and CLI checks.
- `Tessera.WpfStyles` 0.3.0: restored from NuGet.org. If your cache contains the older local build with the same version, restore into a fresh package cache. Font licenses remain in `Tessera.Desktop/Assets/Fonts`.
- `plugins/tessera`: [local ChatGPT plugin and skill](CHATGPT.md); `.agents/plugins/marketplace.json` registers the repository marketplace.
- `artifacts`: ignored local builds, test output and archives.

See [grid alignment](../Tessera.Core/Alignment/README.md), [ICO export](../Tessera.Core/Imaging/Export/ICO.md) and [operation progress](PROGRESS.md).

The editable Tessera mark and its generation script are documented in the [asset README](../Tessera.Desktop/Assets/README.md).

## Project notes implementation

The paired source/result previews use the image with the greater pixel area as a shared display reference. Each image fits into that reference's visible bounds with its own aspect ratio.

The alternative comparison preview uses the shared zoom choices. The wheel changes zoom, middle-button dragging pans, Ctrl+wheel scrolls vertically, and Alt+wheel scrolls horizontally. Holding the comparison key shows the source. The key defaults to Space and is stored in `appearance.json`. Palette and color cleanup render temporary results through their normal processors after a 350 ms debounce. Up to 65,536 source pixels enable preview automatically when opening the tool; larger inputs require confirmation. Stale renders are discarded and never saved to history.

`BitmapFactory.CropTransparentEdges` scans alpha values and clones inclusive bounds for every pixel with alpha above zero. It preserves translucent edge pixels and returns a transparent 1 × 1 bitmap for a fully transparent input. The operation is available in history and recipes. The possible use of vectorization in [project notes](PROJECT_NOTES.md) remains an open fallback; no vectorization is used by these operations.

Run `--project-notes` (also included in `--ui`) for RU/EN docked/floating preview parity, real modal accept/decline, approval reset on input changes, comparison wheel dispatch, differing aspect ratios, scrollbar corners and pending-render cleanup. These checks use programmatic WPF and no physical input.

The local ScrollViewer template fills the corner between simultaneous horizontal and vertical scrollbars with the theme canvas brush, so it follows the dark palette.

## Tile atlas

`TileAtlasPage` owns atlas editing in `Tessera.Desktop/Views/Pages/Atlas`. Pointer and repeat behavior lives in `Tessera.Desktop/Controllers/Tools/Tiles`. `TileSetSlicer` calculates tile rectangles and ranks candidate layouts. `TileSeamAnalyzer` compares the opposite BGRA edge strips using OKLab distance after compositing transparent pixels over both black and white.

Run `dotnet run --project Tessera.RegressionTests -c Release --no-build -- --tiles` for the atlas, slicing and seam checks.

## Compatibility

Tessera uses `%LOCALAPPDATA%/Tessera/Library` as its default workspace path.

Published executables are now `Tessera.exe` and `tessera.exe`. Archive names are `Tessera-win-x64.zip` and `Tessera-cli-win-x64.zip`. Update external scripts that call the old executable name.

## Automated releases

The [Build and release workflow](../.github/workflows/release.yml) runs on pushes to `main`, including merged pull requests. Pull requests targeting `main` run the build and checks without publishing a release.

The Windows job sets up .NET 8 and Python 3.13, builds and tests the solution, publishes both executables, and packages the app, CLI, plugin and skill. Integration checks run CLI and macro help output and actual image operations through the plugin launcher, inspect the archives, and reject the desktop executable without starting it. SHA256SUMS.txt covers the desktop executable and all four distributable ZIPs. A separate job with `contents: write` publishes the seven release assets:

- `Tessera.exe`
- `Tessera-win-x64.zip`
- `Tessera-cli-win-x64.zip`
- `SHA256SUMS.txt`
- `ReleaseNotes.json` (the versioned, bilingual archive embedded in the desktop executable)
- `Tessera-chatgpt-plugin.zip` (local plugin)
- `Tessera-skill.zip` (standalone skill)

The release series starts at `2.0.0` in `release-version.txt`. `Get-ReleaseVersion.ps1` selects the next patch after the highest tag in that major/minor series merged into the current commit; a rerun of a tagged commit keeps its version. PR builds do not consume version numbers. Change the version floor to start a new major/minor series. The workflow passes one resolved version to both the build and publication jobs, so tags, executable versions and embedded notes agree. `tests/ReleaseVersion.Checks.ps1` covers numbering with real isolated Git histories. Release descriptions are in English and use the same current change notes as the bundled desktop catalog.

### Patch notes for every release

Update the `next` entry in `release-notes/catalog.json` with a short title and change list in Russian and English whenever preparing a release. Do not replace the version with a workflow run number or copy the previous release's changes. `Prepare-ReleaseNotes.ps1` stamps the actual version at build time and emits `ReleaseNotes.json` and `ReleaseNotes.md`. `Build-Standalone.ps1 -ReleaseRepository Timbuhtuk/TESSERA` retrieves the published history, preserving user-facing entries from the latest release's `ReleaseNotes.json` asset. The checked-in catalog supplies reviewed, bilingual descriptions for older releases; raw GitHub release bodies are never imported. This requires full Git history and tags, which the workflow checks out. Local builds without `-ReleaseRepository` work offline with the checked-in baseline history.

Patch notes describe only affected tools and their expected behavior: additions, changes, removals, changed defaults and user-visible fixes. Name the tool and explain what the user can do or should expect differently. Do not include implementation details, tests, build/deployment information, commit logs or pull requests. See [Writing patch notes](../release-notes/README.md) for the editorial rules and examples.

Preparation compares the actual `next` title and notes with the last published tag. A new version with unchanged notes fails instead of substituting commit subjects or a rebuild description; changing only historical entries does not bypass the check. Rerunning the same version may reuse its notes. Missing curated history also fails instead of importing an unedited GitHub description. The generated Markdown contains only the release title and user-facing notes and is passed to `gh release create --notes-file`. `ReleaseNotesFile` embeds that same catalog in the tested desktop assembly and published executable. The app does not fetch notes or require an API token at runtime.

`ReleaseNotesStartup` opens the owned, non-modal notes window after the main window or direct tile inspector first renders. It records the version only after the notes themselves render. `ReleaseNotesState` atomically preserves all seen versions beside the appearance settings, tolerates corrupt state, and reports failed writes without blocking the app. The Settings patch-notes tab uses the same `ReleaseNotesView` and catalog as the startup window. Release text is rendered as plain text with simple headings and bullets; no HTML, scripts or external content is loaded.

Run `Tessera.RegressionTests --release-notes` for first-launch/restart state, upgrades/downgrades, offline history selection, RU/EN, three themes, minimum layouts and failed writes. `tests/ReleaseNotes.Checks.ps1` checks curated notes, reruns, rejection of unchanged notes and unreviewed history, API arrays, translated archives and exact GitHub/bundle text parity with isolated fixtures. Both are mandatory in `Build-Standalone.ps1`. Rendered WPF captures are written under `artifacts/wpf/patch-notes`; physical mouse/keyboard desktop interaction remains a separate check.

The release stays a draft until all assets are uploaded. Rerunning a completed publication preserves the existing release; an interrupted draft can resume. An old build is skipped if `main` already points to a newer commit.

Manual publication is available through **Actions → Build and release → Run workflow**, selecting `main`. The workflow uses the built-in `GITHUB_TOKEN`; no additional secret is required. The latest-release link follows the most recent published release.

The desktop app checks the latest public GitHub release on startup and on demand. It reads the `Tessera.exe` asset digest from the release API, using the public release page and `SHA256SUMS.txt` if the API is rate-limited, then verifies the downloaded file before replacing the standalone executable after exit. The running app never embeds a GitHub token. Auto-install requires a writable executable directory.

See also the [desktop guide](USAGE.md) and [CLI reference](CLI.md).
# Workspace experiment

`WorkspaceCatalog` and `WorkspaceBrowser` connect the image history and processors. App startup enables the workspace with portable storage and updates; `MainWindow(libraryDirectory)` remains available for isolated UI regression tests. `--workspace-library <path>` selects an explicit test library. `Build-Standalone.ps1` includes `--navigation --workspace --localization --ui --invariants` in its required checks. See the [workspace guide](WORKSPACE_PROTOTYPE.md).

The tool-improvement checks run with `Tessera.RegressionTests --tool-improvements`. They cover palette dithering and comparison hints, preview geometry/theme colors, replacement masks, manual background edits, cleanup/ASCII quality and project-scoped tile history. Native desktop interaction remains a separate QA gate; a build or in-process render alone does not establish it.

## Material-map editor

The material-map editor is available under the image editor's Tools menu. `Tessera.Core/Imaging/Materials/MaterialMapDocument.cs` owns luminance Height initialization, central-difference normalized normals, eight-direction/four-distance horizon AO and independent normal/AO masks. Canonical normals use +Y up; DirectX export flips only green. Color preview lighting runs in linear light and AO modulates its ambient term. Transparent neighbors are excluded and image edges do not wrap. This is a 2D height-field approximation, not geometry baking or AI depth estimation.

`MaterialMapFile` stores versioned `.tmaps` ZIP files with a JSON header and lossless binary source/layers, validates dimensions and finite layer values, and replaces saves atomically. Stroke/reset/settings history is capped at 30 snapshots or approximately 128 MiB, retaining at least one action. `MaterialMapsWindow` provides the three paint surfaces, per-map reset, throttled preview light, source overlay, PNG/project persistence and source-path protection. It snapshots the selected source/result and holds one session per source/result, so switching assets cannot redirect existing strokes.

`MaterialMapsWindow.Viewport` synchronizes a common source-space center and scale across the 2×2 grid, including the downsampled lighting image, whose logical dimensions remain full-size. Shared header rows keep all four scroll viewports equal. `WorkspaceState.MaterialSessions` stores source/result identities and stable IDs for the three generated image assets. The library keeps the editable `.tmaps` payload internally, while `ImageLibrary.StoreMaterialMap` atomically updates only generated image bases and their metadata. Imported originals and result histories retain their existing behavior. The workspace autosave timer waits for strokes and regeneration to finish; return, main-window save and close flush pending settings. Failed persistence leaves the editor open and dirty. New assets inherit the source's folder in the active project; existing map placements are retained.

Run `dotnet run --project Tessera.RegressionTests -c Release -- --material-maps` for the focused checks; they also run with `--ui` in the existing release pipeline. They exercise flat/gradient/cavity fields, dimensions and alpha, normal conventions, independent masks and resets, history, soft/interpolated strokes, project roundtrips, Tools entry, PNG source protection, wide/minimum 2×2 WPF renders, all four scroll origins, fit/zoom, sibling asset placement, idle/main-menu saving, locked-file failure/retry and layer recovery after reopening the library. Physical pointer interaction remains a separate desktop verification.

## Deferred ideas

- ⭐ Generate a reusable processing macro from a selected result: recover the ordered tools and their settings, then apply the same recipe to other source images. Recorded for consideration; implementation has not been approved.
