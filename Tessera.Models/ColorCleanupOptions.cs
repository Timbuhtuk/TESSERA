namespace Tessera.Models;

public sealed class ColorCleanupOptions
{
    public double ColorTolerance { get; init; } = 12;
    public double Strength { get; init; } = .65;
    public double DetailProtection { get; init; } = .8;
    public double EdgeProtection { get; init; } = .8;
}
