namespace PixelArtDownscale;

using System.Drawing.Imaging;

public sealed class PixelArtDownscaler
{
    public DownscaleResult Process(Bitmap source, DownscaleOptions options)
        => Process(source, options, progress: null);

    public DownscaleResult Process(Bitmap source, DownscaleOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ProcessWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private DownscaleResult ProcessWithProgress(Bitmap source, DownscaleOptions options, ProcessingProgress tracking)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ColorQuantizer.ValidateColorCount(options.QuantizationColors);

        if (options.TargetWidth <= 0 || options.TargetHeight <= 0)
            throw new ArgumentException("Target dimensions must be greater than zero.");

        if (options.BlockMode == BlockSelectionMode.Manual && options.ManualCriteria is null)
            throw new ArgumentException("ManualCriteria is required when BlockMode is Manual.");

        if (options.SpriteMode)
            return SpriteDownscaler.Process(source, options, tracking);

        var timings = new Dictionary<string, double>();

        Bitmap? convertedSource = null;
        if (source.PixelFormat is not (PixelFormat.Format24bppRgb or PixelFormat.Format32bppArgb))
        {
            convertedSource = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
            source = convertedSource;
        }

        try
        {
            var cropStart = DateTime.UtcNow;
            var cropped = IntegerCrop(source, options);
            timings["crop"] = (DateTime.UtcNow - cropStart).TotalSeconds;

            tracking.Report(1.0 / 5);
            var quantStart = DateTime.UtcNow;
            var quantized = ColorQuantizer.Quantize(
                cropped, options.QuantizationColors, options.Quantization, options.ThreadCount, options.UseColorWeights, tracking.Slice(1, 5));
            timings[$"quantize ({options.Quantization})"] = (DateTime.UtcNow - quantStart).TotalSeconds;

            var blockStart = DateTime.UtcNow;
            var downscaled = ProcessBlocks(quantized, options, tracking.Slice(2, 5));
            timings["blocks"] = (DateTime.UtcNow - blockStart).TotalSeconds;

            Bitmap finalBitmap;
            if (options.EnableDithering && options.Palette is not PaletteKind.None and not PaletteKind.Step)
            {
                var ditherStart = DateTime.UtcNow;
                finalBitmap = ApplyPaletteDithering(downscaled, options.Palette, tracking.Slice(3, 5));
                timings["dither"] = (DateTime.UtcNow - ditherStart).TotalSeconds;
            }
            else
            {
                var snapStart = DateTime.UtcNow;
                finalBitmap = ApplyPaletteSnap(downscaled, options, tracking.Slice(3, 5));
                timings["paletteSnap"] = (DateTime.UtcNow - snapStart).TotalSeconds;
            }

            quantized.Dispose();
            downscaled.Dispose();

            var dominant = ComputeDominantColor(finalBitmap, options.ThreadCount, tracking.Slice(4, 5));

            return new DownscaleResult
            {
                CroppedSource = cropped,
                Downscaled = finalBitmap,
                DominantColor = dominant,
                StageTimingsSeconds = timings
            };
        }
        finally
        {
            convertedSource?.Dispose();
        }
    }

    private static Bitmap IntegerCrop(Bitmap source, DownscaleOptions options)
    {
        int blockWidth = source.Width / options.TargetWidth;
        int blockHeight = source.Height / options.TargetHeight;

        if (blockWidth < 1 || blockHeight < 1)
            throw new InvalidOperationException("Target size is too large for the source image.");

        int cropWidth = blockWidth * options.TargetWidth;
        int cropHeight = blockHeight * options.TargetHeight;
        return BitmapFactory.Crop(
            source, cropWidth, cropHeight, options.CropHorizontal, options.CropVertical);
    }

    private static Bitmap ProcessBlocks(Bitmap source, DownscaleOptions options)
        => ProcessBlocks(source, options, progress: null);

    private static Bitmap ProcessBlocks(Bitmap source, DownscaleOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ProcessBlocksWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap ProcessBlocksWithProgress(Bitmap source, DownscaleOptions options, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        int blockWidth = source.Width / options.TargetWidth;
        int blockHeight = source.Height / options.TargetHeight;

        using var buffer = BitmapBuffer.FromBitmap(source);
        float[] edgeMap = ColorWeights.BuildEdgeMap(buffer, options.ThreadCount);

        var result = BitmapFactory.CreateLike(source, options.TargetWidth, options.TargetHeight);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(options.TargetHeight, options.ThreadCount, (startOy, endOy) =>
        {
            for (int oy = startOy; oy < endOy; oy++)
            {
                for (int ox = 0; ox < options.TargetWidth; ox++)
                {
                    var blockPixels = new List<BlockPixel>();
                    int startX = ox * blockWidth;
                    int startY = oy * blockHeight;

                    for (int y = startY; y < startY + blockHeight; y++)
                    {
                        for (int x = startX; x < startX + blockWidth; x++)
                        {
                            int packed = buffer.GetPackedColor(x, y);
                            byte alpha = buffer.GetAlpha(x, y);
                            if (alpha == 0) continue;
                            float edge = edgeMap[y * buffer.Width + x];
                            blockPixels.Add(new BlockPixel((alpha << 24) | packed, edge));
                        }
                    }

                    int representative = blockPixels.Count * 2 < blockWidth * blockHeight ? 0
                        : BlockAnalyzer.SelectRepresentative(blockPixels, options);
                    writeBuffer.SetPackedColor(ox, oy, representative);
                    writeBuffer.SetAlpha(ox, oy, (byte)((uint)representative >> 24));
                }

                tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)options.TargetHeight) / 1.0);
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyPaletteSnap(Bitmap source, DownscaleOptions options)
        => ApplyPaletteSnap(source, options, progress: null);

    private static Bitmap ApplyPaletteSnap(Bitmap source, DownscaleOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ApplyPaletteSnapWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap ApplyPaletteSnapWithProgress(Bitmap source, DownscaleOptions options, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        if (options.Palette == PaletteKind.None)
            return (Bitmap)source.Clone();

        if (options.Palette == PaletteKind.Step)
            return ApplyStepRounding(source, options, tracking);

        var palette = Palettes.GetPalette(options.Palette);
        var result = BitmapFactory.CreateLike(source, source.Width, source.Height);

        using var readBuffer = BitmapBuffer.FromBitmap(source);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(readBuffer.Height, options.ThreadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < readBuffer.Width; x++)
                {
                    int packed = readBuffer.GetPackedColor(x, y);
                    writeBuffer.SetPackedColor(x, y, ColorSpace.NearestPacked(palette, packed));
                    writeBuffer.SetAlpha(x, y, readBuffer.GetAlpha(x, y));
                }

                tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)readBuffer.Height) / 1.0);
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyStepRounding(Bitmap source, DownscaleOptions options)
        => ApplyStepRounding(source, options, progress: null);

    private static Bitmap ApplyStepRounding(Bitmap source, DownscaleOptions options, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ApplyStepRoundingWithProgress(source, options, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap ApplyStepRoundingWithProgress(Bitmap source, DownscaleOptions options, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        var result = BitmapFactory.CreateLike(source, source.Width, source.Height);

        using var readBuffer = BitmapBuffer.FromBitmap(source);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);

        ParallelImageHelper.ForEachRowChunk(readBuffer.Height, options.ThreadCount, (startY, endY) =>
        {
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < readBuffer.Width; x++)
                {
                    int packed = readBuffer.GetPackedColor(x, y);
                    writeBuffer.SetPackedColor(x, y, ColorSpace.RoundToStep(packed, options.PaletteStep));
                    writeBuffer.SetAlpha(x, y, readBuffer.GetAlpha(x, y));
                }

                tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)readBuffer.Height) / 1.0);
            }
        });

        writeBuffer.Flush();
        return result;
    }

    private static Bitmap ApplyPaletteDithering(Bitmap source, PaletteKind paletteKind)
        => ApplyPaletteDithering(source, paletteKind, progress: null);

    private static Bitmap ApplyPaletteDithering(Bitmap source, PaletteKind paletteKind, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ApplyPaletteDitheringWithProgress(source, paletteKind, tracking);
        tracking.Report(1);
        return result;
    }

    private static Bitmap ApplyPaletteDitheringWithProgress(Bitmap source, PaletteKind paletteKind, ProcessingProgress tracking)
    {
        var palette = Palettes.GetPalette(paletteKind);
        using var readBuffer = BitmapBuffer.FromBitmap(source);
        var input = new int[checked(source.Width * source.Height)];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                input[y * source.Width + x] = (readBuffer.GetAlpha(x, y) << 24) | readBuffer.GetPackedColor(x, y);
        int[] output = PaletteDithering.Apply(input, source.Width, source.Height, palette, tracking);
        var result = BitmapFactory.CreateLike(source, source.Width, source.Height);
        using var writeBuffer = BitmapBuffer.FromWritableBitmap(result);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int argb = output[y * source.Width + x];
                writeBuffer.SetPackedColor(x, y, argb & 0xFFFFFF);
                writeBuffer.SetAlpha(x, y, (byte)((uint)argb >> 24));
            }
        return result;
    }

    private static Color ComputeDominantColor(Bitmap bitmap, int threadCount)
        => ComputeDominantColor(bitmap, threadCount, progress: null);

    private static Color ComputeDominantColor(Bitmap bitmap, int threadCount, IProgress<double>? progress = null)
    {
        var tracking = new ProcessingProgress(progress);
        var result = ComputeDominantColorWithProgress(bitmap, threadCount, tracking);
        tracking.Report(1);
        return result;
    }

    private static Color ComputeDominantColorWithProgress(Bitmap bitmap, int threadCount, ProcessingProgress tracking)
    {
        int progressRows0 = 0;
        using var buffer = BitmapBuffer.FromBitmap(bitmap);
        var frequency = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

        ParallelImageHelper.ForEachRowChunk(buffer.Height, threadCount, (startY, endY) =>
        {
            var local = new Dictionary<int, int>();
            for (int y = startY; y < endY; y++)
            {
                for (int x = 0; x < buffer.Width; x++)
                {
                    byte alpha = buffer.GetAlpha(x, y);
                    if (alpha == 0) continue;
                    int argb = (alpha << 24) | buffer.GetPackedColor(x, y);
                    local[argb] = local.GetValueOrDefault(argb) + 1;
                }

                tracking.Report((0 + Interlocked.Increment(ref progressRows0) / (double)buffer.Height) / 1.0);
            }

            foreach (var (packed, count) in local)
                frequency.AddOrUpdate(packed, count, (_, existing) => existing + count);
        });

        int dominantArgb = frequency.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault();
        return Color.FromArgb(dominantArgb);
    }
}
