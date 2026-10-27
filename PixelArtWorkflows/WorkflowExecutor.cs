using System.Drawing.Imaging;
using PixelArtAlignment;
using PixelArtDownscale;

namespace PixelArtWorkflows;

public sealed record WorkflowProgress(int Index, int Count, string OperationId);

public sealed class WorkflowStepException : Exception
{
    public int StepIndex { get; }
    public string OperationId { get; }

    public WorkflowStepException(int stepIndex, string operationId, Exception innerException)
        : base($"Step {stepIndex} ({operationId}) failed: {innerException.Message}", innerException)
    {
        StepIndex = stepIndex;
        OperationId = operationId;
    }
}

public sealed class WorkflowCanceledException : OperationCanceledException
{
    public int StepIndex { get; }
    public string OperationId { get; }

    public WorkflowCanceledException(int stepIndex, string operationId, OperationCanceledException innerException)
        : base($"Step {stepIndex} ({operationId}) was cancelled.", innerException, innerException.CancellationToken)
    {
        StepIndex = stepIndex;
        OperationId = operationId;
    }
}

public sealed class WorkflowExecutor
{
    public Bitmap Execute(Bitmap input, ProcessingRecipe recipe, CancellationToken cancellationToken = default,
        IProgress<WorkflowProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(recipe);
        Validate(recipe);

        Bitmap? current = input.Clone(new Rectangle(0, 0, input.Width, input.Height), PixelFormat.Format32bppArgb);
        int? previousAlignmentCell = null;
        try
        {
            for (int q = 0; q < recipe.Steps.Count; q++)
            {
                ProcessingStep step = recipe.Steps[q];
                Bitmap? next = null;
                int? nextAlignmentCell = previousAlignmentCell;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (next, nextAlignmentCell) = ExecuteStep(current, step, previousAlignmentCell, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException ex)
                {
                    next?.Dispose();
                    throw new WorkflowCanceledException(q + 1, step.OperationId, ex);
                }
                catch (Exception ex)
                {
                    next?.Dispose();
                    throw new WorkflowStepException(q + 1, step.OperationId, ex);
                }

                current.Dispose();
                current = next;
                previousAlignmentCell = nextAlignmentCell;
                progress?.Report(new WorkflowProgress(q + 1, recipe.Steps.Count, step.OperationId));
            }

            Bitmap result = current;
            current = null;
            return result;
        }
        finally { current?.Dispose(); }
    }

    public static void Validate(ProcessingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.Version != 1)
            throw new InvalidDataException($"Unsupported recipe version: {recipe.Version}.");
        if (recipe.Steps is null)
            throw new InvalidDataException("A recipe needs a steps collection.");
        if (recipe.Steps.Count == 0)
            throw new InvalidDataException("A recipe needs at least one step.");

        bool hasAlignment = false;
        for (int q = 0; q < recipe.Steps.Count; q++)
        {
            ProcessingStep? step = recipe.Steps[q];
            string operationId = step?.OperationId ?? "";
            try
            {
                if (step is null) throw new InvalidDataException("Step is null.");
                ValidateStep(step, hasAlignment);
                if (step.OperationId is ProcessingOperations.Align or ProcessingOperations.AlignLocalSnapped) hasAlignment = true;
                if (step.OperationId == ProcessingOperations.AlignLocal) hasAlignment = false;
            }
            catch (WorkflowStepException) { throw; }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                throw new WorkflowStepException(q + 1, operationId, ex);
            }
        }
    }

    private static (Bitmap Bitmap, int? PreviousAlignmentCell) ExecuteStep(Bitmap current, ProcessingStep step,
        int? previousAlignmentCell, CancellationToken cancellationToken)
    {
        switch (step.OperationId)
        {
            case ProcessingOperations.Profile:
            {
                DownscaleOptions options = LocalOptions(step.Downscale!);
                var result = new PixelArtDownscaler().Process(current, options);
                result.CroppedSource.Dispose();
                return (result.Downscaled, previousAlignmentCell);
            }
            case ProcessingOperations.Resize:
                return (IndependentImageProcessor.Scale(current, LocalOptions(step.Downscale!)), previousAlignmentCell);
            case ProcessingOperations.Trim:
                return (BitmapFactory.CropTransparentEdges(current), previousAlignmentCell);
            case ProcessingOperations.Colors:
                return (IndependentImageProcessor.ApplyColors(current, LocalOptions(step.Downscale!)), previousAlignmentCell);
            case ProcessingOperations.Cleanup:
                return (IndependentImageProcessor.ApplyNeighborColors(current, LocalOptions(step.Downscale!)), previousAlignmentCell);
            case ProcessingOperations.Align:
            case ProcessingOperations.AlignLocal:
            case ProcessingOperations.AlignLocalSnapped:
            {
                var mode = step.OperationId == ProcessingOperations.Align ? GridAlignmentMode.Global : GridAlignmentMode.Local;
                bool snap = step.OperationId == ProcessingOperations.AlignLocalSnapped;
                var alignment = new PixelGridAligner().Align(current, new GridAlignmentOptions { CellSize = step.CellSize, Mode = mode, SnapToGlobalGrid = snap });
                return (alignment.Aligned, mode == GridAlignmentMode.Global || snap ? alignment.CellSize : null);
            }
            case ProcessingOperations.Reduce:
            {
                int cellSize = step.UsePreviousAlignment ? previousAlignmentCell!.Value : step.CellSize!.Value;
                return (PixelGridReducer.Reduce(current, cellSize), previousAlignmentCell);
            }
            case ProcessingOperations.Monochrome:
                return (ImageFilters.Monochrome(current), previousAlignmentCell);
            case ProcessingOperations.Ascii:
            {
                AsciiSettings settings = step.Ascii!;
                return (ImageFilters.RenderAscii(current, settings.KeepSourceSize, settings.Characters,
                    settings.InvertSource, settings.InvertColors), previousAlignmentCell);
            }
            case ProcessingOperations.ReplaceColor:
            {
                ColorReplacementSettings settings = step.ColorReplacement!;
                return (ImageColorTools.ReplaceColor(current, Color.FromArgb(settings.FromArgb),
                    Color.FromArgb(settings.ToArgb)), previousAlignmentCell);
            }
            case ProcessingOperations.RemoveBackground:
            {
                BackgroundSettings settings = step.Background!;
                Color? color = settings.ColorArgb is null ? null : Color.FromArgb(settings.ColorArgb.Value);
                return (BackgroundRemover.Remove(current, settings.Mode, settings.Tolerance, color, cancellationToken),
                    previousAlignmentCell);
            }
            case ProcessingOperations.AdjustChannel:
                return (ChannelAdjustment.Apply(current, step.ChannelAdjustment!, cancellationToken), previousAlignmentCell);
            case ProcessingOperations.ContourHeatmap:
                return (ContourNoiseCleaner.Analyze(current, step.Contours!).Heatmap, previousAlignmentCell);
            case ProcessingOperations.CleanContours:
            {
                ContourCleanResult result = ContourNoiseCleaner.Process(current, step.Contours!);
                result.CandidateMap.Dispose();
                return (result.Image, previousAlignmentCell);
            }
            default:
                throw new InvalidDataException($"Unsupported operation: {step.OperationId}.");
        }
    }

    private static DownscaleOptions LocalOptions(DownscaleOptions options) => WorkflowJson.Snapshot(options);

    private static void ValidateStep(ProcessingStep step, bool hasAlignment)
    {
        if (string.IsNullOrWhiteSpace(step.OperationId)) throw new InvalidDataException("OperationId is required.");
        switch (step.OperationId)
        {
            case ProcessingOperations.Profile:
            case ProcessingOperations.Resize:
            case ProcessingOperations.Colors:
            case ProcessingOperations.Cleanup:
                RequireOnly(step, downscale: true);
                if (step.Downscale is null) throw new InvalidDataException("Downscale parameters are required.");
                ValidateOptions(step.Downscale);
                break;
            case ProcessingOperations.Align:
            case ProcessingOperations.AlignLocal:
            case ProcessingOperations.AlignLocalSnapped:
                RequireOnly(step, cellSize: true);
                ValidateCellSize(step.CellSize, optional: true);
                if (step.OperationId != ProcessingOperations.Align && step.CellSize > 64)
                    throw new InvalidDataException("Local cell size must be 2..64.");
                break;
            case ProcessingOperations.Reduce:
                RequireOnly(step, cellSize: true, previousAlignment: true);
                if (step.UsePreviousAlignment)
                {
                    if (!hasAlignment) throw new InvalidDataException("UsePreviousAlignment requires an earlier grid.align step.");
                }
                else ValidateCellSize(step.CellSize, optional: false);
                break;
            case ProcessingOperations.Trim:
            case ProcessingOperations.Monochrome:
                RequireOnly(step);
                break;
            case ProcessingOperations.Ascii:
                RequireOnly(step, ascii: true);
                if (step.Ascii is null) throw new InvalidDataException("ASCII parameters are required.");
                if (string.IsNullOrEmpty(step.Ascii.Characters) || step.Ascii.Characters.Contains('\r') ||
                    step.Ascii.Characters.Contains('\n'))
                    throw new InvalidDataException("ASCII characters must be a non-empty single line.");
                break;
            case ProcessingOperations.ReplaceColor:
                RequireOnly(step, replacement: true);
                if (step.ColorReplacement is null) throw new InvalidDataException("Color replacement parameters are required.");
                break;
            case ProcessingOperations.AdjustChannel:
                RequireOnly(step, channelAdjustment: true);
                if (step.ChannelAdjustment is null) throw new InvalidDataException("Channel adjustment parameters are required.");
                step.ChannelAdjustment.Validate();
                break;
            case ProcessingOperations.RemoveBackground:
                RequireOnly(step, background: true);
                if (step.Background is null) throw new InvalidDataException("Background parameters are required.");
                if (!Enum.IsDefined(step.Background.Mode)) throw new InvalidDataException("Unsupported background mode.");
                if (step.Background.Tolerance is < 0 or > 100)
                    throw new InvalidDataException("Background tolerance must be 0..100.");
                break;
            case ProcessingOperations.ContourHeatmap:
            case ProcessingOperations.CleanContours:
                RequireOnly(step, contours: true);
                if (step.Contours is null) throw new InvalidDataException("Contour parameters are required.");
                ValidateContours(step.Contours);
                break;
            default:
                throw new InvalidDataException($"Unsupported operation: {step.OperationId}.");
        }
    }

    private static void ValidateOptions(DownscaleOptions options)
    {
        if (options.TargetWidth < 1 || options.TargetHeight < 1)
            throw new InvalidDataException("Target dimensions must be positive.");
        if (options.AlphaThreshold is < 1 or > 100)
            throw new InvalidDataException("Alpha threshold must be 1..100.");
        if (options.PaletteStep is < 1 or > 255)
            throw new InvalidDataException("Palette step must be 1..255.");
        if (options.QuantizationColors is < 1 or > DownscaleOptions.MaxQuantizationColors)
            throw new InvalidDataException($"Quantization colors must be 1..{DownscaleOptions.MaxQuantizationColors}.");
        if (options.LocalColorPasses is < 1 or > DownscaleOptions.MaxLocalColorPasses)
            throw new InvalidDataException($"Cleanup passes must be 1..{DownscaleOptions.MaxLocalColorPasses}.");
        if (!Enum.IsDefined(options.Palette) || !Enum.IsDefined(options.Quantization) ||
            !Enum.IsDefined(options.IndependentColorMode) || !Enum.IsDefined(options.BlockMode) ||
            !Enum.IsDefined(options.CropHorizontal) || !Enum.IsDefined(options.CropVertical))
            throw new InvalidDataException("A downscale option contains an unsupported enum value.");
        if (options.BlockMode == BlockSelectionMode.Manual && options.ManualCriteria is null)
            throw new InvalidDataException("Manual criteria are required for manual block selection.");
        ValidateCriteria(options.ManualCriteria);
        ValidateCriteria(options.LocalColorCriteria);
        if (options.Cleanup is { } cleanup && (!double.IsFinite(cleanup.ColorTolerance) || cleanup.ColorTolerance is < 0 or > 40 ||
            !double.IsFinite(cleanup.Strength) || cleanup.Strength is < 0 or > 1 ||
            !double.IsFinite(cleanup.DetailProtection) || cleanup.DetailProtection is < 0 or > 1 ||
            !double.IsFinite(cleanup.EdgeProtection) || cleanup.EdgeProtection is < 0 or > 1))
            throw new InvalidDataException("Cleanup color tolerance must be 0..40; strength and protection must be 0..1.");
    }

    private static void ValidateCriteria(ManualBlockCriteria? criteria)
    {
        if (criteria is null) return;
        double[] values = [criteria.TargetBrightness, criteria.TargetContrast, criteria.TargetSaturation,
            criteria.TargetEdge, criteria.BrightnessImportance, criteria.ContrastImportance,
            criteria.SaturationImportance, criteria.EdgeImportance];
        if (values.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException("Manual criteria values must be finite and within 0..1.");
    }

    private static void ValidateCellSize(int? cellSize, bool optional)
    {
        if (cellSize is null)
        {
            if (!optional) throw new InvalidDataException("CellSize is required.");
            return;
        }
        if (cellSize is < 2 or > 65535) throw new InvalidDataException("CellSize must be 2..65535.");
    }

    private static void ValidateContours(ContourCleanOptions options)
    {
        if (options.WindowSize is < 3 or > 15 || options.WindowSize % 2 == 0 ||
            !Enum.IsDefined(options.Strictness) ||
            !double.IsFinite(options.SimilarColorDistance) || options.SimilarColorDistance is < 0 or > 100 ||
            !double.IsFinite(options.MinimumPixelGradient) || options.MinimumPixelGradient is < 0 or > 20000 ||
            !double.IsFinite(options.MinimumNeighborGradient) || options.MinimumNeighborGradient is < 0 or > 20000 ||
            options.MinimumSimilarNeighbors is < 1 or > 224 ||
            !double.IsFinite(options.MinimumAgreement) || options.MinimumAgreement is < 0 or > 1 ||
            !double.IsFinite(options.MinimumOpposition) || options.MinimumOpposition is < 0 or > 1)
            throw new InvalidDataException("Invalid contour parameters.");
    }

    private static void RequireOnly(ProcessingStep step, bool downscale = false, bool cellSize = false,
        bool previousAlignment = false, bool ascii = false, bool replacement = false, bool background = false,
        bool channelAdjustment = false, bool contours = false)
    {
        if (!downscale && step.Downscale is not null || !cellSize && step.CellSize is not null ||
            !previousAlignment && step.UsePreviousAlignment || !ascii && step.Ascii is not null ||
            !replacement && step.ColorReplacement is not null || !background && step.Background is not null ||
            !channelAdjustment && step.ChannelAdjustment is not null || !contours && step.Contours is not null)
            throw new InvalidDataException($"Operation {step.OperationId} contains unsupported parameters.");
    }
}
