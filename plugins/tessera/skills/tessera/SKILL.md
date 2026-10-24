---
name: tessera
description: Process images and run saved Tessera macros through the local Tessera CLI. Use when the user asks to use Tessera, its image-processing commands, or its saved processing recipes. Does not cover developing Tessera or operations available only in its desktop UI.
---

# Tessera

Run the user's image-processing request through the actual Tessera CLI. All CLI arguments are available; the launcher does not maintain a command or option allowlist. Let the installed CLI validate its own arguments.

## Connect to the local CLI

This skill requires a Windows computer with local command execution and Python 3.10 or later. Use the host's bundled Python when available. A cloud-only environment cannot run the user's Windows executable: switch to a connected Windows/local environment before processing.

Resolve `scripts/tessera.py` relative to this SKILL.md, then run it with the available Python interpreter. In the examples, replace `<skill>` with this skill's absolute directory.

```powershell
python "<skill>/scripts/tessera.py" --locate
python "<skill>/scripts/tessera.py" -- --version
python "<skill>/scripts/tessera.py" -- --help
```

The launcher looks for an explicit `--cli` path, `TESSERA_CLI`, saved local settings, a bundled `bin/tessera.exe`, repository CLI outputs, and PATH, in that order. It distinguishes the console CLI from the similarly named desktop application without starting either one.

If discovery fails, locate the user's unpacked **Tessera-cli-win-x64** release or this repository's `artifacts/cli/tessera.exe`. Save a verified path once, or pass it for a single call:

```powershell
python "<skill>/scripts/tessera.py" --configure "C:/Tools/Tessera-cli/tessera.exe"
python "<skill>/scripts/tessera.py" --cli "C:/Tools/Tessera-cli/tessera.exe" -- --help
```

`--configure` stores only the executable path in `%LOCALAPPDATA%/Tessera/chatgpt-cli.json`. `--config PATH` selects alternate settings for isolated setups. If the executable is absent, the Windows x64 CLI is available from [Tessera releases](https://github.com/Timbuhtuk/TESSERA/releases/latest); a source checkout can also publish `Pixelizator.Cli`. Do not substitute the desktop Tessera.exe.

When local execution redirects `LOCALAPPDATA` and that location has no settings, the launcher also checks the existing `~/AppData/Local/Tessera/chatgpt-cli.json`. Settings in the current `LOCALAPPDATA` take precedence; an explicit `--config` selects only that file.

## Choose and run the operation

Read the installed command's help before choosing unfamiliar flags. The bundled [CLI reference](references/cli.md) explains algorithms, options, constraints, and examples; live `--help` takes precedence if versions differ.

| Goal | CLI arguments before the file/options |
| --- | --- |
| Combined size and palette processing | No subcommand; start with the image path |
| Align an uneven pixel grid | `align` |
| One pixel per already aligned cell | `reduce-grid` or `compact-grid` |
| Change size independently | `resize` |
| Quantize colors or apply a palette independently | `colors` |
| Clean up nearby shades and protect details and edges | `smooth-colors` or `smooth` |
| Convert visible pixels to grayscale | `monochrome` or `mono` |
| Export ASCII text or a rendered ASCII image | `ascii` |
| Count and list visible colors | `palette-info` |
| Replace one exact RGB color | `replace-color` |
| Remove a solid background | `remove-background` or `remove-bg` |
| Export a multi-size Windows icon | `ico` or `icon` |
| Export Aseprite animations to PNG and JSON | `aseprite` |
| List saved processing macros in a library | `macro list --library PATH` |
| Apply a saved macro to an image | `macro apply INPUT --macro FILE -o OUTPUT` |

Always place launcher options before the first `--` and pass every Tessera argument after it, as a separate argument. This also supports future CLI commands and options. Preserve filenames containing spaces, Cyrillic, or shell metacharacters; do not build or evaluate a shell command from a filename.

```powershell
python "<skill>/scripts/tessera.py" -- resize "C:/Images/source.png" -o "C:/Images/result.png" --width 64 --height 64 --sprite --json
python "<skill>/scripts/tessera.py" -- align "C:/Images/source.png" -o "C:/Images/aligned.png" --cell-size 4 --json
python "<skill>/scripts/tessera.py" -- reduce-grid "C:/Images/aligned.png" -o "C:/Images/pixels.png" --cell-size 4 --json
python "<skill>/scripts/tessera.py" -- aseprite "C:/Images/walk.aseprite" "C:/Images/idle.aseprite" --output-dir "C:/Images/sheets" --layout grid --columns 4 --inspection --json
```

Use absolute input and output paths, or `--cwd DIR` before the separator for a known working directory. Prefer explicit output paths and `--json` when supported. Keep the user's requested settings; choose ordinary defaults for unspecified details. Preserve sources and use a fresh output name when an existing result is not meant to be replaced. Use `--overwrite` when replacement is requested; Aseprite reports existing outputs instead of overwriting them.

For an alignment-and-reduction request, run `align` first, read its reported `cellSize`, then pass that value to `reduce-grid`. `resize` changes geometry without quantization; `colors` keeps dimensions. Solid-background removal is color/tolerance based. For capability requests beyond the table, consult current help and use the CLI if it supports them; do not infer UI-only capability from this skill.

## Saved macros

Read [the macro reference](references/macros.md) when listing, choosing, applying, or preparing a saved recipe. Check `macro --help` on the selected CLI first: an older executable can support ordinary commands while lacking macros. Use a current build if macro help is unavailable; refreshing the skill alone does not update the executable.

Pass the library root to `macro list`, then use the actual JSON file for the selected macro in `macro apply`. Inspect listing `warnings` as well as `macros`; invalid files can be skipped while the command succeeds. Listing is read-only, and an empty or missing macro directory returns an empty list. Do not invent a macro or replace the user's requested steps when no match exists.

```powershell
python "<skill>/scripts/tessera.py" -- macro list --library "C:/Images/TesseraLibrary" --json
python "<skill>/scripts/tessera.py" -- macro apply "C:/Images/source.png" --macro "C:/Images/TesseraLibrary/macros/recipe.json" -o "C:/Images/result.png" --json
```

The CLI and editor use the same version-1 workflow executor. A macro is a flattened sequence of image operations, validated before processing, with intermediate images kept in memory and only the final image saved. Keep the selected recipe intact. Prefer PNG for transparency and pixel fidelity. This CLI does not create macros, edit the editor's history trays, or save intermediate history entries.

## Return the result

The launcher preserves the CLI's stdout, stderr, and exit code. CLI codes are 0 for success, 1 for file/processing failure, and 2 for invalid arguments. Launcher setup errors use 3. Correct an invalid parameter using help; report missing inputs or unsupported content without claiming a result exists. For Aseprite, inspect every JSON entry because a batch can contain both successes and failures.

Check that reported output files exist, inspect the resulting image when visual quality matters, and return clickable absolute paths with a brief description of the processing. Show the saved image when useful. Distinguish successful file processing from any desktop UI behavior that was not observed.
