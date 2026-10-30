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

internal static class AsciiWorkspaceChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify("ru", false); Verify("en", true); }
            catch (Exception ex) { failure = ex; }
            finally { Localization.Initialize("ru"); AppearanceResources.Apply(TesseraTheme.Dark); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) throw new Exception("ASCII workspace check timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify(string language, bool docked)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "ascii-" + Guid.NewGuid().ToString("N")));
        string library = Path.Combine(folder, "library"); Directory.CreateDirectory(library);
        new AppearanceSettings { Language = language, Theme = docked ? TesseraTheme.Light : TesseraTheme.Dark }.Save(Path.Combine(library, "appearance.json"));
        string input = Path.Combine(folder, "source.png");
        using (var source = new Bitmap(48, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
                source.SetPixel(x, y, Color.FromArgb(x < 3 ? 0 : (x + y) % 3 == 0 ? 128 : 255, x * 5, y * 8, 200 - y * 4));
            source.Save(input);
        }
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        var window = Open();
        try
        {
            WaitTask(window.ImportWorkspaceFilesAsync([input]));
            Guid asset = window.Workspace!.State.Assets.Single().Id;
            WaitTask(window.OpenWorkspaceAssetAsync(window.Workspace.State.Assets.Single()));
            foreach (string menu in new[] { "monochromeMenuItem", "sharpenMenuItem", "asciiMenuItem" })
            {
                NavigationTest.Click(window, menu); Pump();
                Require(Active().Generations.Count == 0, "Opening a filter created a result");
            }
            var mono = Get<GroupBox>("monochromeToolGroup"); var sharpen = Get<GroupBox>("sharpenToolGroup"); var ascii = Get<GroupBox>("asciiToolGroup");
            Require(!ReferenceEquals(mono.Parent, ascii.Parent) && !ReferenceEquals(sharpen.Parent, ascii.Parent), "Filters share one tool panel");
            if (!docked) { Invoke("FloatDockedTool", "ascii", false); Pump(); }
            Require(Get<CheckBox>("asciiDitherInput").Style == window.FindResource("Ts.SwitchBox"), "ASCII toggle has the wrong style");
            if (language == "en") Require(Get<CheckBox>("asciiDitherInput").Content?.ToString() == "Ordered dithering", "ASCII English label is untranslated");
            var ramp = Get<TextBox>("asciiCharactersInput"); ramp.Text = "@|-. ";
            Get<IntegerInput>("asciiColumnsInput").Value = 12;
            Get<CheckBox>("asciiKeepSourceSizeInput").IsChecked = false;
            Get<CheckBox>("asciiDitherInput").IsChecked = true;
            Get<ComboBox>("asciiColorModeInput").SelectedIndex = (int)AsciiColorMode.Custom;
            Set("_asciiForegroundArgb", Color.Gold.ToArgb()); Set("_asciiBackgroundArgb", Color.Navy.ToArgb()); Invoke("UpdateAsciiColorControls");
            string text = Path.Combine(folder, "result.txt"); WaitTask((Task)Invoke("SaveAsciiTextAsync", text)!);
            string expectedText;
            using (var source = ImageLibrary.ReadBitmap(input)) expectedText = ImageFilters.ToAsciiAtColumns(source, 12, ramp.Text, dither: true);
            Require(File.ReadAllText(text) == expectedText, "TXT export differs from the selected rectangular grid");
            NavigationTest.Click(window, "asciiRenderButton"); WaitProcessing();
            var first = Active().SelectedGeneration!;
            Require(first.AppliedRecipe?.Steps.Single().Ascii is { Columns: 12, Dither: true, ColorMode: AsciiColorMode.Custom, KeepSourceSize: false }, "ASCII history lost custom settings");
            Require(first.AppliedRecipe!.Steps.Single().Ascii!.ForegroundArgb == Color.Gold.ToArgb(), "ASCII history lost custom colors");
            using (var source = ImageLibrary.ReadBitmap(input)) using (var expected = new WorkflowExecutor().Execute(source, first.RecipeSnapshot!)) Equal(expected, Get<Bitmap>("_downscaledResult"));
            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 1;
            Get<ComboBox>("asciiColorModeInput").SelectedIndex = (int)AsciiColorMode.Source;
            Get<IntegerInput>("asciiColumnsInput").Value = 7;
            Get<CheckBox>("asciiKeepSourceSizeInput").IsChecked = true;
            Get<CheckBox>("asciiInvertRenderInput").IsChecked = true;
            NavigationTest.Click(window, "asciiRenderButton"); WaitProcessing();
            var second = Active().SelectedGeneration!;
            Require(second.ParentGenerationId == first.Id && second.RecipeSnapshot?.Steps.Count == 2, "ASCII chain lost its parent or recipe");
            string output = Path.Combine(folder, "result.png"); Invoke("SaveResult", output);
            var store = new MacroStore(library);
            var macro = store.Save(new MacroDefinition { Name = "Color ASCII chain", Steps = WorkflowJson.Snapshot(second.RecipeSnapshot!).Steps });
            using (var source = ImageLibrary.ReadBitmap(input)) using (var expected = new WorkflowExecutor().Execute(source, second.RecipeSnapshot!)) Equal(expected, Get<Bitmap>("_downscaledResult"));
            window.Close(); Pump(); window = Open();
            WaitTask(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(a => a.Id == asset)));
            Require(Active().Generations.Count == 2 && Active().Generations[^1].RecipeSnapshot?.Steps[^1].Ascii is { Columns: 7, ColorMode: AsciiColorMode.Source, InvertColors: true, Dither: true }, "ASCII chain settings did not survive reopening");
            Invoke("RestoreHistorySource", Get<HistoryUndoStore>("_historyUndo").Undo() ?? throw new Exception("ASCII undo is missing"));
            Require(Active().Generations.Count == 1 && Active().SelectedGeneration!.Id == first.Id, "ASCII undo did not restore its previous result");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) == sourceHash, "ASCII changed the source file");
            string manifest = Path.Combine(folder, "evidence.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { language, docked, input, sourceHash, output, macro = store.PathFor(macro.Id), restartVerified = true, undoVerified = true, textVerified = true }));
            Console.WriteLine("ASCII workspace evidence: " + manifest);
        }
        finally { window.Close(); Pump(); }

        MainWindow Open()
        {
            var result = new MainWindow(library) { Width = 1220, Height = 820, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            result.EnableWorkspacePrototype(); result.Show(); Pump(); return result;
        }
        T Get<T>(string name) => (T)(window.FindName(name) ?? typeof(MainWindow).GetField(name, Private)!.GetValue(window))!;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
        SourceEntry Active() => Get<SourceEntry>("_activeSource");
        void WaitProcessing()
        {
            DateTime until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "ASCII operation timed out"); Pump();
        }
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Require(expected.Size == actual.Size, "ASCII replay dimensions differ");
        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "ASCII replay pixels differ");
    }
    private static void WaitTask(Task task)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        Require(task.IsCompleted, "ASCII workspace operation timed out"); task.GetAwaiter().GetResult(); Pump();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
