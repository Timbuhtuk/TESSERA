using System.Drawing.Imaging;
using PixelArtAlignment;
using PixelArtDownscale;

namespace PixelArtWorkflows;

public sealed record WorkflowProgress(int Index, int Count, string OperationId, int? CellSize = null);

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
    // The step callback runs synchronously while its bitmap is alive. Callers must copy or save it before returning.
    public Bitmap Execute(Bitmap input, ProcessingRecipe recipe, CancellationToken cancellationToken = default,
        IProgress<WorkflowProgress>? progress = null, Action<WorkflowProgress, Bitmap>? stepCompleted = null)
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
                    int? cellSize = step.OperationId switch
                    {
                        ProcessingOperations.Align or ProcessingOperations.AlignLocal or ProcessingOperations.AlignLocalSnapped => nextAlignmentCell,
                        ProcessingOperations.Reduce => step.UsePreviousAlignment ? previousAlignmentCell : step.CellSize,
                        _ => null
                    };
                    stepCompleted?.Invoke(new WorkflowProgress(q + 1, recipe.Steps.Count, step.OperationId, cellSize), next);
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
                return (IndependentImageProcessor.Scale(current, step.ResizeFactor is { } factor
                    ? factor.Resolve(current.Width, current.Height) : LocalOptions(step.Downscale!)), previousAlignmentCell);
            case ProcessingOperations.Trim:
                return (BitmapFactory.CropTransparentEdges(current), previousAlignmentCell);
            case ProcessingOperations.Colors:
                return (PaletteProcessor.Apply(current, LocalOptions(step.Downscale!), step.ConnectedPalette), previousAlignmentCell);
            case ProcessingOperations.Cleanup:
                return (IndependentImageProcessor.ApplyNeighborColors(current, LocalOptions(step.Downscale!)), previousAlignmentCell);
            case ProcessingOperations.Align:
            case ProcessingOperations.AlignLocal:
            case ProcessingOperations.AlignLocalSnapped:
            {
                var mode = step.OperationId == ProcessingOperations.Align ? GridAlignmentMode.Global : GridAlignmentMode.Local;
                bool snap = step.OperationId == ProcessingOperations.AlignLocalSnapped;
                var alignment = new PixelGridAligner().Align(current, new GridAlignmentOptions { CellSize = step.CellSize, Mode = mode, SnapToGlobalGrid = snap });
                return (alignment.Aligned, alignment.CellSize);
            }
            case ProcessingOperations.Reduce:
            {
                int cellSize = step.UsePreviousAlignment ? previousAlignmentCell!.Value : step.CellSize!.Value;
                return (PixelGridReducer.Reduce(current, cellSize), previousAlignmentCell);
            }
            case ProcessingOperations.Monochrome:
                return (ImageFilters.Monochrome(current), previousAlignmentCell);
            case ProcessingOperations.Sharpen:
                return (ImageFilters.Sharpen(current, step.Sharpen!.Strength, step.Sharpen.Threshold, cancellationToken), previousAlignmentCell);
            case ProcessingOperations.Ascii:
            {
                AsciiSettings settings = step.Ascii!;
                return (ImageFilters.RenderAscii(current, settings.KeepSourceSize, settings.Characters,
                    settings.InvertSource, settings.InvertColors, settings.ColorMode,
                    settings.ForegroundArgb, settings.BackgroundArgb, settings.Columns, settings.Dither), previousAlignmentCell);
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
            case ProcessingOperations.ConnectedPalette:
                return (ConnectivityPaletteReducer.Process(current, step.ConnectedPalette!).Image, previousAlignmentCell);
            case ProcessingOperations.CleanContours:
            {
                ContourCleanResult result = ContourNoiseCleaner.Process(current, step.Contours!);
                result.CandidateMap.Dispose();
                return (result.Image, previousAlignmentCell);
            }
            case ProcessingOperations.FinalistReduce:
                return (FinalistReducer.Reduce(current, step.FinalistMode!.Value,
                    step.FinalistFactor ?? FinalistReducer.DefaultFactor), previousAlignmentCell);
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
            case ProcessingOperations.Cleanup:
                RequireOnly(step, downscale: true);
                if (step.Downscale is null) throw new InvalidDataException("Downscale parameters are required.");
                ValidateOptions(step.Downscale);
                break;
            case ProcessingOperations.Resize:
                RequireOnly(step, downscale: true, resizeFactor: true);
                if ((step.Downscale is null) == (step.ResizeFactor is null))
                    throw new InvalidDataException("Resize requires exactly one of pixel dimensions or a scale factor.");
                if (step.Downscale is { } dimensions) ValidateOptions(dimensions);
                else ValidateOptions(step.ResizeFactor!.Resolve(1, 1));
                break;
            case ProcessingOperations.Colors:
                RequireOnly(step, downscale: true, connectedPalette: true);
                if (step.Downscale is null) throw new InvalidDataException("Downscale parameters are required.");
                ValidateOptions(step.Downscale);
                if (step.ConnectedPalette is { } connected)
                {
                    if (connected.TargetColors is < 1 or > 4096 || connected.TileSize != 0 && connected.TileSize is < 4 or > 256 ||
                        !Enum.IsDefined(connected.DistanceSpace) || new[] { connected.ConnectivityWeight, connected.LocalWeight,
                            connected.DetailWeight, connected.ContextWeight }.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
                        throw new InvalidDataException("Connected palette parameters are invalid.");
                }
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
            case ProcessingOperations.Sharpen:
                RequireOnly(step, sharpen: true);
                if (step.Sharpen is null || step.Sharpen.Strength is < 0 or > 200 || step.Sharpen.Threshold is < 0 or > 64)
                    throw new InvalidDataException("Sharpen strength must be 0..200 and threshold 0..64.");
                break;
            case ProcessingOperations.Ascii:
                RequireOnly(step, ascii: true);
                if (step.Ascii is null) throw new InvalidDataException("ASCII parameters are required.");
                if (string.IsNullOrEmpty(step.Ascii.Characters) || step.Ascii.Characters.Contains('\r') ||
                    step.Ascii.Characters.Contains('\n'))
                    throw new InvalidDataException("ASCII characters must be a non-empty single line.");
                if (!Enum.IsDefined(step.Ascii.ColorMode) || step.Ascii.Columns is < 0 or > 512)
                    throw new InvalidDataException("ASCII color mode or column count is invalid.");
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
            case ProcessingOperations.ConnectedPalette:
                RequireOnly(step, connectedPalette: true);
                if (step.ConnectedPalette is not { } palette) throw new InvalidDataException("Connected palette parameters are required.");
                if (palette.TargetColors is < 1 or > 4096 || palette.TileSize != 0 && palette.TileSize is < 4 or > 256 ||
                    !Enum.IsDefined(palette.DistanceSpace) ||
                    !double.IsFinite(palette.ConnectivityWeight) || palette.ConnectivityWeight is < 0 or > 1 ||
                    !double.IsFinite(palette.LocalWeight) || palette.LocalWeight is < 0 or > 1 ||
                    !double.IsFinite(palette.DetailWeight) || palette.DetailWeight is < 0 or > 1 ||
                    !double.IsFinite(palette.ContextWeight) || palette.ContextWeight is < 0 or > 1)
                    throw new InvalidDataException("Invalid connected palette parameters.");
                break;
            case ProcessingOperations.FinalistReduce:
                RequireOnly(step, finalistMode: true, finalistFactor: true);
                if (step.FinalistMode is null || !Enum.IsDefined(step.FinalistMode.Value))
                    throw new InvalidDataException("A finalist mode is required.");
                if (step.FinalistFactor is < FinalistReducer.MinFactor or > FinalistReducer.MaxFactor)
                    throw new InvalidDataException("Finalist factor must be 2..64.");
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
        bool channelAdjustment = false, bool contours = false, bool connectedPalette = false, bool finalistMode = false, bool finalistFactor = false, bool sharpen = false, bool resizeFactor = false)
    {
        if (!downscale && step.Downscale is not null || !cellSize && step.CellSize is not null ||
            !previousAlignment && step.UsePreviousAlignment || !ascii && step.Ascii is not null ||
            !replacement && step.ColorReplacement is not null || !background && step.Background is not null ||
            !channelAdjustment && step.ChannelAdjustment is not null || !contours && step.Contours is not null ||
            !connectedPalette && step.ConnectedPalette is not null || !finalistMode && step.FinalistMode is not null || !finalistFactor && step.FinalistFactor is not null || !sharpen && step.Sharpen is not null || !resizeFactor && step.ResizeFactor is not null)
            throw new InvalidDataException($"Operation {step.OperationId} contains unsupported parameters.");
    }
}
