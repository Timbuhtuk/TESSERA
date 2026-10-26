using System.Drawing;
using System.Drawing.Imaging;
using PixelArtDownscale;

if (args.Length > 0 && args[0] == "generate-corpus")
{
    string corpusRoot = args.Length > 1 ? Path.GetFullPath(args[1]) :
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "corpus"));
    int seedOffset = args.Length > 2 ? int.Parse(args[2]) : 0;
    CorpusGenerator.Generate(corpusRoot, seedOffset);
    return;
}
if (args.Length > 0 && args[0] == "evaluate-corpus")
{
    string corpusRoot = args.Length > 1 ? Path.GetFullPath(args[1]) :
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "corpus"));
    CorpusEvaluator.Evaluate(corpusRoot);
    return;
}
if (args.Length > 0 && args[0] == "compare-images")
{
    if (args.Length != 4) throw new ArgumentException("compare-images BEFORE AFTER OUTPUT");
    using var before = new Bitmap(Path.GetFullPath(args[1]));
    using var after = new Bitmap(Path.GetFullPath(args[2]));
    RequireSameAlpha(before, after);
    SaveSideBySide(before, after, Path.GetFullPath(args[3]));
    Console.WriteLine("Сравнение сохранено.");
    return;
}
if (args.Length > 0 && args[0] == "probe-gradient")
{
    if (args.Length != 4) throw new ArgumentException("probe-gradient IMAGE X Y");
    GradientProbe.Run(Path.GetFullPath(args[1]), int.Parse(args[2]), int.Parse(args[3]));
    return;
}
if (args.Length > 0 && args[0] == "probe-patch")
{
    if (args.Length != 4) throw new ArgumentException("probe-patch IMAGE X Y");
    PatchProbe.Run(Path.GetFullPath(args[1]), int.Parse(args[2]), int.Parse(args[3]));
    return;
}
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
string input = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "fixtures", "T_StrongHand_UNP.png");
string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "artifacts", "review");
Directory.CreateDirectory(output);

using var source = new Bitmap(input);
var sourceTopology = MeasureDarkTopology(source, 15);
Console.WriteLine($"source dark L<15: {sourceTopology}");
using (var enlarged = Enlarge(source, 6)) enlarged.Save(Path.Combine(output, "source-6x.png"), ImageFormat.Png);
using (var white = OnWhite(source))
using (var enlarged = Enlarge(white, 6)) enlarged.Save(Path.Combine(output, "source-on-white-6x.png"), ImageFormat.Png);
using (var fingers = CropEnlarge(source, new Rectangle(18, 15, 110, 80), 8))
    fingers.Save(Path.Combine(output, "source-fingers-8x.png"), ImageFormat.Png);
using (var palm = CropEnlarge(source, new Rectangle(18, 80, 110, 65), 8))
    palm.Save(Path.Combine(output, "source-palm-8x.png"), ImageFormat.Png);

var baselineCandidates = new Dictionary<int, HashSet<int>>();
foreach (int window in new[] { 3, 7, 15 })
{
    var options = new ContourCleanOptions
    {
        WindowSize = window,
        Strictness = ContourCleanStrictness.Strong,
        ProtectDetails = false
    };
    var analysis = ContourNoiseCleaner.Analyze(source, options);
    using (analysis.Heatmap)
    {
        analysis.Heatmap.Save(Path.Combine(output, $"baseline-w{window}-heatmap.png"), ImageFormat.Png);
        using var enlarged = Enlarge(analysis.Heatmap, 6);
        enlarged.Save(Path.Combine(output, $"baseline-w{window}-heatmap-6x.png"), ImageFormat.Png);
    }
    var processed = ContourNoiseCleaner.Process(source, options);
    using (processed.Image)
    using (processed.CandidateMap)
    {
        var indices = new HashSet<int>();
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                if (processed.CandidateMap.GetPixel(x, y).A > 0) indices.Add(y * source.Width + x);
        baselineCandidates.Add(window, indices);
        processed.Image.Save(Path.Combine(output, $"baseline-w{window}-repaired.png"), ImageFormat.Png);
        processed.CandidateMap.Save(Path.Combine(output, $"baseline-w{window}-candidates.png"), ImageFormat.Png);
        using var changed = ChangedOverlay(source, processed.Image);
        using var enlarged = Enlarge(changed, 6);
        enlarged.Save(Path.Combine(output, $"baseline-w{window}-changes-6x.png"), ImageFormat.Png);
    }
    Console.WriteLine($"window={window} candidates={analysis.CandidateCount} changed={processed.ChangedPixels}");
}

foreach (var variant in new[]
{
    (Name: "dark-10-n3", Options: new DarkContourNoiseCleaner.Options(10, 3, 28, 12)),
    (Name: "dark-15-n2", Options: new DarkContourNoiseCleaner.Options(15, 2, 28, 12)),
    (Name: "dark-15-n3", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 12)),
    (Name: "dark-15-n3-g20", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 20)),
    (Name: "dark-15-n3-g30", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 30)),
    (Name: "dark-15-n3-g40", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 40)),
    (Name: "dark-15-n3-g40-a2", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 40, 2)),
    (Name: "dark-15-n3-g40-a3", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 40, 3)),
    (Name: "dark-15-n3-g40-a3-c60", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 40, 3, 0.60)),
    (Name: "dark-15-n3-g40-a3-c75", Options: new DarkContourNoiseCleaner.Options(15, 3, 28, 40, 3, 0.75)),
    (Name: "dark-05-n3-g40-a3-c75", Options: new DarkContourNoiseCleaner.Options(5, 3, 28, 40, 3, 0.75)),
    (Name: "dark-10-n3-g40-a3-c75", Options: new DarkContourNoiseCleaner.Options(10, 3, 28, 40, 3, 0.75)),
    (Name: "dark-20-n3", Options: new DarkContourNoiseCleaner.Options(20, 3, 28, 12)),
    (Name: "dark-15-n4", Options: new DarkContourNoiseCleaner.Options(15, 4, 28, 12))
})
{
    var cleaned = DarkContourNoiseCleaner.Clean(source, variant.Options);
    using (cleaned.Image)
    {
        RequireSameAlpha(source, cleaned.Image);
        var topology = MeasureDarkTopology(cleaned.Image, 15);
        cleaned.Image.Save(Path.Combine(output, $"{variant.Name}-result.png"), ImageFormat.Png);
        using (var enlarged = Enlarge(cleaned.Image, 6))
            enlarged.Save(Path.Combine(output, $"{variant.Name}-result-6x.png"), ImageFormat.Png);
        using (var white = OnWhite(cleaned.Image))
        using (var enlarged = Enlarge(white, 6))
            enlarged.Save(Path.Combine(output, $"{variant.Name}-on-white-6x.png"), ImageFormat.Png);
        using (var fingers = CropEnlarge(cleaned.Image, new Rectangle(18, 15, 110, 80), 8))
            fingers.Save(Path.Combine(output, $"{variant.Name}-fingers-8x.png"), ImageFormat.Png);
        using (var palm = CropEnlarge(cleaned.Image, new Rectangle(18, 80, 110, 65), 8))
            palm.Save(Path.Combine(output, $"{variant.Name}-palm-8x.png"), ImageFormat.Png);
        using var changed = ChangedOverlay(source, cleaned.Image);
        using var changesEnlarged = Enlarge(changed, 6);
        changesEnlarged.Save(Path.Combine(output, $"{variant.Name}-changes-6x.png"), ImageFormat.Png);
        if (variant.Name == "dark-15-n3-g40-a3-c75")
        {
            foreach (var pair in baselineCandidates)
            {
                int overlap = 0;
                foreach (int index in pair.Value)
                {
                    int x = index % source.Width, y = index / source.Width;
                    if (source.GetPixel(x, y).ToArgb() != cleaned.Image.GetPixel(x, y).ToArgb()) overlap++;
                }
                Console.WriteLine($"baseline window={pair.Key} overlap with dark experiment={overlap}/{cleaned.ChangedPixels}");
            }
            SaveCandidateAtlas(source, cleaned.Image, Path.Combine(output, "candidate-atlas.png"));
            var second = DarkContourNoiseCleaner.Clean(cleaned.Image, variant.Options);
            using (second.Image)
            {
                RequireSameAlpha(source, second.Image);
                SaveCandidateAtlas(cleaned.Image, second.Image, Path.Combine(output, "pass2-atlas.png"));
                second.Image.Save(Path.Combine(output, "dark-15-n3-g40-a3-c75-pass2.png"), ImageFormat.Png);
                using var white = OnWhite(second.Image);
                using var enlarged = Enlarge(white, 6);
                enlarged.Save(Path.Combine(output, "dark-15-n3-g40-a3-c75-pass2-on-white-6x.png"), ImageFormat.Png);
                var third = DarkContourNoiseCleaner.Clean(second.Image, variant.Options);
                using (third.Image)
                {
                    RequireSameAlpha(source, third.Image);
                    third.Image.Save(Path.Combine(output, "dark-15-n3-g40-a3-c75-pass3.png"), ImageFormat.Png);
                    Console.WriteLine($"repeat pass2={second.ChangedPixels} pass3={third.ChangedPixels}");
                }
            }
        }
        if (variant.Name == "dark-05-n3-g40-a3-c75")
        {
            SaveCandidateAtlas(source, cleaned.Image, Path.Combine(output, "candidate-atlas-dark-05.png"));
            cleaned.Image.Save(Path.Combine(output, "recommended.png"), ImageFormat.Png);
            SaveSideBySide(source, cleaned.Image, Path.Combine(output, "before-after-on-white-6x.png"));
        }
        Console.WriteLine($"{variant.Name} changed={cleaned.ChangedPixels} dark topology: {topology}");
    }
}

var colors = new Dictionary<int, int>();
int visible = 0, transparent = 0;
for (int y = 0; y < source.Height; y++)
    for (int x = 0; x < source.Width; x++)
    {
        Color color = source.GetPixel(x, y);
        if (color.A == 0) { transparent++; continue; }
        visible++;
        int rgb = color.ToArgb() & 0xFFFFFF;
        colors[rgb] = colors.GetValueOrDefault(rgb) + 1;
    }
Console.WriteLine($"size={source.Width}x{source.Height} visible={visible} transparent={transparent} colors={colors.Count}");
foreach (var color in colors.OrderByDescending(pair => pair.Value).Take(20))
    Console.WriteLine($"#{color.Key:X6} {color.Value}");
foreach (int threshold in new[] { 5, 10, 15, 20, 25, 30 })
{
    int total = 0, interior = 0;
    int[] darkNeighbors = new int[9];
    for (int y = 1; y < source.Height - 1; y++)
        for (int x = 1; x < source.Width - 1; x++)
        {
            Color current = source.GetPixel(x, y);
            if (current.A == 0 || ColorSpace.RgbToLab(current.ToArgb() & 0xFFFFFF).L >= threshold) continue;
            total++;
            int count = 0, visibleNeighbors = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Color neighbor = source.GetPixel(x + dx, y + dy);
                    if (neighbor.A == 0) continue;
                    visibleNeighbors++;
                    if (ColorSpace.RgbToLab(neighbor.ToArgb() & 0xFFFFFF).L < threshold) count++;
                }
            if (visibleNeighbors == 8) { interior++; darkNeighbors[count]++; }
        }
    Console.WriteLine($"dark L<{threshold}: total={total} interior={interior} neighbors0={darkNeighbors[0]} 1={darkNeighbors[1]} 2={darkNeighbors[2]} 3={darkNeighbors[3]}");
}
using (var topology = DarkTopologyOverlay(source, 15))
using (var enlarged = Enlarge(topology, 6))
    enlarged.Save(Path.Combine(output, "dark-topology-L15-6x.png"), ImageFormat.Png);

static Bitmap Enlarge(Bitmap image, int scale)
{
    var result = new Bitmap(image.Width * scale, image.Height * scale, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(result);
    graphics.Clear(Color.Transparent);
    using var brush = new SolidBrush(Color.Transparent);
    for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            brush.Color = image.GetPixel(x, y);
            graphics.FillRectangle(brush, x * scale, y * scale, scale, scale);
        }
    return result;
}

static Bitmap CropEnlarge(Bitmap source, Rectangle region, int scale)
{
    using var crop = source.Clone(region, PixelFormat.Format32bppArgb);
    return Enlarge(crop, scale);
}

static Bitmap ChangedOverlay(Bitmap original, Bitmap result)
{
    var overlay = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
    for (int y = 0; y < original.Height; y++)
        for (int x = 0; x < original.Width; x++)
        {
            Color before = original.GetPixel(x, y), after = result.GetPixel(x, y);
            Color value = before.ToArgb() == after.ToArgb()
                ? Color.FromArgb(before.A, before.R / 3, before.G / 3, before.B / 3)
                : Color.Magenta;
            overlay.SetPixel(x, y, value);
        }
    return overlay;
}

static Bitmap OnWhite(Bitmap source)
{
    var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(result);
    graphics.Clear(Color.White);
    graphics.DrawImage(source, 0, 0, source.Width, source.Height);
    return result;
}

static void SaveCandidateAtlas(Bitmap source, Bitmap result, string path)
{
    var points = new List<Point>();
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            if (source.GetPixel(x, y).ToArgb() != result.GetPixel(x, y).ToArgb()) points.Add(new Point(x, y));
    const int columns = 8, tileWidth = 112, tileHeight = 72, pixelSize = 5, radius = 4;
    using var before = OnWhite(source);
    using var after = OnWhite(result);
    using var atlas = new Bitmap(columns * tileWidth,
        ((points.Count + columns - 1) / columns) * tileHeight, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(atlas))
    using (var font = new Font("Arial", 9))
    {
        graphics.Clear(Color.White);
        for (int q = 0; q < points.Count; q++)
        {
            int tileX = q % columns * tileWidth, tileY = q / columns * tileHeight;
            graphics.DrawString($"{q + 1}: {points[q].X},{points[q].Y}", font, Brushes.Black, tileX + 2, tileY);
        }
    }
    for (int q = 0; q < points.Count; q++)
    {
        int tileX = q % columns * tileWidth, tileY = q / columns * tileHeight;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                for (int e = 0; e < pixelSize; e++)
                    for (int f = 0; f < pixelSize; f++)
                    {
                        int x = points[q].X + dx, y = points[q].Y + dy;
                        if (x < 0 || y < 0 || x >= source.Width || y >= source.Height) continue;
                        int px = (dx + radius) * pixelSize + f, py = tileY + 18 + (dy + radius) * pixelSize + e;
                        atlas.SetPixel(tileX + 3 + px, py, before.GetPixel(x, y));
                        atlas.SetPixel(tileX + 59 + px, py, after.GetPixel(x, y));
                    }
    }
    atlas.Save(path, ImageFormat.Png);
}

static void SaveSideBySide(Bitmap source, Bitmap result, string path)
{
    const int scale = 6, margin = 12, header = 34;
    using var beforeWhite = OnWhite(source);
    using var afterWhite = OnWhite(result);
    using var before = Enlarge(beforeWhite, scale);
    using var after = Enlarge(afterWhite, scale);
    using var comparison = new Bitmap(before.Width * 2 + margin * 3, before.Height + header + margin,
        PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(comparison))
    using (var font = new Font("Arial", 14, FontStyle.Bold))
    {
        graphics.Clear(Color.White);
        graphics.DrawString("Исходник", font, Brushes.Black, margin, 5);
        graphics.DrawString("Очистка чёрных линий", font, Brushes.Black, before.Width + margin * 2, 5);
        graphics.DrawImageUnscaled(before, margin, header);
        graphics.DrawImageUnscaled(after, before.Width + margin * 2, header);
    }
    comparison.Save(path, ImageFormat.Png);
}

static Bitmap DarkTopologyOverlay(Bitmap source, double threshold)
{
    var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            Color color = source.GetPixel(x, y);
            Color mark = Color.FromArgb(color.A, color.R / 3, color.G / 3, color.B / 3);
            if (x > 0 && y > 0 && x < source.Width - 1 && y < source.Height - 1 && color.A > 0 &&
                ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L < threshold)
            {
                int count = 0, visible = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Color neighbor = source.GetPixel(x + dx, y + dy);
                        if (neighbor.A == 0) continue;
                        visible++;
                        if (ColorSpace.RgbToLab(neighbor.ToArgb() & 0xFFFFFF).L < threshold) count++;
                    }
                if (visible == 8)
                    mark = count <= 2 ? Color.Cyan : count == 3 ? Color.Orange : mark;
            }
            result.SetPixel(x, y, mark);
        }
    return result;
}

static void RequireSameAlpha(Bitmap source, Bitmap result)
{
    if (source.Width != result.Width || source.Height != result.Height)
        throw new InvalidOperationException("Canvas size changed.");
    for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            if (source.GetPixel(x, y).A != result.GetPixel(x, y).A)
                throw new InvalidOperationException($"Alpha changed at {x},{y}.");
}

static string MeasureDarkTopology(Bitmap source, double threshold)
{
    int width = source.Width, height = source.Height;
    var dark = new bool[width * height];
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color color = source.GetPixel(x, y);
            dark[y * width + x] = color.A > 0 && ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L < threshold;
        }
    int pixels = dark.Count(value => value), components = 0, small = 0, largest = 0;
    var visited = new bool[dark.Length];
    var queue = new int[dark.Length];
    for (int q = 0; q < dark.Length; q++)
    {
        if (!dark[q] || visited[q]) continue;
        components++;
        int head = 0, count = 0;
        visited[q] = true; queue[count++] = q;
        while (head < count)
        {
            int index = queue[head++], x = index % width, y = index / width;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int neighbor = ny * width + nx;
                    if (!dark[neighbor] || visited[neighbor]) continue;
                    visited[neighbor] = true;
                    queue[count++] = neighbor;
                }
        }
        if (count <= 2) small++;
        largest = Math.Max(largest, count);
    }
    return $"pixels={pixels}, components={components}, small(≤2)={small}, largest={largest}";
}
