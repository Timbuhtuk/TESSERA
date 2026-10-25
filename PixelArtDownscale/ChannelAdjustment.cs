using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtDownscale;

public enum RgbaChannel { R, G, B, A }
public enum ChannelAdjustmentMode { Value, Percent }

public sealed class ChannelAdjustmentOptions
{
    public RgbaChannel Channel { get; init; }
    public ChannelAdjustmentMode Mode { get; init; }
    public double Amount { get; init; } = 10;
    public bool Subtract { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Channel)) throw new ArgumentOutOfRangeException(nameof(Channel));
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!double.IsFinite(Amount) || Amount < 0) throw new ArgumentOutOfRangeException(nameof(Amount));
    }
}

public static class ChannelAdjustment
{
    /// <summary>Changes one straight RGBA channel, including hidden RGB, without modifying the source.</summary>
    public static Bitmap Apply(Bitmap source, ChannelAdjustmentOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int shift = options.Channel switch { RgbaChannel.R => 16, RgbaChannel.G => 8, RgbaChannel.B => 0, _ => 24 };
        int mask = ~(255 << shift);
        var values = new int[256];
        for (int q = 0; q < values.Length; q++)
        {
            double change = options.Mode == ChannelAdjustmentMode.Percent ? q * (options.Amount / 100) : options.Amount;
            double next = q + (options.Subtract ? -change : change);
            values[q] = (int)Math.Round(Math.Clamp(next, 0, 255), MidpointRounding.AwayFromZero);
        }

        var result = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        try
        {
            var bits = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[result.Width];
                for (int y = 0; y < result.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IntPtr address = IntPtr.Add(bits.Scan0, y * bits.Stride);
                    Marshal.Copy(address, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++)
                    {
                        int current = (int)((uint)row[x] >> shift) & 255;
                        row[x] = (row[x] & mask) | (values[current] << shift);
                    }
                    Marshal.Copy(row, 0, address, row.Length);
                }
            }
            finally { result.UnlockBits(bits); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
