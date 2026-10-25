using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtAlignment;
using PixelArtWorkflows;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class LocalGridModeChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Local grid retains independent origins and detects the common pitch", VerifyPixels);
        check("Local recipes replay correctly and reject global compaction", VerifyRecipe);
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
            Require(global.IsVisible && local.IsVisible && global.IsChecked == true, "Grid mode controls unavailable");
            Get<IntegerInput>(window, "gridCellInput").Value = 5;
            local.IsChecked = true; Pump();
            Require(Get<IntegerInput>(window, "gridCellInput").Maximum == 64, "Local size limit not applied");
            Require(Get<TextBlock>(window, "localGridHint").IsVisible && !Get<TextBlock>(window, "globalGridHint").IsVisible, "Wrong mode description");
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
            DomainColorTest.Localization.Initialize("en"); Pump();
            Require(local.Content?.ToString() == "Local" && global.Content?.ToString() == "Global", "Grid modes are not translated");
            Require(Get<TextBlock>(window, "localGridHint").Text.StartsWith("Separate grids"), "Grid hint did not update its language");
            Capture(Get<GroupBox>(window, "gridToolGroup"), Path.Combine(folder, "local-en.png"));
        }
        finally { window.Close(); Pump(); DomainColorTest.Localization.Initialize("ru"); }
    }

    private static Bitmap Fixture()
    {
        var image = new Bitmap(180, 100);
        using var graphics = Graphics.FromImage(image); graphics.Clear(Color.FromArgb(34, 32, 51));
        var colors = new Dictionary<char, Color> { ['p'] = Color.FromArgb(65, 61, 112), ['b'] = Color.FromArgb(19, 77, 127),
            ['c'] = Color.FromArgb(0, 151, 207), ['w'] = Color.FromArgb(250, 250, 250) };
        string[] arrow = ["....p....", "....wp...", "....wwp..", "ppppwwwp.", "wwwwwwwwp", "ppppwwwp.", "....wwp..", "....wp...", "....p...."];
        string[] star = ["...b...", "p.bcp..", ".bcwcbp", "..pcp..", "...p...", "....p.."];
        foreach (var item in new[] { (Shape: arrow, X: 15, Y: 25), (Shape: star, X: 117, Y: 32) })
            for (int y = 0; y < item.Shape.Length; y++)
                for (int x = 0; x < item.Shape[y].Length; x++)
                    if (colors.TryGetValue(item.Shape[y][x], out Color color))
                    { using var brush = new System.Drawing.SolidBrush(color); graphics.FillRectangle(brush, item.X + x * 5, item.Y + y * 5, 5, 5); }
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
