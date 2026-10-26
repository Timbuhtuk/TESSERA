"""Prototype: four compress/expand overlays against one unchanged original."""

from pathlib import Path
import json
import math

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SHARED = ROOT.parent / "contour-black-lines" / "experiments" / "ContourCleanup" / "artifacts"
SOURCES = SHARED / "fimoz-user-samples" / "pipeline-smooth-2x4"
BASELINE = SHARED / "shape-research" / "finish-cli"
OUTPUT = ROOT / "experiments" / "ContourCleanup" / "artifacts" / "shape-iterative-feedback"
SCALES = (2, 4, 8, 16)
OUTPUT_SIZE = 157


def nearest_palette(colors, palette):
    difference = colors[:, None, :].astype(np.int16) - palette[None, :, :].astype(np.int16)
    distance = np.sum(difference.astype(np.int32) ** 2, axis=2)
    return palette[np.argmin(distance, axis=1)]


def process(source):
    stem = source.name.removesuffix("-smooth.png")
    with Image.open(source) as opened:
        original = opened.convert("RGBA")
    with Image.open(BASELINE / f"{stem}.png") as opened:
        baseline = np.asarray(opened.convert("RGBA"), dtype=np.uint8)
    original_pixels = np.asarray(original, dtype=np.int32)
    alternatives = []
    overlay_errors = []

    for scale in SCALES:
        small_size = (math.ceil(original.width / scale), math.ceil(original.height / scale))
        compact = original.resize(small_size, Image.Resampling.BOX)
        expanded = compact.resize(original.size, Image.Resampling.NEAREST)
        overlay = np.asarray(expanded, dtype=np.int32)
        color_error = np.mean((overlay[:, :, :3] - original_pixels[:, :, :3]) ** 2, axis=2)
        alpha_error = (overlay[:, :, 3] - original_pixels[:, :, 3]) ** 2
        error = color_error + alpha_error
        overlay_errors.append(np.asarray(Image.fromarray(error.astype(np.float32), "F").resize(
            (OUTPUT_SIZE, OUTPUT_SIZE), Image.Resampling.BOX)))
        reduced = compact.resize((OUTPUT_SIZE, OUTPUT_SIZE),
                                 Image.Resampling.NEAREST if scale == 16 else Image.Resampling.BOX)
        alternatives.append(np.asarray(reduced, dtype=np.uint8))

    candidates = np.stack(alternatives, axis=0)
    errors = np.stack(overlay_errors, axis=0)
    median = np.median(candidates[:, :, :, :3], axis=0)
    spread = np.max(np.linalg.norm(candidates[:, :, :, :3].astype(np.float32) - median, axis=3), axis=0)
    alpha_agreement = np.max(candidates[:, :, :, 3], axis=0) - np.min(candidates[:, :, :, 3], axis=0) <= 16
    visible = baseline[:, :, 3] > 0
    stable = (spread <= 28) & alpha_agreement & visible

    baseline_expanded = Image.fromarray(baseline, "RGBA").resize(original.size, Image.Resampling.NEAREST)
    base_pixels = np.asarray(baseline_expanded, dtype=np.int32)
    base_error = np.mean((base_pixels[:, :, :3] - original_pixels[:, :, :3]) ** 2, axis=2)
    base_error += (base_pixels[:, :, 3] - original_pixels[:, :, 3]) ** 2
    base_cell_error = np.asarray(Image.fromarray(base_error.astype(np.float32), "F").resize(
        (OUTPUT_SIZE, OUTPUT_SIZE), Image.Resampling.BOX))

    best_scale = np.argmin(errors, axis=0)
    best_error = np.take_along_axis(errors, best_scale[None, :, :], axis=0)[0]
    agreement = np.count_nonzero(
        np.linalg.norm(candidates[:, :, :, :3].astype(np.float32) - median, axis=3) <= 28,
        axis=0)
    conservative = stable & (best_error < base_cell_error * 0.9)
    aggressive = (agreement >= 3) & (best_error < base_cell_error) & visible

    palette = np.unique(baseline[baseline[:, :, 3] > 0, :3].reshape(-1, 3), axis=0)
    results = {}
    for name, mask in (("conservative", conservative), ("aggressive", aggressive)):
        changed = baseline.copy()
        chosen = nearest_palette(median[mask], palette)
        changed[mask, :3] = chosen
        target = OUTPUT / f"{stem}-{name}.png"
        Image.fromarray(changed, "RGBA").save(target)
        results[name] = {
            "eligibleCells": int(np.count_nonzero(mask)),
            "changedPixels": int(np.count_nonzero(np.any(changed != baseline, axis=2))),
            "output": str(target),
        }

    heat = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    heat[:, :, 0] = np.clip(spread * 4, 0, 255).astype(np.uint8)
    heat[:, :, 1] = np.where(stable, 220, 20).astype(np.uint8)
    heat[:, :, 3] = baseline[:, :, 3]
    Image.fromarray(heat, "RGBA").save(OUTPUT / f"{stem}-agreement.png")
    return {"file": source.name, "scales": list(SCALES), "stableVisibleCells": int(np.count_nonzero(stable)),
            "uncertainVisibleCells": int(np.count_nonzero(visible & ~stable)),
            "meanOverlayErrorByScale": [round(float(np.mean(e)), 2) for e in errors],
            "results": results}


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    results = [process(source) for source in sorted(SOURCES.glob("T_*-smooth.png"))]
    if len(results) != 5:
        raise RuntimeError(f"Expected five FIMOZ inputs, got {len(results)}")
    (OUTPUT / "metrics.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(results, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
