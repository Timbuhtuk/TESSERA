using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tessera.Core.Workflows;

public sealed record VideoMacroProgress(int Frame);
public sealed record VideoMacroResult(int Frames, int Width, int Height, string FrameRate);

public sealed class VideoMacroProcessor
{
    public VideoMacroResult Apply(string input, string output, ProcessingRecipe recipe, bool overwrite = false,
        string ffmpeg = "ffmpeg", string ffprobe = "ffprobe", CancellationToken cancellationToken = default,
        IProgress<VideoMacroProgress>? progress = null)
    {
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Исходное и выходное видео должны иметь разные пути.");
        string extension = Path.GetExtension(output).ToLowerInvariant();
        if (extension is not (".mp4" or ".mkv"))
            throw new ArgumentException("Выходное видео должно быть MP4 или MKV.");
        if (!File.Exists(input)) throw new FileNotFoundException("Исходное видео не найдено.", input);
        if (Directory.Exists(output)) throw new IOException("Выходной путь указывает на папку.");
        if (File.Exists(output) && !overwrite) throw new IOException("Выходное видео уже существует. Укажите --overwrite.");
        WorkflowExecutor.Validate(recipe);

        var info = Probe(input, ffprobe, cancellationToken);
        int frameBytes = checked(info.Width * info.Height * 4);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, $".tessera-video-{Guid.NewGuid():N}{extension}");
        Process? decoder = null, encoder = null;
        try
        {
            decoder = Start(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin",
                "-i", input, "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough",
                "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"], read: true);
            Task<string> decoderError = decoder.StandardError.ReadToEndAsync(cancellationToken);
            byte[] sourceBytes = new byte[frameBytes];
            int frames = 0, width = 0, height = 0;
            Task<string>? encoderError = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = ReadFrame(decoder.StandardOutput.BaseStream, sourceBytes, cancellationToken);
                if (read == 0) break;
                if (read != frameBytes) throw new InvalidDataException($"Кадр {frames + 1} прочитан не полностью.");
                using var source = FromBgra(sourceBytes, info.Width, info.Height);
                using var result = new WorkflowExecutor().Execute(source, recipe, cancellationToken);
                if (encoder is null)
                {
                    width = result.Width; height = result.Height;
                    var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "rawvideo",
                        "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", info.FrameRate, "-i", "pipe:0",
                        "-i", input, "-map", "0:v:0", "-map", "1:a?" };
                    if (extension == ".mp4")
                        arguments.AddRange(["-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-c:a", "aac"]);
                    else arguments.AddRange(["-c:v", "ffv1", "-c:a", "copy"]);
                    arguments.AddRange(["-y", temporary]);
                    encoder = Start(ffmpeg, arguments, write: true);
                    encoderError = encoder.StandardError.ReadToEndAsync(cancellationToken);
                }
                if (result.Width != width || result.Height != height)
                    throw new InvalidDataException($"Макрос изменил размер кадра {frames + 1}: ожидалось {width}x{height}, получено {result.Width}x{result.Height}.");
                byte[] outputBytes = ToBgra(result);
                encoder.StandardInput.BaseStream.WriteAsync(outputBytes, cancellationToken).GetAwaiter().GetResult();
                frames++;
                progress?.Report(new VideoMacroProgress(frames));
            }
            decoder.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
            if (decoder.ExitCode != 0) throw new IOException($"FFmpeg не декодировал видео: {decoderError.GetAwaiter().GetResult()}");
            if (frames == 0) throw new InvalidDataException("В исходном видео нет кадров.");
            encoder!.StandardInput.Close();
            encoder.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
            if (encoder.ExitCode != 0) throw new IOException($"FFmpeg не записал видео: {encoderError!.GetAwaiter().GetResult()}");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite);
            if (extension == ".mp4")
            {
                width += width % 2;
                height += height % 2;
            }
            return new VideoMacroResult(frames, width, height, info.FrameRate);
        }
        finally
        {
            Kill(encoder); Kill(decoder);
            encoder?.Dispose(); decoder?.Dispose();
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static (int Width, int Height, string FrameRate) Probe(string input, string ffprobe, CancellationToken token)
    {
        using var process = Start(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=width,height,avg_frame_rate:stream_side_data=rotation:stream_tags=rotate", "-of", "json", input], read: true);
        string json;
        try
        {
            Task<string> error = process.StandardError.ReadToEndAsync(token);
            json = process.StandardOutput.ReadToEndAsync(token).GetAwaiter().GetResult();
            process.WaitForExitAsync(token).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new IOException($"FFprobe не прочитал видео: {error.GetAwaiter().GetResult()}");
        }
        catch { Kill(process); throw; }
        using var document = JsonDocument.Parse(json);
        JsonElement streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidDataException("В файле нет видеопотока.");
        JsonElement stream = streams[0];
        int width = stream.GetProperty("width").GetInt32(), height = stream.GetProperty("height").GetInt32();
        int rotation = 0;
        if (stream.TryGetProperty("side_data_list", out JsonElement sideData))
            foreach (JsonElement side in sideData.EnumerateArray())
                if (side.TryGetProperty("rotation", out JsonElement value)) rotation = value.GetInt32();
        if (rotation == 0 && stream.TryGetProperty("tags", out JsonElement tags) &&
            tags.TryGetProperty("rotate", out JsonElement tag))
            int.TryParse(tag.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rotation);
        if (Math.Abs(rotation) % 180 == 90) (width, height) = (height, width);
        string rate = stream.GetProperty("avg_frame_rate").GetString() ?? "";
        string[] parts = rate.Split('/');
        if (width <= 0 || height <= 0 || parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) ||
            numerator <= 0 || denominator <= 0)
            throw new InvalidDataException("Не удалось определить размер или частоту кадров видео.");
        return (width, height, rate);
    }

    private static Process Start(string executable, IEnumerable<string> arguments, bool read = false, bool write = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = read, RedirectStandardInput = write };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        try { return Process.Start(start) ?? throw new IOException($"Не удалось запустить {executable}."); }
        catch (System.ComponentModel.Win32Exception ex)
        { throw new IOException($"Не найден {executable}. Установите FFmpeg с ffprobe или укажите пути к ним.", ex); }
    }

    private static int ReadFrame(Stream stream, byte[] bytes, CancellationToken token)
    {
        int read = 0;
        while (read < bytes.Length)
        {
            int count = stream.ReadAsync(bytes.AsMemory(read), token).GetAwaiter().GetResult();
            if (count == 0) break;
            read += count;
        }
        return read;
    }

    private static Bitmap FromBgra(byte[] bytes, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(bytes, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static byte[] ToBgra(Bitmap bitmap)
    {
        using var copy = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        byte[] bytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        BitmapData data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < copy.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * copy.Width * 4, copy.Width * 4);
        }
        finally { copy.UnlockBits(data); }
        return bytes;
    }

    private static void Kill(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
