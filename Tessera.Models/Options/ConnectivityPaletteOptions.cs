namespace Tessera.Models;

public sealed class ConnectivityPaletteOptions
{
    public int TargetColors { get; init; } = 128;
    public double ConnectivityWeight { get; init; } = 0.7;
    public int TileSize { get; init; } = 32;
    public double LocalWeight { get; init; } = 0.4;
    public double DetailWeight { get; init; } = 0.5;
    public double ContextWeight { get; init; } = 0.3;
    public EPaletteDistanceSpace DistanceSpace { get; init; } = EPaletteDistanceSpace.Oklab;
}
