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
s = s.replace("<AssemblyVersion>4.0.0.0</AssemblyVersion>", "<AssemblyVersion>4.1.0.0</AssemblyVersion>")
s = s.replace("<FileVersion>4.0.0.0</FileVersion>", "<FileVersion>4.1.0.0</FileVersion>")
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
