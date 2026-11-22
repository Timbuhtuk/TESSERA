using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Color = System.Drawing.Color;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using Localization = Tessera.Desktop.Localization;

namespace Tessera.RegressionTests;

internal static class TileInspectorChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Tile seams: exact match, axis mapping, singleton and transparent hidden RGB", () =>
        {
            byte[] left = [0, 0, 0, 255, 255, 255, 255, 255, 12, 34, 56, 0];
            byte[] right = [0, 0, 0, 255, 0, 0, 0, 255, 200, 150, 100, 0];
            byte[] top = [0, 0, 0, 255, 0, 0, 0, 255];
            byte[] bottom = [255, 255, 255, 255, 0, 0, 0, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(left, right, top, bottom);
            Require(result.Horizontal.Length == 3 && result.Vertical.Length == 2, "Non-square edge lengths swapped");
            Require(result.Horizontal[0] == 0 && Math.Abs(result.Horizontal[1] - 100) < .001 && result.Horizontal[2] == 0,
                "Wrong edge row or hidden transparent colors counted");
            Require(Math.Abs(result.Vertical[0] - 100) < .001 && result.Vertical[1] == 0, "Wrong column comparison");
            byte[] pixel = [30, 90, 180, 127];
            var single = TileSeamAnalyzer.AnalyzeEdges(pixel, pixel, pixel, pixel);
            Require(single.Horizontal[0] == 0 && single.Vertical[0] == 0, "Single pixel cannot repeat");
        });
        check("Tile seams: opacity is visible on black OR white, symmetry and threshold equality", () =>
        {
            byte[] black = [0, 0, 0, 255], clear = [0, 0, 0, 0], white = [255, 255, 255, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(black, clear, clear, white);
            Require(Math.Abs(result.Horizontal[0] - 100) < .001 && Math.Abs(result.Vertical[0] - 100) < .001,
                "Black or white background hid alpha mismatch");
            var reverse = TileSeamAnalyzer.AnalyzeEdges(clear, black, white, clear);
            Require(result.Horizontal.SequenceEqual(reverse.Horizontal) && result.Vertical.SequenceEqual(reverse.Vertical), "Distance is not symmetric");
            var summary = TileSeamAnalysis.Summarize([0, 5, 10], 5);
            Require(summary.Mean == 5 && summary.Maximum == 10 && summary.AboveThreshold == 1 && summary.Count == 3,
                "Summary or threshold boundary incorrect");
            Require(TileSeamAnalysis.Summarize([0, 0], 0).AboveThreshold == 0, "Exact match rejected at zero threshold");
        });
        check("Tile seams: published OKLab red-green distance and invalid inputs", () =>
        {
            // Published OKLab sRGB primary coordinates give red-green distance approximately 0.5198.
            byte[] red = [0, 0, 255, 255], green = [0, 255, 0, 255];
            var result = TileSeamAnalyzer.AnalyzeEdges(red, green, red, green);
            Require(Math.Abs(result.Horizontal[0] - 51.9813) < .01, "OKLab conversion incorrect");
            bool rejected = false;
            try { TileSeamAnalyzer.AnalyzeEdges([0], [0], red, green); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Malformed BGRA accepted");
            rejected = false;
            try { TileSeamAnalysis.Summarize([1], double.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Invalid threshold accepted");
        });
        TilePaintChecks.Run(check);
        check("Atlas editor WPF: grid, repeat, cell editing and full image history", AtlasEditorChecks.RunSta);
        TileSetChecks.Run(check);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
