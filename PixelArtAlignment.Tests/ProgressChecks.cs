using PixelArtDownscale;
using PixelArtWorkflows;
using AF = PixelArtAseprite.Tests.Fixtures;

namespace PixelArtAlignment.Tests;

internal static class ProgressChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Progress reports real intermediate work without changing pixels or alpha", AlgorithmParity);
        check("Progress ranges remain monotonic under parallel updates", ParallelRanges);
        check("Progress handles early cleanup completion, validation and cancellation", CompletionAndErrors);
        check("Macro progress includes work inside each step", MacroProgress);
        check("Aseprite progress covers decoding, frames and PNG publication", AsepriteProgress);
    }

    private static void AlgorithmParity()
    {
        using var source = Image();
        var options = new DownscaleOptions
        {
            TargetWidth = 16,
            TargetHeight = 12,
            QuantizationColors = 16,
            ThreadCount = 4,
            Quantization = QuantizationMethod.KMeansLab,
            IndependentColorMode = IndependentColorMode.Quantization
        };
        Compare(p => IndependentImageProcessor.Scale(source, options, p));
        Compare(p => IndependentImageProcessor.Scale(source, new DownscaleOptions { TargetWidth = 96, TargetHeight = 72 }, p));
        Compare(p => IndependentImageProcessor.ApplyColors(source, options, p));
        Compare(p => IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions { LocalColorPasses = 3 }, p));
        Compare(p => ImageFilters.Monochrome(source, p));
        Compare(p => ImageFilters.Sharpen(source, 80, 3, progress: p));
        Compare(p => ImageFilters.RenderAscii(source, columns: 16, fontSize: 10, progress: p));
        Compare(p => ImageFilters.RenderAscii(source, columns: 16, fontSize: ImageFilters.DefaultAsciiFontSize, progress: p));
        Compare(p => ImageColorTools.ReplaceColor(source, Color.White, Color.Black, p));
        Compare(p => ChannelAdjustment.Apply(source, new ChannelAdjustmentOptions { Channel = RgbaChannel.R, Amount = 5 }, progress: p));
        Compare(p => BackgroundRemover.Remove(source, BackgroundRemovalMode.GlobalColor, 8, Color.White, progress: p));
        Compare(p => BackgroundRemover.Remove(source, BackgroundRemovalMode.EdgeConnected, 8, Color.White, progress: p));
        Compare(p => BitmapFactory.CropTransparentEdges(source, p));
        Compare(p => FinalistReducer.Reduce(source, FinalistMode.T3P44, 2, p));
        using var logicalGrid = Fixtures.Logical(1, 73);
        using var alignedGrid = Fixtures.Expand(logicalGrid, 4);
        Compare(p => PixelGridReducer.Reduce(alignedGrid, 4, p));
        Compare(p => new PixelGridAligner().Align(source, new GridAlignmentOptions { CellSize = 4 }, p).Aligned);
        Compare(p =>
        {
            var result = new PixelArtDownscaler().Process(source, options, p);
            result.CroppedSource.Dispose();
            return result.Downscaled;
        });
        Compare(p => IconExporter.CreateFrame(source, 32, IconResizeMode.NearestNeighbor, progress: p));

        var ascii = new Capture();
        string actual = ImageFilters.ToAscii(source, 32, progress: ascii);
        Require(actual == ImageFilters.ToAscii(source, 32), "ASCII progress changed text output");
        ascii.Verify();
        void Compare(Func<IProgress<double>?, Bitmap> produce)
        {
            using var baseline = produce(null);
            var progress = new Capture();
            using var observed = produce(progress);
            Require(baseline.Size == observed.Size, "Progress changed canvas size");
            for (int y = 0; y < baseline.Height; y++)
                for (int x = 0; x < baseline.Width; x++)
                    Require(baseline.GetPixel(x, y) == observed.GetPixel(x, y), "Progress changed image pixels/alpha");
            progress.Verify();
        }
    }

    private static void ParallelRanges()
    {
        var capture = new Capture();
        var root = new ProcessingProgress(capture);
        for (int phase = 0; phase < 4; phase++)
        {
            var part = root.Slice(phase, 4);
            Parallel.For(0, 1000, q => part.Report((q + 1.0) / 1000));
            part.Report(1);
        }
        capture.Verify();
        int count = capture.Values.Count;
        root.Report(double.NaN); root.Report(double.PositiveInfinity); root.Report(-20);
        Require(capture.Values.Count == count, "Invalid or backwards samples escaped the progress range");
    }

    private static void CompletionAndErrors()
    {
        using var source = new Bitmap(20, 12);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.Red);
        var early = new Capture();
        using var result = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions { LocalColorPasses = 100 }, early);
        early.Verify();
        Require(early.Values.Count < 50, "Unchanged cleanup did not stop early");
        var invalid = new Capture();
        try { using var ignored = IndependentImageProcessor.Scale(source, new DownscaleOptions { TargetWidth = 0 }, invalid); throw new Exception("Invalid size accepted"); }
        catch (ArgumentException) { Require(!invalid.Values.Contains(1), "Validation failure reported completion"); }
        var invalidFont = new Capture();
        try { using var ignored = ImageFilters.RenderAscii(source, fontSize: 0, progress: invalidFont); throw new Exception("Invalid font size accepted"); }
        catch (ArgumentOutOfRangeException) { Require(!invalidFont.Values.Contains(1), "Invalid ASCII font reported completion"); }
        using var cancellation = new CancellationTokenSource();
        var canceled = new Capture(value => { if (value > .1) cancellation.Cancel(); });
        try { using var ignored = BackgroundRemover.Remove(source, BackgroundRemovalMode.GlobalColor, 8, cancellationToken: cancellation.Token, progress: canceled); throw new Exception("Cancellation was ignored"); }
        catch (OperationCanceledException) { Require(!canceled.Values.Contains(1), "Canceled removal reported completion"); }
    }

    private static void MacroProgress()
    {
        using var source = Image();
        var recipe = new ProcessingRecipe
        {
            Steps = [new ProcessingStep { OperationId = ProcessingOperations.Resize,
            Downscale = new DownscaleOptions { TargetWidth = 32, TargetHeight = 24 } }, new ProcessingStep { OperationId = ProcessingOperations.Monochrome }]
        };
        var progress = new Capture();
        using var result = new WorkflowExecutor().Execute(source, recipe, operationProgress: progress);
        progress.Verify();
        Require(progress.Values.Any(v => v > 0 && v < .5) && progress.Values.Any(v => v > .5 && v < 1),
            "Macro reports only completed steps");
    }

    private static void AsepriteProgress()
    {
        string directory = Path.GetFullPath(Path.Combine("artifacts", "progress", "aseprite-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "input.aseprite");
        byte[] pixel = [255, 90, 40, 255];
        File.WriteAllBytes(input, AF.File(1, 1, Enumerable.Range(0, 8).Select(q => new AF.Frame(100,
            q == 0 ? [AF.Layer(), AF.Cel(1, 1, pixel)] : [AF.Cel(1, 1, pixel)])).ToArray()));
        var progress = new Capture();
        var result = PixelArtAseprite.AsepriteConverter.Convert(input, Path.Combine(directory, "output"), progress: progress);
        progress.Verify();
        Require(File.Exists(result.ImagePath) && File.Exists(result.JsonPath), "Aseprite progress finished without published files");
    }

    private static Bitmap Image()
    {
        var image = new Bitmap(64, 48, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++) image.SetPixel(x, y, Color.FromArgb(x % 7 == 0 ? 128 : 255,
                x * 4, y * 5, (x * 5 + y * 3) % 256));
        return image;
    }

    private sealed class Capture(Action<double>? received = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) { Values.Add(value); received?.Invoke(value); }
        public void Verify()
        {
            Require(Values.Count > 2 && Values.Any(v => v > 0 && v < 1), "No intermediate progress was reported");
            Require(Values[^1] == 1, "Successful operation did not report completion");
            for (int q = 0; q < Values.Count; q++)
                Require(double.IsFinite(Values[q]) && Values[q] >= 0 && Values[q] <= 1 && (q == 0 || Values[q] >= Values[q - 1]),
                    "Progress moved backwards or escaped its range");
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
