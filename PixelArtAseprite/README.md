# Aseprite → PNG sprite sheets

`PixelArtAseprite` is the offline conversion core for Tessera. It targets **.NET 8**, matching the existing solution, and has no NuGet, System.Drawing, Aseprite executable, browser or network dependency. GUI and CLI project references are already connected. The desktop app includes a dedicated batch workspace; the CLI integration is separate.

## Supported subset

- `.ase` / `.aseprite`, RGBA32, exactly one visible ordinary layer declared in the first frame; Normal blending, effective layer and cel opacity 255, z-index 0.
- Raw, zlib-compressed and linked cels, including forward references and long linked chains. Each linked cel retains its own position.
- Every pixel alpha value, including partial transparency and hidden RGB at alpha zero; no premultiplication, quantization or resizing.
- Negative offsets, partial/complete off-canvas cels and empty frames. Clipping is against each original canvas, so pixels cannot spill into adjacent sheet cells.
- Per-frame duration with the legacy header speed as fallback. A zero duration with no fallback is an error.
- No profile, profile type 0 or ordinary sRGB. PNG receives an sRGB chunk only for an explicitly sRGB source.
- UTF-8 layer/tag names; inclusive tag frame ranges, forward/reverse/pingpong/pingpong_reverse directions and the unmodified repeat count. Tags do not reorder the sheet or add repetitions.
- Horizontal, vertical or grid layouts, configurable grid columns and transparent gaps. Canvas margins stay intact, no trimming or outside border. Unused cells remain transparent.

Multiple/hidden/group/background/reference/tilemap layers, Indexed/Grayscale, partial layer/cel opacity, nonzero z-index, non-square pixels, ICC/special gamma, Cel Extra, slices, external files, tilesets and unknown chunks are explicitly rejected. Known RGBA palette and User Data chunks are skipped and recorded in `Warnings`. This is an intentionally restricted decoder, not a general Aseprite renderer.

## File export and batching

```csharp
using PixelArtAseprite;

var options = new AsepriteExportOptions
{
    Layout = SpriteSheetLayout.Grid,
    Columns = 4,
    Padding = 2,
    IncludeInspection = true
};

var result = AsepriteConverter.Convert("TamaraJump.aseprite", "export", options);
// TamaraJump.png, TamaraJump.json, TamaraJump.inspection.json

var results = AsepriteConverter.ConvertBatch(
    Directory.EnumerateFiles("sprites", "*.aseprite"), "export", options);
bool failed = results.Any(item => !item.Success);
// CLI should return a nonzero status if failed; individual item.Error explains each failure.
```

Default layout is horizontal, with no gap and no inspection file. Grid defaults to four columns; column count is clamped to frame count. Export requires a new output name: **existing results are never overwritten**, including on collisions between different input files with the same stem. Input documents stay unchanged. Each batch item produces its own PNG/JSON; there is no combined multi-document atlas.

All outputs are staged in unique temporary files next to their destination and moved with overwrite disabled. If publishing part of a set fails, the exporter removes only outputs it created and its own temporary files. Cleanup failure is reported rather than hidden. Two or three filesystem moves are not an atomic transaction across process crashes/power loss; callers that require that guarantee should publish a whole result directory or a ZIP separately.

Batch processing is sequential and continues after individual read/format/write failures. Cancellation propagates as `OperationCanceledException` and stops the batch. The caller chooses the batch inputs; a directory is not implicitly scanned recursively.

## Preview and in-memory integration

```csharp
var document = AsepriteReader.Read("TamaraJump.aseprite");
RgbaImage firstFrame = document.RenderFrame(0);
SpriteSheet sheet = SpriteSheet.Create(document, options);
ReadOnlyMemory<byte> rgba = sheet.Image.Rgba; // R,G,B,A; top-to-bottom, straight alpha

using var png = File.Create("preview.png");
PngWriter.Write(png, sheet.Image, document.IsSrgb);
```

The library exposes `Width`, `Height`, `Frames`, `Tags`, `LayerName`, `IsSrgb`, `SourceSha256`, `Warnings` and `TotalDurationMs` on the document. `RenderFrame` returns a new full-canvas image. `SpriteSheet.Metadata.Frames` contains sheet coordinates and timings; cel offsets and sheet coordinates are separate.

For WPF, run conversion on a worker task and convert **a copy for display** from RGBA to the format required by the UI. Export always uses the original straight RGBA buffer, never the rendered preview. Managed result buffers need no `Dispose`; each caller-owned stream remains the caller's responsibility. Public operations take a `CancellationToken`; cancellation is checked while reading, decoding, traversing links, copying rows, writing PNG and before publishing each file.

`AsepriteReader.Read` also accepts `ReadOnlyMemory<byte>` with an optional source filename. Do not mutate that memory during parsing. Format/unsupported-feature errors use `InvalidDataException` with filename, frame and chunk context where available. Invalid options use `ArgumentException` / `ArgumentOutOfRangeException`. File errors retain their normal .NET exception types.

## JSON contract

Schema is **`aseprite-offline/v1`**, the custom schema described in the supplied implementation document, not the official Aseprite CLI JSON format. Properties use camelCase:

`schema`, `image`, `source`, `sourceSha256`, `sheetSize {w,h}`, `frameSize {w,h}`, `layout`, `columns`, `rows`, `padding`, `borderPadding: 0`, `trimmed: false`, `frameCount`, `totalDurationMs`, `tags`, `frames`.

Each frame has `{index,x,y,w,h,durationMs}`. Each tag has `{name,from,to,direction,repeat}`. The SHA-256 is lowercase hex of the original input. Metadata contains filenames, not absolute local paths. `totalDurationMs` is one linear traversal; repeat zero is preserved without inventing playback semantics. Optional `aseprite-inspection/v1` JSON includes profile, cel types/offsets/links, warnings and document details. Its technical details are intended for diagnostics, not required user-facing controls.

## Limits and strict decoding

Default `AsepriteLimits`: input 64 MiB; full-frame pixel total 16,777,216; sheet area 16,777,216 pixels; unique cel data 128 MiB; sheet side 32,768; working-memory budget 256 MiB. These are application limits, not format limits. Individual maxima cannot necessarily be used simultaneously. Geometry, cel buffers, frame metadata, strings/tags and conservative decoder/stream reserves are checked before relevant allocations. Already returned images retained by the calling application are outside this per-operation budget. PNG compression is streamed through bounded 64 KiB IDAT chunks.

The reader bounds each frame/chunk, validates exact lengths, strict UTF-8, supported flags and linked targets/cycles. `StrictZlib` checks stored/fixed/dynamic DEFLATE blocks, Huffman tables, output length, back-reference windows, the final block, exact compressed boundary and Adler-32; `ZLibStream` produces the actual bytes. Truncated streams, missing checksums, appended data and concatenated streams are rejected even when some or all requested pixel bytes can be produced.

## Validation

```powershell
dotnet build Pixelizator.sln -c Release
dotnet run --project PixelArtAseprite.Tests -c Release --no-build
dotnet run --project PixelArtAseprite.Tests -c Release --no-build -- --samples 'C:\path\to\Tamara'
python PixelArtAseprite.Tests/verify_tamara.py 'C:\path\to\Tamara' 'artifacts\aseprite\validation-...'
```

Synthetic checks run as part of `Build-Standalone.ps1` and the existing CI path. They cover raw/zlib parity, all 256 alpha values, hidden RGB, offsets/clipping, blank frames, forward/long links, all layouts, partial rows/gaps, duration fallback, tags/UUID/sRGB, PNG CRC/pixel roundtrip, malformed input, strict zlib failures, limits, cancellation, output conflicts and continuing batches.

On 2026-09-17 all ten supplied Tamara documents (83 frames) were exported horizontally and into four-column grids with 2 px gaps. All 20 PNG+JSON pairs were independently checked using Python's `struct`, `zlib`, `hashlib` and a separate per-pixel canvas reconstruction. Full RGBA and metadata matched; Death retained alpha 0/150/181/255 and Hurt retained 0/181/206/216/255. The earlier JS archive and official Aseprite-rendered references were not available; no equivalence to that renderer beyond the supported subset is claimed.

## Format references

- [Aseprite file specification](https://raw.githubusercontent.com/aseprite/aseprite/main/docs/ase-file-specs.md)
- [RFC 1950: zlib](https://www.rfc-editor.org/rfc/rfc1950)
- [RFC 1951: DEFLATE](https://www.rfc-editor.org/rfc/rfc1951)
- [PNG specification](https://www.w3.org/TR/png-3/)

The supplied `Aseprite_to_PNG_CSharp_Algorithm_RU.txt` defines the intended restricted scope and metadata schema. The implementation uses .NET 8 for compatibility with this repository rather than changing the application's runtime.

## Desktop workspace

The centred animation banner between the image and icon banners opens the workspace. Opening or dropping multiple .ase/.aseprite files queues automatic sequential conversion. Further drops join the current queue. Each item has a ready sprite-sheet preview or its own error; one failed file does not stop others. Layout, columns and transparent padding rebuild results automatically. Save Selected and Save All export PNG/JSON pairs; colliding names receive numeric suffixes and the JSON image reference is updated. Output PNG bytes are copied from the ready result, preserving straight RGBA. Imported source snapshots and converted results are kept in a private temporary session folder, so changing or removing the original file does not change an already imported result. Clearing the list removes session data only, never source or exported files. The session is not a persistent library; save results before closing. Cancellation keeps completed entries. GUI checks cover multi-drop, reconfiguration, file conflicts, cancellation and narrow layout.
