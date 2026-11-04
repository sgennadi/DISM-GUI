#!/usr/bin/env python3
"""Prepare upstream DISM GUI source with local UI improvements for .NET 10."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
source = root / "DISM GUI"
project = source / "DISM GUI.vbproj"
main = source / "frmMain.vb"

if not project.is_file() or not main.is_file():
    if source.exists():
        raise RuntimeError("Incomplete DISM GUI source directory; remove it and retry.")
    revision = "6e368a14dcf241ed9ee898591b0b65239e3a21f7"  # pinned upstream source
    url = f"https://github.com/mikecel79/DISMGUI/archive/{revision}.zip"
    print("Downloading upstream mikecel79/DISMGUI...")
    with tempfile.TemporaryDirectory() as tmp:
        archive = Path(tmp) / "upstream.zip"
        urllib.request.urlretrieve(url, archive)
        with zipfile.ZipFile(archive) as z:
            for item in z.infolist():
                components = item.filename.split("/")
                if len(components) < 3 or components[1] != "DISM GUI":
                    continue
                relative = Path(*components[1:])
                if ".." in relative.parts:
                    raise RuntimeError("Unexpected path in upstream archive")
                target = root / relative
                if item.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with z.open(item) as src, target.open("wb") as dst:
                        shutil.copyfileobj(src, dst)

if "Private Function ChooseFolderModern(" not in main.read_text(encoding="utf-8-sig"):
    print("Applying modern dialogs and safe WIM unmount patch...")
    subprocess.run([sys.executable, str(root / "tools" / "apply_fix.py"), str(root)], check=True)

print("Installing modern UI, About and update manager...")
subprocess.run([sys.executable, str(root / "tools" / "apply_modern.py"), str(root)], check=True)

s = project.read_text(encoding="utf-8-sig")
s = s.replace("<TargetFramework>net9.0-windows</TargetFramework>", "<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>")
s = s.replace('<Platform Condition=" \'$(Platform)\' == \'\' ">x86</Platform>', '<Platform Condition=" \'$(Platform)\' == \'\' ">AnyCPU</Platform>')
s = s.replace("<GenerateAssemblyInfo>false</GenerateAssemblyInfo>", "<GenerateAssemblyInfo>true</GenerateAssemblyInfo>")
if "<ApplicationIcon>App.ico</ApplicationIcon>" not in s:
    s = s.replace("</Project>", """  <PropertyGroup>
    <ApplicationIcon>App.ico</ApplicationIcon>
  </PropertyGroup>
</Project>""")

# A single VERSION file drives EXE metadata, About, and release publishing.
version = (root / "VERSION").read_text(encoding="utf-8").strip()
parts = version.split(".")
if len(parts) != 3 or not all(part.isascii() and part.isdecimal() for part in parts):
    raise RuntimeError("VERSION must contain major.minor.patch, e.g. 4.2.0")
exe_version = version + ".0"
for property_name in ("ApplicationVersion", "AssemblyVersion", "FileVersion"):
    source_value = f"<{property_name}>4.0.0.0</{property_name}>"
    target_value = f"<{property_name}>{exe_version}</{property_name}>"
    if s.count(source_value) != 1:
        raise RuntimeError(f"Expected one upstream {property_name} property")
    s = s.replace(source_value, target_value)
s = s.replace("<Company>http://mikecel79.wordpress.com</Company>", "<Company>DISM GUI contributors</Company>")
if "<DebugType>none</DebugType>" not in s:
    s = s.replace("</Project>", """  <PropertyGroup>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
</Project>""")
project.write_text(s, encoding="utf-8-sig")
assert "<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>" in s, "TargetFramework not updated"
print("Ready:", project)
