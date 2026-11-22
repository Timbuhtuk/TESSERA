# Operation progress

Tessera uses the 20-cell `Ts.ProgressCells` style from `Tessera.WpfStyles` 0.2.0 on NuGet.org. Restoring and building the solution requires no separate style-library checkout.

## Reporting

Image operations accept an optional `IProgress<double>` with fractions between 0 and 1. Rows, quantizer iterations, cleanup passes, alignment/reduction cells, animation frames and PNG rows report completed work. Parallel ranges are monotonic and thread safe. Processing results and alpha are independent of whether an observer is attached. The Aseprite library retains its plain `net8.0` target and has no WPF dependency.

`WorkflowExecutor` keeps its completed-step notifications and additionally accepts `operationProgress` for fractional work inside steps. Files within a batch receive equal ranges. Aseprite files dropped during a running conversion become the next progress batch, preserving queue order.

## Progress and status display

`OperationProgressSession` associates each stream with its initiating action. Worker updates are limited to one dispatcher post every 50 ms; completion and disposal are immediate notifications. Superseded callbacks cannot change a later action. Explicit commands take precedence over automatic previews, and hidden workspace previews do not claim the progress display. This reporting does not change the existing navigation/processing locks.

The processing range ends at 95%; only successful publication of the file, preview or history result completes the UI at 100%. Success stays visible for 350 ms. Cancellation, failure and stale previews dispose their stream without showing success. Opaque system decoders/encoders retain the last confirmed fraction; progress is not an estimate of elapsed or remaining time.

The title bar uses a 14 DIP high Cells bar without a numeric percentage. Its width adapts between 84 and 240 DIP. Status text appears briefly at the upper right of the workspace as an overlay. Styles and foreground/background colors follow the selected application theme.

## Verification

```powershell
dotnet restore Tessera.sln
dotnet build Tessera.sln -c Debug --no-restore -m:1
dotnet build Tessera.sln -c Release --no-restore -m:1
dotnet run --project Tessera.RegressionTests -c Release --no-build -- --progress
dotnet run --project Tessera.RegressionTests -c Release --no-build -- --ui --invariants
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
