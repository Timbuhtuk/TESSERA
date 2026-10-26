# Tessera in ChatGPT

The local **Tessera** plugin lets ChatGPT use every command and option supported by Tessera CLI. Its `tessera` skill translates the request into CLI arguments, runs the actual application, and returns the generated files. It does not require an MCP server or an OpenAI API key.

Use ChatGPT desktop with command execution on the Windows computer that holds Tessera CLI. Installing the plugin alone does not give a cloud-only conversation access to that computer.

## Install from this repository

From the repository root, with Python 3.10+ and the Codex CLI available:

```powershell
python plugins/package_tessera.py
codex plugin marketplace add .
codex plugin add tessera@tessera-local
```

This registers the **Tessera Local** marketplace and installs its **Tessera** plugin. Restart the desktop app if it has not picked up the installation, then open a local chat and select the `tessera` skill. You can also invoke it explicitly with `$tessera`.

Point the launcher at an unpacked CLI release once:

```powershell
python plugins/tessera/skills/tessera/scripts/tessera.py --configure "C:/Tools/Tessera-cli/tessera.exe"
```

For source builds, publish the Windows x64 CLI using the [development guide](DEVELOPMENT.md), then configure its `tessera.exe`. The CLI from `Tessera-cli-win-x64.zip` is distinct from the desktop application, although Windows treats their filenames as identical.

The saved path lives in `%LOCALAPPDATA%/Tessera/chatgpt-cli.json`. An explicit launcher `--cli PATH` or `TESSERA_CLI` environment variable overrides that setting. Without a saved path, the launcher can discover a CLI bundled under the skill's `bin/`, repository CLI output, or PATH. It inspects the executable header to avoid launching the desktop UI by mistake.

If a local execution sandbox redirects `LOCALAPPDATA` and has no settings there, the launcher reads existing settings from `~/AppData/Local/Tessera/chatgpt-cli.json`. Settings in the current `LOCALAPPDATA` still take precedence. Use `--config PATH` when an isolated configuration is intended.

## Use it

Ask ChatGPT normally, for example:

- “Используй Тессеру: выровняй пиксельную сетку этого изображения и сожми каждую ячейку до одного пикселя.”
- “Через Тессеру уменьши спрайт до 64×64, сохрани прозрачность и примени DB32.”
- “Сделай ICO с размерами 16, 32, 48 и 256 без сглаживания.”
- “Экспортируй эти Aseprite-файлы в PNG и JSON, раскладка grid, четыре столбца.”

The skill supports the combined downscaler, `align`, `reduce-grid` / `compact-grid`, `resize`, `colors`, `smooth-colors` / `smooth`, `structure-probe`, `monochrome` / `mono`, `ascii`, `palette-info`, `replace-color`, `remove-background` / `remove-bg`, `ico` / `icon`, `aseprite`, and saved macros through `macro list` and `macro apply`. Its launcher forwards all arguments, so additional CLI options do not require an adapter update. Read [CLI.md](CLI.md) or the installed command's `--help` for the complete option set.

Manual launcher calls put its own options before `--` and every Tessera argument after it:

```powershell
python plugins/tessera/skills/tessera/scripts/tessera.py -- --help
python plugins/tessera/skills/tessera/scripts/tessera.py -- resize "C:/Images/source.png" -o "C:/Images/result.png" --width 64 --height 64 --sprite --json
```

Output, errors, and CLI exit codes are preserved. Code 3 indicates a launcher setup problem. Input images remain protected by the CLI; replacing an existing output uses its normal `--overwrite` option.

## Saved processing macros

Ask, for example, “Покажи макросы в этой библиотеке и примени «Маленький монохром» к изображению.” The skill lists saved definitions from the library's `macros` directory, resolves the selected JSON file, and applies its steps with the same version-1 `PixelArtWorkflows` executor as the editor:

```powershell
python plugins/tessera/skills/tessera/scripts/tessera.py -- macro --help
python plugins/tessera/skills/tessera/scripts/tessera.py -- macro list --library "C:/Images/Library" --json
python plugins/tessera/skills/tessera/scripts/tessera.py -- macro apply "C:/Images/source.png" --macro "C:/Images/Library/macros/recipe.json" -o "C:/Images/result.png" --json
```

Pass the library root to `--library`. Check listing `warnings`: unreadable or invalid files can be skipped while listing still succeeds. Applying a macro validates the whole recipe, runs intermediate operations in memory, and saves only the final result. It preserves the source and recipe, protects existing output unless `--overwrite` is supplied, and does not add editor history or library entries. Prefer PNG for alpha and exact pixels.

Use the [skill's macro reference](../plugins/tessera/skills/tessera/references/macros.md) for the definition format and supported operations. Nested macros are unsupported; saved recipes contain flattened steps. If macro help is missing, update or explicitly select the CLI executable: installing new skill instructions does not upgrade a separately installed CLI.

## Packages and maintenance

`python plugins/package_tessera.py` refreshes the skill's reference from `docs/CLI.md` and writes two verified archives:

- `artifacts/chatgpt-plugin/Tessera-chatgpt-plugin.zip`: portable plugin containing the skill.
- `artifacts/chatgpt-plugin/Tessera-skill.zip`: standalone skill with a single `tessera/` folder.

Both archives are also published with each [GitHub release](https://github.com/Timbuhtuk/TESSERA/releases/latest). The release pipeline runs the launcher against that release's CLI and includes the archive hashes in SHA256SUMS.txt. To package into another directory, pass `--output-directory PATH` to `package_tessera.py`; use `--packages PATH` when running the checks against those archives.

These archives contain the instructions, reference, and launcher. Tessera CLI remains a separate local installation. After editing an installed plugin, refresh its local marketplace and restart the desktop app so it loads the updated package.

Run the integration checks against a published CLI:

```powershell
python plugins/test_tessera.py --cli artifacts/cli/tessera.exe --gui artifacts/standalone/Tessera.exe
```

`--gui` is optional; when supplied, the check verifies that the desktop executable is rejected by inspection. Use `--launcher PATH` to run the same checks through an installed skill's `scripts/tessera.py`. Checks cover all command helps and aliases, real image operations, macro listing and application against the equivalent ordinary pipeline, invalid recipes, configuration and discovery, Unicode and shell metacharacters in paths, error propagation, source preservation, and both archives. Fixtures and generated outputs stay under `artifacts/chatgpt-plugin/checks-*`.

The package follows OpenAI's [plugin packaging](https://developers.openai.com/plugins/build/plugins) and [local skill](https://learn.chatgpt.com/docs/build-skills) formats. CLI tests and installation metadata do not establish that the desktop app has reloaded the skill; verify selection and an actual request in a fresh local chat after installation.
