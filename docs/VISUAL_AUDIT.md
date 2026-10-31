# Tessera visual audit — 2026-10-30

## Scope and method

This is a visual and interaction audit of the current local WPF build in the dirty `PIXELIZATOR` worktree (`2be0363` plus uncommitted changes), not a release audit. I ran copies of the Debug `net8.0-windows` application from `artifacts/visual-audit/isolated-app` and `artifacts/visual-audit/isolated-en-app`. Each copy had its own `WorkspaceData/Library`; imported materials and generated outputs stayed there. Pre-existing tracked files and the user's library were not edited by the audit. The test display was 1920 × 1080; application captures cover approximately 1220 px, 560 px, and wide/maximized windows. I exercised the actual Windows UI with mouse/keyboard input and checked screenshots plus accessibility state. The evidence directory is [artifacts/visual-audit](../artifacts/visual-audit/).

The interactive matrix covered Dark/Russian startup and empty library, Light/Russian settings, Contrast/Russian settings, library, editor, background removal and material maps, and Light/English library, editor, ICO creation, image board and Aseprite export. I used `DomainColorTest/Assets/landscape.png` and a repository `.aseprite` fixture. The narrow 560 px pass covered the loaded library, editor, background removal and Aseprite export. Some theme/workspace combinations were sampled rather than exhaustively multiplied; they are listed below.

## Confirmed findings

### VA-01 · New board cannot be saved into its own workspace (P1)

**Reproduction:** In the isolated Light/English library, choose **File → Create → Board…**, enter a name and press **Done**. The app opens an empty image board and immediately shows “Choose a file outside the internal Tessera library.” The board does not appear as a saved document in the isolated library. [Dialog screenshot](../artifacts/visual-audit/18-board-light-en.jpg); [creation prompt](../artifacts/visual-audit/17-create-board-light-en.jpg).

**Expected:** Creating a board should save its initial `.tboard` document and select it in the workspace. **Actual:** The internal library path is rejected as an external export. In `MainWindow.Workspace.cs`, the `new-board` branch calls `SaveToFileAsync(path)` before assigning `moodboardWorkspace.WorkspaceSavePath = path`. `MoodboardWorkspace.ValidateOutputPath` therefore calls the external `ValidateExportPath` guard, which rejects the library location. The error is directly supported by the observed dialog and code path; the missing document is corroborated by the isolated library contents. The empty board remains visible after the failed create action, so it can be mistaken for a saved board.

### VA-02 · Editor document name disappears at 560 px (P2)

**Reproduction:** Open `landscape.png`, run Resize, then shrink the main window to 560 px in Contrast/Russian. The top bar shows only a period after **Вид**, although the active filename is available in the accessibility state and in the history card. [560 px editor](../artifacts/visual-audit/11-editor-contrast-ru-560.png); [wide editor](../artifacts/visual-audit/12-editor-contrast-ru-wide.png).

**Expected:** The active document should remain identifiable, for example with a useful shortened filename or a tooltip. **Actual:** The header's file label has virtually no visible content. The history card still identifies the file, but it is below the tool panel and not always in view. The rest of the narrow editor reflows vertically and remains scrollable.

### VA-03 · Completed ICO preview retains a running status (P2)

**Reproduction:** Open a material and choose **File → Export → ICO…** in Light/English. The size previews render; the Cells bar reaches 100% and then disappears. The **Save ICO…** button is enabled, while the footer continues to say “Creating icons…”. [At 100%](../artifacts/visual-audit/21-ico-light-en.jpg); [after the bar disappeared](../artifacts/visual-audit/22-ico-complete-light-en.jpg).

**Expected:** The footer should report completion/readiness once the previews have been published. **Actual:** It still describes an in-progress operation. This is a status/feedback defect; the rendered previews and enabled save action do not indicate a failed conversion. The observed 100% frame confirms the Cells style was shown in the real UI, but the operation was too fast to capture a stable intermediate fraction.

### VA-04 · Background-removal footer can retain a running status after results are usable (P2)

**Reproduction:** Open `landscape.png`, choose **Tools → Filters and background → Remove background…**, and wait for the source and result previews. In Contrast/Russian, **Сохранить PNG…** and **В историю ассета** are enabled while the footer still reads “Удаление фона…”. The same footer text remained in subsequent captures at 1220 and 560 px. [Normal width](../artifacts/visual-audit/08-background-removal-contrast-ru-1220.png); [560 px](../artifacts/visual-audit/09-background-removal-contrast-ru-560.png).

**Expected:** The status should distinguish a finished preview from active removal. **Actual:** The running message persists while a result is displayed and actionable. I did not measure a long idle interval after the final screenshot, so this finding is limited to the captured interval; a later delayed status transition is possible.

### VA-05 · Workspace headings truncate at the minimum width (P3)

At 560 px, background removal displays **“Удаление…”** rather than its full heading, and Aseprite export displays **“Anima…”**. The animation's top filename also shortens to **“аним…”**. Buttons fit, settings scroll, and the previews remain visible. This is a readability limitation rather than a blocked workflow. [Background removal](../artifacts/visual-audit/09-background-removal-contrast-ru-560.png); [animation](../artifacts/visual-audit/24-aseprite-light-en-narrow.jpg). A compact title or tooltip would preserve orientation in the narrow layout.

## Screens reviewed without a confirmed visual defect

| Area | Directly observed result | Evidence |
| --- | --- | --- |
| Startup and empty library | Dark/Russian and Light/English launch, release notes, empty state, filter row and side detail pane displayed. | [Dark](../artifacts/visual-audit/02-library-empty-dark-ru-1220.png), [Light](../artifacts/visual-audit/16-library-empty-light-en.jpg) |
| Appearance | Dark, Light and Contrast settings rendered with their labels and controls. | [Dark](../artifacts/visual-audit/03-settings-dark-ru-1220.png), [Light](../artifacts/visual-audit/04-settings-light-ru-1220.png), [Contrast](../artifacts/visual-audit/05-settings-contrast-ru-1220.png) |
| Library at 560 px | Filters and asset count wrap, and selection remains available. No clipped action was confirmed. | [Loaded library](../artifacts/visual-audit/10-library-loaded-contrast-ru-560.png) |
| Editor and resize | The real resize action reported 48% through the accessibility state, then produced a 48 × 48 history result and a completed footer message. The editor and previews remained usable at normal and wide sizes. | [Normal editor](../artifacts/visual-audit/06-editor-contrast-ru-1220.png), [completed resize](../artifacts/visual-audit/07-resize-complete-contrast-ru-1220.png), [Light/English editor](../artifacts/visual-audit/20-editor-light-en-1220.jpg) |
| Material maps | Height, Normal and AO preview panes populated in Contrast/Russian; generated map assets appeared in the isolated library. | [Material maps](../artifacts/visual-audit/13-material-maps-contrast-ru-wide.png) |
| ICO | Source selector, size toggles, preview series and Save action displayed in Light/English. | [ICO screenshot](../artifacts/visual-audit/21-ico-light-en.jpg) |
| Aseprite | A small repository `.aseprite` fixture imported as an animation and opened a one-frame, 2 × 2 export result. Footer reported `Done: 1. Errors: 0`. The settings and result card remained accessible at 560 px. | [Normal width](../artifacts/visual-audit/23-aseprite-light-en.jpg), [560 px](../artifacts/visual-audit/24-aseprite-light-en-narrow.jpg) |
| Tile inspector | Opened on a generated AO asset, saved the collection, then reopened the `.ttiles` document. The full window shows the asset list, edit controls, 3 × 3 repeat preview and X/Y edge-difference summaries. Narrow tile-inspector layout was not exercised. | [Full window](../artifacts/visual-audit/25-tile-inspector-contrast-ru.jpg), [saved collection](../artifacts/visual-audit/tile-collection.ttiles) |

## Progress, errors and cancellation boundary

Direct interactive evidence covers a resize update at 48%, ICO Cells at 100%, successful resize/history publication, successful Aseprite import, and the board creation error. It does **not** cover a stable visual frame for intermediate Cells in each workspace, cancellation while a long operation is running, failed decoding/export, macro batches, or a queued Aseprite batch. Those states must not be inferred from the screenshots. The existing [progress implementation record](PROGRESS.md) describes automated checks for intermediate callbacks, monotonic batches, cancellation, errors, macro substeps, themes and narrow layouts; its [rendered progress captures](../artifacts/wpf/progress/) are test-rendered evidence, not a substitute for those unperformed interactive scenarios.

## Original follow-up priority

1. Fix VA-01 so workspace board saves are allowed before the initial write, then verify that a created board appears in the library and survives reopening.
2. Give completed ICO/background operations a terminal footer state and check that the Cells indicator disappears only after the published result is ready.
3. Preserve an identifiable document/workspace title at 560 px. Recheck the narrow board and tile windows once VA-01 is fixed.

No application source or automated tests were changed for this audit. The pre-existing dirty worktree and the separate `WorkspaceChecks.cs:188` failure recorded in [PROGRESS.md](PROGRESS.md) were left untouched.

## Resolution on 2026-10-30

The follow-up code change addressed VA-01 through VA-05 in the same dirty worktree. The original findings and screenshots above remain as before-fix evidence.

| Finding | Fix and direct verification |
| --- | --- |
| VA-01 | The new board's workspace save path is established before its first write. In a fresh isolated build, **File → Create → Board…** saved `Fixed board` inside its library, showed no error, appeared in the library, and reopened successfully. [Created board](../artifacts/visual-audit/26-fixed-board-created-light-en.jpg). |
| VA-02 | The title bar now leaves usable width for the active file label. At 560 px, it shows the start of the filename rather than a period. [Background workspace](../artifacts/visual-audit/29-fixed-background-light-en-560.jpg), [animation workspace](../artifacts/visual-audit/30-fixed-aseprite-light-en-560.jpg). |
| VA-03 | A published ICO preview now replaces the running footer text with a ready message, without making stale or hidden previews claim the footer. [Ready ICO](../artifacts/visual-audit/27-fixed-ico-ready-light-en.jpg). |
| VA-04 | A published background result now reports that it is ready. [Normal width](../artifacts/visual-audit/28-fixed-background-ready-light-en.jpg), [560 px](../artifacts/visual-audit/29-fixed-background-light-en-560.jpg). |
| VA-05 | Background and Aseprite actions move below the title at 560 px. Their full headings remain visible; controls and previews remain accessible. [Background](../artifacts/visual-audit/29-fixed-background-light-en-560.jpg), [Aseprite](../artifacts/visual-audit/30-fixed-aseprite-light-en-560.jpg). |

Debug application and test-project builds completed with zero warnings and errors. Targeted `--progress`, `--moodboard`, and `--navigation` checks passed. The moodboard check now creates and reloads a workspace board; progress checks assert terminal ICO/background labels; navigation checks reserve visible title space at 560 px. These checks and the direct UI pass do not turn the untested cancellation and batch scenarios above into interactive coverage.
