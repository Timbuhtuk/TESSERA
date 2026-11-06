namespace Tessera.Models;

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
