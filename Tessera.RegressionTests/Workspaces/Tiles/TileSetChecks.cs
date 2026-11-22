using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessera.Desktop;
using Tessera.Core.Imaging;
using Color = System.Drawing.Color;

namespace Tessera.RegressionTests;

internal static class TileSetChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Tileset: exact grids, rectangular cells, gutters and row-major regions", () =>
        {
            var layout = TileSetLayout.FromTileSize(96, 192, 32, 32);
            Require(layout.Columns == 3 && layout.Rows == 6 && layout.Count == 18 && layout == TileSetLayout.FromGrid(96, 192, 3, 6), "96x192 did not produce 3x6 cells of 32x32");
            var regions = TileSetSlicer.GetRegions(96, 192, layout);
            Require(regions[0] == new TileSetRegion(0, 0, 32, 32, 0, 0) && regions[3].X == 0 && regions[3].Y == 32 &&
                regions[^1] == new TileSetRegion(64, 160, 32, 32, 2, 5), "Tile order or last cell origin incorrect");
            var rectangular = TileSetLayout.FromTileSize(96, 128, 24, 16);
            Require(rectangular.Columns == 4 && rectangular.Rows == 8 && rectangular.Count == 32, "Rectangular cells rejected");
            var gutters = TileSetLayout.FromTileSize(100, 199, 32, 32, 1, 1, 1, 1);
            Require(gutters == TileSetLayout.FromGrid(100, 199, 3, 6, 1, 1, 1, 1), "Grid and pixel modes disagree about spacing");
            var spaced = TileSetSlicer.GetRegions(100, 199, gutters);
            Require(spaced[1].X == 34 && spaced[3].Y == 34 && spaced[^1].X == 67 && spaced[^1].Y == 166, "Margins/gutters included inside tiles");
            Reject(() => TileSetLayout.FromTileSize(95, 192, 32, 32));
            Reject(() => TileSetLayout.FromGrid(96, 192, 5, 6));
            Reject(() => TileSetLayout.FromTileSize(96, 192, 0, 32));
            Reject(() => TileSetLayout.FromTileSize(128, 128, 1, 1));
            Reject(() => TileSetSlicer.GetRegions(96, 192, new(32, 32, 4, 6)));
        });
        check("Tileset: automatic 96x192 suggestion, valid alternatives and ambiguous flat images", () =>
        {
            byte[] pixels = new byte[96 * 192 * 4];
            var suggestions = TileSetSlicer.Suggest(pixels, 96, 192);
            Require(suggestions[0].Layout.TileWidth == 32 && suggestions[0].Layout.Columns == 3 && suggestions[0].Layout.Rows == 6,
                "Common 96x192 atlas was not proposed as 3x6");
            Require(suggestions.Count > 1 && suggestions[0].Reason.Contains("Проверьте"), "Ambiguous image was presented as a unique answer");
            foreach (var suggestion in suggestions) Require(TileSetSlicer.GetRegions(96, 192, suggestion.Layout).Count == suggestion.Layout.Count, "Auto mode produced an invalid layout");
            var small = TileSetSlicer.Suggest(new byte[3 * 5 * 4], 3, 5);
            Require(small.Count == 0, "Tiny image received an invented common tile size");
            Reject(() => TileSetSlicer.Suggest(new byte[3], 96, 192));
        });
    }

    private static void Reject(Action action)
    {
        bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid tileset settings were accepted");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
