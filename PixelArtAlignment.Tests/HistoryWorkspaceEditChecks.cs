using System.Collections.ObjectModel;
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
using Bitmap = System.Drawing.Bitmap;
using Color = System.Drawing.Color;
using Localization = DomainColorTest.Localization;

namespace PixelArtAlignment.Tests;

internal static class HistoryWorkspaceEditChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        try { Verify("ru", false); Verify("en", true); }
        finally { Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); }
    }

    private static void Verify(string language, bool docked)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "history-workspace-edits-" + Guid.NewGuid().ToString("N")));
        string libraryPath = Path.Combine(root, "library"); Directory.CreateDirectory(libraryPath);
        new AppearanceSettings { Language = language, Theme = docked ? TesseraTheme.Light : TesseraTheme.Dark, HistoryUndoDepth = 64 }.Save(Path.Combine(libraryPath, "appearance.json"));
        string input = Path.Combine(root, "source.png");
        using var sourceImage = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            sourceImage.SetPixel(x, y, Color.FromArgb(x + y * 16, x * 16, y * 16, (x + y) * 8));
        sourceImage.Save(input);
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        var store = new MacroStore(libraryPath);
        var macro = store.Save(new MacroDefinition { Name = "Editable step chain", Steps =
        [
            new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new DownscaleOptions { TargetWidth = 7, TargetHeight = 5, SpriteMode = true } },
            new ProcessingStep { OperationId = ProcessingOperations.Monochrome },
            new ProcessingStep { OperationId = ProcessingOperations.Sharpen, Sharpen = new SharpenSettings { Strength = 110, Threshold = 3 } }
        ] });
        MainWindow window = Open(); Guid asset;
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.ImportWorkspaceFilesAsync([input]));
            asset = window.Workspace!.State.Assets.Single().Id;
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single()));
            ApplyMacro();
            Require(Active().Generations.Count == 3, "Current macros no longer save each completed step.");
            Undo(); Require(Active().Generations.Count == 2, "A visible macro step was not one undo action.");
            window.Close(); window = Open();
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(a => a.Id == asset)));
            Undo(); Require(Active().Generations.Count == 1, "Macro-step undo did not survive workspace restart.");
            Undo(); Require(Active().Generations.Count == 0, "The first macro step could not be undone.");
            ApplyMacro();
            var original = Active().Generations.ToArray();
            Invoke("SelectGeneration", original[0]);
            NavigationTest.Click(window, "monochromeMenuItem"); Pump();
            if (!docked) { Invoke("FloatDockedTool", "filters", false); Pump(); }
            Get<CheckBox>("insertHistoryInput").IsChecked = true;
            NavigationTest.Click(window, "monochromeButton"); WaitProcessing();
            Require(Active().Generations.Count == 4 && original[1].ParentGenerationId != original[0].Id,
                "The actual tool did not insert and recompute its descendants.");
            Require(!Get<CheckBox>("insertHistoryInput").IsChecked!.Value, "Successful insertion was not one-shot.");
            using (var expected = new WorkflowExecutor().Execute(sourceImage, original[^1].RecipeSnapshot!))
            using (var actual = ImageLibrary.ReadBitmap(Library().ResultPath(Active(), original[^1]))) Equal(expected, actual);
            Undo(); Require(Active().Generations.Count == 3, "Tool insertion was not one undo action.");

            var target = Active().Generations[^1]; window.ProcessingBasis = 0; ApplyMacro();
            var branch = Active().Generations.Skip(3).ToArray();
            Invoke("MergeHistoryBranches", branch[^1], target);
            Require(Active().Generations.Count == 9 && branch.All(g => Active().Generations.Contains(g)), "Merge altered its source branch.");
            var merged = Active().SelectedGeneration!;
            using (var expected = new WorkflowExecutor().Execute(sourceImage, merged.RecipeSnapshot!)) Equal(expected, Get<Bitmap>("_downscaledResult"));
            string output = Path.Combine(root, "merged.png"); Invoke("SaveResult", output);
            var replay = store.Save(new MacroDefinition { Name = "Merged workspace recipe", Steps = WorkflowJson.Snapshot(merged.RecipeSnapshot!).Steps });
            Undo(); Require(Active().Generations.Count == 6, "Merge was not a single undo action.");
            Guid firstId = Active().Generations[0].Id;
            Invoke("RemoveGenerationWithUndo", Active().Generations[0], true);
            Require(Active().Generations.Count == 3 && Active().Generations.All(g => g.Id != firstId), "Subtree deletion missed a completed macro step.");
            window.Close(); window = Open();
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(a => a.Id == asset)));
            Require(Active().Generations.Count == 3 && Active().Generations.All(g => g.Id != firstId), "Deleted macro results were resurrected.");
            Undo(); Require(Active().Generations.Count == 6, "Subtree deletion could not be undone after restart.");

            var blocked = Active().Generations[1];
            blocked.OperationId = "manual.erase"; blocked.AppliedRecipe = null; blocked.RecipeSnapshot = null; blocked.RecipeUnavailableReason = "manual test reason";
            using (var pixels = ImageLibrary.ReadBitmap(Library().ResultPath(Active(), blocked))) Library().ReplaceGeneration(Active(), blocked, pixels);
            Invoke("SelectGeneration", Active().Generations[0]);
            var before = GenerationHashes();
            Get<CheckBox>("insertHistoryInput").IsChecked = true;
            string? message = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                foreach (var dialog in window.OwnedWindows.OfType<TesseraDialog>().ToArray())
                { message = ((TextBlock)dialog.FindName("dialogMessage")).Text; dialog.Close(); }
            };
            timer.Start();
            try { NavigationTest.Click(window, "monochromeButton"); WaitProcessing(); }
            finally { timer.Stop(); }
            Require(message?.Contains("manual.erase") == true && message.Contains("manual test reason"), "The actual tool failure did not explain its node and cause in a modal.");
            Require(Active().Generations.Count == 6 && Get<CheckBox>("insertHistoryInput").IsChecked == true && before.OrderBy(x => x.Key).SequenceEqual(GenerationHashes().OrderBy(x => x.Key)),
                "Failed workspace insertion changed saved records or disabled retry.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) == sourceHash, "History editing changed the original image.");
            string manifest = Path.Combine(root, "evidence.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { language, docked, input, sourceHash, output, macro = store.PathFor(replay.Id), restartVerified = true, stepUndoVerified = true, atomicFailureVerified = true }));
            Console.WriteLine("History workspace evidence: " + manifest);
        }
        finally { window.Close(); Pump(); }

        MainWindow Open() => new(libraryPath) { Width = 1220, Height = 820, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
        T Get<T>(string name) => (T)(typeof(MainWindow).GetField(name, Private)?.GetValue(window) ?? window.FindName(name))!;
        SourceEntry Active() => Get<SourceEntry>("_activeSource");
        ImageLibrary Library() => Get<ImageLibrary>("_library");
        void Undo() => Invoke("RestoreHistorySource", Get<HistoryUndoStore>("_historyUndo").Undo() ?? throw new Exception("History undo is empty."));
        void ApplyMacro()
        {
            Get<ListBox>("_macroList").SelectedItem = Get<ObservableCollection<MacroDefinition>>("_macros").Single(m => m.Id == macro.Id);
            Wait((Task)Invoke("ApplySelectedMacroAsync")!);
        }
        Dictionary<string, string> GenerationHashes() => Directory.EnumerateFiles(Path.Combine(libraryPath, Active().Id.ToString("N"), "generations"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(libraryPath, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        void WaitProcessing()
        {
            DateTime until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "History operation timed out."); Pump();
        }
    }

    private static void Wait(Task task)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "Workspace task timed out."); task.GetAwaiter().GetResult(); Pump();
    }
    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "History recipe dimensions differ.");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++) Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "History recipe RGBA differs.");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
}
