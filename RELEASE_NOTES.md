# DISM GUI 4.3.26 — Stable

Portable Windows builds for x86, x64 and ARM64, with Visual Studio 2026
and self-contained .NET 10. Maintenance update to Driver Studio's source
file selection controls, following operator screenshots from v4.3.25.

## Driver Studio: intuitive source workflow

- **Choose folder** moved into the upper-right Driver Studio workspace
  header. The button width is calculated from its actual font/caption
  rather than assuming a fixed size, and the workspace header adapts to
  larger UI fonts to avoid truncation.
- **Scan / Refresh** moved to the editable INF source path row. At a normal
  window width, the path field and the single Scan button sit together.
  At higher display scaling, the path spans the top row and the Scan
  action sits directly below at the right. There are no duplicate Scan
  controls and no separate manual Compare action.
- The source field has a readable minimum height and smaller margins
  to avoid appearing as a cropped line in a compact window.
- Browsing is disabled while a scan/driver import is in progress, so
  the source cannot be changed through the chooser mid-operation.
- Existing selection/audit buttons, adaptive high-DPI selection
  toolbar, INF details, and optional **Add selected to WIM...** controls
  from v4.3.25 remain as-is.

## Safety and behavior

- Choosing a folder only fills in the source path; it does not install
  drivers or change a WIM.
- Scan / Refresh is read-only. It examines source INF files and performs
  automatic comparison only against an already-mounted image verified
  for the exact WIM path/index/mount directory by WIMGAPI.
- New/Newer INF are not checked by scanning. Import is an explicit,
  independently confirmed write to the **mounted offline WIM**, never
  the running Windows installation. No automatic Commit/Discard,
  mount recovery, driver removal or force-unsigned operation is added.
- Signature audit, installed-driver inventory validation, WIM
  architecture and image writer lease, and postflight validation
  remain unchanged from v4.3.25.

## Validation

- Windows x86 / x64 / ARM64 CI, plus signed OEM/WIM verification on
  a hosted Windows VM: PASS for the source-change PR.
- Real read-only WIM mount smoke tests and WinForms control checks: PASS.
- Simulated font scaling 125%, 150%, 175%, 200%: PASS, including
  full captions, header placement, path-field height and no
  source/scan control overlap. These tests do not replace manual
  testing on a physical Windows system.

## Rollback

The original, unchanged portable release
[DISM GUI v4.3.25](https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.25)
remains published with x86, x64 and ARM64 downloads.

Original upstream attribution and project licensing are retained.
