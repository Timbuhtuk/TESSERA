namespace Tessera.Models;

public sealed record ConnectivityPaletteResult(Bitmap Image, int ColorsBefore, int ColorsAfter,
    int ComponentsBefore, int RemovedColors, int ChangedPixels);
