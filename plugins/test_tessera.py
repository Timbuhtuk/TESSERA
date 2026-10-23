"""Integration checks against the real CLI; outputs stay under artifacts."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import uuid
import zipfile
import zlib


def png(path):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    rows = bytearray()
    for y in range(16):
        rows.append(0)
        for x in range(16):
            rows.extend((255, 255, 255, 255) if x < 4 or y < 4 or x >= 12 or y >= 12
                        else (240 if x < 8 else 20, 40, 220 if y < 8 else 20, 255))
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def aseprite(path):
    name = b"Layer"
    layer = struct.pack("<HHHHHHB3sH", 3, 0, 0, 0, 0, 0, 255, bytes(3), len(name)) + name
    cel = struct.pack("<HhhBHh5sHH", 0, 0, 0, 255, 0, 0, bytes(5), 2, 2)
    cel += bytes((255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0))
    chunks = struct.pack("<IH", 6 + len(layer), 0x2004) + layer
    chunks += struct.pack("<IH", 6 + len(cel), 0x2005) + cel
    frame = struct.pack("<IHHHHI", 16 + len(chunks), 0xF1FA, 2, 100, 0, 0) + chunks
    header = bytearray(128)
    struct.pack_into("<IHHHHHIH", header, 0, 128 + len(frame), 0xA5E0, 1, 2, 2, 32, 1, 100)
    header[34:36] = b"\x01\x01"
    path.write_bytes(header + frame)


def dimensions(path):
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    return struct.unpack(">II", data[16:24])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", required=True, type=Path)
    parser.add_argument("--gui", type=Path, help="Optional desktop EXE to verify it is rejected without launching")
    parser.add_argument("--packages", type=Path, help="Directory containing the plugin and skill release ZIPs")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    launcher = root / "plugins" / "tessera" / "skills" / "tessera" / "scripts" / "tessera.py"
    directory = root / "artifacts" / "chatgpt-plugin" / ("checks-" + uuid.uuid4().hex)
    directory.mkdir(parents=True)
    executable = args.cli.resolve()
    settings = directory / "settings.json"
    environment = os.environ.copy()
    environment.pop("TESSERA_CLI", None)
    base = [sys.executable, str(launcher), "--config", str(settings)]
    passed = []

    def process(command, expected=0, cwd=None, env=None):
        result = subprocess.run(command, cwd=cwd or directory, env=env or environment,
                                capture_output=True, timeout=60)
        assert result.returncode == expected, (command, result.returncode, result.stderr.decode("utf-8"))
        return result

    def run(*arguments, expected=0):
        return process([*base, "--cli", str(executable), "--", *map(str, arguments)], expected)

    def check(label):
        passed.append(label)
        print("PASS " + label, flush=True)

    # Byte-for-byte forwarding covers every canonical command and alias.
    for command in (None, "align", "reduce-grid", "compact-grid", "resize", "colors",
                    "smooth-colors", "smooth", "monochrome", "mono", "ascii", "palette-info",
                    "replace-color", "remove-background", "remove-bg", "ico", "icon", "aseprite"):
        arguments = [command, "--help"] if command else ["--help"]
        direct = process([str(executable), *arguments])
        wrapped = run(*arguments)
        assert (direct.stdout, direct.stderr) == (wrapped.stdout, wrapped.stderr)
    check("help output preserved for all 18 commands and aliases")

    process([*base, "--configure", str(executable)])
    located = json.loads(process([*base, "--locate"]).stdout)
    assert Path(located["executable"]) == executable
    assert process([*base, "--", "--version"]).stdout == process([str(executable), "--version"]).stdout
    check("saved executable works from a different working directory")

    located = json.loads(process([*base, "--config", str(directory / "unused.json"), "--locate"], cwd=root).stdout)
    assert located["source"] == "discovery"
    configured_env = {**environment, "TESSERA_CLI": str(executable)}
    assert json.loads(process([*base, "--locate"], env=configured_env).stdout)["source"] == "TESSERA_CLI"
    check("repository discovery and environment override work")

    source = directory / "пиксели ; & $() ' вход.png"
    png(source)
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    output = directory / "результат ; &.png"
    crop = directory / "crop.png"
    preview = directory / "preview.png"
    report = json.loads(run(source, "-o", output, "--width=4", "--height", "4", "--palette", "step",
                            "--palette-step", "32", "--quantization", "median-cut", "--quantization-colors", "8",
                            "--color-weights", "--crop-horizontal", "right", "--crop-vertical", "bottom",
                            "--block-mode", "manual", "--brightness", "60", "--contrast", "70", "--saturation", "80",
                            "--edge", "90", "--threads", "1", "--cropped-output", crop,
                            "--preview-output", preview, "--preview-scale", "3", "--json").stdout)
    assert Path(report["output"]) == output and dimensions(output) == (4, 4)
    assert dimensions(crop) == (16, 16) and dimensions(preview) == (12, 12)
    check("downscale with full parameters, JSON, Unicode, spaces and shell metacharacters")

    resized = directory / "resize.png"
    process([*base, "--cwd", str(directory), "--", "resize", source.name, "-o", resized.name,
             "--width", "8", "--height", "8", "--sprite", "--alpha-threshold", "75", "--json"])
    assert dimensions(resized) == (8, 8)
    run("colors", resized, "-o", directory / "colors.png", "--palette", "db32", "--quantization",
        "kmeans-linear", "--quantization-colors", "16", "--dithering", "--color-weights", "--json")
    assert dimensions(directory / "colors.png") == (8, 8)
    check("independent resize and colors, relative paths and dithering")

    smoothed = directory / "smooth.png"
    monochrome = directory / "monochrome.png"
    ascii_text = directory / "ascii.txt"
    ascii_image = directory / "ascii.png"
    replaced = directory / "replaced.png"
    run("smooth", source, "-o", smoothed, "--passes", "2", "--json")
    run("mono", source, "-o", monochrome, "--json")
    run("ascii", source, "--text-output", ascii_text, "--image-output", ascii_image,
        "--preset", "minimal", "--json")
    palette = json.loads(run("palette-info", source, "--json").stdout)
    run("replace-color", source, "-o", replaced, "--from", "#FFFFFF", "--to", "#010203", "--json")
    assert dimensions(smoothed) == dimensions(monochrome) == dimensions(ascii_image) == dimensions(replaced) == (16, 16)
    assert ascii_text.read_text(encoding="utf-8") and palette["count"] > 0
    check("smoothing, monochrome, ASCII, palette inspection and color replacement")

    aligned = directory / "aligned.png"
    report = json.loads(run("align", source, "-o", aligned, "--cell-size", "4", "--rigid", "--json").stdout)
    run("compact-grid", aligned, "-o", directory / "pixels.png", "--cell-size", str(report["cellSize"]), "--json")
    assert dimensions(aligned) == (16, 16) and dimensions(directory / "pixels.png") == (4, 4)
    check("alignment and reduction pipeline uses reported cell size")

    transparent = directory / "transparent.png"
    run("remove-bg", source, "-o", transparent, "--mode", "edges", "--background-color", "#FFFFFF",
        "--tolerance", "0", "--json")
    icon = directory / "test.ico"
    run("icon", transparent, "-o", icon, "--sizes", "16,32", "--resize", "nearest-neighbor", "--fit", "contain", "--json")
    assert dimensions(transparent) == (16, 16)
    assert struct.unpack("<HHH", icon.read_bytes()[:6]) == (0, 1, 2)
    check("edge-connected background removal and multi-size ICO")

    animation = directory / "анимация.aseprite"
    second = directory / "вторая.aseprite"
    aseprite(animation)
    second.write_bytes(animation.read_bytes())
    sheets = directory / "sheets"
    run("aseprite", animation, second, "--output-dir", sheets, "--layout", "grid", "--columns", "2",
        "--padding", "2", "--inspection", "--json")
    for name in (animation.stem, second.stem):
        assert (sheets / (name + ".png")).is_file()
        assert (sheets / (name + ".inspection.json")).is_file()
        assert json.loads((sheets / (name + ".json")).read_text(encoding="utf-8-sig"))["frameCount"] == 1
    run("aseprite", animation, directory / "missing.aseprite", "--output-dir", directory / "partial", "--json", expected=1)
    assert (directory / "partial" / (animation.stem + ".png")).is_file()
    check("Aseprite batch, inspection and partial failure")

    unknown = [str(source), "--future-option-that-cli-does-not-know"]
    direct = process([str(executable), *unknown], expected=2)
    wrapped = run(*unknown, expected=2)
    assert (direct.stdout, direct.stderr) == (wrapped.stdout, wrapped.stderr)
    run("resize", directory / "missing.png", "-o", directory / "missing-output.png", expected=1)
    run("resize", source, "-o", resized, expected=1)
    run("resize", source, "-o", resized, "--width", "4", "--height", "4", "--overwrite", "--json")
    assert dimensions(resized) == (4, 4) and hashlib.sha256(source.read_bytes()).hexdigest() == digest
    check("CLI errors and overwrite behavior preserved; source unchanged")

    process([*base, "--cli", str(directory / "missing.exe"), "--locate"], expected=3)
    if args.gui:
        rejected = process([*base, "--cli", str(args.gui.resolve()), "--locate"], expected=3)
        assert b"console CLI" in rejected.stderr
        check("desktop GUI rejected by inspecting the file without launching")

    package_directory = args.packages.resolve() if args.packages else directory.parent
    for name in ("Tessera-chatgpt-plugin.zip", "Tessera-skill.zip"):
        with zipfile.ZipFile(package_directory / name) as archive:
            assert archive.testzip() is None
            assert any(item.endswith("SKILL.md") for item in archive.namelist())
            assert not any("__pycache__" in item or item.endswith(".exe") for item in archive.namelist())
            reference_name = "skills/tessera/references/cli.md" if "plugin" in name else "tessera/references/cli.md"
            reference = archive.read(reference_name).decode("utf-8-sig")
            for command in ("smooth-colors", "monochrome", "ascii", "palette-info", "replace-color", "--color-mode", "--detail-protection"):
                assert command in reference, (name, command)
            if "plugin" in name:
                assert json.loads(archive.read("plugin.json"))["version"] == json.loads((root / "plugins/tessera/plugin.json").read_text(encoding="utf-8"))["version"]
    check("both distributable archives are valid and contain the skill")
    (directory / "report.json").write_text(json.dumps({"passed": passed, "cli": str(executable)},
                                                     ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(passed)} checks passed; evidence: {directory}")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
