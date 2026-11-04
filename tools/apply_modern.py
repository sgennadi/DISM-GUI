#!/usr/bin/env python3
"""Install the modern WinForms shell, About dialog and release updater.

Idempotent; validates upstream markers to avoid silent partial transformations.
"""
from pathlib import Path
import shutil
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: apply_modern.py REPOSITORY_PATH")
repo = Path(sys.argv[1]).resolve()
project = repo / "DISM GUI"
tools = repo / "tools"
main = project / "frmMain.vb"
progress = project / "frmProgress.vb"

def edit_once(path, old, new, label):
    s = path.read_text(encoding="utf-8-sig")
    if new in s:
        print(f"Already patched: {label}")
        return
    if s.count(old) != 1:
        raise RuntimeError(f"{label}: expected one matching marker, got {s.count(old)}")
    path.write_text(s.replace(old, new, 1), encoding="utf-8-sig")
    print("Patched:", label)

for name in ("ModernTheme.vb", "ResponsiveSectionLayout.vb", "UpdateManager.vb"):
    shutil.copyfile(tools / name, project / name)

# Embed the same multi-resolution icon in the executable and every window.
subprocess.run([sys.executable, str(tools / "generate_icon.py"),
                str(project / "App.ico")], check=True)

# Replace obsolete About dialog controls and unneeded original logo resources.
shutil.copyfile(tools / "frmAbout.Modern.vb", project / "frmAbout.vb")
for old in ("frmAbout.Designer.vb", "frmAbout.resx"):
    oldfile = project / old
    if oldfile.exists():
        oldfile.unlink()

edit_once(main,
          "    Private Sub frmMain_Load(sender As System.Object, e As System.EventArgs) Handles MyBase.Load\n",
          """    Friend ReadOnly Property HasMountedImage As Boolean
        Get
            Return WIMMounted OrElse BackgroundWorkerMount.IsBusy OrElse
                   BackgroundWorkerDisMount.IsBusy OrElse BackgroundWorkerDISMCommand.IsBusy
        End Get
    End Property

    Private Sub frmMain_Load(sender As System.Object, e As System.EventArgs) Handles MyBase.Load
        If UpdateManager.HandleSelfUpdateMode() Then Return
        ModernTheme.ApplyMain(Me, TabControl1, txtOutput, MenuStrip1, btnExit, Label4)
""", "main form theme and update hook")

edit_once(progress,
          "        Timer1.Enabled = True",
          """        ModernTheme.ApplyProgress(Me, Label1, ProgressBar1)
        Timer1.Enabled = True""", "progress dialog visual theme")
s = main.read_text(encoding="utf-8-sig")
s = s.replace("frmProgress.ShowDialog()", "frmProgress.ShowDialog(Me)")
s = s.replace("frmAbout.ShowDialog()", "frmAbout.ShowDialog(Me)")
main.write_text(s, encoding="utf-8-sig")
print("Installed themed UI, update manager and clean About dialog")
