using System.Drawing.Imaging;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class SharpenChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Sharpen strengthens synthetic edges and preserves alpha through PNG", Algorithm);
        check("Sharpen recipe validates and replays exact settings", Recipe);
        check("Sharpen WPF preview and saved result use the selected history image", Editor);
    }

    private static void Algorithm()
    {
        using var source = BlurredEdge();
        using var result = ImageFilters.Sharpen(source, 100, 0);
        Require(result.Size == source.Size, "Sharpen changed dimensions");
        Require(result.GetPixel(3, 1).R < source.GetPixel(3, 1).R &&
            result.GetPixel(4, 1).R > source.GetPixel(4, 1).R, "Blurred boundary did not become steeper");
        Require(result.GetPixel(3, 1).R is > 0 and < 64 && result.GetPixel(4, 1).R is > 191 and < 255,
            "Sharpen stopped creating useful intermediate colors");
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
        {
            int value = result.GetPixel(x, y).R;
            Require(value >= 0 && value <= 255 && result.GetPixel(x, y).A == source.GetPixel(x, y).A,
                "Sharpen created a halo or changed alpha");
        }
        using var off = ImageFilters.Sharpen(source, 0, 0);
        using var gated = ImageFilters.Sharpen(source, 100, 64);
        Equal(source, off);
        Equal(source, gated);
        Require(source.GetPixel(3, 1).R == 64, "Sharpen modified the source");

        using var mixed = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++) mixed.SetPixel(x, y, Color.FromArgb(20, 20, 120));
        mixed.SetPixel(0, 0, Color.FromArgb(220, 30, 30));
        mixed.SetPixel(2, 0, Color.FromArgb(30, 220, 30));
        mixed.SetPixel(1, 1, Color.FromArgb(100, 100, 100));
        using var mixedResult = ImageFilters.Sharpen(mixed, 100, 0);
        Require(mixedResult.GetPixel(1, 1).ToArgb() == mixed.GetPixel(1, 1).ToArgb(),
            "Sharpen combined unrelated red and green neighbors into a yellow outlier");

        using var transparent = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++) transparent.SetPixel(x, y, Color.FromArgb(0, 0, 0, 255));
        transparent.SetPixel(1, 1, Color.FromArgb(128, 200, 30, 20));
        using var alpha = ImageFilters.Sharpen(transparent, 200, 0);
        Require(alpha.GetPixel(1, 1).A == 128 && alpha.GetPixel(1, 1).R == 200 &&
            alpha.GetPixel(0, 0).A == 0, "Transparent neighbors contaminated visible color");
        using var flat = new Bitmap(3, 3);
        using (var graphics = Graphics.FromImage(flat)) graphics.Clear(Color.FromArgb(100, 120, 140));
        using var flatResult = ImageFilters.Sharpen(flat);
        Equal(flat, flatResult);
        using var levels = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            levels.SetPixel(x, y, Color.FromArgb(y * 16 + x, 73 + x * 8, 30 + y * 10, 190 - x * 5));
        using (var allAlpha = ImageFilters.Sharpen(levels, 200, 0))
        using (var stream = new MemoryStream())
        {
            allAlpha.Save(stream, ImageFormat.Png); stream.Position = 0;
            using var reopened = new Bitmap(stream);
            Equal(allAlpha, reopened);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                Require(levels.GetPixel(x, y).A == reopened.GetPixel(x, y).A, "Sharpen changed an alpha level through PNG");
            Require(levels.GetPixel(0, 0).ToArgb() == reopened.GetPixel(0, 0).ToArgb(), "Sharpen changed hidden RGB");
        }
        Throws<ArgumentOutOfRangeException>(() => ImageFilters.Sharpen(source, 201));
        Throws<ArgumentOutOfRangeException>(() => ImageFilters.Sharpen(source, 100, 65));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => ImageFilters.Sharpen(source, 100, 0, cancelled.Token));
        Throws<OperationCanceledException>(() => ImageFilters.Sharpen(source, 0, 0, cancelled.Token));
    }

    private static void Recipe()
    {
        using var source = BlurredEdge();
        var step = new ProcessingStep { OperationId = ProcessingOperations.Sharpen,
            Sharpen = new SharpenSettings { Strength = 150, Threshold = 2 } };
        var recipe = new ProcessingRecipe { Steps = [step] };
        WorkflowExecutor.Validate(recipe);
        var snapshot = WorkflowJson.Snapshot(recipe);
        using var replay = new WorkflowExecutor().Execute(source, snapshot);
        using var direct = ImageFilters.Sharpen(source, 150, 2);
        Equal(direct, replay);
        Throws<WorkflowStepException>(() => WorkflowExecutor.Validate(new ProcessingRecipe
        { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Sharpen }] }));
        Throws<WorkflowStepException>(() => WorkflowExecutor.Validate(new ProcessingRecipe
        { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Sharpen,
            Sharpen = new SharpenSettings { Strength = 250 } }] }));
    }

    private static void Editor()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyEditor(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Sharpen editor check timed out");
        if (failure is not null) throw failure;
    }

    private static void VerifyEditor()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "sharpen-verification", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using var source = BlurredEdge(); source.Save(path, ImageFormat.Png);
        byte[] original = File.ReadAllBytes(path);
        string libraryPath = Path.Combine(folder, "library");
        var library = new ImageLibrary(libraryPath);
        var document = library.Import(path, []);
        var parent = library.SaveGeneration(document, source, null, new GenerationEntry
        { Caption = "Сжатый результат", OperationId = ProcessingOperations.Monochrome,
            AppliedRecipe = new ProcessingRecipe { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Monochrome }] } });
        document.Generations.Add(parent);
        var catalog = new WorkspaceCatalog(libraryPath); catalog.SyncImages([document]);
        catalog.State.Assets.Single().SelectedResult = parent.Id; catalog.Save();
        var window = new MainWindow(libraryPath) { Width = 1100, Height = 780, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            Get<ListBox>("generationStrip").SelectedItem = Get<SourceEntry>("_activeSource").Generations.Single(g => g.Id == parent.Id);
            Pump();
            Require(window.ProcessingBasis == 1, "Saved result was not selected as the sharpening input");
            NavigationTest.Click(window, "sharpenMenuItem"); Pump();
            var group = Get<GroupBox>("sharpenToolGroup");
            Require(ReferenceEquals(Window.GetWindow(group), window), "Sharpen filter did not dock in the editor");
            Get<IntegerInput>("sharpenStrengthInput").Value = 150;
            Get<IntegerInput>("sharpenThresholdInput").Value = 0;
            Get<CheckBox>("sharpenLivePreviewInput").IsChecked = true;
            Until(() => Get<Border>("livePreviewFrame").Visibility == Visibility.Visible &&
                Get<PixelPreview>("resultPreview").Image is not null &&
                !ReferenceEquals(Get<PixelPreview>("resultPreview").Image, Get<Bitmap>("_downscaledResult")));
            using var expected = ImageFilters.Sharpen(source, 150, 0);
            Equal(expected, Get<PixelPreview>("resultPreview").Image!);
            Require(!Get<MenuItem>("saveMenuItem").IsEnabled, "Temporary sharpen preview permits export");
            Get<Button>("sharpenButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => Get<SourceEntry>("_activeSource").Generations.Count == 2 && !Get<bool>("_processing"));
            var saved = Get<SourceEntry>("_activeSource").Generations.Last();
            Require(saved.OperationId == ProcessingOperations.Sharpen && saved.ParentGenerationId == parent.Id &&
                saved.AppliedRecipe?.Steps.Single().Sharpen is { Strength: 150, Threshold: 0 } &&
                Get<CheckBox>("sharpenLivePreviewInput").IsChecked == false,
                $"Sharpen history provenance or preview state is wrong: operation={saved.OperationId}, " +
                $"parent={saved.ParentGenerationId}, expected={parent.Id}, strength={saved.AppliedRecipe?.Steps.Single().Sharpen?.Strength}, " +
                $"threshold={saved.AppliedRecipe?.Steps.Single().Sharpen?.Threshold}, preview={Get<CheckBox>("sharpenLivePreviewInput").IsChecked}");
            using var stored = ImageLibrary.ReadBitmap(library.ResultPath(document, saved));
            Equal(expected, stored);
            var store = new MacroStore(Path.Combine(folder, "macro-library"));
            var macro = store.Save(new MacroDefinition { Name = "Sharpen 150", Steps = saved.RecipeSnapshot!.Steps });
            using (var replay = new WorkflowExecutor().Execute(source, new ProcessingRecipe { Steps = MacroStore.Read(store.PathFor(macro.Id)).Steps }))
                Equal(expected, replay);
            typeof(MainWindow).GetMethod("SaveResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Path.Combine(folder, "sharpen-export.png")]);
            Get<IntegerInput>("sharpenStrengthInput").Value = 37;
            Get<IntegerInput>("sharpenThresholdInput").Value = 9;
            typeof(MainWindow).GetMethod("SelectGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [saved]);
            Require(Get<IntegerInput>("sharpenStrengthInput").Value == 150 && Get<IntegerInput>("sharpenThresholdInput").Value == 0,
                "History selection lost sharpen strength or threshold");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Sharpen overwrote its source PNG");
            Console.WriteLine("  Sharpen WPF evidence: " + folder);
        }
        finally { window.Close(); Pump(); }

        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
    }

    private static Bitmap BlurredEdge()
    {
        var result = new Bitmap(7, 3, PixelFormat.Format32bppArgb);
        int[] values = [0, 0, 0, 64, 191, 255, 255];
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++) result.SetPixel(x, y, Color.FromArgb(255, values[x], values[x], values[x]));
        return result;
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Sharpen dimensions differ");
        for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Sharpen pixel differs at {x},{y}");
    }

    private static void Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(condition(), "Sharpen editor operation timed out"); Pump();
    }

    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Pump(); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
