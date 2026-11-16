# DISM GUI 4.3.21 — Stable

Portable, self-contained .NET 10 / Visual Studio 2026 Windows builds for
x86, x64 and ARM64. Update from stable 4.3.20 using the built-in updater
or download the matching release ZIP. The existing 4.3.20 release is preserved.

## WIM 360: visual index differences

- **Compare indexes** now shows the direction in the Comparison header
  (for example, `Change 1 -> 2`) and classifies each row as **Added,
  Removed, Modified, Ambiguous, Unchanged**, or **Outside pair**.
- Soft green/red/amber/violet row tints make changes recognizable without
  relying on color alone. All statuses remain visible as text.
- Choose **Before -> After** by clicking the Comparison column header or
  summary line, or using the grid's context menu. **Reverse direction**
  is available from the same context menu; color and status update immediately.
- New Result filters **Pair changes only**, **Added (pair)**,
  **Removed (pair)**, **Modified (pair)**, **Ambiguous (pair)** and
  **Unchanged (pair)** respect the selected direction, search, and type.
- The existing filtered CSV export remains available under **Export...**.
  **Change report (Before -> After)** exports a CSV with counts and
  before/after values for a chosen pair of indexes. CSV is formula-safe.
- Per-index missing counts and detailed present/missing/unique coverage
  remain available for 2–8 selected indexes.

## Driver Studio and WIM validation

- Installed-driver inventory metadata is validated strictly before an
  import is classified as safe; incomplete structured data blocks writes.
- Signed OEM test-copy integration validates staging and full driver
  inventory equality after Commit/read-only remount, preserving the
  original WIM.
- The portable RealOemIntegration runner supports an opt-in multi-image
  `--matrix` mode and a read-only `--probe --matrix` command to list all
  missing/unreadable WIM and INF sources at once. Real signed OEM testing
  in disposable Windows RE copies is covered in CI.

## Scope and limitations

- Compare Indexes is read-only. **Modified** refers to different reported
  values, not necessarily different file bytes. Equal paths and sizes
  are not a byte-for-byte comparison; use explicit bounded SHA-256 checks
  on already-mounted indexes when exact content matters.
- The test matrix accepts real Windows 11 / Server 2022 / Server 2025
  installation WIMs supplied by the operator. Hosted CI does not have
  those full installation images, so those three media families have not
  been end-to-end certified.
- Cooperative application writer locks do not block manual DISM sessions
  or unrelated applications.

## Verification

- Windows x86, x64 and ARM64 portable build and packaging checks.
- WinForms comparison, pairwise status, color and filter regression tests;
  read-only WIM mount and simulated 125%, 150% and 175% font scaling.
- Signed OEM runner self-tests and existing WIM/driver safety suites.
- The prior 4.3.20 release remains available as a fallback.

Original DISMGUI attribution and redistribution notices remain intact.
