"""Refresh the skill's CLI reference and package the local ChatGPT plugin and skill."""

import argparse
import json
from pathlib import Path
import zipfile


root = Path(__file__).resolve().parents[1]
plugin = root / "plugins" / "tessera"
skill = plugin / "skills" / "tessera"
reference = (root / "docs" / "CLI.md").read_text(encoding="utf-8-sig")
reference = reference.replace(
    "../PixelArtAlignment/README.md", "https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtAlignment/README.md"
).replace(
    "../PixelArtDownscale/ICO.md", "https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtDownscale/ICO.md"
).replace(
    "../PixelArtAseprite/README.md", "https://github.com/Timbuhtuk/TESSERA/blob/main/PixelArtAseprite/README.md"
)
(skill / "references" / "cli.md").write_text(reference, encoding="utf-8")
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output-directory", type=Path, default=root / "artifacts" / "chatgpt-plugin")
args = parser.parse_args()
destination = args.output_directory.resolve()
destination.mkdir(parents=True, exist_ok=True)


def package(source, output, prefix):
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(source.rglob("*")):
            if path.is_file() and "__pycache__" not in path.parts and path.suffix != ".pyc":
                archive.write(path, (Path(prefix) / path.relative_to(source)).as_posix())
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None:
            raise RuntimeError(f"Archive verification failed: {output}")
    return str(output)


print(json.dumps({
    "plugin": package(plugin, destination / "Tessera-chatgpt-plugin.zip", "."),
    "skill": package(skill, destination / "Tessera-skill.zip", "tessera")
}, indent=2))
