# UniversalDiagnostics integration — DISM GUI 4.3.10

This is an **opt-in, local-only** connection to the independent
[UniversalDiagnocticsModule](https://github.com/sgennadi/UniversalDiagnocticsModule)
(0.1.0-rc.1). DISM GUI continues to identify itself as **4.3.10**.
The published stable `v4.3.10` tag and binaries are not overwritten.

## Installation on Windows x64

The collector is one independent EXE shared by DISM GUI, ISO-WCX, and other
applications. Because the common repository is private, sign in with GitHub
CLI `gh auth login` to access its RC asset.

Run the script from the universal repository
[`Install-UniversalDiagnostics.ps1`](https://github.com/sgennadi/UniversalDiagnocticsModule/blob/main/Install-UniversalDiagnostics.ps1):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-UniversalDiagnostics.ps1
```

It validates SHA-256 and the reported version before placing the EXE here:

`%LOCALAPPDATA%\UniversalDiagnostics\UniversalDiagnostics.exe`

Alternatively place the verified EXE next to `DISM GUI.exe`, or set the
`UNIVERSAL_DIAGNOSTICS_EXE` environment variable to an **absolute path**.
No collector is downloaded or executed automatically by DISM GUI.

**Architecture:** the current common RC binary is Windows x64; it can run
alongside a 32-bit DISM GUI process on a 64-bit Windows host. Native x86 and
ARM64-only systems need their own portable collector builds.

## Usage

1. Open **Tools → Report error to GitHub** (or About → Report error).
2. Read the existing diagnostics preview. Optional DISM output and local
   history still use their existing independent checkboxes.
3. Check the explicit **I reviewed...** box.
4. Choose **Save to shared queue**, review the exact JSON summary in the
   confirmation dialog, then confirm.
5. The status bar displays a randomly generated report ID after the
   collector writes it locally.

This action only sends the `capture-json` command over UTF-8 standard
input to the verified, locally installed executable. The event contains
`project`, `component`, `version`, `error_type`, `code`,
`summary` and `level`. No `excerpt`, raw logs, computer name,
Windows username, domain details, or access tokens are passed.

The collector redacts the summary again and stores a pending event at
`%LOCALAPPDATA%\UniversalDiagnostics\<project-hash>\<report-id>.json`.
GitHub is **not contacted** when the event is captured; the helper process
does not inherit `GH_TOKEN` or `GITHUB_TOKEN`.

To review pending records manually:

```powershell
& "$env:LOCALAPPDATA\UniversalDiagnostics\UniversalDiagnostics.exe" list --project sgennadi/DISM-GUI
& "$env:LOCALAPPDATA\UniversalDiagnostics\UniversalDiagnostics.exe" preview --project sgennadi/DISM-GUI --id REPORT-ID
```

The preview displays the SHA-256 of the exact issue text. The shared
collector supports manual browser draft or authorized, explicitly approved
GitHub API submission. Those steps are intentionally **not triggered by
DISM GUI**. The original **Open GitHub Issue draft** action still works
independently.

## Existing logs and compatibility

- Existing `RuntimeDiagnostics` event logs, rotation, clear history, support
  email, and direct browser GitHub draft remain available.
- Collector failure or missing installation never prevents mounting or
  servicing Windows images.
- `VERSION` stays **4.3.10**. No code is merged into the old stable release
  until this integration has passed the separate PR and Windows tests.
- The legacy DISM-GUI-specific GitHub App device-flow draft PR #9 remains
  deferred; reusable future reporting belongs in the shared repository.
