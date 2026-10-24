using System.Windows.Media;
using System.Windows.Media.Imaging;
using DomainColorTest;
using Color = System.Windows.Media.Color;

namespace PixelArtAlignment.Tests;

internal static class TilePaintChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Tile paint: source-over on opaque, translucent and transparent pixels", () => Sta(() =>
        {
            var asset = Asset(3, 1, [255, 0, 0, 255, 255, 0, 0, 128, 17, 43, 61, 0]);
            var before = asset.Capture();
            asset.PaintLine(0, 0, 2, 0, Color.FromArgb(128, 255, 0, 0), 1, before);
            Require(asset.GetPixel(0, 0) == Color.FromArgb(255, 128, 0, 127), "Opaque destination lost alpha or color");
            Require(asset.GetPixel(1, 0) == Color.FromArgb(192, 170, 0, 85), "Translucent destination was blended incorrectly");
            Require(asset.GetPixel(2, 0) == Color.FromArgb(128, 255, 0, 0), "Hidden RGB polluted transparent pixels");
            Require(asset.FinishEdit(before), "Stroke did not enter history");
            byte[] painted = (byte[])asset.Pixels.Clone();
            asset.Undo(); Require(asset.Pixels.SequenceEqual(before.Pixels), "Undo changed original pixels");
            asset.Redo(); Require(asset.Pixels.SequenceEqual(painted), "Redo changed painted pixels");
        }));
        check("Tile paint: even overlapping strokes, separate strokes accumulate, zero and full opacity", () => Sta(() =>
        {
            var asset = Asset(5, 3, Enumerable.Repeat(new byte[] { 255, 255, 255, 255 }, 15).SelectMany(p => p).ToArray());
            var before = asset.Capture();
            var ink = Color.FromArgb(128, 0, 0, 0);
            asset.PaintLine(0, 1, 4, 1, ink, 2, before);
            asset.PaintLine(4, 1, 0, 1, ink, 2, before);
            for (int q = 0; q < 5; q++) Require(asset.GetPixel(q, 1) == Color.FromRgb(127, 127, 127), "Overlapping dabs accumulated");
            asset.FinishEdit(before);
            var next = asset.Capture();
            asset.PaintLine(0, 1, 4, 1, ink, 1, next); asset.FinishEdit(next);
            Require(asset.GetPixel(2, 1) == Color.FromRgb(63, 63, 63), "A separate stroke did not accumulate");
            asset.Undo();
            before = asset.Capture(); long version = asset.Version;
            asset.PaintLine(0, 0, 4, 2, Colors.Transparent, 4, before);
            Require(!asset.FinishEdit(before) && asset.CanRedo && asset.Version == version, "Zero opacity changed pixels, history or version");
            asset.PaintLine(0, 0, 0, 0, Colors.Red, 1);
            Require(asset.GetPixel(0, 0) == Colors.Red, "Opaque paint did not replace color");
            asset.PaintLine(0, 0, 0, 0, Colors.Transparent, 1, erase: true);
            Require(asset.GetPixel(0, 0).A == 0, "Eraser did not clear alpha");
        }));
        check("Tile fill: connected region, no edge wrapping, hidden RGB and one undo", () => Sta(() =>
        {
            byte[] pixels = Enumerable.Repeat(new byte[] { 255, 255, 255, 255 }, 15).SelectMany(p => p).ToArray();
            for (int y = 0; y < 3; y++) { int q = (y * 5 + 2) * 4; pixels[q] = pixels[q + 1] = pixels[q + 2] = 0; }
            var asset = Asset(5, 3, pixels);
            Require(asset.Fill(0, 1, Color.FromArgb(128, 255, 0, 0)), "Fill did not change region");
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 5; x++)
                    Require(asset.GetPixel(x, y) == (x < 2 ? Color.FromRgb(255, 127, 127) : x == 2 ? Colors.Black : Colors.White), "Fill crossed boundary or missed a connected pixel");
            asset.Undo(); Require(asset.Pixels.SequenceEqual(pixels) && !asset.CanUndo, "Fill did not undo as one action");
            Require(!asset.Fill(0, 0, Colors.White) && asset.CanRedo, "Unchanged fill cleared redo");
            Require(!asset.Fill(0, 0, Colors.Transparent) && asset.CanRedo, "Invisible fill cleared redo");
            var clear = Asset(3, 1, [9, 3, 7, 0, 90, 30, 70, 0, 0, 0, 0, 255]);
            clear.Fill(0, 0, Color.FromArgb(128, 255, 0, 0));
            Require(clear.GetPixel(0, 0) == clear.GetPixel(1, 0) && clear.GetPixel(2, 0) == Colors.Black, "Transparent area segmentation is incorrect");
            var diagonal = Asset(2, 2, [255,255,255,255, 0,0,0,255, 0,0,0,255, 255,255,255,255]);
            diagonal.Fill(0, 0, Colors.Red);
            Require(diagonal.GetPixel(1, 1) == Colors.White, "Fill crossed a diagonal corner");
        }));
        check("Tile fill: whole-tile tint preserves tone differences and large regions are iterative", () => Sta(() =>
        {
            var asset = Asset(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]);
            asset.Fill(0, 0, Color.FromArgb(128, 255, 0, 0), entireTile: true);
            Require(asset.GetPixel(0, 0) == Color.FromRgb(128, 0, 0) && asset.GetPixel(1, 0) == Color.FromRgb(255, 127, 127), "Whole-tile tint flattened source tones");
            var large = Asset(256, 256, new byte[256 * 256 * 4]);
            large.Fill(0, 0, Colors.Red);
            Require(large.GetPixel(255, 255) == Colors.Red && large.Pixels.Where((_, q) => q % 4 == 3).All(a => a == 255), "Large connected fill was incomplete");
        }));
    }

    private static TileAsset Asset(int width, int height, byte[] pixels) => new(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20))) throw new Exception("Tile painting check timed out");
        if (failure is not null) throw new Exception(failure.ToString(), failure);
    }
}
