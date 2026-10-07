"""Audit project dependencies, neutral layers and declared file names. No external packages."""
import json
import re
import sys
import subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
tracked_sources = set(subprocess.check_output(['git', '-C', str(root), 'ls-files', '-z']).decode('utf-8').strip('\0').split('\0'))
errors = []
projects = list(root.glob("src/*/*.csproj")) + list(root.glob("tests/*/*.csproj"))
graph = {}
for project in projects:
    name = project.stem
    references = [Path(x.attrib["Include"].replace("\\", "/")).stem for x in ET.parse(project).findall(".//ProjectReference")]
    graph[name] = references
    if name == "AgentLoop.Kernel" and references:
        errors.append("Kernel references another project")
    if name == "AgentLoop.Core" and set(references) != {"AgentLoop.Kernel"}:
        errors.append("Core references implementations")
    if name == "AgentLoop.Harness" and set(references) != {"AgentLoop.Core", "AgentLoop.Kernel"}:
        errors.append("Harness references implementations")
    if project.parent.parent.name == "src" and any("Tests" in r for r in references):
        errors.append("Production references tests: " + name)

checked = 0
for folder in [root / "src", root / "tests"]:
    for path in folder.rglob("*.cs"):
        if {"obj", "bin"} & set(path.parts):
            continue
        checked += 1
        source = path.read_text(encoding="utf-8-sig")
        relative = path.relative_to(root).as_posix()
        if relative not in tracked_sources:
            errors.append("Source missing from Git: " + relative)
        if "AgentLoop.Kernel" in relative or "AgentLoop.Core" in relative:
            if re.search(r"\b(?:File|Directory|Process|HttpClient)\.", source):
                errors.append("I/O in neutral layer: " + relative)
        if "AgentLoop.Kernel" in relative and ("JsonPropertyName(" in source or "JsonIgnore]" in source):
            errors.append("Wire serialization in Kernel: " + relative)
        if relative.startswith("src/") and re.search(r"public\s+(?:sealed\s+)?(?:static\s+)?class\s+\w*(?:SelfTest|FixtureClient)\b", source):
            errors.append("Test implementation in production: " + relative)
        # Top-level declarations are indented at column zero in this layout.
        names = re.findall(r"^(?:public|internal)\s+(?:(?:sealed|static|abstract|partial|readonly)\s+)*(?:class|record(?:\s+struct)?|struct|interface|enum)\s+(\w+)", source, re.M)
        if names and path.stem not in names and not path.name.startswith("Program."):
            errors.append("File/type name mismatch: " + relative)

matrix = json.loads((root / "docs/architecture/matriz-archivos.json").read_text(encoding="utf-8"))
for row in matrix:
    if not row["destinations"]:
        errors.append("Unmapped original source: " + row["source"])
    for destination in row["destinations"]:
        if not (root / destination).is_file():
            errors.append("Missing migration destination: " + destination)
report = {"projects": len(projects), "sources": checked, "originalSources": len(matrix), "dependencies": graph, "errors": errors}
print(json.dumps(report, indent=2, ensure_ascii=False))
sys.exit(bool(errors))
