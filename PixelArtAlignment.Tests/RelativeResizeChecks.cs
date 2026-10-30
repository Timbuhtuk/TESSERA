using PixelArtDownscale;
using PixelArtWorkflows;

namespace PixelArtAlignment.Tests;

internal static class RelativeResizeChecks
{
    public static void Run()
    {
        var executor = new WorkflowExecutor();
        foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity, (double)int.MaxValue * 2 })
            Reject(new ProcessingStep { OperationId = ProcessingOperations.Resize, ResizeFactor = new() { Factor = value } });
        Reject(new ProcessingStep { OperationId = ProcessingOperations.Resize });
        Reject(new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new(), ResizeFactor = new() { Factor = 2 } });
        Reject(new ProcessingStep { OperationId = ProcessingOperations.Monochrome, ResizeFactor = new() { Factor = 2 } });
        Reject(new ProcessingStep { OperationId = ProcessingOperations.Resize, ResizeFactor = new() { Factor = 2, AlphaThreshold = 101 } });
        using var source = new Bitmap(10, 7);
        var legacy = new ProcessingRecipe { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Resize, Downscale = new() { TargetWidth = 3, TargetHeight = 4 } }] };
        using var fixedOutput = executor.Execute(source, WorkflowJson.Snapshot(legacy));
        if (fixedOutput.Width != 3 || fixedOutput.Height != 4) throw new Exception("Legacy pixel dimensions became relative");
        var relative = new ProcessingRecipe { Steps = [new ProcessingStep { OperationId = ProcessingOperations.Resize, ResizeFactor = new() { Factor = .5 } }] };
        using var half = executor.Execute(source, WorkflowJson.Snapshot(relative));
        if (half.Width != 5 || half.Height != 4) throw new Exception("Relative factor did not round away from zero");
        void Reject(ProcessingStep step)
        {
            try { WorkflowExecutor.Validate(new ProcessingRecipe { Steps = [step] }); }
            catch (WorkflowStepException) { return; }
            throw new Exception("Invalid relative resize parameters were accepted");
        }
    }
}
