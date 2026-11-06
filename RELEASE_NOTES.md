# DISM GUI 4.3.16 — Stable

Portable self-contained Windows builds for x86, x64 and ARM64
(Visual Studio 2026 / .NET 10). Previous 4.3.15 remains available.

## WIM 360 — compare two independent WIM/ESD files

- New **Compare 2 files** action beside Compare indexes inside WIM 360.
  Two separate WIM/ESD paths, Inspect actions and per-container index
  selectors. Each column shows A/B, original file name and original
  WIM image index, even for Index 1 versus Index 1.
- **Index metadata (no mounting):** compare image name, build,
  architecture, edition, languages, image size, product and install type.
- **Files + folders (no mounting):** use the Windows DISM PowerShell
  Get-WindowsImageContent catalog to compare which Unicode file/folder
  paths are present or absent in A and B. Filters: Differences only,
  Only in A/B, Different, Exists in both, Present in both, type and search.
- **Safety:** check catalog snapshots, verify FULL source-file SHA-256
  before and after each offline listing, and revalidate both sources
  before presenting the result. On cancellation, changed files, partial
  catalogs or failed checks, no partial comparison is displayed.
- **CSV export:** preserve A/B image/index labels and protect names
  against spreadsheet formula injection. The virtual data grid scales
  to large catalogs with cancellable enumeration and progress reporting.
- **High-DPI:** adaptive A/B column sizing, sticky Compare/Export/Close
  toolbar at the bottom of the dialog, shorter stacked rows, version
  in title, separate persisted window size/position.

## Scope and limits

- **Path presence is NOT file-content equality.** Two paths can point to
  different bytes or lengths. This feature intentionally makes no
  assertion about per-file hashes or file sizes without mounting.
- This mode never mounts, applies, commits, converts or discards an image.
  Split SWM is not supported for direct cross-container path enumeration.
- Some DISM API versions may touch the source's last-modified timestamp.
  WIM 360 restores it only after confirming the entire WIM/ESD SHA-256
  is unchanged. On failed/canceled operations, timestamp restoration
  cannot be guaranteed even though the application itself does not
  issue any image-writing command.
- CI uses two separately captured, disposable, **real DISM-created**
  WIM files, validates missing/common Unicode paths, source SHA-256
  and timestamps, and checks synthetic matrices, filters, CSV and
  WinForms layouts at 125%, 150%, 175%. A production image still
  requires operator validation before deployment decisions.

Existing WIM 360 same-container comparison, Driver Studio 4.3.15
integrity audit, diagnostics and update paths remain unchanged.
