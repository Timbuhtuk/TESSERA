# Saved Tessera macros

Use `macro --help`, `macro list --help`, and `macro apply --help` on the selected executable. These commands require the CLI with the version-1 `PixelArtWorkflows` executor; earlier Tessera builds may lack them.

## Find and apply a macro

```powershell
python "<skill>/scripts/tessera.py" -- macro list --library "C:/Images/Library" --json
python "<skill>/scripts/tessera.py" -- macro apply "C:/Images/source.png" --macro "C:/Images/Library/macros/recipe.json" -o "C:/Images/result.png" --json
```

Replace `<skill>` with this skill's absolute directory. Launcher options such as `--cli`, `--config`, and `--cwd` go before the separator; every Tessera argument goes after it.

`--library` is the library root, not its `macros` subdirectory. Listing scans only `<library>/macros/*.json` and returns `process`, `library`, `macroDirectory`, `count`, `warnings`, and `macros`. Each entry has `id`, `name`, `version`, `stepCount`, and `operations`. A missing directory returns zero macros without creating it. Bad files are skipped with warnings even when the exit code is zero; report relevant warnings. Listing validates the saved definition's metadata, while execution also validates every operation and its parameters.

Find the selected macro's actual JSON file under `macroDirectory` and check its `id` and `name`. Editor-saved files normally use the ID without hyphens as the filename, but a manually supplied file can have another filename. The apply command takes a file path, not an ID or display name.

`macro apply` requires an input, `--macro FILE`, and `-o OUTPUT` (or `--output OUTPUT`). PNG, JPG/JPEG, and BMP output are supported. Use PNG to retain alpha and exact pixels. It refuses the input as the output and refuses an existing output without `--overwrite`. Source and recipe files remain unchanged. Successful JSON includes `process: "macro-apply"`, input/output paths, macro ID/name/version/step count, and `outputSize`.

Exit codes: 0 success; 1 file, recipe, or processing failure; 2 invalid CLI arguments; launcher setup errors use 3. On a step failure, preserve the reported 1-based step index and operation ID in the explanation. Invalid versions and unsupported operations are failures, not reasons to silently alter or partially execute a recipe. Intermediate steps are kept in memory; the final image is saved atomically only after the whole recipe succeeds. The CLI does not add results to the editor library or history.

## Version-1 definition

Prefer a macro saved by the editor. If the user requests a new recipe file, use the actual schema, with semantic settings only. There is no CLI create/update/delete subcommand. A minimal two-step example is:

```json
{
  "version": 1,
  "id": "7e479a41-cebf-4272-819c-c6bcf0e2d480",
  "name": "Small monochrome",
  "steps": [
    {
      "operationId": "image.resize",
      "downscale": { "targetWidth": 64, "targetHeight": 64, "spriteMode": true }
    },
    { "operationId": "filter.monochrome" }
  ]
}
```

Use a fresh nonempty UUID when creating a separate macro, a nonblank name, and at least one step. Preserve an existing macro's identifier and settings when applying it. Only version 1 is supported. `threadCount` is machine-local and is not persisted; each execution uses local defaults. Input/output paths and editor appearance are not recipe parameters.

| Operation ID | Relevant fields on the step |
| --- | --- |
| `image.profile` | `downscale`: combined downscale and palette settings |
| `image.resize` | `downscale`: independent size settings |
| `image.trim` | No payload; trim fully transparent borders, retaining semitransparent pixels (Tessera 2.5+) |
| `image.colors` | `downscale`: independent color settings |
| `image.cleanup` | `downscale`: local cleanup settings |
| `grid.align` | Optional `cellSize`; omit/null to detect separately for each input |
| `grid.align.local` | Optional `cellSize` (2–64); restore independent region grids without moving figures |
| `grid.align.local.snapped` | Optional `cellSize` (2–64); restore local grids, then move figures onto the shared canvas-origin grid |
| `grid.reduce` | Literal `cellSize`, or `usePreviousAlignment: true` following `grid.align` or `grid.align.local.snapped` in this recipe |
| `filter.monochrome` | No additional payload |
| `filter.ascii` | `ascii`: `characters`, `keepSourceSize`, `invertSource`, `invertColors`; produces an image |
| `color.channel.adjust` | `channelAdjustment`: `channel` (`R`, `G`, `B`, `A`), `mode` (`Value`, `Percent`), nonnegative finite `amount`, optional `subtract` (Tessera 2.6+) |
| `color.replace` | `colorReplacement`: `fromArgb` and `toArgb` as signed 32-bit ARGB integers |
| `background.remove` | `background`: `mode` (`GlobalColor` or `EdgeConnected`), `tolerance` (0–100), optional `colorArgb`; omit/null to estimate separately for each input |
| `contours.heatmap` | `contours`: analysis settings captured by the desktop Contours map tool |
| `contours.clean` | `contours`: settings for the image cleanup performed by `clean-contours` |

For both contour operations, `contours` accepts `windowSize` (odd 3–15), `strictness` (`Cautious`, `Normal`, `Strong`), `protectDetails`, `similarColorDistance` (0–100), `minimumPixelGradient` and `minimumNeighborGradient` (0–20000), `minimumSimilarNeighbors` (1–224), `minimumAgreement` and `minimumOpposition` (0–1). The desktop saves its actual slider values in the map step. The cleanup step returns the cleaned image; its diagnostic candidate map is not a separate macro output.

Payloads belonging to other operations are rejected, so do not copy a full step with unrelated fields. `downscale` uses the shared `DownscaleOptions` property names in camelCase, such as `targetWidth`, `targetHeight`, `palette`, and `localColorPasses`; these are JSON fields, not CLI flags. Use a saved recipe or current source contract for additional settings rather than guessing translations. Auto detection runs anew for each input. Previous-alignment reduction uses the most recent alignment in this execution, not state from another file or editor session.

Nested `macro.apply` steps are unsupported by this contract. Saved editor macros contain flattened operations. ICO export, Aseprite export, palette inspection, and ASCII text export are ordinary CLI operations, not steps in this image recipe.
