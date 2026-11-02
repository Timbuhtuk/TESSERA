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
        var resize = new GenerationEntry { OperationId = "image.resize", Operation = "Размер", Width = 8, Height = 6,
            Caption = "РАЗМЕР · 8 × 6", Log = "Размер изменён." };
        string[] lines = resize.ResultTreeDescription.Split('\n');
        if (lines.Length != 4 || lines[0] != "Размер" || lines[1] != "Размер: 8 × 6 px" ||
            !lines[2].StartsWith("Создан:") || lines[3] != "Размер изменён." || resize.ResultTreeDescription.Contains("РАЗМЕР ·"))
            throw new Exception("Result hint does not follow the shared operation, size, time and details format");
        var alignment = new GenerationEntry { IsAlignment = true, CellSize = 4, Width = 12, Height = 10 };
        if (!alignment.ResultTreeDescription.StartsWith("Глобальная сетка\nРазмер: 12 × 10 px\nСоздан:"))
            throw new Exception("Alignment hint does not follow the shared format");
        var ascii = new GenerationEntry { Operation = "ASCII", Width = 8, Height = 6,
            Log = "ASCII: 8 × 6 px; цвет: монохром." };
        if (ascii.ToolTipDetails != "цвет: монохром." || ascii.ResultTreeDescription.Split("8 × 6").Length != 2)
            throw new Exception("Result hint repeats dimensions from its details");
        var missingParent = new GenerationEntry { ParentGenerationId = Guid.NewGuid() };
        HistoryBranchLayout.Build([missingParent], new BranchPresentation { Detached = [missingParent.Id] });
        if (missingParent.ResultTreeDescription.Contains(missingParent.ParentGenerationId!.Value.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !missingParent.ResultTreeDescription.Contains("Прежний результат недоступен"))
            throw new Exception("Detached branch hint exposed an internal parent id");
    }
}
