using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DomainColorTest;
using PixelArtWorkflows;
using Tessera.WpfStyles.Themes;
using Color = System.Drawing.Color;
using Localization = DomainColorTest.Localization;
using Bitmap = System.Drawing.Bitmap;

namespace PixelArtAlignment.Tests;

internal static class SizeFactorUiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify("ru", false); Verify("en", true); }
            catch (Exception ex) { failure = ex; }
            finally { Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Size factor UI verification timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(string language, bool docked)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "size-factor-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string imagePath = Path.Combine(folder, "source.png");
        using (var source = new Bitmap(4, 3))
        {
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 4; x++) source.SetPixel(x, y, Color.FromArgb((x + y * 4) * 23, x * 60, y * 90, 130));
            source.Save(imagePath);
        }
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)));
        string libraryPath = Path.Combine(folder, "library");
        Directory.CreateDirectory(libraryPath);
        new AppearanceSettings { Language = language, Theme = docked ? TesseraTheme.Light : TesseraTheme.Dark }.Save(Path.Combine(libraryPath, "appearance.json"));

        var window = new MainWindow(libraryPath) { Width = 1220, Height = 820, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.EnableWorkspacePrototype(); window.Show(); Pump();
            WaitTask(window.ImportWorkspaceFilesAsync([imagePath]));
            WaitTask(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single()));
            NavigationTest.Click(window, "sizeMenuItem"); Pump();
            if (!docked) { Invoke("FloatDockedTool", "size", false); Pump(); }
            Get<RadioButton>("scaleFactorModeInput").IsChecked = true;
            var factor = Get<TextBox>("scaleFactorInput");
            factor.Text = "0,5";
            Require(Get<TextBlock>("sizeHint").Text.Contains("2 × 2"), "Half scale did not preview rounded dimensions");
            NavigationTest.Click(window, "scaleOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(2, 2), "Half scale produced the wrong dimensions");
            var first = Get<SourceEntry>("_activeSource").SelectedGeneration!;

            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 1;
            factor.Text = "1.5";
            Require(Get<TextBlock>("sizeHint").Text.Contains("3 × 3"), "One-and-a-half scale did not use the selected result");
            NavigationTest.Click(window, "scaleOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(3, 3), "One-and-a-half scale produced the wrong dimensions");
            Require(Get<SourceEntry>("_activeSource").SelectedGeneration!.ParentGenerationId == first.Id, "Factor result lost its parent");

            var document = Get<SourceEntry>("_activeSource");
            var second = document.SelectedGeneration!;
            Require(second.RecipeSnapshot?.Steps.Count == 2 && first.Settings.Downscale.TargetWidth == 2 && second.Settings.Downscale.TargetWidth == 3,
                "Factor history did not preserve resolved dimensions and its replayable chain");
            using var original = ImageLibrary.ReadBitmap(imagePath);
            using var expected = new WorkflowExecutor().Execute(original, second.RecipeSnapshot!);
            Equal(expected, Get<Bitmap>("_downscaledResult"));
            string output = Path.Combine(folder, "result.png"); Invoke("SaveResult", output);
            var store = new MacroStore(libraryPath);
            var macro = store.Save(new MacroDefinition { Name = "Fractional scale chain", Steps = WorkflowJson.Snapshot(second.RecipeSnapshot!).Steps });

            foreach (string invalid in new[] { "0", "-1", "NaN", "Infinity", "1e309", "999999999999", "abc", "" })
            {
                factor.Text = invalid;
                Require(!Get<Button>("scaleOnlyButton").IsEnabled, "Invalid factor remained actionable: " + invalid);
                Invoke("SetProcessingState", false);
                Require(!Get<Button>("scaleOnlyButton").IsEnabled, "Processing-state refresh enabled an invalid factor: " + invalid);
            }
            factor.Text = "0.00000001";
            Require(Get<TextBlock>("sizeHint").Text.Contains("1 × 1"), "Tiny factor did not clamp to one pixel");
            Get<RadioButton>("pixelSizeModeInput").IsChecked = true;
            Require(Get<StackPanel>("pixelSizeControls").Visibility == Visibility.Visible, "Pixel-size mode did not return");
            Require(Get<Button>("scaleOnlyButton").IsEnabled, "Pixel-size mode remained disabled");
            Require(Get<IntegerInput>("widthInput").Value == second.Settings.Downscale.TargetWidth, "Saved pixel dimensions were not restored");
            if (language == "en") Require(Localization.Text("Масштаб") == "Zoom" && Get<RadioButton>("scaleFactorModeInput").Content?.ToString() == "Scale", "Factor label changed the existing zoom translation");
            Guid asset = window.Workspace!.State.Assets.Single().Id;
            window.Close(); Pump();
            var reopened = new MainWindow(libraryPath) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                reopened.EnableWorkspacePrototype(); reopened.Show(); Pump();
                WaitTask(reopened.OpenWorkspaceAssetAsync(reopened.Workspace!.State.Assets.Single(a => a.Id == asset)));
                var restored = (SourceEntry)typeof(MainWindow).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
                Require(restored.Generations.Count == 2 && restored.Generations[^1].ParentGenerationId == first.Id && restored.Generations[^1].RecipeSnapshot?.Steps.Count == 2,
                    "Factor chain did not survive reopening the workspace");
            }
            finally { reopened.Close(); Pump(); }
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))) == sourceHash, "Factor processing changed the source file");
            string manifest = Path.Combine(folder, "evidence.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { language, docked, input = imagePath, sourceHash, output, macro = store.PathFor(macro.Id), restartVerified = true }));
            Console.WriteLine("Size factor evidence: " + manifest);
        }
        finally { if (window.IsVisible) window.Close(); Pump(); }

        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Scale operation timed out"); Pump();
        }
    }

    private static void WaitTask(Task task)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "Workspace operation timed out"); task.GetAwaiter().GetResult(); Pump();
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "Factor recipe size differs");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++) Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Factor recipe RGBA differs");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
