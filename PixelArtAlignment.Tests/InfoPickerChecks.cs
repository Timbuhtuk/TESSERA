using System.Windows;
using System.Windows.Controls;
using DomainColorTest;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;
using Color = System.Drawing.Color;

namespace PixelArtAlignment.Tests;

internal static class InfoPickerChecks
{
    public static void Run()
    {
        foreach (bool docked in new[] { false, true }) Verify(docked);
    }

    private static void Verify(bool docked)
    {
        string run = Path.Combine(ArtifactRoot, "info-" + (docked ? "docked-" : "floating-") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string path = Path.Combine(run, "source.png"), other = Path.Combine(run, "other.png");
        using var fixture = new Bitmap(16, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < fixture.Height; y++)
            for (int x = 0; x < fixture.Width; x++)
                fixture.SetPixel(x, y, x < 8 ? Color.Red : Color.Blue);
        fixture.SetPixel(1, 1, Color.FromArgb(70, Color.Red));
        fixture.Save(path);
        using (var replacement = new Bitmap(9, 7)) replacement.Save(other);
        int[] original = Pixels(fixture);
        var window = new MainWindow(Path.Combine(run, "library")) { ShowActivated = false };
        try
        {
            if (docked) window.EnableWorkspacePrototype();
            window.Show(); Pump();
            if (docked)
            {
                Wait(window.ImportWorkspaceFilesAsync(new[] { path }));
                Wait(window.OpenWorkspaceAssetAsync(window.Workspace!.State.Assets.Single(asset => asset.Kind == WorkspaceAssetKind.Image)));
            }
            else Call(window, "LoadImage", path);
            Get<MenuItem>(window, "infoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            AwaitInfo();
            var source = Get<PixelPreview>(window, "sourcePreview");
            var result = Get<PixelPreview>(window, "resultPreview");
            var palette = Get<ItemsControl>(window, "infoPaletteList").Items.Cast<PaletteColorEntry>().ToArray();
            ToolImprovementsChecks.Require(palette.Length == 2, "Info color palette did not populate in " + (docked ? "docked" : "floating") + " host");
            var red = palette.Single(item => item.Color.ToArgb() == Color.Red.ToArgb());
            Call(window, "InfoColorClick", new Button { DataContext = red }, new RoutedEventArgs(Button.ClickEvent));
            var dialog = (ColorReplaceWindow)Field(window, "_replaceWindow")!;
            ToolImprovementsChecks.Require(dialog.IsVisible && source.HighlightRgb == 0xFF0000 && result.HighlightRgb is null,
                "Selecting a swatch did not highlight its exact current source basis");
            ToolImprovementsChecks.Require(docked ? ReferenceEquals(dialog.Owner, window) : !ReferenceEquals(dialog.Owner, window),
                "Color picker owner does not follow docked/floating tool host");
            Get<Button>(dialog, "sourcePickerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ToolImprovementsChecks.Require(source.IsColorPicking && !result.IsColorPicking, "Info pipette was not restricted to source basis");
            Call(window, "InfoColorPicked", source, new PixelColorPickedEventArgs(Color.Blue, 12, 3)); Pump();
            ToolImprovementsChecks.Require(Get<TextBox>(dialog, "oldHexInput").Text == "#0000FF" && source.HighlightRgb == 0x0000FF &&
                !source.IsColorPicking, "Picked RGB did not update replacement dialog and selected pixel highlight");
            Capture(window, Path.Combine(run, "source-highlight.png"));
            Capture(dialog, Path.Combine(run, "replacement-dialog.png"));
            dialog.Close(); Pump();
            ToolImprovementsChecks.Require(source.HighlightRgb is null && !source.IsColorPicking, "Closing replacement dialog left stale source selection");

            Get<IntegerInput>(window, "widthInput").Value = 8; Get<IntegerInput>(window, "heightInput").Value = 4;
            Get<CheckBox>(window, "spriteInput").IsChecked = true;
            Call(window, "ScaleOnlyImage", null, EventArgs.Empty);
            Until(() => !(bool)Field(window, "_processing")!, "size result");
            window.ProcessingBasis = 1; Call(window, "RefreshInfo"); AwaitInfo();
            ToolImprovementsChecks.Require(ReferenceEquals(Field(window, "_infoBasisShown"), result.Image), "Info basis did not refresh to selected result");
            window.AlternativePreview = true; Pump();
            var comparison = Get<PixelPreview>(window, "comparisonPreview");
            Call(window, "BeginInfoColorPicking");
            ToolImprovementsChecks.Require(result.IsColorPicking && comparison.IsColorPicking && !source.IsColorPicking,
                "Result-basis pipette did not follow the alternative preview or leaked to source preview");
            Call(window, "InfoColorPicked", result, new PixelColorPickedEventArgs(Color.Red, 1, 1)); Pump();
            ToolImprovementsChecks.Require(result.HighlightRgb == 0xFF0000 && comparison.HighlightRgb == 0xFF0000 && source.HighlightRgb is null,
                "Result selection did not isolate highlight across matching previews");
            window.ProcessingBasis = 0; Call(window, "RefreshInfo"); Pump(); AwaitInfo();
            ToolImprovementsChecks.Require(Field(window, "_replaceWindow") is null && source.HighlightRgb is null &&
                result.HighlightRgb is null && !source.IsColorPicking && !result.IsColorPicking, "Switching basis retained dialog or stale highlight/pipette");
            ToolImprovementsChecks.Require(comparison.HighlightRgb is null && !comparison.IsColorPicking, "Basis switch left stale alternative preview selection");
            window.AlternativePreview = false;
            ToolImprovementsChecks.Require(Pixels((Bitmap)Field(window, "_sourceImage")!).SequenceEqual(original), "Info picking changed original pixels");

            Call(window, "BeginInfoColorPicking"); Call(window, "LoadImage", other); Pump();
            ToolImprovementsChecks.Require(!source.IsColorPicking && source.HighlightRgb is null && Field(window, "_replaceWindow") is null,
                "Switching source left a stale info picker");
            Call(window, "CloseEditorTools"); Pump();
            ToolImprovementsChecks.Require(!source.IsColorPicking && source.HighlightRgb is null && result.HighlightRgb is null,
                "Closing the tool left a stale color highlight");
        }
        finally { window.Close(); Pump(); }

        void AwaitInfo() => Until(() => ((Button)Field(window, "_infoEyedropperButton")!).IsEnabled, "info palette");
    }

    private static void Until(Func<bool> condition, string operation)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Pump(); Thread.Sleep(5);
            ToolImprovementsChecks.Require(watch.Elapsed < TimeSpan.FromSeconds(20), "Timed out waiting for " + operation);
        }
        Pump();
    }
}
