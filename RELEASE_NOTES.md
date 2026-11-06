# DISM GUI 4.3.18 — Stable

Portable, self-contained Windows builds for x64, x86 and ARM64
(Visual Studio 2026 / .NET 10). Previous stable 4.3.17 is retained.

## WIM 360 — safe Commit/Discard and Mount health

- Legacy **Dismount WIM** now performs a fresh read-only WIMGAPI
  verification of the exact WIM/ESD source, numeric image index, registered
  mount directory and access mode **before starting DISM**.
- **Commit** requires an active `Mounted (R/W)` registration. **Discard**
  requires a valid active R/W or read-only mount. Missing files, stale
  mount selection, linked mount roots, changed registration or an
  incorrect index reject the operation without calling DISM.
- A second validation occurs immediately inside the background worker,
  before either of the legacy Commit or Discard DISM launch branches.
  It guards against an external mount change after the user confirms.
- **Atomic local recovery journal:** a `Pending` JSON entry is persisted
  before DISM starts. The operation is blocked if the entry cannot be
  written. After execution, the application checks both the DISM exit
  status and WIMGAPI registration to distinguish `Verified` from
  `Review required`. A process/power interruption leaves the pending
  record for review instead of assuming success.
- If DISM successfully dismounts the image but the journal cannot be
  finalized, the Mount UI still reflects the verified dismount, while
  the user sees an explicit journal warning rather than a false success.
- **Mount health** in WIM 360 shows the selected source's registration,
  local recent operation history, and safe recovery guidance. Actions
  are limited to Refresh, Copy report, Close. The dialog is English,
  DPI-aware, and retains its size/position.
- No automatic retry, cleanup-mountpoints, remount, rollback, Commit or
  Discard. Existing Driver Studio, WIM 360 comparisons and Universal
  Diagnostics remain unchanged.

## Automated verification

- Windows x86 / x64 / ARM64 compilation and packaging.
- Synthetic tests: mismatched WIM index/root, read-only Commit blocked,
  invalid mount state, pending/failed/verified local journal and
  DPI-aware Mount health window at simulated 125%, 150%, 175%.
- Elevated Windows x64 CI creates a disposable WIM, copies it to an
  isolated file, mounts it **read/write**, adds a test file, commits and
  verifies persistence; then mounts it again, adds a second test file
  and discards, confirming that the second file did NOT persist and
  that the post-Commit WIM hash stayed unchanged. A final read-only
  mount verifies content and dismount cleanup. No production image
  or actual Windows servicing installation is altered.

## Safety limitation

DISM Commit is not an atomic, reversible transaction. Sudden power loss
or process termination may leave partially persisted content even if
the local journal says Pending. The program does NOT automatically
retry, clean or discard an uncertain mount. Inspect DISM logs and
registered mount state, and use only disposable image copies for
initial testing. These tests do not validate real Windows 11/Server
driver/package servicing or every storage/permission configuration.

Original DISMGUI by Mike Celone (mikecel79/DISMGUI); original attribution
and existing distribution notices retained.
