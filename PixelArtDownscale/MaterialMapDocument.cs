using System.Numerics;

namespace PixelArtDownscale;

public enum MaterialMapKind { Height, Normal, AO }

public sealed record MaterialMapSnapshot(float[] Heights, Vector3[] Normals, float[] NormalMask,
    float[] Occlusion, float[] OcclusionMask, float NormalStrength, float AoStrength, int AoRadius, bool DirectX);

/// <summary>Editable height field and independent, non-destructive normal/AO paint layers.</summary>
public sealed class MaterialMapDocument
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Source { get; }
    public float[] Heights { get; private set; }
    public Vector3[] NormalLayer { get; private set; }
    public float[] NormalMask { get; private set; }
    public float[] AoLayer { get; private set; }
    public float[] AoMask { get; private set; }
    public Vector3[] GeneratedNormals { get; }
    public float[] GeneratedAO { get; }
    public float NormalStrength { get; set; } = 4;
    public float AoStrength { get; set; } = 2;
    public int AoRadius { get; set; } = 8;
    public bool DirectX { get; set; } = true;
    public bool HasChanges { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    private readonly List<MaterialMapSnapshot> _undo = [], _redo = [];

    public MaterialMapDocument(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid material image dimensions or pixel buffer.");
        Width = width; Height = height; Source = (byte[])bgra.Clone();
        int count = checked(width * height);
        Heights = new float[count]; NormalLayer = new Vector3[count]; NormalMask = new float[count];
        AoLayer = new float[count]; AoMask = new float[count];
        GeneratedNormals = new Vector3[count]; GeneratedAO = new float[count];
        for (int q = 0; q < count; q++) Heights[q] = SourceHeight(q);
        Recalculate();
    }

    private float SourceHeight(int q) => (.2126f * Source[q * 4 + 2] + .7152f * Source[q * 4 + 1] + .0722f * Source[q * 4]) / 255;
    private bool Visible(int q) => Source[q * 4 + 3] > 0;

    public void Recalculate()
    {
        if (!float.IsFinite(NormalStrength) || NormalStrength is < 0 or > 32 ||
            !float.IsFinite(AoStrength) || AoStrength is < 0 or > 16 || AoRadius is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(NormalStrength), "Invalid material generation settings.");
        var samples = new (int X, int Y, float Scale)[32];
        for (int direction = 0; direction < 8; direction++)
            for (int step = 0; step < 4; step++)
            {
                double angle = direction * Math.PI / 4;
                int distance = Math.Max(1, (int)Math.Round(AoRadius * (step + 1) / 4.0));
                int dx = (int)Math.Round(Math.Cos(angle) * distance), dy = (int)Math.Round(Math.Sin(angle) * distance);
                samples[direction * 4 + step] = (dx, dy, AoRadius / MathF.Sqrt(dx * dx + dy * dy));
            }
        // Canonical normals use +Y up. Export convention only changes the green encoding.
        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                int q = y * Width + x;
                if (!Visible(q)) { GeneratedNormals[q] = Vector3.UnitZ; GeneratedAO[q] = 1; continue; }
                float center = Heights[q];
                float Sample(int sx, int sy)
                {
                    int index = Math.Clamp(sy, 0, Height - 1) * Width + Math.Clamp(sx, 0, Width - 1);
                    return Visible(index) ? Heights[index] : center;
                }
                float dx = (Sample(x + 1, y) - Sample(x - 1, y)) * .5f;
                float dy = (Sample(x, y + 1) - Sample(x, y - 1)) * .5f;
                GeneratedNormals[q] = Vector3.Normalize(new(-dx * NormalStrength, dy * NormalStrength, 1));
                float occlusion = 0;
                for (int direction = 0; direction < 8; direction++)
                {
                    float horizon = 0;
                    for (int step = 0; step < 4; step++)
                    {
                        var sample = samples[direction * 4 + step];
                        int sx = x + sample.X, sy = y + sample.Y;
                        // Do not wrap a sprite or an atlas onto its opposite edge.
                        if (sx < 0 || sy < 0 || sx >= Width || sy >= Height || !Visible(sy * Width + sx)) continue;
                        float slope = Math.Max(0, Heights[sy * Width + sx] - center) * sample.Scale;
                        horizon = Math.Max(horizon, slope / MathF.Sqrt(1 + slope * slope));
                    }
                    occlusion += horizon;
                }
                GeneratedAO[q] = Math.Clamp(1 - occlusion / 8 * AoStrength, 0, 1);
            }
        });
    }

    public Vector3 NormalAt(int q)
    {
        Vector3 normal = Vector3.Lerp(GeneratedNormals[q], NormalLayer[q], NormalMask[q]);
        return normal.LengthSquared() < .000001f ? Vector3.UnitZ : Vector3.Normalize(normal);
    }
    public float AoAt(int q) => GeneratedAO[q] * (1 - AoMask[q]) + AoLayer[q] * AoMask[q];

    public void PaintLine(MaterialMapKind kind, int fromX, int fromY, int toX, int toY, int size,
        float value, Vector3 normal, float strength, float softness, bool erase)
    {
        if (size is < 1 or > 256 || !float.IsFinite(value) || value is < 0 or > 1 ||
            !float.IsFinite(strength) || strength is < 0 or > 1 || !float.IsFinite(softness) || softness is < 0 or > 1 ||
            !float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < .000001f)
            throw new ArgumentOutOfRangeException(nameof(size));
        normal = Vector3.Normalize(normal);
        int steps = Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
        for (int q = 0; q <= steps; q++)
        {
            double amount = steps == 0 ? 1 : (double)q / steps;
            int x = (int)Math.Round(fromX + (toX - fromX) * amount);
            int y = (int)Math.Round(fromY + (toY - fromY) * amount);
            Dab(x, y);
        }

        void Dab(int cx, int cy)
        {
            int left = cx - (size - 1) / 2, top = cy - (size - 1) / 2;
            for (int y = Math.Max(0, top); y < Math.Min(Height, top + size); y++)
                for (int x = Math.Max(0, left); x < Math.Min(Width, left + size); x++)
                {
                    int index = y * Width + x;
                    if (!Visible(index)) continue;
                    float coverage = strength;
                    if (softness > 0 && size > 1)
                    {
                        float radius = size / 2f;
                        float distance = MathF.Sqrt(MathF.Pow(x - left + .5f - radius, 2) + MathF.Pow(y - top + .5f - radius, 2)) / radius;
                        coverage *= Math.Clamp((1 - distance) / softness, 0, 1);
                    }
                    if (coverage <= 0) continue;
                    if (kind == MaterialMapKind.Height)
                        Heights[index] += ((erase ? SourceHeight(index) : value) - Heights[index]) * coverage;
                    else if (kind == MaterialMapKind.Normal)
                    {
                        if (erase) NormalMask[index] *= 1 - coverage;
                        else
                        {
                            float previous = NormalMask[index] * (1 - coverage), total = previous + coverage;
                            var blended = NormalLayer[index] * previous + normal * coverage;
                            NormalLayer[index] = blended.LengthSquared() < .000001f ? normal : Vector3.Normalize(blended);
                            NormalMask[index] = total;
                        }
                    }
                    else if (erase) AoMask[index] *= 1 - coverage;
                    else
                    {
                        float previous = AoMask[index] * (1 - coverage), total = previous + coverage;
                        AoLayer[index] = (AoLayer[index] * previous + value * coverage) / total;
                        AoMask[index] = total;
                    }
                }
        }
    }

    public MaterialMapSnapshot Capture() => new((float[])Heights.Clone(), (Vector3[])NormalLayer.Clone(), (float[])NormalMask.Clone(),
        (float[])AoLayer.Clone(), (float[])AoMask.Clone(), NormalStrength, AoStrength, AoRadius, DirectX);

    public bool Commit(MaterialMapSnapshot before)
    {
        if (before.Heights.AsSpan().SequenceEqual(Heights) && before.Normals.AsSpan().SequenceEqual(NormalLayer) &&
            before.NormalMask.AsSpan().SequenceEqual(NormalMask) && before.Occlusion.AsSpan().SequenceEqual(AoLayer) &&
            before.OcclusionMask.AsSpan().SequenceEqual(AoMask) && before.NormalStrength == NormalStrength &&
            before.AoStrength == AoStrength && before.AoRadius == AoRadius && before.DirectX == DirectX) return false;
        _undo.Add(before); _redo.Clear(); Trim(_undo); HasChanges = true;
        return true;
    }

    private void Trim(List<MaterialMapSnapshot> history)
    {
        long bytes = (long)Width * Height * 28;
        while (history.Count > 1 && (history.Count > 30 || history.Count * bytes > 128L * 1024 * 1024)) history.RemoveAt(0);
    }
    public bool Undo() => RestoreHistory(_undo, _redo);
    public bool Redo() => RestoreHistory(_redo, _undo);
    private bool RestoreHistory(List<MaterialMapSnapshot> from, List<MaterialMapSnapshot> to)
    {
        if (from.Count == 0) return false;
        to.Add(Capture()); Trim(to);
        Restore(from[^1]); from.RemoveAt(from.Count - 1); HasChanges = true; return true;
    }
    public void Restore(MaterialMapSnapshot state)
    {
        int count = checked(Width * Height);
        if (state.Heights.Length != count || state.Normals.Length != count || state.NormalMask.Length != count ||
            state.Occlusion.Length != count || state.OcclusionMask.Length != count) throw new ArgumentException("Invalid material layer dimensions.");
        Heights = (float[])state.Heights.Clone(); NormalLayer = (Vector3[])state.Normals.Clone(); NormalMask = (float[])state.NormalMask.Clone();
        AoLayer = (float[])state.Occlusion.Clone(); AoMask = (float[])state.OcclusionMask.Clone();
        NormalStrength = state.NormalStrength; AoStrength = state.AoStrength; AoRadius = state.AoRadius; DirectX = state.DirectX;
        Recalculate();
    }
    public void ResetManual(MaterialMapKind kind)
    {
        if (kind == MaterialMapKind.Height) throw new ArgumentException("Height is the shared relief, not a manual override layer.");
        var before = Capture();
        if (kind == MaterialMapKind.Normal) { Array.Clear(NormalLayer); Array.Clear(NormalMask); }
        else { Array.Clear(AoLayer); Array.Clear(AoMask); }
        Commit(before);
    }
    public void MarkSaved() => HasChanges = false;

    public byte[] MapPixels(MaterialMapKind kind)
    {
        byte[] pixels = new byte[Source.Length];
        for (int q = 0; q < Heights.Length; q++)
        {
            int index = q * 4;
            pixels[index + 3] = Source[index + 3];
            if (kind == MaterialMapKind.Normal)
            {
                var normal = NormalAt(q);
                pixels[index + 2] = Byte(normal.X * .5f + .5f);
                pixels[index + 1] = Byte((DirectX ? -normal.Y : normal.Y) * .5f + .5f);
                pixels[index] = Byte(normal.Z * .5f + .5f);
            }
            else pixels[index] = pixels[index + 1] = pixels[index + 2] = Byte(kind == MaterialMapKind.Height ? Heights[q] : AoAt(q));
        }
        return pixels;
    }

    public byte[] LitPixels(int width, int height, Vector3 light, bool useNormal, bool useAO, bool unlit)
    {
        if (width <= 0 || height <= 0 || !float.IsFinite(light.LengthSquared()) || light.LengthSquared() < .000001f)
            throw new ArgumentOutOfRangeException(nameof(width));
        light = Vector3.Normalize(light);
        byte[] pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int q = Math.Min(Height - 1, (int)((long)y * Height / height)) * Width + Math.Min(Width - 1, (int)((long)x * Width / width));
                int index = (y * width + x) * 4;
                float ao = useAO ? AoAt(q) : 1;
                float diffuse = Math.Max(0, Vector3.Dot(useNormal ? NormalAt(q) : Vector3.UnitZ, light));
                // AO modulates the ambient term; direct illumination remains independent.
                float brightness = unlit ? 1 : .25f * ao + .85f * diffuse;
                for (int channel = 0; channel < 3; channel++)
                {
                    float srgb = Source[q * 4 + channel] / 255f;
                    float linear = srgb <= .04045f ? srgb / 12.92f : MathF.Pow((srgb + .055f) / 1.055f, 2.4f);
                    float lit = linear * brightness;
                    pixels[index + channel] = Byte(lit <= .0031308f ? lit * 12.92f : 1.055f * MathF.Pow(lit, 1 / 2.4f) - .055f);
                }
                pixels[index + 3] = Source[q * 4 + 3];
            }
        return pixels;
    }
    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
