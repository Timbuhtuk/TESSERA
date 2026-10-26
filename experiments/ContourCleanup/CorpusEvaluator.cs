using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;

internal static class CorpusEvaluator
{
    private sealed record Score(string Id, string Motif, int Injected, int Corrected, int RecoveredExactly,
        int WrongColor, int OtherChanges, int CleanDamage, double ErrorBefore, double ErrorAfter);

    internal static void Evaluate(string root)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var scores = new List<Score>();
        var missed = new List<(string Id, int X, int Y)>();
        var featureRows = new List<string> { "label,id,x,y,component,largest,component4,largest4,dark8,dark4,dark5,run2,opposite,clearance,trueGain,ring,expectedConsensus" };
        string review = Path.Combine(root, "review-after");
        Directory.CreateDirectory(review);
        foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            string motif = entry.GetProperty("motif").GetString()!;
            using var clean = new Bitmap(Path.Combine(root, "clean", id + ".png"));
            using var noisy = new Bitmap(Path.Combine(root, "noisy", id + ".png"));
            using var result = new Bitmap(Path.Combine(root, "processed-noisy", id + ".png"));
            using var cleanResult = new Bitmap(Path.Combine(root, "processed-clean", id + ".png"));
            var injected = entry.GetProperty("injected").EnumerateArray()
                .Select(point => point.GetProperty("y").GetInt32() * clean.Width + point.GetProperty("x").GetInt32())
                .ToHashSet();
            int[] cleanComponents = DarkComponentSizes(clean), noisyComponents = DarkComponentSizes(noisy);
            int[] cleanComponents4 = DarkComponentSizes(clean, false), noisyComponents4 = DarkComponentSizes(noisy, false);
            int corrected = 0, recovered = 0, wrongColor = 0, other = 0, cleanDamage = 0;
            double beforeError = 0, afterError = 0;
            RequireSameCanvasAndAlpha(clean, noisy, id + " noisy");
            RequireSameCanvasAndAlpha(clean, result, id + " processed");
            RequireSameCanvasAndAlpha(clean, cleanResult, id + " clean control");
            for (int y = 0; y < clean.Height; y++)
                for (int x = 0; x < clean.Width; x++)
                {
                    int index = y * clean.Width + x;
                    Color expected = clean.GetPixel(x, y), damaged = noisy.GetPixel(x, y), actual = result.GetPixel(x, y);
                    if (expected.A == 0) continue;
                    beforeError += Difference(expected, damaged);
                    afterError += Difference(expected, actual);
                    if (injected.Contains(index))
                    {
                        string label = actual.ToArgb() == expected.ToArgb() ? "noise_corrected" :
                            actual.ToArgb() != damaged.ToArgb() ? "noise_wrong_color" : "noise_missed";
                        featureRows.Add(Features(label,
                            id, noisy, x, y, noisyComponents, noisyComponents4, expected));
                        if (actual.ToArgb() != damaged.ToArgb()) corrected++;
                        if (actual.ToArgb() == expected.ToArgb()) recovered++;
                        else
                        {
                            missed.Add((id, x, y));
                            if (actual.ToArgb() != damaged.ToArgb())
                            {
                                wrongColor++;
                                Console.WriteLine($"wrong-color {id} {x},{y} expected=#{expected.R:X2}{expected.G:X2}{expected.B:X2} " +
                                    $"actual=#{actual.R:X2}{actual.G:X2}{actual.B:X2} " +
                                    $"neighbors={NeighborColors(noisy, x, y)}");
                            }
                        }
                    }
                    else if (actual.ToArgb() != damaged.ToArgb())
                    {
                        other++;
                        featureRows.Add(Features("other_change", id, noisy, x, y, noisyComponents, noisyComponents4, expected));
                        Console.WriteLine($"other-change {id} {x},{y} expected=#{expected.R:X2}{expected.G:X2}{expected.B:X2} " +
                            $"actual=#{actual.R:X2}{actual.G:X2}{actual.B:X2} neighbors={NeighborColors(noisy, x, y)}");
                    }
                    if (cleanResult.GetPixel(x, y).ToArgb() != expected.ToArgb())
                    {
                        cleanDamage++;
                        featureRows.Add(Features("clean_damage", id, clean, x, y, cleanComponents, cleanComponents4, expected));
                    }
                }
            scores.Add(new Score(id, motif, injected.Count, corrected, recovered, wrongColor, other, cleanDamage,
                Math.Round(beforeError, 1), Math.Round(afterError, 1)));
            Console.WriteLine($"{id} corrected={corrected}/{injected.Count} exact={recovered} wrongColor={wrongColor} " +
                $"other={other} cleanDamage={cleanDamage} error={beforeError:F0}->{afterError:F0}");
        }
        foreach (string motif in scores.Select(score => score.Motif).Distinct())
            SaveMotifReview(root, review, motif, scores.Where(score => score.Motif == motif).ToArray());
        SaveMissedAtlas(root, review, missed);
        var aggregate = new
        {
            images = scores.Count,
            injected = scores.Sum(score => score.Injected),
            corrected = scores.Sum(score => score.Corrected),
            recoveredExactly = scores.Sum(score => score.RecoveredExactly),
            wrongColor = scores.Sum(score => score.WrongColor),
            otherChanges = scores.Sum(score => score.OtherChanges),
            cleanDamage = scores.Sum(score => score.CleanDamage),
            errorBefore = Math.Round(scores.Sum(score => score.ErrorBefore), 1),
            errorAfter = Math.Round(scores.Sum(score => score.ErrorAfter), 1),
            imagesWithCleanDamage = scores.Count(score => score.CleanDamage > 0),
            imagesWithWrongColor = scores.Count(score => score.WrongColor > 0),
            imagesWithMissedNoise = scores.Count(score => score.Corrected < score.Injected)
        };
        File.WriteAllText(Path.Combine(root, "evaluation.json"), JsonSerializer.Serialize(new { aggregate, scores },
            new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(root, "features.csv"), featureRows);
        Console.WriteLine(JsonSerializer.Serialize(aggregate));
    }

    private static int[] DarkComponentSizes(Bitmap image, bool includeDiagonals = true)
    {
        int width = image.Width, height = image.Height;
        var sizes = new int[width * height];
        var dark = new bool[sizes.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color color = image.GetPixel(x, y);
                dark[y * width + x] = color.A > 0 && ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L < 5;
            }
        var queue = new int[dark.Length];
        for (int q = 0; q < dark.Length; q++)
        {
            if (!dark[q] || sizes[q] != 0) continue;
            int head = 0, count = 0;
            queue[count++] = q; sizes[q] = -1;
            while (head < count)
            {
                int index = queue[head++], x = index % width, y = index / width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!includeDiagonals && Math.Abs(dx) + Math.Abs(dy) != 1) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int neighbor = ny * width + nx;
                        if (!dark[neighbor] || sizes[neighbor] != 0) continue;
                        sizes[neighbor] = -1;
                        queue[count++] = neighbor;
                    }
            }
            for (int e = 0; e < count; e++) sizes[queue[e]] = count;
        }
        return sizes;
    }

    private static string Features(string label, string id, Bitmap image, int x, int y,
        int[] components, int[] components4, Color expected)
    {
        int dark8 = 0, dark4 = 0, dark5 = 0, run2 = 0, clearance = 6;
        int bright = 0, expectedMatches = 0;
        bool Dark(int px, int py)
        {
            if (px < 0 || py < 0 || px >= image.Width || py >= image.Height) return false;
            Color color = image.GetPixel(px, py);
            return color.A > 0 && ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L < 5;
        }
        for (int dy = -5; dy <= 5; dy++)
            for (int dx = -5; dx <= 5; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= image.Width || ny >= image.Height ||
                    image.GetPixel(nx, ny).A == 0)
                    clearance = Math.Min(clearance, Math.Max(Math.Abs(dx), Math.Abs(dy)));
                if (dx == 0 && dy == 0) continue;
                if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2 && Dark(nx, ny)) dark5++;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && Dark(nx, ny))
                {
                    dark8++;
                    if (Math.Abs(dx) + Math.Abs(dy) == 1) dark4++;
                    if (Dark(x + dx * 2, y + dy * 2)) run2++;
                }
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 &&
                    nx >= 0 && ny >= 0 && nx < image.Width && ny < image.Height &&
                    image.GetPixel(nx, ny).A > 0 && !Dark(nx, ny))
                {
                    bright++;
                    if (Difference(expected, image.GetPixel(nx, ny)) <= 28 * 28) expectedMatches++;
                }
            }
        bool opposite = Dark(x - 1, y) && Dark(x + 1, y) || Dark(x, y - 1) && Dark(x, y + 1) ||
            Dark(x - 1, y - 1) && Dark(x + 1, y + 1) || Dark(x + 1, y - 1) && Dark(x - 1, y + 1);
        double trueGain = ColorSpace.RgbToLab(expected.ToArgb() & 0xFFFFFF).L -
            ColorSpace.RgbToLab(image.GetPixel(x, y).ToArgb() & 0xFFFFFF).L;
        int ring = DarkRingComponents(Dark, x, y);
        double consensus = bright == 0 ? 0 : expectedMatches / (double)bright;
        return $"{label},{id},{x},{y},{components[y * image.Width + x]},{components.Max()}," +
            $"{components4[y * image.Width + x]},{components4.Max()}," +
            $"{dark8},{dark4},{dark5},{run2},{(opposite ? 1 : 0)},{clearance}," +
            $"{trueGain.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{ring},{consensus.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static int DarkRingComponents(Func<int, int, bool> dark, int x, int y)
    {
        var present = new bool[9];
        var seen = new bool[9];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) present[(dy + 1) * 3 + dx + 1] = dark(x + dx, y + dy);
        int components = 0;
        var queue = new int[8];
        for (int q = 0; q < 9; q++)
        {
            if (!present[q] || seen[q]) continue;
            components++;
            int head = 0, count = 0;
            queue[count++] = q; seen[q] = true;
            while (head < count)
            {
                int index = queue[head++], px = index % 3, py = index / 3;
                for (int dy = Math.Max(0, py - 1); dy <= Math.Min(2, py + 1); dy++)
                    for (int dx = Math.Max(0, px - 1); dx <= Math.Min(2, px + 1); dx++)
                    {
                        int neighbor = dy * 3 + dx;
                        if (neighbor == 4 || !present[neighbor] || seen[neighbor]) continue;
                        seen[neighbor] = true;
                        queue[count++] = neighbor;
                    }
            }
        }
        return components;
    }

    private static void RequireSameCanvasAndAlpha(Bitmap expected, Bitmap actual, string name)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            throw new InvalidOperationException($"Размер изменился: {name}");
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (expected.GetPixel(x, y).A != actual.GetPixel(x, y).A)
                    throw new InvalidOperationException($"Альфа изменилась: {name} {x},{y}");
    }

    private static double Difference(Color expected, Color actual)
    {
        var a = ColorSpace.RgbToLab(expected.ToArgb() & 0xFFFFFF);
        var b = ColorSpace.RgbToLab(actual.ToArgb() & 0xFFFFFF);
        double l = a.L - b.L, c = a.A - b.A, d = a.B - b.B;
        return l * l + c * c + d * d;
    }

    private static string NeighborColors(Bitmap image, int x, int y)
    {
        var rows = new List<string>();
        for (int dy = -1; dy <= 1; dy++)
        {
            var colors = new List<string>();
            for (int dx = -1; dx <= 1; dx++)
            {
                Color color = image.GetPixel(x + dx, y + dy);
                colors.Add(color.A == 0 ? "------" : $"{color.R:X2}{color.G:X2}{color.B:X2}");
            }
            rows.Add(string.Join("/", colors));
        }
        return string.Join("|", rows);
    }

    private static void SaveMotifReview(string root, string review, string motif, Score[] scores)
    {
        const int scale = 2, panel = 72 * scale, row = 177, gap = 10, left = 8;
        using var sheet = new Bitmap(left * 2 + panel * 4 + gap * 3, row * scores.Length,
            PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        using var font = new Font("Arial", 9);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int q = 0; q < scores.Length; q++)
        {
            string id = scores[q].Id;
            using var clean = new Bitmap(Path.Combine(root, "clean", id + ".png"));
            using var noisy = new Bitmap(Path.Combine(root, "noisy", id + ".png"));
            using var result = new Bitmap(Path.Combine(root, "processed-noisy", id + ".png"));
            using var changes = new Bitmap(clean.Width, clean.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < clean.Height; y++)
                for (int x = 0; x < clean.Width; x++)
                {
                    Color before = noisy.GetPixel(x, y), after = result.GetPixel(x, y);
                    changes.SetPixel(x, y, before.ToArgb() == after.ToArgb()
                        ? Color.FromArgb(before.A, before.R / 3, before.G / 3, before.B / 3)
                        : Color.Magenta);
                }
            Bitmap[] images = [clean, noisy, result, changes];
            string[] labels = ["clean", "noisy", "Tessera", "changes"];
            for (int e = 0; e < images.Length; e++)
            {
                int x = left + e * (panel + gap), y = q * row;
                g.DrawString($"{id}: {labels[e]}", font, Brushes.Black, x, y + 2);
                g.DrawImage(images[e], new Rectangle(x, y + 22, panel, panel));
            }
        }
        sheet.Save(Path.Combine(review, motif + ".png"), ImageFormat.Png);
    }

    private static void SaveMissedAtlas(string root, string review, List<(string Id, int X, int Y)> missed)
    {
        const int crop = 9, scale = 16, panel = crop * scale, gap = 12, row = 175;
        const int pageSize = 40;
        for (int page = 0; page < Math.Max(1, (missed.Count + pageSize - 1) / pageSize); page++)
        {
            var points = missed.Skip(page * pageSize).Take(pageSize).ToArray();
            using var sheet = new Bitmap(3 * panel + 4 * gap, Math.Max(1, points.Length) * row,
                PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(sheet);
            using var font = new Font("Arial", 9);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int q = 0; q < points.Length; q++)
            {
                var point = points[q];
                using var clean = new Bitmap(Path.Combine(root, "clean", point.Id + ".png"));
                using var noisy = new Bitmap(Path.Combine(root, "noisy", point.Id + ".png"));
                using var result = new Bitmap(Path.Combine(root, "processed-noisy", point.Id + ".png"));
                Bitmap[] images = [clean, noisy, result];
                string[] labels = ["clean", "noisy", "Tessera"];
                for (int e = 0; e < 3; e++)
                {
                    int left = gap + e * (panel + gap), top = q * row;
                    g.DrawString($"{point.Id} ({point.X},{point.Y}) {labels[e]}", font,
                        Brushes.Black, left, top + 2);
                    g.DrawImage(images[e], new Rectangle(left, top + 22, panel, panel),
                        new Rectangle(point.X - 4, point.Y - 4, crop, crop), GraphicsUnit.Pixel);
                    using var pen = new Pen(Color.Red, 2);
                    g.DrawRectangle(pen, left + 4 * scale, top + 22 + 4 * scale, scale, scale);
                }
            }
            string name = missed.Count <= pageSize ? "missed-atlas.png" : $"missed-atlas-{page + 1:00}.png";
            sheet.Save(Path.Combine(review, name), ImageFormat.Png);
        }
    }
}
