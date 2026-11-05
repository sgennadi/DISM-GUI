# DISM GUI 4.3.3

## WIM 360 — Mounted Image Servicing Audit

- Added **Scan mounted index** for an already-mounted WIM image index, including read-only mounts; this is separate from ordinary metadata inspection.
- Shows driver inventory totals, including inbox/third-party breakdown, top driver classes and providers.
- Reads Windows package inventory and displays Installed, Pending and Staged totals, highlighting up to four pending package names.
- Stores each audit under its selected WIM index, with a UTC scan timestamp, and includes results in the existing **Copy report** export.
- Uses read-only DISM PowerShell commands Get-WindowsDriver -Path -All and Get-WindowsPackage -Path; no image modification, automatic mount, commit or discard.
- Validates the WIM/index/mount path before and after auditing, and cancels stale reads when the source is changed; three-minute timeout per inventory step.
- Fits the additional audit button on a second actions row for legibility on high-DPI and narrow screens.
- Extended parser, reporting and WinForms smoke tests.

## Existing Driver Studio — 4.3.2

- Retains selective per-INF install, only verified New / Newer entries on read/write-mounted indexes.
- Preflight validates the mount and incoming source-INF hashes; Stop after current ensures an in-flight DISM write finishes safely.
- Same/Older/Review/Not checked sources remain blocked; no implicit Commit or Discard.

## Testing and limitations

- Windows CI checks parser/report invariants, WinForms layout and portable .NET 10 builds for x86, x64 and ARM64.
- CI cannot validate the mounted servicing inventory against a full real Windows image. First test with a disposable mounted WIM.
- Requires Windows PowerShell with DISM module and suitable read permissions for the mounted image; split .swm unsupported.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
