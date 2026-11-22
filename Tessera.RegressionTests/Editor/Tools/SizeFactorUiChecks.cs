using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Workflows;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.RegressionTests;

internal static class SizeFactorUiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Size factor UI verification timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "wpf", "verification", "size-factor-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        string imagePath = Path.Combine(folder, "source.png");
        using (var source = new Bitmap(4, 3)) source.Save(imagePath);

        var window = new MainWindow(Path.Combine(folder, "library")) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            WindowTestAccess.Invoke(window, "LoadImage", BindingFlags.Instance | BindingFlags.NonPublic, new object[] { imagePath });
            Get<RadioButton>("scaleFactorModeInput").IsChecked = true;
            var factor = Get<TextBox>("scaleFactorInput");
            factor.Text = "0,5";
            Require(Get<TextBlock>("sizeHint").Text.Contains("2 × 2"), "Half scale did not preview rounded dimensions");
            NavigationTest.Click(window, "scaleOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(2, 2), "Half scale produced the wrong dimensions");
            var first = Get<SourceEntry>("_activeSource").SelectedGeneration!;
            Require(first.AppliedRecipe!.Steps.Single().ResizeFactor?.Factor == 0.5 && first.AppliedRecipe.Steps.Single().Downscale is null,
                "Half scale was recorded as fixed pixel dimensions");

            NavigationTest.Select(window, "ProcessingBasis").SelectedIndex = 1;
            factor.Text = "1.5";
            Require(Get<TextBlock>("sizeHint").Text.Contains("3 × 3"), "One-and-a-half scale did not use the selected result");
            NavigationTest.Click(window, "scaleOnlyButton"); Wait();
            Require(Get<Bitmap>("_downscaledResult").Size == new System.Drawing.Size(3, 3), "One-and-a-half scale produced the wrong dimensions");
            var asset = Get<SourceEntry>("_activeSource");
            Require(asset.SelectedGeneration!.ParentGenerationId == first.Id, "Factor result lost its parent");
            Require(HistoryRecipes.TryBuild(asset, asset.SelectedGeneration, out var recipe, out _), "Factor recipe could not be built");
            var store = new MacroStore(Path.Combine(folder, "library"));
            var macro = store.Save(new MacroDefinition { Name = "Масштаб дважды", Steps = recipe!.Steps });
            string json = File.ReadAllText(store.PathFor(macro.Id));
            Require(!json.Contains("targetWidth", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("targetHeight", StringComparison.OrdinalIgnoreCase), "Macro persisted computed pixel dimensions");
            using (var other = new Bitmap(10, 7))
            using (var replay = new WorkflowExecutor().Execute(other, new ProcessingRecipe { Steps = MacroStore.Read(store.PathFor(macro.Id)).Steps }))
                Require(replay.Size == new System.Drawing.Size(8, 6), "Factor macro did not resize relative to the new image");

            factor.Text = "0";
            Require(!Get<Button>("scaleOnlyButton").IsEnabled, "Zero factor remained actionable");
            factor.Text = "999999999999";
            Require(!Get<Button>("scaleOnlyButton").IsEnabled, "Overflowing factor remained actionable");
            Get<RadioButton>("pixelSizeModeInput").IsChecked = true;
            Require(Get<StackPanel>("pixelSizeControls").Visibility == Visibility.Visible, "Pixel-size mode did not return");
        }
        finally { window.Close(); }

        T Get<T>(string name) => (T)(window.FindControl(name) ?? WindowTestAccess.GetField(window, name, BindingFlags.Instance | BindingFlags.NonPublic))!;
        void Wait()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Get<bool>("_processing") && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
            Require(!Get<bool>("_processing"), "Scale operation timed out"); Pump();
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
