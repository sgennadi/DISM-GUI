# DISM GUI 4.3.22 — Stable

Published Windows portable builds for x86, x64 and ARM64, created with
Visual Studio 2026 / self-contained .NET 10. This release replaces
4.3.21 as **latest stable**; v4.3.21 and its original ZIPs are preserved
for rollback.

This update includes improvements merged after stable v4.3.21,
covering PRs #38 through #43. Install the appropriate archive for
your host architecture; run as administrator for image servicing.

## Driver Studio: clearer comparison and safer selections

- **Review** marks conflicting or ambiguous source INF packages;
  **Not checked** means the mounted WIM driver inventory has not been
  verified. Neither is proof that the image lacks the driver.
  Per-row details and tooltips now explain which condition applies.
- On returning to Driver Studio after a successful mount, a previously
  scanned source can be compared automatically against the selected
  mounted WIM index. The index and mount registration must still verify.
- **Select all eligible** selects only verified **New** or **Newer**
  driver packages. **Clear** deselects the plan. **Audit all INF**
  checks source packages/CAT signatures without installing or mounting;
  audits remain bounded to 30 INF packages per batch.
- Navigating from **Driver Studio** to **Driver tools** transfers the
  selected source folder to the Driver folder field. For a single INF,
  the parent folder is shown. Merely navigating never installs drivers.
- **Newer** means the incoming INF's version is newer than the matching
  driver in the mounted image. Adding it stages a package; it does
  **not** automatically remove the older version. At boot, Windows PnP
  performs device matching and driver ranking.
- The legacy **Driver tools → Add drivers** action still processes
  its chosen folder (including subfolders when Recurse is enabled)
  without Smart Driver Studio's New/Newer filtering. Use Driver Studio's
  selective import if you need a filtered installation plan.
- A source/package audit or a checked-looking row is never sufficient
  to authorize a write: verified WIM inventory, signed package checks,
  correct architecture, a mounted read/write target, explicit approval
  and postflight inventory checks still apply.

## WIM Mount safety and recovery

- Detect registered mount collisions **before** starting DISM:
  already-mounted WIM/index, `Invalid` or `Needs Remount` status,
  occupied mount directories and uncertain registration states.
- Prevent repeated `/Mount-WIM` commands that would fail with
  `0xC1420127`. The program offers read-only **WIM 360 → Mount health**
  instead of silently retrying or deleting WIMMount registry entries.
- **Mount Recovery** provides read-only state and lock diagnostics,
  interrupt/restart checkpoints and explicit handling of uncertain
  writes. It never automatically deletes folders, performs
  Commit/Discard, cleans registrations or destroys uncommitted work.
- Operation Center offers guided checks, persistent profiles, bounded
  run history and local queue serialization. Explicitly approved
  one-index export writes to a new WIM only after source verification
  and capacity and destination safeguards; existing output is never
  overwritten.

## Signed OEM ARM64 integration

- Fixed false-negative staged-driver verification when Windows
  numerically normalizes INF `DriverVer` components, for example
  `1152.23.20.0605` versus `1152.23.20.605`. Matching INF,
  provider, class and date remain required; malformed inventory fails
  safely instead of authorizing Commit.
- The isolated Windows 11 ARM64 field test with signed Realtek
  `rtu52cx22arm64sta.inf` passed: driver inventory
  **410 → 411 → 411** (before, staged, committed), with successful
  read-only remount and unchanged original WIM SHA-256.
- Improved local OEM verification diagnostics and packaged the
  four-stage CMD acceptance launcher with the opt-in test runner.
  The OEM runner is available from CI artifacts, not as part of the
  regular DISM GUI portable release.
- This validates driver staging and persistence in a disposable
  image; it does **not** certify hardware binding or device operation.

## Verification

- Successful x86, x64 and ARM64 Windows CI build and portable
  packaging checks for the functional changes.
- WinForms tests for Driver Studio mass selection, source folder
  handoff, status explanations and simulated 125%, 150%, 175% and
  200% font scaling.
- Real disposable WIM mount/read/Commit/Discard tests, Mount Recovery
  safety and driver inventory regression checks.
- Hosted Windows VM signed-OEM servicing verification and the
  operator's ARM64 WIM/INF copy-only field acceptance.
- The final v4.3.22 tag and downloadable checksums are generated
  only after all three release builds succeed.

## Scope, limitations and rollback

- There is **no** automatic recovery or deletion of broken WIM mounts
  and **no** removal of older OEM drivers during `/Add-Driver`.
- Cooperative application locks cannot prevent external DISM tools,
  other administrators or processes from modifying the image.
- Real production Windows 11, Windows Server 2022 and Server 2025
  installations, and physical DPI/driver behavior, still require
  environment-specific validation before deployment.
- If necessary, uninstall/replace the portable folder with the
  archived [v4.3.21 release](https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.21).
  Do not treat a discarded, uncommitted mount as a reversible installation.

Original DISMGUI authorship and redistribution notices are retained.
