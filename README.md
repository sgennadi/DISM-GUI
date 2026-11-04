# DISM GUI - Visual Studio 2026 / .NET 10

Modernized from [mikecel79/DISMGUI](https://github.com/mikecel79/DISMGUI). Original author credited.

## Builds
- Windows x64: `DISMGUI-VS2026-win-x64.zip`
- Windows ARM64: `DISMGUI-VS2026-win-arm64.zip`
- Windows x86: `DISMGUI-VS2026-win-x86.zip`

**Download**: [Actions -> latest successful build -> Artifacts](https://github.com/sgennadi/DISM-GUI/actions/workflows/build.yml). Each archive contains a portable self-contained application.

## WIM 360 — Image Studio

Select **WIM 360** at the top of the left navigation. Choose a `.wim` or
`.esd` file, or select one in **Mount & Inspect** first. Click **Inspect** to
read all image-index metadata through the Windows WIMGAPI in read-only mode.
Select an index to view architecture, build, edition, languages and
mount state. **Use index in Mount** transfers the selected WIM path and
index to the original Mount form but **never mounts or modifies the image**.
Use **Copy report** to share the read-only inventory.

WIM 360 currently does not inspect split `.swm` sets and cannot count drivers
or installed packages until further work. The first stage does not perform
any destructive operation. For actual image servicing, use the existing
DISM sections only after verifying image backups.

## Changes
- Modern Windows dialog for selecting mount, apply, driver, package and capture folders; matching file picker for WIM images.
- Last used paths stored per category in `HKCU\Software\DISMGUI\RecentPaths`.
- Cancel selection preserves previous path; mount and apply destinations require empty directories.
- Commit / Discard / Cancel on the main UI thread.
- Foreground, main-window-owned unmount notification on success or failure; includes error code.
- Capture DISM stdout and stderr without hanging on full pipes.

## Local Visual Studio 2026
Requires .NET 10 SDK and Python 3 on Windows.

1. Clone this repository.
2. Run `python tools/prepare_source.py` once to download the pinned upstream VB.NET source, apply the UI fixes and update the project to .NET 10.
3. Open `DISM GUI/DISM GUI.vbproj` in Visual Studio 2026.
4. Build in Visual Studio or publish via PowerShell: `./Build.ps1 -Runtime all`.

The source is downloaded from a **pinned upstream revision** at preparation time, rather than copied into this repository. GitHub Actions automatically performs preparation and publishes x86/x64/ARM64 portable ZIP artifacts.

## Test safely
CI tests compilation, not real WIM operations. Test with disposable WIM copies first. ARM64 runtime testing requires a Windows ARM64 machine.

**Attribution and redistribution:** Upstream license terms are not clearly stated. Verify redistribution rights before publishing modified binaries.

## Support
For DISM GUI questions or bug reports, use **About → Email Support** or
**Tools → Email Support...** in the application. The default email client
opens a draft addressed to **[sgennadi@hotmail.com](mailto:sgennadi@hotmail.com?subject=DISM%20GUI%20Support)**,
including the installed version and architecture. No message is sent
automatically.
