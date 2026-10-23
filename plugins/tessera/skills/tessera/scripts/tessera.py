"""Locate Tessera CLI and forward its arguments without a shell or option filter."""

import argparse
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys


def config_path():
    local = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData" / "Local"))
    return local / "Tessera" / "chatgpt-cli.json"


def console_executable(value):
    path = Path(os.path.expandvars(value)).expanduser().resolve()
    if path.suffix.lower() != ".exe" or not path.is_file():
        raise ValueError(f"Tessera CLI executable not found: {path}")

    # Windows filenames are case-insensitive: Tessera.exe can be the GUI.
    # Read the PE subsystem instead of probing --help and accidentally opening it.
    try:
        with path.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise ValueError("Not a Windows executable")
            stream.seek(0x3C)
            pe_offset = struct.unpack("<I", stream.read(4))[0]
            stream.seek(pe_offset)
            if stream.read(4) != b"PE\0\0":
                raise ValueError("Invalid PE header")
            stream.seek(pe_offset + 24 + 68)
            subsystem = struct.unpack("<H", stream.read(2))[0]
            if subsystem != 3:
                raise ValueError("Expected the console CLI, not the desktop application")
    except (OSError, ValueError, struct.error) as error:
        raise ValueError(f"Cannot use {path}: {error}") from error
    return path


def locate(explicit, settings, cwd):
    if explicit:
        return console_executable(explicit), "--cli"
    if os.environ.get("TESSERA_CLI"):
        return console_executable(os.environ["TESSERA_CLI"]), "TESSERA_CLI"
    if settings.is_file():
        try:
            saved = json.loads(settings.read_text(encoding="utf-8-sig"))
            value = saved["executable"]
            if not isinstance(value, str) or not value.strip():
                raise ValueError("'executable' must be a non-empty path")
            return console_executable(value), str(settings)
        except (OSError, ValueError, KeyError, TypeError) as error:
            raise ValueError(f"Invalid Tessera settings at {settings}: {error}. Use --configure PATH.") from error

    skill = Path(__file__).resolve().parents[1]
    candidates = [skill / "bin" / "tessera.exe"]
    visited = set()
    for start in (cwd, skill):
        for root in (start, *start.parents):
            if root in visited:
                continue
            visited.add(root)
            if not (root / "Pixelizator.Cli" / "Pixelizator.Cli.csproj").is_file():
                continue
            candidates.append(root / "artifacts" / "cli" / "tessera.exe")
            for configuration in ("Release", "Debug"):
                candidates.append(root / "Pixelizator.Cli" / "bin" / configuration / "net8.0-windows" / "tessera.exe")
    on_path = shutil.which("tessera.exe")
    if on_path:
        candidates.append(Path(on_path))
    for path in candidates:
        try:
            return console_executable(str(path)), "discovery"
        except ValueError:
            continue
    raise ValueError("Tessera CLI not found. Use --configure PATH, --cli PATH, or TESSERA_CLI. "
                     "Choose tessera.exe from Tessera-cli-win-x64, not the desktop app.")


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--cli", help="Tessera CLI executable for this call")
    mode.add_argument("--configure", metavar="PATH", help="Save the local CLI path and exit")
    parser.add_argument("--config", type=Path, default=config_path(), help="Local CLI settings JSON")
    parser.add_argument("--cwd", type=Path, help="Working directory for relative CLI paths")
    parser.add_argument("--locate", action="store_true", help="Print executable discovery as JSON and exit")
    parser.add_argument("arguments", nargs=argparse.REMAINDER, help="After --, all arguments go directly to Tessera")
    args = parser.parse_args()
    arguments = args.arguments
    if arguments[:1] == ["--"]:
        arguments = arguments[1:]

    try:
        if os.name != "nt":
            raise ValueError("Tessera CLI requires a connected Windows computer with local command execution.")
        cwd = (args.cwd or Path.cwd()).expanduser().resolve()
        if not cwd.is_dir():
            raise ValueError(f"Working directory not found: {cwd}")
        settings = args.config.expanduser().resolve()
        if (args.configure or args.locate) and arguments:
            raise ValueError("--configure and --locate do not run CLI arguments; use a separate call.")
        if args.configure:
            executable = console_executable(args.configure)
            settings.parent.mkdir(parents=True, exist_ok=True)
            saved = {"executable": str(executable)}
            temporary = settings.with_name(settings.name + f".{os.getpid()}.tmp")
            try:
                temporary.write_text(json.dumps(saved, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
                temporary.replace(settings)
            finally:
                temporary.unlink(missing_ok=True)
            print(json.dumps({**saved, "config": str(settings)}, ensure_ascii=False))
            return 0
        executable, source = locate(args.cli, settings, cwd)
        if args.locate:
            print(json.dumps({"executable": str(executable), "source": source}, ensure_ascii=False))
            return 0
        return subprocess.run([str(executable), *arguments], cwd=cwd, stdout=sys.stdout, stderr=sys.stderr,
                              shell=False, creationflags=subprocess.CREATE_NO_WINDOW).returncode
    except (OSError, ValueError) as error:
        print(f"Tessera launcher: {error}", file=sys.stderr)
        return 3
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    sys.exit(main())
