using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtAlignment;
using PixelArtDownscale;
using PixelArtWorkflows;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class LocalGridModeChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Local grid retains independent origins and detects the common pitch", VerifyPixels);
        check("Local recipes replay correctly and reject global compaction", VerifyRecipe);
        check("Optional snapping preserves rigid shapes across odd and even grid phases", VerifySnapping);
        check("Snapping preserves border cells, tiny stars and alpha samples", VerifySnapDetails);
        check("Grid modes work in floating and docked WPF tools, history and English", VerifyUi);
    }

    private static void VerifyPixels()
    {
        using var input = Fixture();
        using var result = new PixelGridAligner().Align(input, new GridAlignmentOptions { CellSize = 5, Mode = GridAlignmentMode.Local });
        Equal(input, result.Aligned);
        Require(result.Mode == GridAlignmentMode.Local && result.CellSize == 5, "Local metadata missing");
        using var automatic = new PixelGridAligner().Align(input, new GridAlignmentOptions { Mode = GridAlignmentMode.Local });
        Require(automatic.CellSize == 5 && automatic.WasDetected, "Local auto detection chose another pitch");
        Equal(input, automatic.Aligned);
        Require(new EditorSettings().AlignmentMode == GridAlignmentMode.Global, "Legacy settings changed their mode");
        Require(JsonSerializer.Deserialize<EditorSettings>("{\"CellSize\":5}")!.AlignmentMode == GridAlignmentMode.Global, "Old history is not global");
        Require(!JsonSerializer.Deserialize<EditorSettings>("{\"CellSize\":5}")!.SnapToGlobalGrid, "Old history enables snapping");
    }

    private static void VerifyRecipe()
    {
        using var input = Fixture();
        var recipe = new ProcessingRecipe { Steps = [new ProcessingStep { OperationId = ProcessingOperations.AlignLocal, CellSize = 5 }] };
        string json = JsonSerializer.Serialize(recipe, WorkflowJson.Options);
        Require(json.Contains("grid.align.local"), "A local macro can silently become a global one");
        var restored = JsonSerializer.Deserialize<ProcessingRecipe>(json, WorkflowJson.Options)!;
        using var result = new WorkflowExecutor().Execute(input, restored);
        Equal(input, result);
        try
        {
            WorkflowExecutor.Validate(new ProcessingRecipe { Steps = [recipe.Steps[0], new ProcessingStep
                { OperationId = ProcessingOperations.Reduce, UsePreviousAlignment = true }] });
            throw new Exception("A local grid was accepted as a globally compactable grid");
        }
        catch (WorkflowStepException exception) { Require(exception.StepIndex == 2, "Wrong validation step"); }
        var snappedRecipe = new ProcessingRecipe { Steps = [new ProcessingStep
            { OperationId = ProcessingOperations.AlignLocalSnapped, CellSize = 5 }, new ProcessingStep
            { OperationId = ProcessingOperations.Reduce, UsePreviousAlignment = true }] };
        string snappedJson = JsonSerializer.Serialize(snappedRecipe, WorkflowJson.Options);
        Require(snappedJson.Contains("grid.align.local.snapped"), "Snapping is not explicit in the macro");
        using var compact = new WorkflowExecutor().Execute(input, JsonSerializer.Deserialize<ProcessingRecipe>(snappedJson, WorkflowJson.Options)!);
        using var expected = Fixture(starX: 115, starY: 30);
        using var expectedCompact = PixelGridReducer.Reduce(expected, 5);
        Equal(compact, expectedCompact);
    }

    private static void VerifySnapping()
    {
        foreach (int pitch in new[] { 4, 5, 6 })
            for (int y = 0; y < pitch; y++)
                for (int x = 0; x < pitch; x++)
                {
                    using var input = Fixture(pitch, pitch * 23 + x, pitch * 6 + y);
                    using var off = new PixelGridAligner().Align(input, new GridAlignmentOptions { Mode = GridAlignmentMode.Local, CellSize = pitch });
                    Equal(input, off.Aligned);
                    using var on = new PixelGridAligner().Align(input, new GridAlignmentOptions
                        { Mode = GridAlignmentMode.Local, CellSize = pitch, SnapToGlobalGrid = true });
                    int sx = x <= pitch / 2 ? 0 : pitch, sy = y <= pitch / 2 ? 0 : pitch;
                    using var expected = Fixture(pitch, pitch * 23 + sx, pitch * 6 + sy);
                    Equal(expected, on.Aligned);
                    Require(on.SnapToGlobalGrid && on.ConflictingCellCount == 0, "Snapping metadata or independent shapes failed");
                    using var compact = PixelGridReducer.Reduce(on.Aligned, pitch);
                    for (int py = 0; py < input.Height; py++) for (int px = 0; px < input.Width; px++)
                        Require(on.Aligned.GetPixel(px, py) == compact.GetPixel(px / pitch, py / pitch), "Compaction loses a cell");
                    using var again = new PixelGridAligner().Align(on.Aligned, new GridAlignmentOptions
                        { Mode = GridAlignmentMode.Local, CellSize = pitch, SnapToGlobalGrid = true });
                    Equal(on.Aligned, again.Aligned);
                }
    }

    private static void VerifySnapDetails()
    {
        using var input = new Bitmap(80, 60);
        using (var graphics = Graphics.FromImage(input))
        {
            graphics.Clear(Color.FromArgb(12, 12, 20));
            using var brush = new System.Drawing.SolidBrush(Color.White);
            graphics.FillRectangle(brush, 1, 11, 5, 10);
            graphics.FillRectangle(brush, 27, 37, 1, 1);
        }
        using var result = new PixelGridAligner().Align(input, new GridAlignmentOptions
            { Mode = GridAlignmentMode.Local, CellSize = 5, SnapToGlobalGrid = true });
        using var compact = PixelGridReducer.Reduce(result.Aligned, 5);
        Require(result.Aligned.GetPixel(0, 10).R == 255 && result.Aligned.GetPixel(0, 15).R == 255, "Border clipping removed a cell");
        Require(result.Aligned.GetPixel(25, 35).R == 255, "A tiny white star disappeared");
        Require(result.Aligned.GetPixel(5, 10).R == 12, "Vacated shape left a ghost");
        using var alpha = new Bitmap(1280, 10, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < alpha.Height; y++) for (int x = 0; x < alpha.Width; x++)
            alpha.SetPixel(x, y, Color.FromArgb(x / 5, 80, 120, 160));
        var restored = LocalGridProcessor.Process(alpha, new LocalGridOptions(5, SnapToGlobalGrid: true));
        using (restored.Image) Equal(alpha, restored.Image);
    }

    private static void VerifyUi()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyEditor(false); VerifyEditor(true); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("Local grid WPF checks timed out");
        if (failure is not null) throw new Exception("Local grid WPF checks failed", failure);
    }

    private static void VerifyEditor(bool docked)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "local-grid-ui", "wpf", docked ? "docked" : "floating"));
        Directory.CreateDirectory(folder);
        string sourcePath = Path.Combine(folder, "dual-grid.png");
        using (var input = Fixture()) input.Save(sourcePath, System.Drawing.Imaging.ImageFormat.Png);
        var window = new MainWindow(Path.Combine(folder, "library-" + Guid.NewGuid().ToString("N")))
            { ShowActivated = false, ShowInTaskbar = false, Width = 1500, Height = 1000, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 20 };
        try
        {
            if (docked) window.EnableWorkspacePrototype();
            window.Show(); Pump();
            if (docked)
            {
                Wait(window.ImportWorkspaceFilesAsync([sourcePath]));
                Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            }
            else Invoke(window, "LoadImage", sourcePath);
            NavigationTest.Click(window, "gridMenuItem"); Pump();
            var global = Get<RadioButton>(window, "globalGridModeInput");
            var local = Get<RadioButton>(window, "localGridModeInput");
            var snap = Get<CheckBox>(window, "snapLocalGridInput");
            Require(global.IsVisible && local.IsVisible && global.IsChecked == true, "Grid mode controls unavailable");
            Require(!snap.IsVisible && snap.IsChecked != true, "Snapping is enabled by default or exposed in global mode");
            Get<IntegerInput>(window, "gridCellInput").Value = 5;
            local.IsChecked = true; Pump();
            Require(Get<IntegerInput>(window, "gridCellInput").Maximum == 64, "Local size limit not applied");
            Require(Get<TextBlock>(window, "localGridHint").IsVisible && !Get<TextBlock>(window, "globalGridHint").IsVisible, "Wrong mode description");
            Require(snap.IsVisible && snap.IsChecked != true, "Local snap switch is missing");
            var source = Field<SourceEntry>(window, "_activeSource");
            NavigationTest.Click(window, "alignButton");
            Until(() => !Field<bool>(window, "_processing"), "Local alignment did not complete");
            Require(source.Generations.Count == 1, "Local UI did not save a result");
            var localEntry = source.SelectedGeneration!;
            Require(localEntry.Settings.AlignmentMode == GridAlignmentMode.Local && localEntry.OperationId == ProcessingOperations.AlignLocal,
                "History lost the selected alignment mode");
            Require(localEntry.AppliedRecipe!.Steps.Single().OperationId == ProcessingOperations.AlignLocal, "Macro lost the local mode");
            using (var saved = new Bitmap(Field<ImageLibrary>(window, "_library").ResultPath(source, localEntry)))
            using (var input = Fixture()) Equal(saved, input);
            Require(!Get<Button>(window, "compactGridButton").IsEnabled, "Local result can be compacted globally");
            Capture(Get<GroupBox>(window, "gridToolGroup"), Path.Combine(folder, "local-ru.png"));

            global.IsChecked = true; Pump();
            Require(Field<bool>(window, "_resultStale"), "Mode switch did not mark the result stale");
            Require(Get<IntegerInput>(window, "gridCellInput").Maximum == 256, "Global range not restored");
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 0;
            NavigationTest.Click(window, "alignButton");
            Until(() => !Field<bool>(window, "_processing"), "Global alignment did not complete");
            Require(source.Generations.Count == 2 && source.SelectedGeneration!.IsGlobalAlignment, "Global mode did not use the classic path");
            Require(Get<Button>(window, "compactGridButton").IsEnabled, "Global compaction is disabled");
            Capture(Get<GroupBox>(window, "gridToolGroup"), Path.Combine(folder, "global-ru.png"));

            Invoke(window, "SelectGeneration", localEntry); Pump();
            Require(local.IsChecked == true && !Get<Button>(window, "compactGridButton").IsEnabled, "History did not restore local mode");
            var imported = Field<ImageLibrary>(window, "_library").ImportGeneration(source, localEntry, [source]);
            Require(imported.AlignedCellSize == 0, "Local source copy was marked globally aligned");
            snap.IsChecked = true; Pump();
            Require(Field<bool>(window, "_resultStale") && Get<TextBlock>(window, "snappedGridHint").IsVisible
                && !Get<TextBlock>(window, "localGridHint").IsVisible, "Snapping did not update the hint and invalidate the result");
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 0;
            NavigationTest.Click(window, "alignButton");
            Until(() => !Field<bool>(window, "_processing"), "Snapped alignment did not complete");
            var snappedEntry = source.SelectedGeneration!;
            Require(snappedEntry.Settings.SnapToGlobalGrid && snappedEntry.IsGlobalAlignment
                && snappedEntry.AppliedRecipe!.Steps.Single().OperationId == ProcessingOperations.AlignLocalSnapped, "History or macro lost snapping");
            Require(Get<Button>(window, "compactGridButton").IsEnabled, "Snapped local result cannot be compacted");
            using (var saved = new Bitmap(Field<ImageLibrary>(window, "_library").ResultPath(source, snappedEntry)))
            using (var expected = Fixture(starX: 115, starY: 30)) Equal(saved, expected);
            Require(Field<ImageLibrary>(window, "_library").ImportGeneration(source, snappedEntry, [source]).AlignedCellSize == 5,
                "Snapped source copy lost its global cell size");
            Capture(Get<GroupBox>(window, "gridToolGroup"), Path.Combine(folder, "snap-ru.png"));
            Invoke(window, "SelectGeneration", localEntry); Pump();
            Require(snap.IsChecked != true, "Old local history inherited snapping");
            Invoke(window, "SelectGeneration", snappedEntry); Pump();
            Require(snap.IsChecked == true, "History did not restore snapping");
            DomainColorTest.Localization.Initialize("en"); Pump();
            Require(local.Content?.ToString() == "Local" && global.Content?.ToString() == "Global", "Grid modes are not translated");
            Require(snap.Content?.ToString() == "Snap to shared grid", "Snap switch did not update its language");
            Capture(Get<GroupBox>(window, "gridToolGroup"), Path.Combine(folder, "snap-en.png"));
        }
        finally { window.Close(); Pump(); DomainColorTest.Localization.Initialize("ru"); }
    }

    private static Bitmap Fixture(int pitch = 5, int starX = 117, int starY = 32)
    {
        var image = new Bitmap(pitch * 36, pitch * 20);
        using var graphics = Graphics.FromImage(image); graphics.Clear(Color.FromArgb(34, 32, 51));
        var colors = new Dictionary<char, Color> { ['p'] = Color.FromArgb(65, 61, 112), ['b'] = Color.FromArgb(19, 77, 127),
            ['c'] = Color.FromArgb(0, 151, 207), ['w'] = Color.FromArgb(250, 250, 250) };
        string[] arrow = ["....p....", "....wp...", "....wwp..", "ppppwwwp.", "wwwwwwwwp", "ppppwwwp.", "....wwp..", "....wp...", "....p...."];
        string[] star = ["...b...", "p.bcp..", ".bcwcbp", "..pcp..", "...p...", "....p.."];
        foreach (var item in new[] { (Shape: arrow, X: pitch * 3, Y: pitch * 5), (Shape: star, X: starX, Y: starY) })
            for (int y = 0; y < item.Shape.Length; y++)
                for (int x = 0; x < item.Shape[y].Length; x++)
                    if (colors.TryGetValue(item.Shape[y][x], out Color color))
                    { using var brush = new System.Drawing.SolidBrush(color); graphics.FillRectangle(brush, item.X + x * pitch, item.Y + y * pitch, pitch, pitch); }
        return image;
    }

    private static T Get<T>(FrameworkElement window, string name) where T : class
        => (T)(window.FindName(name) ?? window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
    private static void Until(Func<bool> condition, string message)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), message); Pump();
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted, "Workspace task timed out"); task.GetAwaiter().GetResult(); }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout(); Pump();
        // Allow the theme's checked-state animation to reach its final frame.
        for (int q = 0; q < 30; q++) { Thread.Sleep(10); Pump(); }
        Require(element.ActualWidth > 150 && element.ActualHeight > 100, "Grid panel did not render");
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        image.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Equal(Bitmap a, Bitmap b)
    {
        Require(a.Size == b.Size, "Canvas changed");
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
            Require(a.GetPixel(x, y).ToArgb() == b.GetPixel(x, y).ToArgb(), $"A local grid shifted a pixel at {x},{y}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
