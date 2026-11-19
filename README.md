<img src="Tessera.Desktop/Assets/tessera-mark.svg" width="88" height="88" alt="Tessera mosaic T">

# Tessera

**An offline Windows workspace for 2D game assets.**

Organize project assets, clean up pixel art, turn Aseprite animations into sprite sheets, and edit textures linked to Unreal Engine 5. Tessera keeps your sources and processing history in a local library and lets you replay processing steps as macros in the app or from the command line.

[**Download Tessera for Windows x64**](https://github.com/Timbuhtuk/TESSERA/releases/latest) · [User guide](docs/USAGE.md) · [CLI reference](docs/CLI.md)

## What you can do

- **Organize assets.** Group images, moodboards and tile collections into projects and nested folders. Search by name or tag, filter by dimensions and transparency, and share an asset across projects.
- **Prepare pixel art.** Align and reduce uneven pixel grids, resize with crisp previews, import custom palettes from images, replace colors, clean up contours, and remove solid backgrounds. Compare results side by side and explore alternatives in a branching history.
- **Build assets for export.** Batch-export supported RGBA Aseprite files to PNG sprite sheets with optional JSON frame timings. Inspect repeating tiles and seams, compose layered moodboards, and generate multi-size Windows ICO files.
- **Work with textures.** Generate and paint height, normal, and ambient occlusion maps with a lighting preview. Browse linked Unreal textures, save edits back to the existing assets, and publish maps through Tessera Sync. The integration targets Unreal Editor 5.3. [Unreal setup guide](docs/UNREAL_SYNC.md)
- **Repeat a workflow.** Save a chain of edits as a macro and run it on another image in the desktop app or CLI. Apply a macro to every frame of a video with FFmpeg. [History and macros](docs/HISTORY_MACROS.md)

## Get started

Run `Tessera.exe` directly or unpack `Tessera-win-x64.zip` and launch the executable. The desktop app processes images offline and requires no .NET installation. When online, it can check GitHub for updates and install them with your confirmation. Keep `WorkspaceData` beside the executable when moving a portable installation with its library. The interface is available in English and Russian. [Workspace guide](docs/WORKSPACE_PROTOTYPE.md)

For scripting, unpack `Tessera-cli-win-x64.zip` into its own folder and run `.\tessera.exe --help`. The local Tessera plugin can also run the CLI from ChatGPT. [ChatGPT setup](docs/CHATGPT.md)

## Build from source

Build `Tessera.sln` on Windows with the .NET 8 SDK, or run `.\Build-Standalone.ps1` from the repository root to build, test and package the desktop app and CLI. [Development guide](docs/DEVELOPMENT.md) · [Architecture](docs/ARCHITECTURE.md)
