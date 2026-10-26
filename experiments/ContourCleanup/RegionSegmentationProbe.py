"""Prototype: segment color-connected parts before assigning them to one output grid."""

from collections import deque
from pathlib import Path
import json

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT.parent / "contour-black-lines" / "experiments" / "ContourCleanup" / "artifacts" / "fimoz-user-samples" / "pipeline-smooth-2x4"
OUTPUT = ROOT / "experiments" / "ContourCleanup" / "artifacts" / "shape-regions"
INTERMEDIATE_SIZE = 314
OUTPUT_SIZE = 157


def connected_regions(colors: np.ndarray, visible: np.ndarray):
    height, width = colors.shape
    labels = np.full((height, width), -1, dtype=np.int32)
    sizes = []
    four = ((-1, 0), (1, 0), (0, -1), (0, 1))
    for y in range(height):
        for x in range(width):
            if labels[y, x] >= 0 or not visible[y, x]:
                continue
            label = len(sizes)
            value = colors[y, x]
            queue = deque([(x, y)])
            labels[y, x] = label
            size = 0
            while queue:
                px, py = queue.popleft()
                size += 1
                for dx, dy in four:
                    nx, ny = px + dx, py + dy
                    if (0 <= nx < width and 0 <= ny < height and
                            labels[ny, nx] < 0 and visible[ny, nx] and colors[ny, nx] == value):
                        labels[ny, nx] = label
                        queue.append((nx, ny))
            sizes.append(size)
    return labels, np.asarray(sizes, dtype=np.int32)


def process(source: Path):
    with Image.open(source) as opened:
        intermediate = opened.convert("RGBA").resize(
            (INTERMEDIATE_SIZE, INTERMEDIATE_SIZE), Image.Resampling.BOX)
    rgba = np.asarray(intermediate, dtype=np.uint8)
    visible = rgba[:, :, 3] >= 128
    rgb = Image.fromarray(rgba[:, :, :3], "RGB")
    palette_image = rgb.quantize(colors=128, method=Image.Quantize.MEDIANCUT)
    colors = np.asarray(palette_image, dtype=np.uint8)
    palette = np.asarray(palette_image.getpalette(), dtype=np.uint8).reshape(-1, 3)
    labels, sizes = connected_regions(colors, visible)
    output = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    ties = 0
    for y in range(OUTPUT_SIZE):
        for x in range(OUTPUT_SIZE):
            block = labels[y * 2:y * 2 + 2, x * 2:x * 2 + 2].ravel()
            choices, counts = np.unique(block[block >= 0], return_counts=True)
            if len(choices) == 0:
                continue
            highest = np.max(counts)
            winners = choices[counts == highest]
            if len(winners) > 1:
                ties += 1
            winner = max(winners, key=lambda label: (sizes[label], -int(label)))
            location = np.argwhere(labels[y * 2:y * 2 + 2, x * 2:x * 2 + 2] == winner)[0]
            chosen = colors[y * 2 + location[0], x * 2 + location[1]]
            output[y, x, :3] = palette[chosen]
            output[y, x, 3] = 255
    target = OUTPUT / f"{source.name.removesuffix('-smooth.png')}-regions.png"
    Image.fromarray(output, "RGBA").save(target)
    return {
        "file": source.name,
        "regions": len(sizes),
        "singlePixelRegions": int(np.count_nonzero(sizes == 1)),
        "tiedOutputCells": ties,
        "size": [OUTPUT_SIZE, OUTPUT_SIZE],
        "output": str(target),
    }


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    results = [process(source) for source in sorted(SOURCES.glob("T_*-smooth.png"))]
    if len(results) != 5:
        raise RuntimeError(f"Expected five FIMOZ inputs, got {len(results)}")
    (OUTPUT / "metrics.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
