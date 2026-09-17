"""Independent test oracle for the supplied single-layer, compressed-RGBA Tamara fixtures.

Usage: python verify_tamara.py INPUT_DIRECTORY EXPORTED_VALIDATION_DIRECTORY
No Aseprite installation or third-party Python packages required.
"""
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path


def read_source(path):
    data = path.read_bytes()
    length, magic, count, width, height, depth = struct.unpack_from("<I5H", data)
    assert length == len(data) and magic == 0xA5E0 and depth == 32
    frames, durations = [], []
    position = 128
    for index in range(count):
        size, signature, old_count, duration, new_count = struct.unpack_from("<I3H2xI", data, position)
        assert signature == 0xF1FA
        end = position + size
        position += 16
        canvas = bytearray(width * height * 4)
        cel_count = 0
        for _ in range(new_count or old_count):
            chunk_size, kind = struct.unpack_from("<IH", data, position)
            chunk = data[position + 6:position + chunk_size]
            if kind == 0x2005:
                layer, x, y, opacity, cel_type, z_index = struct.unpack_from("<HhhBHh", chunk)
                assert (layer, opacity, cel_type, z_index) == (0, 255, 2, 0)
                cel_width, cel_height = struct.unpack_from("<HH", chunk, 16)
                inflater = zlib.decompressobj()
                rgba = inflater.decompress(chunk[20:]) + inflater.flush()
                assert inflater.eof and not inflater.unused_data and len(rgba) == cel_width * cel_height * 4
                cel_count += 1
                # Per-pixel copy is intentionally different from the C# row-copy implementation.
                for cy in range(cel_height):
                    for cx in range(cel_width):
                        if 0 <= x + cx < width and 0 <= y + cy < height:
                            src = (cy * cel_width + cx) * 4
                            dst = ((y + cy) * width + x + cx) * 4
                            canvas[dst:dst + 4] = rgba[src:src + 4]
            position += chunk_size
        assert position == end and cel_count <= 1
        frames.append(canvas)
        durations.append(duration or struct.unpack_from("<H", data, 18)[0])
    assert position == len(data)
    return width, height, frames, durations, hashlib.sha256(data).hexdigest()


def read_png(path):
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    position, compressed, kinds = 8, bytearray(), []
    width = height = 0
    while position < len(data):
        size, = struct.unpack_from(">I", data, position)
        kind = data[position + 4:position + 8]
        payload = data[position + 8:position + 8 + size]
        expected_crc, = struct.unpack_from(">I", data, position + 8 + size)
        assert zlib.crc32(kind + payload) == expected_crc
        kinds.append(kind)
        if kind == b"IHDR":
            width, height, *format_fields = struct.unpack(">II5B", payload)
            assert format_fields == [8, 6, 0, 0, 0]
        elif kind == b"IDAT":
            compressed.extend(payload)
        position += size + 12
    assert kinds[0] == b"IHDR" and kinds[-1] == b"IEND" and position == len(data)
    inflater = zlib.decompressobj()
    rows = inflater.decompress(compressed) + inflater.flush()
    assert inflater.eof and not inflater.unused_data and len(rows) == height * (width * 4 + 1)
    pixels = bytearray()
    for y in range(height):
        start = y * (width * 4 + 1)
        assert rows[start] == 0
        pixels.extend(rows[start + 1:start + width * 4 + 1])
    return width, height, pixels


inputs, outputs = map(Path, sys.argv[1:])
total_frames = total_sheets = 0
for path in sorted(inputs.glob("Tamara*.aseprite")):
    width, height, frames, durations, sha = read_source(path)
    for layout, columns, padding in [("horizontal", len(frames), 0), ("grid", min(4, len(frames)), 2)]:
        rows = (len(frames) + columns - 1) // columns
        sw, sh, actual = read_png(outputs / layout / (path.stem + ".png"))
        assert sw == width * columns + padding * (columns - 1)
        assert sh == height * rows + padding * (rows - 1)
        expected = bytearray(sw * sh * 4)
        expected_metadata = []
        for index, frame in enumerate(frames):
            x, y = index % columns * (width + padding), index // columns * (height + padding)
            for row in range(height):
                target = ((y + row) * sw + x) * 4
                expected[target:target + width * 4] = frame[row * width * 4:(row + 1) * width * 4]
            expected_metadata.append(dict(index=index, x=x, y=y, w=width, h=height, durationMs=durations[index]))
        assert actual == expected, (path.name, layout, "RGBA differs")
        metadata = json.loads((outputs / layout / (path.stem + ".json")).read_text(encoding="utf-8"))
        assert metadata["frames"] == expected_metadata
        assert metadata["sourceSha256"] == sha and metadata["totalDurationMs"] == sum(durations)
        assert metadata["source"] == path.name and metadata["image"] == path.stem + ".png"
        assert metadata["frameCount"] == len(frames) and metadata["schema"] == "aseprite-offline/v1"
        assert metadata["columns"] == columns and metadata["rows"] == rows and metadata["padding"] == padding
        assert metadata["frameSize"] == dict(w=width, h=height) and metadata["sheetSize"] == dict(w=sw, h=sh)
        assert metadata["layout"] == layout and metadata["borderPadding"] == 0 and metadata["trimmed"] is False
        assert metadata["tags"] == []
        total_sheets += 1
    print(f"PASS independent RGBA + metadata: {path.stem}, {len(frames)} frames")
    total_frames += len(frames)
assert total_frames == 83 and total_sheets == 20
print(f"PASS {total_frames} source frames, {total_sheets} PNG+JSON exports")
