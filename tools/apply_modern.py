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

for name in ("ModernTheme.vb", "ResponsiveSectionLayout.vb", "ResponsiveForms.vb", "WindowGeometry.vb", "UpdateManager.vb", "SupportContact.vb", "ErrorDiagnostics.vb", "ErrorReportForm.vb", "RuntimeDiagnostics.vb", "UniversalDiagnosticsBridge.vb", "UniversalDiagnosticsEmbedded.vb", "UniversalDiagnosticsRuntime.vb", "UniversalDiagnosticsDelivery.vb", "UniversalDiagnosticsQueueForm.vb",
             "Wim360Reader.vb", "Wim360SplitSet.vb", "Wim360SwmOperations.vb", "Wim360SwmToolsForm.vb", "WimImageOperationLease.vb", "Wim360Dashboard.vb", "Wim360InspectOverview.vb",
             "IntegratedTestChecks.vb", "IntegratedTestCenterForm.vb", "Wim360Servicing.vb",
             "Wim360Catalog.vb", "Wim360MetadataExport.vb", "Wim360IndexComparer.vb", "Wim360OfflineList.vb",
             "Wim360CompareIndexesForm.vb", "Wim360CrossImage.vb", "Wim360CrossImageForm.vb", "Wim360CrossMounted.vb", "Wim360ReparseSafety.vb", "Wim360InventoryForm.vb",
             "Wim360SnapshotDiff.vb", "Wim360CompareForm.vb", "WimMountSafety.vb", "WimMountHealthForm.vb", "DriverStudioEngine.vb",
             "DriverStudioInventory.vb", "DriverStudioPanel.vb",
             "DriverStudioInstaller.vb", "DriverStudioPackageAudit.vb"):
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
        RuntimeDiagnostics.Initialize()
        ModernTheme.ApplyMain(Me, TabControl1, txtOutput, MenuStrip1, btnExit, Label4)
""", "main form theme and update hook")

edit_once(progress,
          "        Timer1.Enabled = True",
          """        ModernTheme.ApplyProgress(Me, Label1, ProgressBar1)
        Timer1.Enabled = True""", "progress dialog visual theme")
s = main.read_text(encoding="utf-8-sig")
# Record failed DISM exits from every legacy command, mount and dismount path.
# Commands can fail without throwing a managed exception; the local journal
# therefore records nonzero exit codes without copying full DISM output.
if "RuntimeDiagnostics.RecordDismExit(" not in main.read_text(encoding="utf-8-sig"):
    original_main = main.read_text(encoding="utf-8-sig")
    command_exit = "        strDISMExitCode = DISM.ExitCode\n"
    unmount_exit = "        strDISMExitCode = DISM.ExitCode.ToString()\n"
    if original_main.count(command_exit) != 2 or original_main.count(unmount_exit) != 1:
        raise RuntimeError("Expected three legacy DISM exit points for safe instrumentation")
    original_main = original_main.replace(command_exit,
        command_exit + '        RuntimeDiagnostics.RecordDismExit("DISM operation", strDISMExitCode)\n')
    original_main = original_main.replace(unmount_exit,
        unmount_exit + '        RuntimeDiagnostics.RecordDismExit("DISM dismount", strDISMExitCode)\n')
    main.write_text(original_main, encoding="utf-8-sig")
s = s.replace("frmProgress.ShowDialog()", "frmProgress.ShowDialog(Me)")
s = s.replace("frmAbout.ShowDialog()", "frmAbout.ShowDialog(Me)")
main.write_text(s, encoding="utf-8-sig")
print("Installed themed UI, update manager and clean About dialog")

# Repair command-line construction inherited from the pinned upstream release.
# The legacy Mount-WIM and Apply-Image builders put an opening quote before
# /Index but never close it; consequently DISM treats /MountDir or /ApplyDir
# as part of /Index and returns error 87. Index is numeric, so do not quote it.
# Quote every file/directory path independently, including ApplyDir.
def repair_dism_image_commands(source):
    lines = source.splitlines(keepends=True)
    mounted = 0
    applied = 0
    for pos, line in enumerate(lines):
        if 'DISM.StartInfo.Arguments = "/Mount-WIM ' in line:
            if ' & " /index:""" & strIndex & " /MountDir:"' not in line:
                raise RuntimeError("Unexpected upstream Mount-WIM argument format")
            lines[pos] = line.replace(
                ' & " /index:""" & strIndex & " /MountDir:"',
                ' & " /Index:" & strIndex & " /MountDir:"')
            mounted += 1
        elif 'strDISMArguments = "/Apply-Image ' in line:
            verify = ' & " /Verify"' if ' /Verify"' in line else ''
            indent = line[:len(line) - len(line.lstrip())]
            ending = "\r\n" if line.endswith("\r\n") else "\n"
            lines[pos] = (indent +
                'strDISMArguments = "/Apply-Image /ImageFile:""" & strSource & ' +
                '"""" & " /Index:" & strAppendIndex & " /ApplyDir:""" & ' +
                'strDest & """"' + verify + ending)
            applied += 1
    if mounted != 2 or applied != 2:
        raise RuntimeError(
            f"Expected 2 Mount-WIM and 2 Apply-Image builders, found {mounted}/{applied}")
    repaired = "".join(lines)
    if ' & " /index:""" &' in repaired:
        raise RuntimeError("Malformed numeric DISM /index argument remains")
    return repaired

main.write_text(repair_dism_image_commands(
    main.read_text(encoding="utf-8-sig")), encoding="utf-8-sig")
print("Repaired DISM Mount-WIM and Apply-Image index/path quoting")
