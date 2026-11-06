# DISM GUI 4.3.15 — Stable

Portable self-contained Windows builds (x86 / x64 / ARM64), .NET 10
and Visual Studio 2026. The previous 4.3.14 stable release is retained.

## Driver Studio — driver package integrity

- **Audit selected** appears in the Driver Studio details header. It audits
  selected New/Newer driver packages, or the highlighted INF if none are
  selected. A batch accepts up to 30 INFs.
- A read-only, deterministic **SHA-256 package manifest** includes the
  complete source-folder tree: INF, CAT, SYS, DLL, other files and their
  relative paths and sizes. The scan rejects reparse points, path anomalies
  and source files changing while read. Packages above **1,600 files / 2 GiB**
  are rejected rather than silently incompletely checked.
- INF parsing records generic/decorated CatalogFile declarations and
  architecture hints from model sections. Definite architecture mismatches
  become **Review**, never automatically selected New/Newer.
- Windows Authenticode verifies the selected CAT file. Missing, invalid or
  unverified catalogs are **not** approved for selective import. Signature
  validity alone does not certify each driver payload or device ranking.
- **Install selected INF** requires a completed approved source-package
  audit for every selected INF. Before *every* DISM operation, it rechecks
  the entire folder fingerprint and CAT signature. A changed package
  immediately stops the batch rather than using stale decisions.
- Following successful DISM commands, Driver Studio queries the mounted
  image inventory and reports how many original INF/provider/class/version/
  date identities are found. This verifies **staging**, not actual PnP
  selection on a device. Unavailable or mismatching postflight inventory
  is reported explicitly.
- No automatic mount/commit/discard/rollback, unsigned-driver override or
  GitHub diagnostics upload. Existing DISM output and user control remain.

## Interface and testing

- High DPI shows a short **PACKAGE DETAILS** label beside Audit selected.
  The source-package metric uses a compact label on smaller displays.
- GitHub Windows CI covers x86/x64/ARM64 compilation/packaging, synthetic
  signed-status-negative CAT fixtures, architecture and catalog selection,
  payload tampering and read-only real WIM mount tests, with simulated
  125%, 150%, and 175% WinForms layout checks.
- Tested code is read-only except when the operator explicitly confirms
  selective DISM import. Full CAT trust / OEM compatibility and write-mode
  install / discard cycles on a real disposable Windows install.wim must
  still be validated on a Windows workstation before production use.

Based on DISMGUI by Mike Celone (mikecel79/DISMGUI); attribution and existing
redistribution notices remain.
