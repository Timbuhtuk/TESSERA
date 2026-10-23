# Tessera WPF styles package migration

Verified on Windows on 2026-10-23 in `codex/material-maps`.

## Dependency

Both `DomainColorTest` and `PixelArtAlignment.Tests` use `PackageReference` to [Tessera.WpfStyles 0.1.0](https://www.nuget.org/packages/Tessera.WpfStyles/0.1.0). The previously checked-in `DomainColorTest/Lib/Tessera.WpfStyles.dll` and its two file references were removed. No local style-library checkout is required.

The assembly name remains `Tessera.WpfStyles`, so existing theme and font resource URIs continue to work. The desktop publish retains both font license texts under `Assets/Fonts`.

## Verification

- Restored all solution projects from NuGet.org into a fresh, isolated package cache, with the copied DLL already removed. The package metadata records `https://api.nuget.org/v3/index.json` as its source.
- Clean Release solution build: **0 warnings, 0 errors**.
- Full processing, WPF, material-map, navigation, workspace, localization, release-note and invariant regression run: **78 PASS, 0 FAIL**.
- Both desktop and test output dependency manifests identify `Tessera.WpfStyles/0.1.0` as a package. Both output DLLs match the restored package assembly's SHA-256: `42425084F19FA61329E0870F859646B58FC9CFAC86CB73F6A382B986618ED41D`.
- Windows x64 self-contained single-file publish succeeded. Launched the exact executable from `artifacts/nuget-migration/publish/Tessera.exe` and verified its process path. Its library, release-note dialog and settings rendered successfully. Switched through Light, Contrast and Dark themes using the actual Windows UI; surfaces, controls, text and theme selection updated successfully.
- No direct style DLL or style-project references remain in the build files. Font licenses remain in the standalone output.
- All **82** pre-existing modified/untracked files recorded in the primary checkout's preservation manifest retained their hashes.

Logs, the isolated package cache, standalone executable and three native theme screenshots are under the ignored `artifacts/nuget-migration` directory. This is a local Windows verification; no GitHub Actions run, merge or release is claimed.

## Commands

Run from the repository root in PowerShell:

```powershell
dotnet restore Pixelizator.sln --source https://api.nuget.org/v3/index.json --packages artifacts/nuget-migration/packages --no-http-cache --force --nologo
dotnet clean Pixelizator.sln -c Release --nologo
dotnet build Pixelizator.sln -c Release --no-restore -m:1 --nologo
dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --release-notes --tool-improvements --navigation --workspace --localization --ui --invariants

$env:NUGET_PACKAGES = Join-Path (Get-Location) 'artifacts/nuget-migration/packages'
$publishPath = Join-Path (Get-Location) 'artifacts/nuget-migration/publish/'
dotnet publish DomainColorTest/DomainColorTest.csproj -c Release -p:PublishProfile=win-x64 "-p:PublishDir=$publishPath" -p:RestoreSources=https://api.nuget.org/v3/index.json --nologo
```
