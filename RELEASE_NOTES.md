# DISM GUI 4.3.19 — Stable

Portable self-contained builds for Windows x64, x86 and ARM64
(.NET 10 / VS2026). Version 4.3.18 remains available.

## WIM 360: SWM Integrity & Export

- **SWM audit / export** opens a new English, high-DPI dialog.
  Select any member of a complete split-WIM (.swm) set; inspect
  original indexes, then click **Audit all SWM parts**.
- The audit validates part headers, matching identity, indices, size
  and filenames, computes a full **SHA-256 for each part**, and
  publishes a reproducible combined SHA-256 fingerprint. Missing
  parts, extra wildcard-matching SWM files, symlinks, changed
  files, invalid metadata or cancellation fail closed.
- Export one chosen source image index to a **NEW WIM file** via
  Windows DISM `/Export-Image /SWMFile /CheckIntegrity`. Every source
  part is rehashed before and after the export. DISM writes to an
  application-owned temporary WIM; the requested filename is created
  only if DISM exits successfully, source checks pass, and WIMGAPI
  confirms a valid one-index destination image.
- **No overwriting:** existing WIM destinations are refused.
  No input SWM part is mounted, changed, moved or discarded.
  Cancellation terminates the child DISM process before any
  application-owned temporary export file is removed. If process
  termination cannot be verified, the temporary file is retained
  for manual examination instead of deleting files while in use.
- Persistent SWM tool window geometry, cancellable progress,
  readable controls and sticky bottom actions at 125/150/175% DPI.
  The SWM source form scrolls vertically without horizontal clipping.

## Cross-instance mount safety

- Legacy **WIM Mount** and **Commit/Discard** acquire machine-local
  cooperative file-share locks for their WIM and mount directories
  throughout the DISM operation. Colliding DISM GUI instances on
  the same computer fail before launching conflicting commands.
- All handles are released by Windows if the owning application
  exits; persistent zero-byte lock filenames must not be deleted.
- These locks do **not** control manually launched DISM, other
  tools, remote computers, all legacy servicing commands, or
  different paths resolving to the same image; plan operations
  accordingly. No global DISM exclusivity or automatic rollback
  is claimed.

## Verification and limitations

- Verified GitHub Windows x86/x64/ARM64 builds and x64 WinForms
  DPI 125/150/175% checks.
- Real disposable Windows DISM test creates a multipart SWM,
  verifies every part, exports index 1 to a new WIM, then
  **applies the new WIM** into a temporary test folder.
  Tests also detect missing, altered, wildcard-colliding parts,
  cancelation, destination collision, and unchanged source hashes.
- Split SWM is immutable per Microsoft DISM documentation.
  The new export does not modify an existing split set.
  Actual Windows 11/Server install-image deployments and driver
  servicing still require testing with safe image copies.

Original DISM GUI attribution, existing updates and diagnostics
are retained. The universal updater remains unintegrated.
