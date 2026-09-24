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
- `PixelArtDownscale`: downscaling, quantization, palettes, sprites and [ICO export](../PixelArtDownscale/ICO.md).
- `PixelArtAlignment`: [grid alignment and cell reduction](../PixelArtAlignment/README.md).
- `PixelArtAseprite`: offline [Aseprite decoding and sprite sheet export](../PixelArtAseprite/README.md).
- `PixelArtAseprite.Tests`: synthetic format, transparency, export, limits and cancellation checks.
- `Pixelizator.Cli`: [command-line interface](CLI.md) to the image algorithms.
- `PixelArtAlignment.Tests`: algorithms, editor, library and navigation checks.
- `Pixelizator.Cli.Tests`: CLI integration and image-processing checks.
- `artifacts`: local builds, test output and archives; ignored by Git.

The editable Tessera mark and its generation script are documented in the [asset README](../DomainColorTest/Assets/README.md).

## Compatibility

Tessera was previously named Pixelizator. The repository is now `Timbuhtuk/TESSERA`; historical solution and namespace names remain unchanged. The library now lives at `%LOCALAPPDATA%\Tessera\Library`. On first launch, `LibraryLocation` copies entries from `%LOCALAPPDATA%\Pixelizator\Library` without deleting the old directory. A marker in the Tessera data directory prevents removed entries from being imported again on later launches.

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

The desktop app checks the latest public GitHub release on startup and on demand. It reads the `Tessera.exe` asset digest from the release API, falling back to `SHA256SUMS.txt`, then verifies the downloaded file before replacing the standalone executable after exit. The running app never embeds a GitHub token. Auto-install requires a writable executable directory; the library remains in `%LOCALAPPDATA%\Tessera\Library`.

See also the [desktop guide](USAGE.md) and [CLI reference](CLI.md).
