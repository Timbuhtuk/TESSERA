using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

internal static class ResultIconChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Result icon rendering timed out");
        if (failure is not null) throw failure;
    }

    private static void Verify()
    {
        string[] ids =
        [
            "image.resize", "image.trim", "image.colors", "image.cleanup",
            "grid.align", "grid.align.local", "grid.align.local.snapped", "grid.reduce",
            "filter.monochrome", "filter.sharpen", "filter.ascii", "color.replace",
            "color.channel.adjust", "background.remove", "image.finalist.reduce",
            "macro.apply", "manual.erase"
        ];
        foreach (string id in ids)
        {
            if (!ResultIcon.Supports(id)) throw new Exception($"Icon not mapped: {id}");
            var icon = new ResultIcon { OperationId = id, Width = 32, Height = 32 };
            icon.Measure(new System.Windows.Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32));
            var rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(icon);
            var pixels = new byte[32 * 32 * 4]; rendered.CopyPixels(pixels, 32 * 4, 0);
            if (!pixels.Where((_, q) => q % 4 == 3).Any(alpha => alpha > 0))
                throw new Exception($"Icon rendered empty: {id}");
        }

        foreach (string id in new[] { "image.profile", "contours.heatmap", "manual.tile" })
        {
            if (ResultIcon.Supports(id)) throw new Exception($"Rejected icon still mapped: {id}");
            var icon = new ResultIcon { OperationId = id, Width = 32, Height = 32 };
            icon.Measure(new System.Windows.Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32));
            var rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(icon);
            var pixels = new byte[32 * 32 * 4]; rendered.CopyPixels(pixels, 32 * 4, 0);
            if (!pixels.Where((_, q) => q % 4 == 3).Any(alpha => alpha > 0))
                throw new Exception($"Generic result icon rendered empty: {id}");
        }
        var resize = new GenerationEntry { OperationId = "image.resize", Operation = "Размер", Width = 8, Height = 6 };
        if (!resize.ResultTreeDescription.Contains("Размер") || !resize.ResultTreeDescription.Contains("8 × 6 px"))
            throw new Exception("Result card hint lost the operation or resolution");
    }
}
