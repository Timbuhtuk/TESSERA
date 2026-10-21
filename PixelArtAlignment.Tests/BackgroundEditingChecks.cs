using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DomainColorTest;
using static PixelArtAlignment.Tests.ToolImprovementsChecks;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;

namespace PixelArtAlignment.Tests;

internal static class BackgroundEditingChecks
{
    public static void Run()
    {
        using var source = new Bitmap(23, 17, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Color background = Color.FromArgb(70, 110, 150);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(background);
        for (int y = 4; y <= 12; y++)
            for (int x = 5; x <= 17; x++) source.SetPixel(x, y, Color.Coral);
        source.SetPixel(11, 8, background);
        source.SetPixel(7, 6, Color.FromArgb(127, Color.Coral));
        int[] original = Pixels(source);
        var workspace = new BackgroundWorkspace();
        var host = new Window { Content = workspace, Width = 1000, Height = 720, ShowActivated = false };
        try
        {
            host.Show(); Pump(); workspace.SetImage(source, "Disposable QA fixture");
            workspace.SetCustomBackgroundColor(background);
            Get<Slider>(workspace, "backgroundToleranceInput").Value = 0;
            Wait(workspace.RefreshPreviewAsync());
            ToolImprovementsChecks.Require(workspace.GetOptions().Color?.ToArgb() == background.ToArgb(), "Manual custom background color was not applied");
            Bitmap Result() => (Bitmap)Field(workspace, "_result")!;
            ToolImprovementsChecks.Require(Result().GetPixel(0, 0).A == 0 && Result().GetPixel(11, 8).A == 0 && Result().GetPixel(7, 6).A == 127,
                "Exact custom-color removal failed or changed unrelated alpha");
            int[] unedited = Pixels(Result());
            Get<Slider>(workspace, "backgroundBrushSizeInput").Value = 1;
            typeof(BackgroundWorkspace).GetField("_eraseStroke", Private)!.SetValue(workspace, new HashSet<int>());
            Call(workspace, "PaintEraseSegment", new Point(6, 5), new Point(16, 5));
            Call(workspace, "FinishEraseStroke");
            var expectedErased = Enumerable.Range(6, 11).Select(x => 5 * source.Width + x).ToHashSet();
            int[] edited = Pixels(Result());
            for (int q = 0; q < edited.Length; q++)
                ToolImprovementsChecks.Require(expectedErased.Contains(q) ? (uint)edited[q] >> 24 == 0 : edited[q] == unedited[q],
                    "One-pixel eraser changed pixels outside the stroke or missed a stroke pixel");
            Wait(workspace.SaveAsync(Path.Combine(ArtifactRoot, "erased-custom-background.png"), true));
            using (var exported = new Bitmap(Path.Combine(ArtifactRoot, "erased-custom-background.png")))
                ToolImprovementsChecks.Require(Pixels(exported).SequenceEqual(edited), "PNG export omitted manual eraser corrections");
            Get<ComboBox>(workspace, "backgroundModeInput").SelectedIndex = 1;
            Wait(workspace.RefreshPreviewAsync());
            ToolImprovementsChecks.Require(Result().GetPixel(11, 8).ToArgb() == background.ToArgb(), "Edge-only mode removed an enclosed interior color");
            foreach (int q in expectedErased) ToolImprovementsChecks.Require(Result().GetPixel(q % 23, q / 23).A == 0, "Reprocessing dropped an existing manual erase mask");
            Call(workspace, "UndoEraseStroke");
            foreach (int q in expectedErased) ToolImprovementsChecks.Require(Result().GetPixel(q % 23, q / 23).ToArgb() == Color.Coral.ToArgb(),
                "Undo restored an obsolete pre-reprocessing bitmap instead of the current removal result");
            typeof(BackgroundWorkspace).GetField("_eraseStroke", Private)!.SetValue(workspace, new HashSet<int>());
            Call(workspace, "PaintEraseSegment", new Point(0, 0), new Point(22, 16));
            Call(workspace, "FinishEraseStroke");
            workspace.SetImage(source, "Replacement source"); Wait(workspace.RefreshPreviewAsync());
            ToolImprovementsChecks.Require((int)Field(workspace, "_erasedCount")! == 0 && Field(workspace, "_manualEraseMask") is null,
                "New source retained old manual correction mask");
            ToolImprovementsChecks.Require(Pixels(source).SequenceEqual(original), "Background removal or manual eraser mutated the original source");
            Capture(host, Path.Combine(ArtifactRoot, "background-editor.png"));
        }
        finally { host.Close(); workspace.Dispose(); Pump(); }
    }
}
