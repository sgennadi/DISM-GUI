#!/usr/bin/env python3
"""Fail-fast checks for DISM GUI preparation, without network calls."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
modules = (
    "ErrorDiagnostics.vb", "ErrorReportForm.vb", "RuntimeDiagnostics.vb", "UniversalDiagnosticsBridge.vb", "UniversalDiagnosticsEmbedded.vb", "UniversalDiagnosticsQueueForm.vb",
    "Wim360SplitSet.vb", "Wim360InspectOverview.vb", "IntegratedTestChecks.vb", "IntegratedTestCenterForm.vb",
    "Wim360Servicing.vb", "Wim360MetadataExport.vb",
    "Wim360IndexComparer.vb", "Wim360OfflineList.vb", "Wim360CompareIndexesForm.vb",
    "DriverStudioInstaller.vb",
)
prepared = (ROOT / "tools" / "apply_modern.py").read_text(encoding="utf-8")
issues = []
for name in modules:
    if not (ROOT / "tools" / name).is_file():
        issues.append("Missing " + name)
    if name not in prepared:
        issues.append("Not included in source preparation: " + name)
version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
if re.fullmatch(r"[0-9]+[.][0-9]+[.][0-9]+", version) is None:
    issues.append("Invalid VERSION")
servicing = (ROOT / "tools" / "Wim360Servicing.vb").read_text(encoding="utf-8")
if servicing.count("ValidateTarget(") < 2:
    issues.append("Mounted-image validation has been removed")
for vb_path in sorted((ROOT / "tools").glob("*.vb")):
    module = vb_path.read_text(encoding="utf-8")
    if re.search(r"[.]Count\s*\(\s*Function", module):
        issues.append("VB.NET List.Count(predicate) compilation regression in " + vb_path.name)
if "repair_dism_image_commands(" not in prepared:
    issues.append("DISM mount/apply index quoting guard missing")
if issues:
    for item in issues:
        print("PRE-BUILD ERROR:", item)
    raise SystemExit(1)
print("PASS pre-build diagnostics: versions, sources, WIM guards, VB.NET signatures")
