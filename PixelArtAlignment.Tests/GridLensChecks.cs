using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DomainColorTest;
using Bitmap = System.Drawing.Bitmap;
using MediaColor = System.Windows.Media.Color;

namespace PixelArtAlignment.Tests;

internal static class GridLensChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Grid lens checks timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        using var bitmap = new Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.FromArgb(255, 32, 32, 32));
        using var original = new MemoryStream(); bitmap.Save(original, ImageFormat.Png);
        var preview = new PixelPreview { Image = bitmap, Zoom = 8 };
        var window = new Window { Content = preview, Width = 360, Height = 300, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Pump();
            byte[] plain = Capture(preview);
            preview.GridLens = new PreviewGridLens(8, 6, 0, 0, MediaColor.FromArgb(255, 255, 0, 0));
            byte[] red = Capture(preview);
            Require(!plain.SequenceEqual(red) && CountPixels(red, 360, 300, 0) > 50, "Grid lines are missing from preview rendering");
            preview.GridLens = new PreviewGridLens(8, 6, 3, 2, MediaColor.FromArgb(255, 255, 0, 0));
            byte[] moved = Capture(preview);
            Require(!red.SequenceEqual(moved), "Grid offset did not move the lines");
            preview.GridLens = new PreviewGridLens(8, 6, 3, 2, MediaColor.FromArgb(255, 0, 255, 0));
            byte[] green = Capture(preview);
            Require(!moved.SequenceEqual(green) && CountPixels(green, 360, 300, 1) > 50, "Grid color did not change");
            preview.Zoom = 4; Pump();
            byte[] zoomed = Capture(preview);
            Require(CountPixels(zoomed, 360, 300, 1) > 20, "Grid vanished after zooming");
            preview.GridLens = null;
            byte[] cleared = Capture(preview);
            Require(!zoomed.SequenceEqual(cleared), "Closing the lens left lines on the preview");
            using var current = new MemoryStream(); bitmap.Save(current, ImageFormat.Png);
            Require(original.ToArray().SequenceEqual(current.ToArray()) && ReferenceEquals(preview.Image, bitmap),
                "Preview grid changed source pixels or exported image data");
        }
        finally { preview.Image = null; window.Close(); Pump(); }
        Console.WriteLine("  Grid render, offsets, color, zoom and source PNG isolation passed");
    }

    private static byte[] Capture(PixelPreview preview)
    {
        preview.UpdateLayout();
        var image = new RenderTargetBitmap(360, 300, 96, 96, PixelFormats.Pbgra32);
        image.Render(preview);
        byte[] pixels = new byte[360 * 300 * 4];
        image.CopyPixels(pixels, 360 * 4, 0);
        return pixels;
    }

    private static int CountPixels(byte[] pixels, int width, int height, int channel)
    {
        int count = 0;
        for (int q = 0; q < width * height * 4; q += 4)
            if (channel == 0 && pixels[q + 2] > 150 && pixels[q + 1] < 100 ||
                channel == 1 && pixels[q + 1] > 150 && pixels[q + 2] < 100) count++;
        return count;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
