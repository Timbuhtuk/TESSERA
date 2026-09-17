using System.Windows.Media.Imaging;
using PixelArtDownscale;

namespace PixelArtAlignment.Tests;

internal static class BackgroundRemovalChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Edge removal preserves enclosed colors and follows open background passages", () =>
        {
            using var image = Fixture();
            using var cleared = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0);
            Require(cleared.GetPixel(0, 0).A == 0 && cleared.GetPixel(3, 3).ToArgb() == Color.White.ToArgb(), "Exterior remained or enclosed color was erased");
            Require(cleared.GetPixel(4, 4).ToArgb() == image.GetPixel(4, 4).ToArgb(), "Translucent foreground changed");
            Require(image.GetPixel(0, 0).A == 255, "Source changed");
            image.SetPixel(3, 1, Color.White);
            image.SetPixel(3, 2, Color.White);
            using var open = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0);
            Require(open.GetPixel(3, 3).A == 0 && open.GetPixel(2, 3).A == 255, "Open passage was not followed or boundary was crossed");
        });
        check("Edge removal starts on all sides and does not cross diagonal boundaries", () =>
        {
            using var image = new Bitmap(7, 7);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.Red, 0, 0, 7, 1);
                graphics.FillRectangle(Brushes.Red, 0, 6, 7, 1);
                graphics.FillRectangle(Brushes.Red, 3, 0, 1, 7);
            }
            using var sides = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0, Color.White);
            Require(sides.GetPixel(1, 3).A == 0 && sides.GetPixel(5, 3).A == 0, "Disconnected left/right backgrounds were missed");
            Require(sides.GetPixel(3, 0).A == 255 && sides.GetPixel(3, 3).A == 255, "Foreground touching an edge was erased");
            image.RotateFlip(RotateFlipType.Rotate90FlipNone);
            using var ends = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0, Color.White);
            Require(ends.GetPixel(3, 1).A == 0 && ends.GetPixel(3, 5).A == 0, "Top/bottom backgrounds were missed");
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
            foreach (var (x, y) in new[] { (3, 2), (2, 3), (4, 3), (3, 4) }) image.SetPixel(x, y, Color.Red);
            using var diagonal = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0);
            Require(diagonal.GetPixel(3, 3).A == 255 && diagonal.GetPixel(2, 2).A == 0, "Diagonal contact leaked into an enclosed pixel");
        });
        check("Edge tolerance does not accumulate and transparent pixels remain traversable", () =>
        {
            using var image = new Bitmap(15, 3);
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
            for (int x = 0; x < 15; x++) image.SetPixel(x, 1, Color.FromArgb(x * 5, x * 5, x * 5));
            using var exact = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0, Color.Black);
            using var tolerant = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 4, Color.Black);
            Require(exact.GetPixel(1, 1).A == 255 && tolerant.GetPixel(2, 1).A == 0, "Tolerance was ignored");
            Require(tolerant.GetPixel(3, 1).A == 255 && tolerant.GetPixel(14, 1).A == 255, "Tolerance accumulated along a gradient");
            image.SetPixel(1, 1, Color.FromArgb(0, 13, 157, 209));
            image.SetPixel(2, 1, Color.Black);
            using var transparent = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0, Color.Black);
            Require(transparent.GetPixel(2, 1).A == 0, "Hidden RGB blocked a transparent passage");
            using var alreadyTransparent = new Bitmap(7, 7);
            alreadyTransparent.SetPixel(3, 3, Color.Black);
            using var automatic = BackgroundRemover.Remove(alreadyTransparent, BackgroundRemovalMode.EdgeConnected);
            Require(automatic.GetPixel(3, 3).A == 255, "Automatic detection erased foreground on a transparent canvas");
        });
        check("Edge removal handles thin images, validation and cancellation", () =>
        {
            foreach (var (width, height) in new[] { (1, 1), (1, 9), (9, 1) })
            {
                using var image = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
                using var cleared = BackgroundRemover.Remove(image, BackgroundRemovalMode.EdgeConnected, 0);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++) Require(cleared.GetPixel(x, y).A == 0, "Thin image background remained");
            }
            using var source = Fixture();
            try { using var unexpected = BackgroundRemover.Remove(source, (BackgroundRemovalMode)99); throw new Exception("Invalid mode accepted"); }
            catch (ArgumentOutOfRangeException) { }
            try { using var unexpected = BackgroundRemover.Remove(source, BackgroundRemovalMode.EdgeConnected, cancellationToken: new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Require(source.GetPixel(0, 0).A == 255, "Cancellation changed source");
        });
        check("Both ICO modes match previews with different enclosed-area behavior at every size", () =>
        {
            using var image = Fixture();
            foreach (var mode in new[] { BackgroundRemovalMode.GlobalColor, BackgroundRemovalMode.EdgeConnected })
            {
                using var prepared = BackgroundRemover.Remove(image, mode, 0);
                var options = new IconExportOptions
                {
                    Sizes = new[] { 7, 14, 28 }, ResizeMode = IconResizeMode.NearestNeighbor,
                    RemoveBackground = true, BackgroundRemovalMode = mode, BackgroundTolerance = 0
                };
                byte[] bytes = IconExporter.Encode(image, options);
                byte[] expected = IconExporter.Encode(prepared, new() { Sizes = options.Sizes, ResizeMode = options.ResizeMode });
                Require(bytes.SequenceEqual(expected), "Export differs from prepared preview source");
                using var stream = new MemoryStream(bytes);
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames)
                {
                    int size = frame.PixelWidth;
                    var data = new byte[size * size * 4];
                    frame.CopyPixels(data, size * 4, 0);
                    int center = ((size / 2) * size + size / 2) * 4 + 3;
                    Require(data[3] == 0 && data[center] == (mode == BackgroundRemovalMode.EdgeConnected ? 255 : 0), "Mode was not applied to every ICO frame");
                }
            }
        });
        check("Background removal preserves foreground RGBA and the original, including enclosed areas", () =>
        {
            using var image = Fixture();
            using var cleared = BackgroundRemover.Remove(image, 0);
            Require(cleared.Size == image.Size && cleared.GetPixel(0, 0).A == 0 && cleared.GetPixel(3, 3).A == 0, "Background or enclosed hole remained");
            Require(cleared.GetPixel(2, 2).ToArgb() == image.GetPixel(2, 2).ToArgb(), "Foreground RGBA changed");
            Require(cleared.GetPixel(4, 4).ToArgb() == image.GetPixel(4, 4).ToArgb(), "Translucent foreground changed");
            Require(image.GetPixel(0, 0).ToArgb() == Color.White.ToArgb(), "Source was modified");
        });
        check("Background tolerance, explicit colors and transparent borders", () =>
        {
            using var image = Fixture();
            image.SetPixel(1, 1, Color.FromArgb(240, 240, 240));
            using var exact = BackgroundRemover.Remove(image, 2);
            using var tolerant = BackgroundRemover.Remove(image, 8);
            Require(exact.GetPixel(1, 1).A == 255 && tolerant.GetPixel(1, 1).A == 0, "Tolerance did not affect similar colors");
            using var transparent = new Bitmap(7, 7);
            transparent.SetPixel(3, 3, Color.Black);
            using var automatic = BackgroundRemover.Remove(transparent);
            using var manual = BackgroundRemover.Remove(transparent, 0, Color.Black);
            Require(automatic.GetPixel(3, 3).A == 255 && manual.GetPixel(3, 3).A == 0, "Transparent border erased foreground or explicit color ignored");
            using var one = new Bitmap(1, 1); one.SetPixel(0, 0, Color.Blue);
            using var removed = BackgroundRemover.Remove(one);
            Require(removed.GetPixel(0, 0).A == 0, "Single-pixel image failed");
        });
        check("ICO background removal matches prepared previews and is opt-in", () =>
        {
            using var image = Fixture();
            using var prepared = BackgroundRemover.Remove(image, 0);
            var options = new IconExportOptions { Sizes = new[] { 7 }, ResizeMode = IconResizeMode.NearestNeighbor, RemoveBackground = true, BackgroundTolerance = 0 };
            using var stream = new MemoryStream(IconExporter.Encode(image, options));
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var data = new byte[7 * 7 * 4];
            decoder.Frames[0].CopyPixels(data, 7 * 4, 0);
            Require(data[3] == 0 && data[(2 * 7 + 2) * 4 + 3] == 255, "Saved ICO lost background removal or foreground");
            byte[] expected = IconExporter.Encode(prepared, new IconExportOptions { Sizes = new[] { 7 }, ResizeMode = IconResizeMode.NearestNeighbor });
            Require(stream.ToArray().SequenceEqual(expected), "Export differs from prepared preview source");
            using var opaqueStream = new MemoryStream(IconExporter.Encode(image, new IconExportOptions { Sizes = new[] { 7 } }));
            var originalDecoder = new IconBitmapDecoder(opaqueStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            originalDecoder.Frames[0].CopyPixels(data, 7 * 4, 0);
            Require(data[3] == 255, "Background removed when disabled");
        });
        check("Background removal validates tolerance and cancellation", () =>
        {
            using var image = Fixture();
            foreach (int value in new[] { -1, 101 })
            {
                try { using var unexpected = BackgroundRemover.Remove(image, value); throw new Exception("Invalid tolerance accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            try { using var unexpected = BackgroundRemover.Remove(image, cancellationToken: new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Require(image.GetPixel(0, 0).A == 255, "Cancellation changed source");
        });
    }

    private static Bitmap Fixture()
    {
        var image = new Bitmap(7, 7);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.FillRectangle(Brushes.Red, 1, 1, 5, 5);
        image.SetPixel(3, 3, Color.White);
        image.SetPixel(4, 4, Color.FromArgb(73, 30, 50, 200));
        return image;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
