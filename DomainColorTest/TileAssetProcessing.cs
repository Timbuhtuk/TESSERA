using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PixelArtDownscale;

namespace DomainColorTest;

public static class TileAssetProcessing
{
    public static byte[] Smooth(byte[] pixels, int width, int height, int passes, bool edgesOnly, int band)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4) || band < 1)
            throw new ArgumentException("Некорректный размер тайла или краевой полосы.");
        using var source = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bounds = new Rectangle(0, 0, width, height);
        var sourceBits = source.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int q = 0; q < height; q++)
                Marshal.Copy(pixels, q * width * 4, IntPtr.Add(sourceBits.Scan0, q * sourceBits.Stride), width * 4);
        }
        finally { source.UnlockBits(sourceBits); }
        using var result = IndependentImageProcessor.ApplyNeighborColors(source, new DownscaleOptions { LocalColorPasses = passes });
        var output = new byte[pixels.Length];
        var resultBits = result.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int q = 0; q < height; q++)
                Marshal.Copy(IntPtr.Add(resultBits.Scan0, q * resultBits.Stride), output, q * width * 4, width * 4);
        }
        finally { result.UnlockBits(resultBits); }
        if (edgesOnly && width > band * 2)
            for (int y = band; y < height - band; y++)
                Buffer.BlockCopy(pixels, (y * width + band) * 4, output, (y * width + band) * 4, (width - band * 2) * 4);
        return output;
    }
}
