using Tessera.RegressionTests;

bool backgroundWorker;
try { backgroundWorker = TestDesktop.IsWorker(ref args); }
catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 1; }
bool backgroundRequested = args is ["--background", ..];
if (backgroundRequested) args = args[1..];
bool useBackgroundDesktop = backgroundRequested && !backgroundWorker;

if (useBackgroundDesktop && args is ["--uninstall"] or ["--progress"] or ["--finalists-ui", _])
    return RunOnBackgroundDesktop();

if (args is ["--uninstall"])
{
    try { UninstallChecks.Run(); Console.WriteLine("PASS Tessera uninstall"); return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}

if (args is ["--progress"])
{
    int errors = 0;
    ProgressChecks.Run((name, run) =>
    {
        try { run(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { errors++; Console.WriteLine("FAIL " + name + ": " + e); }
    });
    Exception? uiError = null;
    var ui = new Thread(() => { try { ProgressUiChecks.Run(); } catch (Exception e) { uiError = e; } }) { IsBackground = true };
    ui.SetApartmentState(ApartmentState.STA); ui.Start();
    if (!ui.Join(TimeSpan.FromSeconds(90))) { errors++; Console.WriteLine("FAIL Progress WPF timeout"); }
    else if (uiError is not null) { errors++; Console.WriteLine("FAIL Progress WPF: " + uiError); }
    else Console.WriteLine("PASS Progress WPF lifecycle and themes");
    return errors == 0 ? 0 : 1;
}

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("Tessera.RegressionTests: --progress, --window-chrome, --unreal-sync, --contours, --lenses, --sharpen, --channels, --palette-dock, --result-icons, --ascii-preview, --clipboard, --size-factor, --project-notes, --detail-protection, --local-grid, --updates, --history-macros, --history-edits, --invariants, --ui, --ui-core, --aseprite-ui, --alpha, --material-maps, --release-notes, --tool-improvements, --moodboard, --tiles, --info, --workspace, --localization, --navigation, --benchmark [--holdout | --fresh]");
    Console.WriteLine("Put --background before the suite flags to run WPF checks on a separate Windows desktop. Native mouse capture requires an interactive run.");
    Console.WriteLine("Compare with a known reference: --evaluate actual.png reference.png --cell-size 8");
    Console.WriteLine("No arguments: evaluator checks, invariants and development benchmark. Exit: 0 pass, 1 fail, 2 usage.");
    return 0;
}
if (args is ["--evaluate", var actualPath, var referencePath, "--cell-size", var pitchText] &&
    int.TryParse(pitchText, out int evaluatePitch) && evaluatePitch >= 2)
{
    using var actual = new Bitmap(actualPath);
    using var reference = new Bitmap(referencePath);
    var quality = AlignmentQuality.Measure(actual, reference, evaluatePitch);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(quality, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return quality.Score >= 90 ? 0 : 1;
}
if (args is ["--finalists", var labRoot])
{
    FinalistChecks.Run(labRoot);
    return 0;
}
if (args is ["--finalists-ui", var uiLabRoot])
{
    FinalistUiChecks.Run(uiLabRoot);
    return 0;
}
if (args.Any(a => a is not ("--window-chrome" or "--unreal-sync" or "--contours" or "--lenses" or "--sharpen" or "--channels" or "--palette-dock" or "--result-icons" or "--ascii-preview" or "--clipboard" or "--size-factor" or "--project-notes" or "--detail-protection" or "--local-grid" or "--updates" or "--history-macros" or "--history-edits" or "--invariants" or "--ui" or "--ui-core" or "--aseprite-ui" or "--alpha" or "--material-maps" or "--release-notes" or "--tool-improvements" or "--moodboard" or "--tiles" or "--info" or "--workspace" or "--localization" or "--navigation" or "--benchmark" or "--holdout" or "--fresh")) ||
    ((args.Contains("--holdout") || args.Contains("--fresh")) && !args.Contains("--benchmark")) ||
    (args.Contains("--holdout") && args.Contains("--fresh")))
{
    Console.Error.WriteLine("Invalid arguments. Use --help.");
    return 2;
}
if (args.Length == 0) args = ["--invariants", "--benchmark"];
if (useBackgroundDesktop && args.Any(a => a is not ("--invariants" or "--benchmark" or "--holdout" or "--fresh")))
    return RunOnBackgroundDesktop();

// Written before the alignment implementation. Scores are 0..100, with a fixed 90 gate.
// Color recall is macro-averaged: large backgrounds cannot hide missing small colors.
int failed = 0;
if (args.Contains("--project-notes") || args.Contains("--ui")) Check("Project notes: live previews, modal decisions, comparison wheel and aspect ratios", ProjectNotesChecks.Run);
if (args.Contains("--detail-protection") || args.Contains("--ui")) DetailProtectionChecks.Run(Check);
if (args.Contains("--local-grid") || args.Contains("--ui")) LocalGridModeChecks.Run(Check);
if (args.Contains("--material-maps") || args.Contains("--ui")) MaterialMapChecks.Run(Check);
if (args.Contains("--unreal-sync")) UnrealSyncChecks.Run(Check);
if (args.Contains("--window-chrome")) Check("Window chrome and child window lifecycle", () =>
{
    Exception? error = null;
    var ui = new Thread(() => { try { WindowChromeChecks.Run(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
    ui.SetApartmentState(ApartmentState.STA); ui.Start();
    if (!ui.Join(TimeSpan.FromSeconds(60))) throw new TimeoutException("Window chrome check timed out");
    if (error is not null) throw error;
});
Check("Perfect grid and intact artwork score 100", () =>
{
    using var logical = Fixtures.Logical(0, 101);
    using var truth = Fixtures.Expand(logical, 8);
    Require(AlignmentQuality.Measure(truth, truth, 8).Score == 100, "Perfect reference rejected");
});
Check("A solid fill cannot game the grid score", () =>
{
    using var logical = Fixtures.Logical(0, 101);
    using var truth = Fixtures.Expand(logical, 8);
    using var blank = new Bitmap(truth.Width, truth.Height);
    var score = AlignmentQuality.Measure(blank, truth, 8);
    Require(score.GridPurity == 1 && score.Score < 20, $"Blank scored {score.Score}");
});
Check("A whole-cell translation is aligned but fails reconstruction", () =>
{
    using var logical = Fixtures.Logical(2, 101);
    using var truth = Fixtures.Expand(logical, 8);
    using var moved = new Bitmap(truth.Width, truth.Height);
    for (int y = 0; y < moved.Height; y++)
        for (int x = 0; x < moved.Width; x++) moved.SetPixel(x, y, truth.GetPixel(Math.Max(0, x - 8), y));
    var score = AlignmentQuality.Measure(moved, truth, 8);
    Require(score.GridPurity == 1 && score.Score < 80, $"Translation scored {score.Score}");
});
Check("Wrong colors fail even with exactly correct grid boundaries", () =>
{
    using var logical = Fixtures.Logical(3, 101);
    using var truth = Fixtures.Expand(logical, 6);
    using var wrong = new Bitmap(truth.Width, truth.Height);
    for (int y = 0; y < wrong.Height; y++)
        for (int x = 0; x < wrong.Width; x++)
        {
            var c = truth.GetPixel(x, y);
            wrong.SetPixel(x, y, Color.FromArgb(c.A, 255 - c.R, 255 - c.G, 255 - c.B));
        }
    Require(AlignmentQuality.Measure(wrong, truth, 6).Score == 0, "Recoloring passed");
});
Check("Local drift and unequal widths fail the 90 gate before repair", () =>
{
    using var logical = Fixtures.Logical(2, 101);
    using var truth = Fixtures.Expand(logical, 8);
    foreach (string kind in Fixtures.Distortions.Where(k => k != "perfect"))
    {
        using var distorted = Fixtures.Distort(logical, 8, kind, 77);
        var score = AlignmentQuality.Measure(distorted, truth, 8);
        Console.WriteLine($"  unaligned {kind}: {score.Score:F2}");
        Require(score.Score < 90, $"Distortion not detected: {kind}");
    }
});
if (args.Contains("--invariants")) Check("Canvas, source colors, alpha, idempotence, errors and detection", Invariants.Run);
if (args.Contains("--invariants")) IconExportChecks.Run(Check);
if (args.Contains("--invariants")) ProgressChecks.Run(Check);
if (args.Contains("--invariants")) IndependentImageProcessorChecks.Run(Check);
if (args.Contains("--invariants")) ImageColorToolsChecks.Run(Check);
if (args.Contains("--invariants") || args.Contains("--channels")) ChannelAdjustmentChecks.Run(Check);
if (args.Contains("--invariants")) ImageFilterChecks.Run(Check);
if (args.Contains("--invariants")) BackgroundRemovalChecks.Run(Check);
if (args.Contains("--invariants") || args.Contains("--updates")) GitHubUpdaterChecks.Run(Check);
if (args.Contains("--release-notes")) Check("Bundled patch notes, first launch and settings history", ReleaseNotesChecks.Run);
if (args.Contains("--tool-improvements")) ToolImprovementsChecks.Run(Check);
if (args.Contains("--history-macros")) HistoryMacroChecks.Run(Check);
if (args.Contains("--history-edits")) Check("History insertion, merge, bake, persistent undo and macro deletion", HistoryEditChecks.Run);
if (args.Contains("--navigation")) Check("Top navigation, menus, state and responsive layouts", NavigationChecks.Run);
if (args.Contains("--size-factor")) Check("Size and frame fractional scale factor", SizeFactorUiChecks.Run);
if (args.Contains("--lenses") || args.Contains("--ui")) Check("Grid lens rendering, movement, color, zoom and export isolation", GridLensChecks.Run);
if (args.Contains("--sharpen") || args.Contains("--ui")) SharpenChecks.Run(Check);
if (args.Contains("--palette-dock") || args.Contains("--ui")) Check("Combined palette and movable tool dock", ToolDockPaletteChecks.Run);
if (args.Contains("--result-icons") || args.Contains("--ui")) Check("Result tree SVG icons and generic fallback", ResultIconChecks.Run);
if (args.Contains("--ascii-preview") || args.Contains("--ui")) Check("ASCII live preview and diagonal result card", AsciiPreviewChecks.Run);
if (args.Contains("--clipboard") || args.Contains("--ui")) Check("Result PNG clipboard and independent history branch", ResultClipboardChecks.Run);
if (args.Contains("--workspace")) Check("Workspace catalog, projects, shared assets, copies and WPF", WorkspaceChecks.Run);
if (args.Contains("--aseprite-ui")) Check("Aseprite batch workspace and PNG exports", () =>
{
    Exception? error = null;
    var ui = new Thread(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        try { AsepriteUiChecks.Run(); } catch (Exception ex) { error = ex; }
    }) { IsBackground = true };
    ui.SetApartmentState(ApartmentState.STA); ui.Start();
    if (!ui.Join(TimeSpan.FromSeconds(60))) throw new TimeoutException("Aseprite UI verification timed out");
    if (error is not null) throw error;
});
if (args.Contains("--localization")) Check("English and Russian catalogs, SVG settings and language persistence", LocalizationChecks.Run);
if (args.Contains("--contours") || args.Contains("--ui")) Check("Contour map preview, settings, history, export and tool isolation", ContourUiChecks.Run);
if (args.Contains("--ui") || args.Contains("--ui-core")) Check("Editor WPF themes, tools, workflows and workspaces", UiChecks.Run);
if (args.Contains("--ui") || args.Contains("--channels")) Check("RGBA channel editor, history, PNG, recipes and both hosts", ChannelAdjustmentUiChecks.Run);
if (args.Contains("--ui") || args.Contains("--updates")) Check("Automatic update checks, deferred modal offers and installation guards", AutomaticUpdateChecks.Run);
if (args.Contains("--ui") || args.Contains("--alpha")) Check("Editor resize, crop, partial alpha, PNG export and history", AlphaChannelUiChecks.Run);
if (args.Contains("--ui") || args.Contains("--info")) Check("Image information and color replacement in docked and floating editors", InfoWorkspaceChecks.Run);
if (args.Contains("--ui") || args.Contains("--moodboard")) Check("Moodboard canvas, layers, project and PNG export", MoodboardWorkspaceChecks.Run);
if (args.Contains("--ui") || args.Contains("--tiles")) TileInspectorChecks.Run(Check);
if (args.Contains("--benchmark"))
{
    bool holdout = args.Contains("--holdout");
    bool fresh = args.Contains("--fresh");
    string directory = Path.GetFullPath(Path.Combine("artifacts", "alignment", fresh ? "fresh-validation" : holdout ? "holdout" : "development"));
    if (!Benchmark.Run(directory, holdout, fresh)) failed++;
}
return failed == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

static int RunOnBackgroundDesktop()
{
    try { return TestDesktop.Run(); }
    catch (System.ComponentModel.Win32Exception ex)
    {
        Console.Error.WriteLine($"Cannot start the WPF checks on a background Windows desktop: {ex.Message}");
        return 1;
    }
}
