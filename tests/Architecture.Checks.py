"""Guard the solution's production dependency direction and source ownership."""

from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


root = Path(__file__).resolve().parents[1]
references = {
    "Tessera.Models": set(),
    "Tessera.Core": {"Tessera.Models"},
    "Tessera.Aseprite": set(),
    "Tessera.Rendering": {"Tessera.Core"},
    "Tessera.Desktop": {"Tessera.Models", "Tessera.Core", "Tessera.Aseprite", "Tessera.Rendering"},
    "Tessera.Cli": {"Tessera.Models", "Tessera.Core", "Tessera.Aseprite", "Tessera.Rendering"},
}
errors = []

solution = root / "Tessera.sln"
if not solution.is_file():
    errors.append("Tessera.sln is missing")
else:
    projects = set(re.findall(r'"([^"\\]+)\\[^"\\]+\.csproj"', solution.read_text(encoding="utf-8-sig")))
    expected = set(references) | {"Tessera.RegressionTests", "Tessera.Aseprite.Tests", "Tessera.Cli.Tests"}
    if projects != expected:
        errors.append(f"solution projects differ: expected {sorted(expected)}, got {sorted(projects)}")

for name, allowed in references.items():
    project = root / name / f"{name}.csproj"
    if not project.is_file():
        errors.append(f"missing project: {project}")
        continue
    tree = ET.parse(project)
    actual = {Path(item.attrib["Include"].replace("\\", "/")).stem
              for item in tree.findall(".//ProjectReference")}
    if actual != allowed:
        errors.append(f"{name} references {sorted(actual)}, expected {sorted(allowed)}")
    for item in tree.findall(".//Compile"):
        if ".." in Path(item.attrib.get("Include", "").replace("\\", "/")).parts:
            errors.append(f"{name} compiles source from another project")

for name in ("Tessera.Models", "Tessera.Core", "Tessera.Aseprite"):
    for file in (root / name).rglob("*.cs"):
        if "obj" in file.parts or "bin" in file.parts:
            continue
        text = file.read_text(encoding="utf-8-sig")
        if re.search(r"\busing\s+(?:System\.Windows|Tessera\.(?:Desktop|Rendering|Cli))\b", text):
            errors.append(f"UI or entry-point dependency in {file.relative_to(root)}")

for file in (root / "Tessera.Models").rglob("*.cs"):
    if "obj" in file.parts or "bin" in file.parts:
        continue
    if "namespace Tessera.Models;" not in file.read_text(encoding="utf-8-sig"):
        errors.append(f"model contract has another namespace: {file.relative_to(root)}")

for file in (root / "Tessera.Desktop").glob("*.xaml"):
    if file.name not in {"App.xaml", "Theme.xaml"}:
        errors.append(f"WPF view outside Views: {file.name}")
for file in (root / "Tessera.Desktop" / "Views").glob("*.xaml"):
    if '="Assets/' in file.read_text(encoding="utf-8-sig"):
        errors.append(f"relative asset URI in moved view: {file.name}")

if errors:
    print("\n".join("FAIL " + error for error in errors), file=sys.stderr)
    sys.exit(1)
print("PASS Tessera solution ownership and dependency direction")
