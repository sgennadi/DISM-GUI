# DISM GUI 4.3.20 — Stable

Portable, self-contained .NET 10 / Visual Studio 2026 Windows builds for
x86, x64 and ARM64. Previous stable release 4.3.19 remains available.

## Cross-instance image-operation protection

- Introduce a common, cooperative, machine-local image writer lock in
  **WIM Mount**, **Commit / Discard**, **Driver Studio selective INF import**,
  **SWM to WIM export**, and the original **legacy DISM command worker**.
  The latter includes legacy Packages, Features, Driver commands,
  Capture, Append, Apply and Cleanup. It also serializes legacy
  read-only commands in that worker, prioritizing predictable ordering.
- Driver Studio holds its exclusive lease throughout the entire INF
  batch, from fresh mount/inventory checks to staged-driver postflight.
  It does not release the lock between individual INF imports.
- Locked resources are released through Windows when an owning process
  terminates; zero-byte lock files persist and should not be deleted.
  Colliding DISM GUI writers fail clearly before starting a conflicting
  operation, without an automatic rollback or DISM mount cleanup.
- This is NOT a universal DISM lock. Manually run DISM, external software,
  disconnected systems, remote hosts and third-party writers remain
  outside this cooperative protocol.

## SWM export destination capacity

- Before exporting an index from immutable SWM parts into a **new WIM**,
  verify disk space available **to the current account** on the selected
  destination using Windows GetDiskFreeSpaceEx.
- Require at least the greater of 1 GiB, 2 times all input SWM part
  bytes, or 60% of the selected image's uncompressed size, PLUS
  512 MiB of headroom. Reject unknown capacity, underflow/overflow,
  missing directory and insufficient capacity without starting DISM.
- The SWM dialog shows current available space and the required
  estimate before confirmation. The operation rechecks again in the
  worker and **immediately before launching DISM**.
- This estimate is intentionally conservative, but **does not reserve
  space**. Another process can consume capacity during export.
  The normal output-file collision, source SHA-256, partial-file cleanup
  and destination-verification protections continue to apply.
- SWM sources remain read-only and a pre-existing destination WIM is
  never overwritten.

## Verification and limitations

- GitHub Actions: Windows x86, x64 and ARM64 portable builds.
- Elevated Windows x64 CI: disposable DISM-created WIM and split SWM;
  full source SHA-256 audit, free-space gate, export to standalone WIM,
  Apply-Image into scratch folder and WIM Mount/Commit/Discard tests.
- Synthetic tests: global write-lock collisions between different
  paths, safe lease release, low-space rejection, overflow rejection,
  source integrity, UI controls and 125/150/175% simulated DPI layouts.
- No test media containing a genuine Windows 11 or Windows Server
  installation image with signed OEM drivers/packages was supplied.
  Such end-to-end servicing and real production network shares are
  NOT covered by these automated fixtures. Start with disposable
  image copies and do not use first on production sources.

Universal updater remains unintegrated, as requested. Existing
diagnostics, UI defaults, WIM 360 and Driver Studio behavior is retained.
Original DISMGUI attribution and redistribution notices remain.
