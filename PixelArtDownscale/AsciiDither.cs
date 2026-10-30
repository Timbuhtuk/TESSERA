namespace PixelArtDownscale;

internal static class AsciiDither
{
    private static readonly int[] Bayer4 =
    [
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    ];

    public static double Adjust(double tone, int x, int y, double strength)
    {
        if (tone <= 0 || tone >= 1) return tone;
        double offset = ((Bayer4[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0 - 0.5) * strength;
        return Math.Clamp(tone + offset, 0, 1);
    }
}
