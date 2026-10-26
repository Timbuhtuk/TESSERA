"""Prototype: sparse control points, color-constrained growth, region graph, shared output grid."""

from collections import deque, defaultdict
from pathlib import Path
from heapq import heappush, heappop
import json

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT.parent / "contour-black-lines" / "experiments" / "ContourCleanup" / "artifacts" / "fimoz-user-samples" / "pipeline-smooth-2x4"
OUTPUT = ROOT / "experiments" / "ContourCleanup" / "artifacts" / "shape-region-graph"
INTERMEDIATE_SIZE = 314
OUTPUT_SIZE = 157
SEED_STEP = 5
SEED_DISTANCE = 18.0
LOCAL_DISTANCE = 12.0


def rgb_to_lab(rgb):
    value = rgb.astype(np.float32) / 255.0
    value = np.where(value <= 0.04045, value / 12.92, ((value + 0.055) / 1.055) ** 2.4)
    matrix = np.array([[0.4124564, 0.3575761, 0.1804375],
                       [0.2126729, 0.7151522, 0.0721750],
                       [0.0193339, 0.1191920, 0.9503041]], dtype=np.float32)
    xyz = value @ matrix.T / np.array([0.95047, 1.0, 1.08883], dtype=np.float32)
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack((116 * f[:, :, 1] - 16, 500 * (f[:, :, 0] - f[:, :, 1]),
                     200 * (f[:, :, 1] - f[:, :, 2])), axis=-1)


def grow_regions(lab, visible):
    height, width = visible.shape
    labels = np.full((height, width), -1, dtype=np.int32)
    seeds = []
    queue = []
    for y in range(SEED_STEP // 2, height, SEED_STEP):
        for x in range(SEED_STEP // 2, width, SEED_STEP):
            if not visible[y, x]:
                continue
            label = len(seeds)
            seeds.append(lab[y, x].copy())
            heappush(queue, (0.0, label, x, y))

    four = ((-1, 0), (1, 0), (0, -1), (0, 1))
    while queue:
        cost, label, x, y = heappop(queue)
        if labels[y, x] >= 0:
            continue
        labels[y, x] = label
        for dx, dy in four:
            nx, ny = x + dx, y + dy
            if not (0 <= nx < width and 0 <= ny < height and visible[ny, nx] and labels[ny, nx] < 0):
                continue
            local = float(np.linalg.norm(lab[ny, nx] - lab[y, x]))
            seed = float(np.linalg.norm(lab[ny, nx] - seeds[label]))
            if local <= LOCAL_DISTANCE and seed <= SEED_DISTANCE:
                heappush(queue, (max(cost, seed) + 0.01, label, nx, ny))

    grid_seeds = len(seeds)
    fallback = 0
    for y in range(height):
        for x in range(width):
            if not visible[y, x] or labels[y, x] >= 0:
                continue
            label = len(seeds)
            seeds.append(lab[y, x].copy())
            fallback += 1
            labels[y, x] = label
            pending = deque([(x, y)])
            while pending:
                px, py = pending.popleft()
                for dx, dy in four:
                    nx, ny = px + dx, py + dy
                    if not (0 <= nx < width and 0 <= ny < height and visible[ny, nx] and labels[ny, nx] < 0):
                        continue
                    if (np.linalg.norm(lab[ny, nx] - lab[py, px]) <= LOCAL_DISTANCE and
                            np.linalg.norm(lab[ny, nx] - seeds[label]) <= SEED_DISTANCE):
                        labels[ny, nx] = label
                        pending.append((nx, ny))
    return labels, grid_seeds, fallback


def region_graph(labels, lab):
    count = int(labels.max()) + 1
    flat = labels.ravel()
    valid = flat >= 0
    sizes = np.bincount(flat[valid], minlength=count)
    means = np.zeros((count, 3), dtype=np.float64)
    for channel in range(3):
        means[:, channel] = np.bincount(flat[valid], weights=lab[:, :, channel].ravel()[valid], minlength=count)
    means /= np.maximum(sizes[:, None], 1)
    adjacency = defaultdict(lambda: defaultdict(int))
    for a, b in ((labels[:, :-1], labels[:, 1:]), (labels[:-1, :], labels[1:, :])):
        mask = (a >= 0) & (b >= 0) & (a != b)
        for first, second in zip(a[mask], b[mask]):
            adjacency[int(first)][int(second)] += 1
            adjacency[int(second)][int(first)] += 1
    return sizes, means, adjacency


def merge_small(labels, sizes, means, adjacency):
    replacement = np.arange(len(sizes), dtype=np.int32)
    merged = 0
    for label in range(len(sizes)):
        if sizes[label] > 4 or not adjacency[label]:
            continue
        candidates = [(float(np.linalg.norm(means[label] - means[neighbor])), -border, neighbor)
                      for neighbor, border in adjacency[label].items() if sizes[neighbor] > sizes[label]]
        if not candidates:
            continue
        distance, _, target = min(candidates)
        if distance <= 20:
            replacement[label] = target
            merged += 1
    valid = labels >= 0
    labels[valid] = replacement[labels[valid]]
    return merged


def process(source):
    with Image.open(source) as opened:
        intermediate = opened.convert("RGBA").resize(
            (INTERMEDIATE_SIZE, INTERMEDIATE_SIZE), Image.Resampling.BOX)
    rgba = np.asarray(intermediate, dtype=np.uint8)
    visible = rgba[:, :, 3] >= 128
    lab = rgb_to_lab(rgba[:, :, :3])
    labels, grid_seeds, fallback = grow_regions(lab, visible)
    sizes, means, adjacency = region_graph(labels, lab)
    graph_edges = sum(len(neighbors) for neighbors in adjacency.values()) // 2
    merged = merge_small(labels, sizes, means, adjacency)
    sizes = np.bincount(labels[labels >= 0], minlength=len(sizes))

    output = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    ties = 0
    for y in range(OUTPUT_SIZE):
        for x in range(OUTPUT_SIZE):
            block = labels[y * 2:y * 2 + 2, x * 2:x * 2 + 2]
            choices, counts = np.unique(block[block >= 0], return_counts=True)
            if len(choices) == 0:
                continue
            winners = choices[counts == counts.max()]
            if len(winners) > 1:
                ties += 1
            winner = max(winners, key=lambda label: (sizes[label], -int(label)))
            yy, xx = np.argwhere(block == winner)[0]
            output[y, x] = rgba[y * 2 + yy, x * 2 + xx]

    quantized = Image.fromarray(output[:, :, :3], "RGB").quantize(
        colors=128, method=Image.Quantize.MEDIANCUT).convert("RGB")
    output[:, :, :3] = np.asarray(quantized)
    image = Image.fromarray(output, "RGBA")
    target = OUTPUT / f"{source.name.removesuffix('-smooth.png')}-graph.png"
    image.save(target)
    return {"file": source.name, "gridSeeds": grid_seeds, "fallbackSeeds": fallback,
            "graphEdges": graph_edges, "mergedRegions": merged, "tiedOutputCells": ties,
            "size": [OUTPUT_SIZE, OUTPUT_SIZE], "output": str(target)}


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    results = [process(source) for source in sorted(SOURCES.glob("T_*-smooth.png"))]
    if len(results) != 5:
        raise RuntimeError(f"Expected five FIMOZ inputs, got {len(results)}")
    (OUTPUT / "metrics.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
