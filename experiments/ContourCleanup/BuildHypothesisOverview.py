"""Build local contact sheets from the four isolated shape experiments."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT.parent
REVIEW = ROOT / "experiments" / "ContourCleanup" / "artifacts" / "shape-research" / "review"
NAMES = ("Assaultman", "Bowman", "Pistolman", "Rifleman", "StrongHand")
SOURCE = ROOT / "experiments" / "ContourCleanup" / "artifacts" / "shape-research" / "finish-cli"


def paths(name):
    stem = f"T_{name}_UNP"
    return {
        "Baseline": SOURCE / f"{stem}.png",
        "Gradient": ARTIFACTS / "shape-gradient" / "experiments" / "ContourCleanup" / "artifacts" / "shape-gradient" / f"{stem}-gradient-default.png",
        "Regions": ARTIFACTS / "shape-regions" / "experiments" / "ContourCleanup" / "artifacts" / "shape-regions" / f"{stem}-regions.png",
        "Graph": ARTIFACTS / "shape-region-graph" / "experiments" / "ContourCleanup" / "artifacts" / "shape-region-graph" / f"{stem}-graph.png",
        "Feedback conservative": ARTIFACTS / "shape-iterative-feedback" / "experiments" / "ContourCleanup" / "artifacts" / "shape-iterative-feedback" / f"{stem}-conservative.png",
        "Feedback aggressive": ARTIFACTS / "shape-iterative-feedback" / "experiments" / "ContourCleanup" / "artifacts" / "shape-iterative-feedback" / f"{stem}-aggressive.png",
    }


def sheet(columns, target):
    width = len(columns) * 320
    height = len(NAMES) * 340
    canvas = Image.new("RGB", (width, height), "#202020")
    draw = ImageDraw.Draw(canvas)
    for row, name in enumerate(NAMES):
        available = paths(name)
        for column, label in enumerate(columns):
            path = available[label]
            with Image.open(path) as opened:
                sprite = opened.convert("RGBA").resize((314, 314), Image.Resampling.NEAREST)
            canvas.paste(sprite, (column * 320 + 3, row * 340 + 22), sprite)
            draw.text((column * 320 + 3, row * 340 + 4), f"{name}: {label}", fill="white")
    canvas.save(target)


def main():
    REVIEW.mkdir(parents=True, exist_ok=True)
    sheet(("Baseline", "Gradient", "Regions", "Graph"), REVIEW / "hypotheses-regions.png")
    sheet(("Baseline", "Feedback conservative", "Feedback aggressive"), REVIEW / "hypotheses-iterations.png")


if __name__ == "__main__":
    main()
