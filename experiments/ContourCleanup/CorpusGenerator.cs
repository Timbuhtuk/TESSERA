using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using PixelArtDownscale;

internal static class CorpusGenerator
{
    private readonly record struct Palette(Color Main, Color Shade, Color Light, Color Accent);
    private static readonly Color Ink = Color.FromArgb(2, 3, 5);
    private static readonly string[] Names =
        ["sword", "shield", "potion", "mushroom", "robot", "tree", "lantern", "chest", "alien", "gauntlet"];
    private static readonly Palette[] Palettes =
    [
        new(Color.FromArgb(78, 127, 177), Color.FromArgb(38, 70, 113), Color.FromArgb(166, 210, 236), Color.FromArgb(243, 191, 72)),
        new(Color.FromArgb(179, 70, 81), Color.FromArgb(103, 35, 57), Color.FromArgb(245, 151, 145), Color.FromArgb(254, 219, 92)),
        new(Color.FromArgb(111, 164, 82), Color.FromArgb(52, 101, 60), Color.FromArgb(194, 225, 125), Color.FromArgb(239, 158, 70)),
        new(Color.FromArgb(161, 105, 177), Color.FromArgb(86, 52, 114), Color.FromArgb(219, 168, 236), Color.FromArgb(99, 212, 211)),
        new(Color.FromArgb(197, 141, 77), Color.FromArgb(105, 71, 49), Color.FromArgb(244, 212, 140), Color.FromArgb(114, 184, 218))
    ];

    internal static void Generate(string root, int seedOffset = 0)
    {
        string cleanDir = Path.Combine(root, "clean"), noisyDir = Path.Combine(root, "noisy");
        Directory.CreateDirectory(cleanDir);
        Directory.CreateDirectory(noisyDir);
        var manifest = new List<object>();
        for (int motif = 0; motif < Names.Length; motif++)
            for (int variant = 0; variant < Palettes.Length; variant++)
            {
                string id = $"{motif + 1:D2}-{Names[motif]}-{variant + 1:D2}";
                using var clean = Draw(motif, variant);
                using var noisy = (Bitmap)clean.Clone();
                var injected = InjectNoise(clean, noisy, motif * 1009 + variant * 37 + 20261026 + seedOffset);
                clean.Save(Path.Combine(cleanDir, id + ".png"), ImageFormat.Png);
                noisy.Save(Path.Combine(noisyDir, id + ".png"), ImageFormat.Png);
                manifest.Add(new { id, motif = Names[motif], variant = variant + 1,
                    injected = injected.Select(point => new { x = point.X, y = point.Y }).ToArray() });
                Console.WriteLine($"{id} injected={injected.Count}");
            }
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        foreach (string name in Names) SaveContactSheet(root, name);
        Console.WriteLine($"Generated {manifest.Count} clean/noisy pairs in {root}");
    }

    private static void SaveContactSheet(string root, string name)
    {
        string reviewDir = Path.Combine(root, "review");
        Directory.CreateDirectory(reviewDir);
        using var sheet = new Bitmap(1500, 180, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        using var font = new Font("Arial", 10);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int v = 0; v < 5; v++)
        {
            string id = $"{Array.IndexOf(Names, name) + 1:D2}-{name}-{v + 1:D2}.png";
            using var clean = new Bitmap(Path.Combine(root, "clean", id));
            using var noisy = new Bitmap(Path.Combine(root, "noisy", id));
            int x = v * 300;
            g.DrawString($"{name} {v + 1} · clean / noisy", font, Brushes.Black, x + 5, 3);
            g.DrawImage(clean, new Rectangle(x + 3, 24, 144, 144));
            g.DrawImage(noisy, new Rectangle(x + 151, 24, 144, 144));
        }
        sheet.Save(Path.Combine(reviewDir, name + ".png"), ImageFormat.Png);
    }

    private static Bitmap Draw(int motif, int variant)
    {
        var image = new Bitmap(72, 72, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TranslateTransform((variant % 3 - 1) * 2, (variant / 3 - 1) * 2);
        Palette p = Palettes[variant];
        switch (motif)
        {
            case 0: Sword(g, p, variant); break;
            case 1: Shield(g, p, variant); break;
            case 2: Potion(g, p, variant); break;
            case 3: Mushroom(g, p, variant); break;
            case 4: Robot(g, p, variant); break;
            case 5: Tree(g, p, variant); break;
            case 6: Lantern(g, p, variant); break;
            case 7: Chest(g, p, variant); break;
            case 8: Alien(g, p, variant); break;
            case 9: Gauntlet(g, p, variant); break;
        }
        return image;
    }

    private static List<Point> InjectNoise(Bitmap clean, Bitmap noisy, int seed)
    {
        var candidates = new List<Point>();
        for (int y = 5; y < clean.Height - 5; y++)
            for (int x = 5; x < clean.Width - 5; x++)
            {
                Color current = clean.GetPixel(x, y);
                if (current.A != 255 || Lightness(current) < 30) continue;
                int dark = 0, visible = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        Color neighbor = clean.GetPixel(x + dx, y + dy);
                        if (neighbor.A > 0) visible++;
                        if (neighbor.A > 0 && Lightness(neighbor) < 5) dark++;
                    }
                if (dark is >= 1 and <= 3 && visible >= 6) candidates.Add(new Point(x, y));
            }
        var random = new Random(seed);
        for (int q = candidates.Count - 1; q > 0; q--)
        {
            int e = random.Next(q + 1);
            (candidates[q], candidates[e]) = (candidates[e], candidates[q]);
        }
        var injected = new List<Point>();
        foreach (Point candidate in candidates)
        {
            if (injected.Any(point => Math.Abs(point.X - candidate.X) <= 4 &&
                Math.Abs(point.Y - candidate.Y) <= 4)) continue;
            noisy.SetPixel(candidate.X, candidate.Y, injected.Count % 2 == 0 ? Ink : Color.FromArgb(7, 6, 9));
            injected.Add(candidate);
            if (injected.Count == 8) break;
        }
        return injected;
    }

    private static double Lightness(Color color)
        => ColorSpace.RgbToLab(color.ToArgb() & 0xFFFFFF).L;

    private static void Sword(Graphics g, Palette p, int v)
    {
        Polygon(g, Ink, (33, 5), (42, 5), (43, 42), (38, 49), (32, 42));
        Polygon(g, p.Light, (36, 9), (39, 9), (40, 40), (37, 45), (35, 40));
        Rect(g, p.Shade, 39, 11, 2, 27);
        Rect(g, Ink, 19, 42, 37, 6);
        Rect(g, p.Accent, 22, 44, 31, 2);
        Rect(g, Ink, 34, 47, 7, 18);
        Rect(g, p.Shade, 36, 49, 3, 13);
        Rect(g, Ink, 29, 63, 17, 4);
        Rect(g, p.Accent, 32, 64, 11, 2);
        Rect(g, Ink, 36, 16 + v, 2, 16);
    }

    private static void Shield(Graphics g, Palette p, int v)
    {
        Polygon(g, Ink, (36, 6), (60, 15), (57, 46), (36, 66), (15, 46), (12, 15));
        Polygon(g, p.Shade, (36, 10), (56, 18), (53, 45), (36, 61), (19, 45), (16, 18));
        Polygon(g, p.Main, (36, 12), (51, 19), (49, 43), (36, 57), (36, 12));
        Line(g, Ink, 36, 12, 36, 58, 3);
        Rect(g, p.Accent, 30 - v / 2, 25, 12 + v, 19);
        Rect(g, Ink, 34, 20, 4, 30);
        Rect(g, Ink, 27, 31, 18, 4);
        Rect(g, p.Light, 20, 19, 4, 19);
        Rect(g, p.Light, 43, 18, 4, 21);
    }

    private static void Potion(Graphics g, Palette p, int v)
    {
        Rect(g, Ink, 27, 7, 20, 8);
        Rect(g, p.Accent, 30, 9, 14, 4);
        Rect(g, Ink, 29, 15, 16, 13);
        Rect(g, p.Light, 32, 16, 10, 13);
        Ellipse(g, Ink, 12, 22, 51, 45);
        Ellipse(g, p.Shade, 16, 26, 43, 37);
        Rect(g, p.Main, 18, 41 - v, 39, 16 + v);
        Rect(g, p.Light, 21, 45, 5, 13);
        Rect(g, p.Accent, 27, 38, 4, 4);
        Rect(g, p.Accent, 45, 31, 3 + v, 3);
        Line(g, Ink, 18, 42, 54, 42, 2);
    }

    private static void Mushroom(Graphics g, Palette p, int v)
    {
        Rect(g, Ink, 29, 38, 17, 27);
        Rect(g, p.Light, 33, 41, 9, 20);
        Ellipse(g, Ink, 9, 12, 57, 39);
        Ellipse(g, p.Main, 13, 16, 49, 29);
        Rect(g, Ink, 13, 39, 49, 6);
        Rect(g, p.Shade, 18, 39, 39, 3);
        Ellipse(g, p.Light, 23, 20, 8 + v, 7);
        Ellipse(g, p.Light, 44, 23, 9, 7);
        Ellipse(g, p.Light, 34, 31, 6, 5);
        Line(g, Ink, 27, 44, 32, 53, 2);
    }

    private static void Robot(Graphics g, Palette p, int v)
    {
        Rect(g, Ink, 20, 29, 35, 31);
        Rect(g, p.Shade, 24, 32, 27, 24);
        Rect(g, p.Main, 26, 33, 23, 16);
        Rect(g, Ink, 24, 9, 27, 22);
        Rect(g, p.Main, 27, 12, 21, 15);
        Rect(g, Ink, 18, 17, 6, 9);
        Rect(g, Ink, 51, 17, 6, 9);
        Rect(g, p.Accent, 31, 19, 5, 4);
        Rect(g, p.Accent, 41, 19, 5, 4);
        Rect(g, Ink, 35, 26, 6, 3);
        Rect(g, Ink, 10, 35, 10, 17);
        Rect(g, Ink, 55, 35, 10, 17);
        Rect(g, p.Light, 13, 37, 4, 10);
        Rect(g, p.Light, 58, 37, 4, 10);
        Rect(g, Ink, 24, 60, 10, 7);
        Rect(g, Ink, 42, 60, 10, 7);
        Rect(g, p.Accent, 33, 37 + v, 10, 4);
    }

    private static void Tree(Graphics g, Palette p, int v)
    {
        Rect(g, Ink, 28, 38, 19, 29);
        Rect(g, p.Shade, 32, 40, 11, 23);
        Ellipse(g, Ink, 13, 20, 30, 30);
        Ellipse(g, Ink, 31, 18, 31, 32);
        Ellipse(g, Ink, 21, 8, 31, 31);
        Ellipse(g, p.Main, 16, 23, 26, 23);
        Ellipse(g, p.Main, 34, 21, 25, 25);
        Ellipse(g, p.Light, 24, 11, 25, 24);
        Line(g, Ink, 37, 34, 37, 55, 3);
        Line(g, Ink, 37, 47, 25, 37, 2);
        Ellipse(g, p.Accent, 22, 30, 4 + v, 4);
        Ellipse(g, p.Accent, 47, 34, 4, 4);
    }

    private static void Lantern(Graphics g, Palette p, int v)
    {
        Ellipse(g, Ink, 27, 5, 22, 18);
        Ellipse(g, p.Accent, 31, 9, 14, 10);
        Rect(g, Ink, 18, 22, 39, 43);
        Rect(g, p.Shade, 22, 26, 31, 35);
        Rect(g, p.Accent, 26, 29, 23, 27);
        Rect(g, p.Light, 31, 32, 13, 21);
        Rect(g, Ink, 35, 26, 4, 35);
        Rect(g, Ink, 22, 40, 31, 3);
        Rect(g, Ink, 14, 61, 47, 6);
        Rect(g, p.Main, 18, 63, 39, 2);
        Rect(g, Ink, 17, 19 - v / 2, 41, 5);
    }

    private static void Chest(Graphics g, Palette p, int v)
    {
        Rect(g, Ink, 11, 32, 54, 32);
        Rect(g, p.Main, 15, 36, 46, 24);
        Ellipse(g, Ink, 11, 13, 54, 32);
        Ellipse(g, p.Shade, 15, 17, 46, 24);
        Rect(g, Ink, 11, 37, 54, 7);
        Rect(g, p.Accent, 16, 39, 44, 3);
        Rect(g, Ink, 29, 18, 7, 44);
        Rect(g, Ink, 51, 18, 6, 44);
        Rect(g, p.Light, 18, 22, 6, 13);
        Rect(g, p.Accent, 35, 37, 12, 15 + v / 2);
        Rect(g, Ink, 38, 43, 6, 5);
        Rect(g, Ink, 16, 64, 9, 4);
        Rect(g, Ink, 52, 64, 9, 4);
    }

    private static void Alien(Graphics g, Palette p, int v)
    {
        Ellipse(g, Ink, 13, 7, 50, 55);
        Ellipse(g, p.Main, 17, 11, 42, 47);
        Ellipse(g, p.Light, 22, 14, 28, 18);
        Ellipse(g, Ink, 21, 30, 15, 14);
        Ellipse(g, Ink, 40, 30, 15, 14);
        Ellipse(g, p.Accent, 25, 33, 6, 5);
        Ellipse(g, p.Accent, 44, 33, 6, 5);
        Line(g, Ink, 31, 49, 45, 49, 3);
        Rect(g, Ink, 33, 52, 10, 4);
        Rect(g, Ink, 21, 59, 35, 8);
        Rect(g, p.Shade, 25, 61, 27, 3);
        Rect(g, p.Accent, 36, 17 + v, 4, 4);
    }

    private static void Gauntlet(Graphics g, Palette p, int v)
    {
        Polygon(g, Ink, (18, 27), (22, 17), (30, 15), (34, 10), (43, 11), (46, 17),
            (56, 20), (62, 30), (59, 45), (52, 53), (50, 65), (25, 65), (23, 54), (15, 44));
        Polygon(g, p.Shade, (21, 29), (25, 20), (31, 19), (36, 14), (42, 15), (44, 21),
            (54, 23), (58, 31), (55, 43), (49, 49), (46, 59), (28, 59), (26, 49), (19, 42));
        Rect(g, p.Main, 25, 26, 27, 25);
        Rect(g, p.Light, 30, 28, 15, 12);
        Line(g, Ink, 30, 16, 32, 34, 3);
        Line(g, Ink, 43, 18, 43, 36, 3);
        Line(g, Ink, 55, 26, 50, 39, 3);
        Line(g, Ink, 19, 42, 31, 44, 3);
        Rect(g, Ink, 23, 54, 30, 6);
        Rect(g, p.Accent, 28, 56, 20, 2);
        Rect(g, Ink, 23, 62, 31, 6);
        Rect(g, p.Main, 28, 64, 21, 2);
        Rect(g, p.Accent, 37, 45 + v / 2, 4, 4);
    }

    private static void Rect(Graphics g, Color color, int x, int y, int width, int height)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y, width, height);
    }

    private static void Ellipse(Graphics g, Color color, int x, int y, int width, int height)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, y, width, height);
    }

    private static void Polygon(Graphics g, Color color, params (int X, int Y)[] points)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points.Select(point => new Point(point.X, point.Y)).ToArray());
    }

    private static void Line(Graphics g, Color color, int x1, int y1, int x2, int y2, int width)
    {
        using var pen = new Pen(color, width);
        g.DrawLine(pen, x1, y1, x2, y2);
    }
}
