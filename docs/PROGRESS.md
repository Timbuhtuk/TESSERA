# Operation progress

Tessera uses the 20-cell `Ts.ProgressCells` style from the local `Tessera.WpfStyles` 0.2.0 package. The package is checked in under `.nuget/local-packages`; the root `NuGet.Config` maps that package ID to this feed. Other packages restore from NuGet.org. Normal restore/build does not require the source style-library directory.

## Reporting

Image operations accept an optional `IProgress<double>` with fractions between 0 and 1. Rows, quantizer iterations, cleanup passes, alignment/reduction cells, animation frames and PNG rows report completed work. Parallel ranges are monotonic and thread safe. Processing results and alpha are independent of whether an observer is attached. The Aseprite library retains its plain `net8.0` target and has no WPF dependency.

`WorkflowExecutor` keeps its completed-step notifications and additionally accepts `operationProgress` for fractional work inside steps. Files within a batch receive equal ranges. Aseprite files dropped during a running conversion become the next progress batch, preserving queue order.

## Footer lifecycle

`OperationProgressSession` associates each stream with its initiating action. Worker updates are limited to one dispatcher post every 50 ms; completion and disposal are immediate notifications. Superseded callbacks cannot change a later action. Explicit commands take precedence over automatic previews, and hidden workspace previews do not claim the footer. This reporting does not change the existing navigation/processing locks.

The processing range ends at 95%; only successful publication of the file, preview or history result completes the UI at 100%. Success stays visible for 350 ms. Cancellation, failure and stale previews dispose their stream without showing success. Opaque system decoders/encoders retain the last confirmed fraction; progress is not an estimate of elapsed or remaining time.

The footer uses a 240 DIP by 14 DIP Cells bar without a numeric percentage. Below 760 DIP window width the bar shrinks to 180 DIP. Stage/file status stays on the left. Styles and foreground/background colors follow the selected application theme.

## Verification

```powershell
dotnet restore Pixelizator.sln
dotnet build Pixelizator.sln -c Debug --no-restore -m:1
dotnet build Pixelizator.sln -c Release --no-restore -m:1
dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --progress
dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --ui --invariants
```

The focused progress checks cover pixel/alpha parity, intermediate callbacks, parallel ranges, early completion, cancellation, validation failures, macro substeps and Aseprite publication. WPF checks verify all 20 cells, three themes, narrow layouts, value updates without a percentage label, stale callbacks, preview priority, throttling and the completion timer. Captures are written to `artifacts/wpf/progress`.

## Validation on 2026-10-30

- Debug and Release solution builds: passed without warnings or errors.
- Restore into an empty package cache: passed; package metadata resolves Tessera.WpfStyles 0.2.0 from this repository's `.nuget/local-packages`, without the adjacent style-library checkout.
- Focused `--progress` checks: all passed, including real ICO/background workspaces and exported files.
- Full `--ui --invariants` run: passed.
- CLI checks: 89 passed, 0 failed. Aseprite checks: 10 passed, 0 failed.
- Additional history/macro and English localization checks: passed.
- The additional workspace check fails at `WorkspaceChecks.cs:188`: an independent UI copy does not preserve an older selected result. The same failure is reproduced in an isolated copy of the sources from before this progress change, using styles 0.1.0. It remains a separate existing issue. Evidence is in `artifacts/progress-implementation/workflow-workspace-checks.log` and `baseline-workspace.log`.

Rendered progress captures are under `artifacts/wpf/progress`; test logs and the isolated before-change comparison are under `artifacts/progress-implementation`.

## Integration on the published 2.19.0 baseline

The observations above describe the older primary worktree. Integration retains the published history transactions, selected-result copy fix, relative-resize metadata and guarded Sharpen behavior. The older workspace-copy failure is not a known failure of that published baseline.

The integrated editor keeps one history card and undo action per macro step. Its progress reaches success only after every step has been saved; canceled and failed runs retain their completed prefix without displaying success. Focused programmatic WPF checks cover this lifecycle in both languages and tool hosts.

An independent comparison against the downloaded 2.19.0 CLI verified 43 exact RGBA results across 22 operation cases and the same rejection of an unaligned grid reduction. Original inputs stayed unchanged. Release acceptance evidence is kept under `artifacts/release-2.20`; the interactive audit in `VISUAL_AUDIT.md` remains a separate record of the primary worktree.

## Integration on 2.19.1 with ASCII resolution

The release preserves the published SVG result icons and tool layout. ASCII rendering reports progress for both the new 16 px glyphs and older saved recipes at 10 px; the saved font size is applied before measuring cells. Programmatic checks compare pixel/alpha output with and without callbacks and reject invalid font sizes without reporting completion. The earlier 2.19.0 parity evidence above remains historical. Current acceptance and comparisons against 2.19.1 are recorded in `artifacts/release-2.20`. Native input checks run only in the unchanged Windows CI.
