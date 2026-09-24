<img src="DomainColorTest/Assets/tessera-mark.svg" width="88" height="88" alt="Tessera mosaic T">

# Tessera

**Turn uneven pixels into usable pixel art.**

Tessera is a standalone Windows app for preparing pixel art and icons. It began with a specific problem: AI-generated pixel art often looks pixelated, but its grid is uneven and its “pixels” have different sizes. Tessera helps align those shapes to a consistent grid so you can keep working with the image.

The name comes from a *tessera*, an individual piece of a mosaic.

Today, Tessera brings the rest of that workflow together:

- Align a pixel grid and reduce each cell to a single pixel.
- Enlarge pixels without blur, reduce image size and adjust colors independently, or combine size and palette processing.
- Smooth local color noise using neighboring pixels, inspect image palettes and replace individual colors while keeping transparency.
- Keep source images and a history of results in a local library.
- Compare previews with crisp zoom, synchronized scrolling and transparency backgrounds.
- Create Windows ICO files containing several icon sizes.
- Remove a solid background in its own workspace and save a transparent PNG, or apply it while creating an ICO. Adjust tolerance and remove matching colors globally or only from edge-connected regions.
- Preserve transparency and export one result or all results for an image.
- Drop several Aseprite animations to create PNG sprite sheets with JSON frame timings, then save individual results or the whole batch.

Everything runs locally, without an internet connection. Download, unpack and launch.

When online, Tessera can check GitHub for newer releases and install an update after confirmation. Your image library stays in place.

[**Download Tessera for Windows x64**](https://github.com/Timbuhtuk/TESSERA/releases/latest)
