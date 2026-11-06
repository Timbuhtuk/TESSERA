namespace Tessera.Models;

public sealed class ChannelAdjustmentOptions
{
    public ERgbaChannel Channel { get; init; }
    public EChannelAdjustmentMode Mode { get; init; }
    public double Amount { get; init; } = 10;
    public bool Subtract { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Channel)) throw new ArgumentOutOfRangeException(nameof(Channel));
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!double.IsFinite(Amount) || Amount < 0) throw new ArgumentOutOfRangeException(nameof(Amount));
    }
}
