using System.Drawing.Imaging;
using PixelArtDownscale;
using PixelArtWorkflows;

namespace PixelArtAlignment.Tests;

internal static class ChannelAdjustmentChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("RGBA adjustment changes only the selected channel across all 256 values", ChannelsAndClamping);
        check("RGBA percentages use current values, support fractions and keep zero at zero", FractionsAndTransparency);
        check("RGBA adjustment rejects invalid settings and supports cancellation", Validation);
        check("RGBA adjustment recipes roundtrip and replay exactly", Recipes);
    }

    private static void ChannelsAndClamping()
    {
        using var source = new Bitmap(256, 2, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 256; x++) source.SetPixel(x, y, Color.FromArgb(y == 0 ? 0 : x, x, 255 - x, x * 13 % 256));
        using var snapshot = (Bitmap)source.Clone();
        foreach (var channel in Enum.GetValues<RgbaChannel>())
        foreach (bool subtract in new[] { false, true })
        foreach (var mode in Enum.GetValues<ChannelAdjustmentMode>())
        {
            using var output = ChannelAdjustment.Apply(source, new()
            {
                Channel = channel, Mode = mode, Amount = mode == ChannelAdjustmentMode.Value ? 10 : 25, Subtract = subtract
            });
            Require(output.Size == source.Size, "Channel adjustment resized the image");
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 256; x++)
                {
                    var before = source.GetPixel(x, y); var after = output.GetPixel(x, y);
                    int original = Get(before, channel);
                    int expected = mode == ChannelAdjustmentMode.Value ? Math.Clamp(original + (subtract ? -10 : 10), 0, 255)
                        : Math.Min(255, (original * (subtract ? 3 : 5) + 2) / 4);
                    foreach (var q in Enum.GetValues<RgbaChannel>())
                        Require(Get(after, q) == (q == channel ? expected : Get(before, q)), $"{channel}, {mode}, subtract={subtract}: unexpected {q} at {x},{y}");
                    Require(before.ToArgb() == snapshot.GetPixel(x, y).ToArgb(), "Channel adjustment mutated the source");
                }
        }
    }

    private static void FractionsAndTransparency()
    {
        using var source = new Bitmap(2, 1, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(0, 100, 80, 60));
        source.SetPixel(1, 0, Color.FromArgb(97, 100, 80, 60));
        using var fraction = ChannelAdjustment.Apply(source, new() { Channel = RgbaChannel.R, Amount = .5 });
        Require(fraction.GetPixel(0, 0).R == 101 && fraction.GetPixel(0, 0).A == 0, "Fractional values or hidden RGB were lost");
        using var percent = ChannelAdjustment.Apply(source, new() { Channel = RgbaChannel.A, Mode = ChannelAdjustmentMode.Percent, Amount = 50 });
        Require(percent.GetPixel(0, 0).A == 0 && percent.GetPixel(1, 0).A == 146, "Percent used a fixed 255 baseline or rounded down");
        using var reveal = ChannelAdjustment.Apply(source, new() { Channel = RgbaChannel.A, Amount = 10 });
        Require(reveal.GetPixel(0, 0).ToArgb() == Color.FromArgb(10, 100, 80, 60).ToArgb(), "Adding alpha did not retain hidden RGB");
        using var saturated = ChannelAdjustment.Apply(source, new() { Channel = RgbaChannel.R, Mode = ChannelAdjustmentMode.Percent, Amount = double.MaxValue });
        Require(saturated.GetPixel(0, 0).R == 255, "Large finite values did not clamp");
        using var stream = new MemoryStream(); reveal.Save(stream, ImageFormat.Png); stream.Position = 0;
        using var reopened = new Bitmap(stream);
        Require(reopened.GetPixel(0, 0).ToArgb() == reveal.GetPixel(0, 0).ToArgb(), "PNG changed adjusted alpha or RGB");
    }

    private static void Validation()
    {
        using var source = new Bitmap(2, 2);
        foreach (double amount in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Expect<ArgumentOutOfRangeException>(() => ChannelAdjustment.Apply(source, new() { Amount = amount }));
        Expect<ArgumentOutOfRangeException>(() => ChannelAdjustment.Apply(source, new() { Channel = (RgbaChannel)4 }));
        Expect<ArgumentOutOfRangeException>(() => ChannelAdjustment.Apply(source, new() { Mode = (ChannelAdjustmentMode)2 }));
        Expect<ArgumentNullException>(() => ChannelAdjustment.Apply(null!, new()));
        Expect<ArgumentNullException>(() => ChannelAdjustment.Apply(source, null!));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Expect<OperationCanceledException>(() => ChannelAdjustment.Apply(source, new(), cancellation.Token));
    }

    private static void Recipes()
    {
        using var source = new Bitmap(1, 1);
        source.SetPixel(0, 0, Color.FromArgb(64, 100, 150, 200));
        var recipe = WorkflowJson.Snapshot(new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.AdjustChannel,
                ChannelAdjustment = new() { Channel = RgbaChannel.A, Mode = ChannelAdjustmentMode.Percent, Amount = 25.5, Subtract = true } }]
        });
        using var expected = ChannelAdjustment.Apply(source, recipe.Steps[0].ChannelAdjustment!);
        using var actual = new WorkflowExecutor().Execute(source, recipe);
        Require(actual.GetPixel(0, 0).ToArgb() == expected.GetPixel(0, 0).ToArgb(), "Recipe lost RGBA settings");
        Expect<WorkflowStepException>(() => WorkflowExecutor.Validate(new() { Steps = [new() { OperationId = ProcessingOperations.AdjustChannel }] }));
        Expect<WorkflowStepException>(() => WorkflowExecutor.Validate(new() { Steps = [new() { OperationId = ProcessingOperations.Monochrome, ChannelAdjustment = new() }] }));
    }

    private static int Get(Color color, RgbaChannel channel)
        => channel switch { RgbaChannel.R => color.R, RgbaChannel.G => color.G, RgbaChannel.B => color.B, _ => color.A };
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
