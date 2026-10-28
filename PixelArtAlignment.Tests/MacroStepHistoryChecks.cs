using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtDownscale;
using PixelArtWorkflows;
using Tessera.WpfStyles.Themes;
using Color = System.Drawing.Color;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class MacroStepHistoryChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Require(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Macro history checks need the editor STA thread");
        try { Verify("ru", false); Verify("en", true); }
        finally { Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); }
    }

    private static void Verify(string language, bool docked)
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "macro-step-history-verification", Guid.NewGuid().ToString("N")));
        string libraryPath = Path.Combine(folder, "library"); Directory.CreateDirectory(libraryPath);
        new AppearanceSettings { Language = language, Theme = docked ? TesseraTheme.Light : TesseraTheme.Dark }.Save(Path.Combine(libraryPath, "appearance.json"));
        string input = Path.Combine(folder, "source.png");
        using var source = new Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) source.SetPixel(x, y, Color.FromArgb((x + y * source.Width) % 256, x * 7, y * 9, (x + y) * 4));
        source.Save(input);
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        var store = new MacroStore(libraryPath);
        var resize = new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new DownscaleOptions { TargetWidth = 17, TargetHeight = 11, SpriteMode = true } };
        var normal = store.Save(new MacroDefinition { Name = "Five steps", Steps =
        [
            resize,
            new ProcessingStep { OperationId = ProcessingOperations.Colors, Downscale = new DownscaleOptions { TargetWidth = 17, TargetHeight = 11, Palette = PaletteKind.None, IndependentColorMode = IndependentColorMode.Quantization, QuantizationColors = 4, Quantization = QuantizationMethod.KMeansLab }, ConnectedPalette = new ConnectivityPaletteOptions { TargetColors = 4, TileSize = 0 } },
            new ProcessingStep { OperationId = ProcessingOperations.Sharpen, Sharpen = new SharpenSettings { Strength = 150, Threshold = 3 } },
            new ProcessingStep { OperationId = ProcessingOperations.Monochrome },
            new ProcessingStep { OperationId = ProcessingOperations.FinalistReduce, FinalistMode = FinalistMode.P5Off, FinalistFactor = 2 }
        ] });
        var failing = store.Save(new MacroDefinition { Name = "Runtime failure", Steps = [resize, new ProcessingStep { OperationId = ProcessingOperations.Reduce, CellSize = 2 }] });
        var window = new MainWindow(libraryPath) { Width = 1220, Height = 820, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        string? dialogText = null;
        var dialogWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        dialogWatch.Tick += (_, _) =>
        {
            foreach (var dialog in window.OwnedWindows.OfType<TesseraDialog>().ToArray())
            { dialogText = ((TextBlock)dialog.FindName("dialogMessage")).Text; dialog.Close(); }
        };
        dialogWatch.Start();
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([input]));
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            NavigationTest.Click(window, "macrosMenuItem"); Pump();
            if (!docked) { Invoke(window, "FloatDockedTool", "macros", false); Pump(); }
            var document = Get<SourceEntry>(window, "_activeSource");
            var library = Get<ImageLibrary>(window, "_library");
            var list = Get<ListBox>(window, "_macroList");
            list.SelectedItem = Get<ObservableCollection<MacroDefinition>>(window, "_macros").Single(m => m.Id == normal.Id);
            window.ProcessingBasis = 0;
            Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!);
            Require(dialogText is null && document.Generations.Count == 5, "Five-step macro did not create exactly five results: " + dialogText);
            for (int q = 0; q < 5; q++)
            {
                var entry = document.Generations[q];
                Require(entry.ParentGenerationId == (q == 0 ? null : document.Generations[q - 1].Id), "Intermediate parent chain is wrong");
                Require(entry.OperationId == normal.Steps[q].OperationId && entry.AppliedRecipe?.Steps.Count == 1 && entry.RecipeSnapshot?.Steps.Count == q + 1, "Intermediate recipe is not independent and cumulative");
                using var expected = new WorkflowExecutor().Execute(source, new ProcessingRecipe { Steps = normal.Steps.Take(q + 1).ToList() });
                using var actual = ImageLibrary.ReadBitmap(library.ResultPath(document, entry));
                Equal(expected, actual);
            }
            Require(document.Generations[1].Settings.ConnectedPalette?.TargetColors == 4 && document.Generations[1].Settings.ConnectedPaletteEnabled == true,
                "Connected palette step lost its own settings");
            Require(document.Generations.Take(4).All(g => g.Settings.Downscale.TargetWidth == 17 && g.Settings.Downscale.TargetHeight == 11),
                "Later macro cards reset the resize parameters");
            Invoke(window, "SelectGeneration", document.Generations[2]);
            Require(Get<IntegerInput>(window, "sharpenStrengthInput").Value == 150 && Get<IntegerInput>(window, "sharpenThresholdInput").Value == 3, "Sharpen card did not restore its parameters");
            Invoke(window, "SelectGeneration", document.Generations[4]);
            Require((int)Get<ComboBox>(window, "_finalistFactor").SelectedItem == 2, "TCR card lost its reduction factor");
            window.ProcessingBasis = 1;
            Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!);
            Require(document.Generations.Count == 10 && document.Generations[5].ParentGenerationId == document.Generations[4].Id && document.Generations[^1].RecipeSnapshot?.Steps.Count == 10,
                "Macro on selected result did not extend its recipe and branch");
            string exported = Path.Combine(folder, "final.png"); Invoke(window, "SaveResult", exported);
            var replay = store.Save(new MacroDefinition { Name = "Step chain replay", Steps = WorkflowJson.Snapshot(document.Generations[^1].RecipeSnapshot!).Steps });
            string replayPath = Directory.EnumerateFiles(Path.Combine(libraryPath, "macros"), "*.json").Single(p => Path.GetFileNameWithoutExtension(p).Replace("-", "") == replay.Id.ToString("N"));
            Guid parent = document.Generations[^1].Id;
            bool cancelClicked = false;
            NotifyCollectionChangedEventHandler cancel = (_, e) =>
            {
                if (e.Action != NotifyCollectionChangedAction.Add || cancelClicked) return;
                var button = Get<Button>(window, "_macroCancel");
                Require(button.IsEnabled && button.IsVisible, "Cancel button is unavailable in the active tool host");
                Require(!Get<GroupBox>(window, "sizeToolGroup").IsEnabled, "Another tool remains editable while a macro runs");
                cancelClicked = true; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            document.Generations.CollectionChanged += cancel;
            try { Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!); }
            finally { document.Generations.CollectionChanged -= cancel; }
            Require(cancelClicked && document.Generations.Count == 11 && document.Generations[^1].ParentGenerationId == parent,
                "Cancellation discarded a finished step or created an unfinished card");
            string savedOne = language == "en" ? "Saved steps: 1" : "Сохранено шагов: 1";
            Require(Get<TextBlock>(window, "statusLabel").Text.Contains(savedOne), "Cancellation status is untranslated or has the wrong saved count");
            window.ProcessingBasis = 0;
            list.SelectedItem = Get<ObservableCollection<MacroDefinition>>(window, "_macros").Single(m => m.Id == failing.Id);
            Wait((Task)Invoke(window, "ApplySelectedMacroAsync")!);
            Require(document.Generations.Count == 12 && document.Generations[^1].ParentGenerationId is null && document.Generations[^1].OperationId == ProcessingOperations.Resize,
                "Runtime failure discarded the completed prefix or saved the failed step");
            Require(dialogText?.Contains(language == "en" ? "Step 2" : "Шаг 2") == true && dialogText.Contains(savedOne), "Failure status lost its step, count or language");
            Require(document.Generations.All(g => g.OperationId != ProcessingOperations.Macro), "An extra aggregate macro result was created");
            Equal(source, Get<Bitmap>(window, "_sourceImage"));
            Guid asset = window.Workspace!.State.Assets.Single().Id;
            window.Close(); Pump();
            var reopened = new MainWindow(libraryPath) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            try
            {
                reopened.EnableWorkspacePrototype(); reopened.Show(); Pump();
                Wait(reopened.OpenWorkspaceAssetAsync(reopened.Workspace!.State.Assets.Single(a => a.Id == asset)));
                var restored = Get<SourceEntry>(reopened, "_activeSource");
                Require(restored.Generations.Count == 12 && restored.Generations[10].ParentGenerationId == parent && restored.Generations[^1].ParentGenerationId is null,
                    "Completed prefixes and their parents did not survive restart");
            }
            finally { reopened.Close(); Pump(); }
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) == sourceHash, "Macro processing changed the original file");
            string manifest = Path.Combine(folder, "evidence.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { language, docked, input, sourceSha256 = sourceHash, output = exported, macro = replayPath, failingMacro = store.PathFor(failing.Id), savedResults = 12, canceledPrefix = 1, failedPrefix = 1, restartVerified = true }));
            Console.WriteLine("Macro step history evidence: " + manifest);
        }
        finally { dialogWatch.Stop(); if (window.IsVisible) window.Close(); Pump(); }
    }

    private static T Get<T>(MainWindow window, string name) => (T)(typeof(MainWindow).GetField(name, Private)?.GetValue(window) ?? window.FindName(name))!;
    private static object? Invoke(MainWindow window, string name, params object?[] values) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, values);
    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "Macro operation timed out"); task.GetAwaiter().GetResult(); Pump();
    }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame); }
    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Macro output size differs");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++) Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), $"Macro RGBA differs at {x},{y}");
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
}
