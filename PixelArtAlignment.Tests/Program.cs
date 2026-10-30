using PixelArtAlignment.Tests;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("PixelArtAlignment.Tests: --compact-switch, --relative-resize, --ascii, --connected-palette, --contours, --lenses, --sharpen, --channels, --palette-dock, --size-factor, --history-edits, --project-notes, --detail-protection, --local-grid, --updates, --history-macros, --invariants, --ui, --ui-core, --alpha, --material-maps, --release-notes, --tool-improvements, --moodboard, --tiles, --info, --workspace, --localization, --navigation, --benchmark [--holdout | --fresh]");
    Console.WriteLine("Programmatic editor regression checks without native input: --editor-regressions");
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
if (args is ["--finalists-ui"])
{
    FinalistUiChecks.Run();
    return 0;
}
if (args is ["--finalists-ui", var uiLabRoot])
{
    FinalistUiChecks.Run(uiLabRoot);
    return 0;
}
if (args.Any(a => a is not ("--compact-switch" or "--relative-resize" or "--ascii" or "--editor-regressions" or "--connected-palette" or "--contours" or "--lenses" or "--sharpen" or "--channels" or "--palette-dock" or "--size-factor" or "--history-edits" or "--project-notes" or "--detail-protection" or "--local-grid" or "--updates" or "--history-macros" or "--invariants" or "--ui" or "--ui-core" or "--alpha" or "--material-maps" or "--release-notes" or "--tool-improvements" or "--moodboard" or "--tiles" or "--info" or "--workspace" or "--localization" or "--navigation" or "--benchmark" or "--holdout" or "--fresh")) ||
    ((args.Contains("--holdout") || args.Contains("--fresh")) && !args.Contains("--benchmark")) ||
    (args.Contains("--holdout") && args.Contains("--fresh")))
{
    Console.Error.WriteLine("Invalid arguments. Use --help.");
    return 2;
}
if (args.Length == 0) args = ["--invariants", "--benchmark"];
// Written before the alignment implementation. Scores are 0..100, with a fixed 90 gate.
// Color recall is macro-averaged: large backgrounds cannot hide missing small colors.
int failed = 0;
if (args.Contains("--editor-regressions")) Check("Selected-history editor tools and export protection", EditorRegressionChecks.RunStandalone);
if (args.Contains("--project-notes") || args.Contains("--ui")) Check("Project notes: live previews, modal decisions, comparison wheel and aspect ratios", ProjectNotesChecks.Run);
if (args.Contains("--detail-protection") || args.Contains("--ui")) DetailProtectionChecks.Run(Check);
if (args.Contains("--local-grid") || args.Contains("--ui")) LocalGridModeChecks.Run(Check);
if (args.Contains("--material-maps") || args.Contains("--ui")) MaterialMapChecks.Run(Check);
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
if (args.Contains("--invariants")) FinalistInvariantChecks.Run(Check);
if (args.Contains("--ui")) Check("TCR modes, history, macro recipes and PNG in WPF", FinalistUiChecks.Run);
if (args.Contains("--invariants")) IconExportChecks.Run(Check);
if (args.Contains("--invariants")) IndependentImageProcessorChecks.Run(Check);
if (args.Contains("--invariants")) ImageColorToolsChecks.Run(Check);
if (args.Contains("--invariants") || args.Contains("--channels")) ChannelAdjustmentChecks.Run(Check);
if (args.Contains("--invariants")) ImageFilterChecks.Run(Check);
if (args.Contains("--invariants")) BackgroundRemovalChecks.Run(Check);
if (args.Contains("--invariants")) GitHubUpdaterChecks.Run(Check);
if (args.Contains("--release-notes")) Check("Bundled patch notes, first launch and settings history", ReleaseNotesChecks.Run);
if (args.Contains("--tool-improvements")) ToolImprovementsChecks.Run(Check);
if (args.Contains("--history-macros")) HistoryMacroChecks.Run(Check);
if (args.Contains("--history-edits") || args.Contains("--ui")) Check("History insert, merge, subtree deletion and persistent undo", HistoryEditChecks.Run);
if (args.Contains("--compact-switch") || args.Contains("--ui")) Check("Compact switches in three themes, RU/EN, enabled and checked states", CompactSwitchChecks.Run);
if (args.Contains("--relative-resize") || args.Contains("--ui")) Check("Relative resize validation and legacy fixed dimensions", RelativeResizeChecks.Run);
if (args.Contains("--ascii") || args.Contains("--ui")) Check("ASCII workspace colors, text, history, undo and macro replay", AsciiWorkspaceChecks.Run);
if (args.Contains("--size-factor") || args.Contains("--ui")) Check("Size and frame fractional scale factor", SizeFactorUiChecks.Run);
if (args.Contains("--navigation")) Check("Top navigation, menus, state and responsive layouts", NavigationChecks.Run);
if (args.Contains("--sharpen") || args.Contains("--ui")) SharpenChecks.Run(Check);
if (args.Contains("--lenses") || args.Contains("--ui")) Check("Grid lens rendering, movement, color, zoom and export isolation", GridLensChecks.Run);
if (args.Contains("--palette-dock") || args.Contains("--ui")) Check("Combined palette and movable tool dock", ToolDockPaletteChecks.Run);
if (args.Contains("--workspace")) Check("Workspace catalog, projects, shared assets, copies and WPF", WorkspaceChecks.Run);
if (args.Contains("--localization")) Check("English localization, SVG settings and language persistence", LocalizationChecks.Run);
if (args.Contains("--connected-palette") || args.Contains("--ui")) Check("Connected palette preview, controls, history, PNG and restart", ConnectedPaletteUiChecks.Run);
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
