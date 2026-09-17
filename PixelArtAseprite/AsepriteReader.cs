using System.Security.Cryptography;

namespace PixelArtAseprite;

public static class AsepriteReader
{
    public static AsepriteDocument Read(string path, AsepriteLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        limits ??= new AsepriteLimits();
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 128 || stream.Length > limits.MaxInputBytes)
            throw new InvalidDataException($"{Path.GetFileName(path)}: размер файла вне допустимых пределов.");
        limits.CheckMemory(stream.Length * 2);
        var data = new byte[(int)stream.Length];
        for (int q = 0; q < data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = stream.Read(data, q, Math.Min(65536, data.Length - q));
            if (count == 0) throw new InvalidDataException("Входной файл оборван.");
            q += count;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Размер входного файла изменился во время чтения.");
        return Read(data, Path.GetFileName(path), limits, cancellationToken);
    }

    public static AsepriteDocument Read(ReadOnlyMemory<byte> data, string sourceName = "sprite.aseprite",
        AsepriteLimits? limits = null, CancellationToken cancellationToken = default)
    {
        limits ??= new AsepriteLimits();
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        string name = Path.GetFileName(sourceName);
        try { return Parse(data.Span, name, limits, cancellationToken); }
        catch (InvalidDataException ex) { throw new InvalidDataException($"{name}: {ex.Message}", ex); }
        catch (OverflowException ex) { throw new InvalidDataException($"{name}: переполнение размеров.", ex); }
    }

    private static AsepriteDocument Parse(ReadOnlySpan<byte> data, string source, AsepriteLimits limits, CancellationToken token)
    {
        if (data.Length < 128 || data.Length > limits.MaxInputBytes) throw new InvalidDataException("Размер файла вне допустимых пределов.");
        limits.CheckMemory(data.Length * 2L);
        var reader = new LeReader(data);
        var header = new LeReader(reader.Take(128));
        if (header.U32() != data.Length || header.U16() != 0xA5E0) throw new InvalidDataException("Неверный размер или сигнатура Aseprite.");
        int count = header.U16(), width = header.U16(), height = header.U16(), depth = header.U16();
        uint flags = header.U32();
        int speed = header.U16();
        header.Skip(14);
        int pixelWidth = header.U8(), pixelHeight = header.U8();
        if (count == 0 || width == 0 || height == 0) throw new InvalidDataException("Пустой размер холста или число кадров.");
        if (depth != 32) throw new InvalidDataException($"Глубина {depth} не поддерживается: требуется RGBA32.");
        if ((flags & ~7u) != 0) throw new InvalidDataException("Неизвестные флаги заголовка.");
        if (pixelWidth != 0 && pixelHeight != 0 && pixelWidth != pixelHeight) throw new InvalidDataException("Неквадратные пиксели не поддерживаются.");
        if (checked((long)width * height * count) > limits.MaxFramePixels || (long)width * height * 4 > int.MaxValue)
            throw new InvalidDataException("Превышен лимит пикселей полных кадров.");
        var document = new AsepriteDocument
        {
            Width = width, Height = height, Source = source,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            Limits = limits, WorkingBytes = data.Length * 2L + count * 256L
        };
        limits.CheckMemory(document.WorkingBytes);
        var frames = new List<AsepriteFrame>(count);
        var tags = new List<AsepriteTag>();
        var warnings = new HashSet<string>();
        bool layerSeen = false, profileSeen = false, tagsSeen = false;
        long celBytes = 0;
        for (int q = 0; q < count; q++)
        {
            token.ThrowIfCancellationRequested();
            uint frameBytes = reader.U32();
            if (frameBytes < 16 || frameBytes - 4 > reader.Remaining) throw new InvalidDataException($"Кадр {q + 1}: неверная длина.");
            var frame = new LeReader(reader.Take((int)frameBytes - 4));
            if (frame.U16() != 0xF1FA) throw new InvalidDataException($"Кадр {q + 1}: неверная сигнатура.");
            int oldCount = frame.U16(), duration = frame.U16();
            frame.Skip(2);
            uint newCount = frame.U32();
            uint chunkCount = newCount != 0 ? newCount : (uint)oldCount;
            if (chunkCount > frame.Remaining / 6) throw new InvalidDataException($"Кадр {q + 1}: неверное число чанков.");
            if (duration == 0) duration = speed;
            if (duration == 0) throw new InvalidDataException($"Кадр {q + 1}: длительность не задана.");
            AsepriteCel? cel = null;
            for (int e = 0; e < chunkCount; e++)
            {
                token.ThrowIfCancellationRequested();
                uint length = frame.U32();
                ushort type = frame.U16();
                if (length < 6 || length - 6 > frame.Remaining) throw new InvalidDataException($"Кадр {q + 1}: чанк 0x{type:X4} выходит за границу.");
                var chunk = new LeReader(frame.Take((int)length - 6));
                try
                {
                    switch (type)
                    {
                        case 0x2004:
                            if (q != 0 || layerSeen) throw new InvalidDataException("Поддерживается ровно один слой в первом кадре.");
                            limits.CheckMemory(document.WorkingBytes + chunk.Remaining * 2L + 128);
                            document.LayerName = ReadLayer(ref chunk, flags);
                            document.WorkingBytes += document.LayerName.Length * 2L + 128;
                            layerSeen = true;
                            break;
                        case 0x2005:
                            if (cel is not null) throw new InvalidDataException("Несколько cel в одном кадре не поддерживаются.");
                            cel = ReadCel(ref chunk, document, ref celBytes, token);
                            break;
                        case 0x2007:
                            if (profileSeen || q != 0) throw new InvalidDataException("Повторный или меняющийся цветовой профиль не поддерживается.");
                            int profile = chunk.U16(), profileFlags = chunk.U16();
                            chunk.Skip(12);
                            if (profile is not (0 or 1) || profileFlags != 0) throw new InvalidDataException("ICC, особая гамма и неизвестные профили не поддерживаются.");
                            document.IsSrgb = profile == 1;
                            profileSeen = true;
                            break;
                        case 0x2018:
                            if (tagsSeen) throw new InvalidDataException("Повторный чанк тегов не поддерживается.");
                            ReadTags(ref chunk, tags, count, document);
                            tagsSeen = true;
                            break;
                        case 0x0004:
                        case 0x0011:
                        case 0x2019:
                            warnings.Add("Палитра не перенесена: RGBA-пиксели уже содержат цвета.");
                            chunk.Skip(chunk.Remaining);
                            break;
                        case 0x2020:
                            warnings.Add("User Data не перенесены в экспорт.");
                            chunk.Skip(chunk.Remaining);
                            break;
                        default: throw new InvalidDataException($"Чанк 0x{type:X4} не поддерживается.");
                    }
                    chunk.RequireEnd();
                }
                catch (InvalidDataException ex) { throw new InvalidDataException($"Кадр {q + 1}, чанк 0x{type:X4}: {ex.Message}", ex); }
            }
            frame.RequireEnd();
            if (!layerSeen) throw new InvalidDataException("Первый кадр не содержит обычного видимого слоя.");
            frames.Add(new AsepriteFrame(q, duration, cel));
        }
        reader.RequireEnd();
        document.Frames = frames.AsReadOnly();
        document.Tags = tags.AsReadOnly();
        document.Warnings = Array.AsReadOnly(warnings.Order().ToArray());
        ResolveLinks(document, token);
        return document;
    }

    private static string ReadLayer(ref LeReader reader, uint headerFlags)
    {
        int flags = reader.U16(), type = reader.U16(), level = reader.U16();
        reader.Skip(4);
        int blend = reader.U16(), opacity = reader.U8();
        reader.Skip(3);
        string name = reader.ReadString();
        if ((flags & ~127) != 0 || (flags & 1) == 0 || (flags & (8 | 64)) != 0 || type != 0 || level != 0)
            throw new InvalidDataException("Нужен видимый обычный слой без вложенности, background/reference/tilemap.");
        if (blend != 0 || ((headerFlags & 1) != 0 && opacity != 255))
            throw new InvalidDataException($"Режим смешивания {blend} / opacity слоя {opacity} не поддерживается.");
        if ((headerFlags & 4) != 0) reader.Skip(16);
        return name;
    }

    private static AsepriteCel ReadCel(ref LeReader reader, AsepriteDocument document, ref long celBytes, CancellationToken token)
    {
        int layer = reader.U16();
        short x = reader.I16(), y = reader.I16();
        int opacity = reader.U8(), type = reader.U16();
        short z = reader.I16();
        reader.Skip(5);
        if (layer != 0 || opacity != 255 || z != 0)
            throw new InvalidDataException($"Cel слоя {layer}: opacity {opacity}, z-index {z}; требуется слой 0, opacity 255, z-index 0.");
        if (type == 1) return new AsepriteCel { X = x, Y = y, Type = type, LinkedFrame = reader.U16() };
        if (type is not (0 or 2)) throw new InvalidDataException($"Cel типа {type} не поддерживается (включая tilemap).");
        int width = reader.U16(), height = reader.U16();
        long bytes = checked((long)width * height * 4);
        if (width == 0 || height == 0 || bytes > int.MaxValue || bytes > document.Limits.MaxCelBytes - celBytes)
            throw new InvalidDataException("Размер cel превышает лимит или равен нулю.");
        document.Limits.CheckMemory(document.WorkingBytes + bytes);
        byte[] pixels = type == 0 ? reader.Take((int)bytes).ToArray() : StrictZlib.Inflate(reader.Take(reader.Remaining), (int)bytes, token);
        celBytes += bytes;
        document.WorkingBytes += bytes;
        return new AsepriteCel { X = x, Y = y, Type = type, Image = new RgbaImage(width, height, pixels) };
    }

    private static void ReadTags(ref LeReader reader, List<AsepriteTag> tags, int frameCount, AsepriteDocument document)
    {
        int count = reader.U16();
        reader.Skip(8);
        if (count > reader.Remaining / 19) throw new InvalidDataException("Неверное число тегов.");
        long reserve = count * 128L + reader.Remaining * 2L;
        document.Limits.CheckMemory(document.WorkingBytes + reserve);
        document.WorkingBytes += reserve;
        string[] directions = ["forward", "reverse", "pingpong", "pingpong_reverse"];
        for (int q = 0; q < count; q++)
        {
            int from = reader.U16(), to = reader.U16(), direction = reader.U8(), repeat = reader.U16();
            reader.Skip(10);
            string name = reader.ReadString();
            if (from > to || to >= frameCount || direction >= directions.Length) throw new InvalidDataException("Неверный диапазон или направление тега.");
            tags.Add(new AsepriteTag(name, from, to, directions[direction], repeat));
        }
    }

    private static void ResolveLinks(AsepriteDocument document, CancellationToken token)
    {
        var visiting = new bool[document.Frames.Count];
        var path = new List<int>();
        for (int q = 0; q < document.Frames.Count; q++)
        {
            token.ThrowIfCancellationRequested();
            if (document.Frames[q].Cel is not { Image: null }) continue;
            path.Clear();
            int current = q;
            RgbaImage? image;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (current < 0 || current >= document.Frames.Count || document.Frames[current].Cel is not { } cel)
                    throw new InvalidDataException($"Кадр {q + 1}: ссылка на отсутствующий cel кадра {current + 1}.");
                image = cel.Image;
                if (image is not null) break;
                if (visiting[current]) throw new InvalidDataException($"Кадр {q + 1}: цикл linked cels.");
                visiting[current] = true;
                path.Add(current);
                current = cel.LinkedFrame ?? throw new InvalidDataException("Cel не содержит изображения или ссылки.");
            }
            foreach (int index in path)
            {
                document.Frames[index].Cel!.Image = image;
                visiting[index] = false;
            }
        }
    }
}
