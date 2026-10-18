# Workspace prototype

This experimental branch presents Tessera as a local workspace for preparing and assembling 2D graphics for games, applications and everyday image work.

## Start and storage

Run `artifacts/workspace-preview/Tessera.exe`, or `Start-Workspace-Prototype.cmd` after building. No .NET installation is needed for the published executable. Automatic updates are disabled in the prototype.

The default library is `WorkspaceData/Library` beside the executable. On first launch, existing image snapshots and histories from `%LOCALAPPDATA%/Tessera/Library` are copied into it. The original library is never used for edits. Keep the `WorkspaceData` directory when moving the prototype. An explicit `--workspace-library <absolute-directory>` selects a separate library for testing.

## Organize materials

The gear icon in the title bar opens **Settings**: theme and interface language (Русский / English). Theme changes apply immediately. The language is saved in the library's `appearance.json` and applies on the next launch; the active editor is left intact. Existing settings default to Russian. Asset names, projects, folders, paths and tags are never translated.

- **Все ассеты** lists the whole library; **Без проекта** lists assets with no project memberships; **Недавние** lists previously opened materials.
- Create projects using **+** beside **Проекты**. Right-click a project or folder to create a folder inside it, rename it or remove it. The same menu opens with Shift+F10 on the focused tree item.
- Open or drop images, Aseprite files, `.tboard` boards and `.ttiles` collections. Imports made while a project is selected are linked into its selected folder. Other imports remain standalone.
- Use Ctrl/Shift for multiple selection. Drag assets onto project/folder nodes, or use **Добавить в проект…** / **Переместить в папку…**. Folders can be dragged within their own project.
- A project holds references. The same asset, edits, results and tags are shared by every project using it. Each project has its own folder placement.
- **Независимая копия** creates a new asset with separate payloads and image history. Only the current project's membership is replaced. History identifiers and parent links are remapped.
- **Убрать из проекта** removes one membership. Deleting a project keeps its assets. Deleting a folder promotes its children and memberships to the parent. **Удалить из библиотеки…** explicitly removes all memberships, with a confirmation listing project usage; external source files remain intact.

Tags belong to the shared asset. Enter comma-separated tags and apply them; for multiple selected assets this replaces each asset's tag list. The context menu provides the same actions when the details panel is hidden in a narrow window.

Search matches asset names and tags; multiple words must all match. Choose the current folder including descendants, the whole project, or the whole library. Combine search with material type, exact width/height and transparency filters. Cards show project/folder paths. Collection dimensions describe the first tile.

## Edit and export

Open a material by double-click or Enter. Image processing settings are docked beside the previews; narrow windows place the settings above them. Advanced settings remain collapsible. The image algorithms are the existing shared processors.

Opening an image from a project limits the source filmstrip to that project's images, including all its folders. Switching images keeps this context; shared images use the project they were opened from. **Все ассеты** opens the full image library, while **Без проекта** keeps only standalone images. Imports and independent copies update the filmstrip. Removing the last project image returns to its browser.

Use the image action bar to remove a solid background, inspect tiles, export ICO, replace a color, export PNG, or add the chosen input to an existing board. Background removal and tile correction offer **В историю ассета**, which appends a result directly to the originating image. The **Обрабатывать** selector identifies whether the source or a saved result is the next input.

Boards and tile collections remain editable documents. **Сохранить** / Ctrl+S stores their library-owned document; **Экспортировать** writes a deliverable. Aseprite source snapshots and layout settings are retained in the catalog; the PNG/JSON sheet can be regenerated and exported. Boards preserve separate layers and Ctrl/Shift selection.

Organization is saved automatically. Returning to the browser keeps its project, folder, search and filters. Image settings/selected result and animation layout are retained per asset. Unsaved board/collection edits retain the existing save/leave checks.

## Implementation and verification

`WorkspaceCatalog` stores versioned JSON metadata, projects, nested folders and one asset membership per project. Images retain their existing GUID directories and immutable PNG result history. Other document payloads are owned copies under `documents/<asset-id>/`. `WorkspaceBrowser` handles navigation, organization and queries; the main window connects it to the existing editor/workspaces.

Build and check with `Build-WorkspacePrototype.ps1`. The focused suite is:

```powershell
dotnet run --project PixelArtAlignment.Tests -c Release -- --workspace
```

It covers shared membership, independent deep copies, parent history links, invalid/cyclic folder moves, deletion semantics, combined search, persistence, wide/narrow WPF layouts, multiple selection, image processing, background/tile roundtrips, board layers, document thumbnails and PNG/JSON animation export. Add `--localization` to check catalog placeholders, English settings/workspaces, language persistence, old settings and unchanged user names. The existing `--invariants --ui` suite checks the previous tools. Captures are under `artifacts/workspace-verification/run-*`.

Translations live in `DomainColorTest/Localization/en.json`, keyed by the Russian source text. C# uses `Localization.Text` / `Localization.Format`; XAML uses `Translate` with stable keys derived from the source text. Keep format placeholders and file-filter patterns intact when editing translations. `Assets/settings.svg` is the original vector source used by the gear control.

This branch is for local review. Global tags are implemented; separate per-project labels, cloud synchronization and project archive interchange are outside this prototype. Removed document payloads are retained locally for recovery and are not shown in the catalog.
