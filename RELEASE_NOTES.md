# DISM GUI 4.3.23 — Stable

Portable, self-contained .NET 10 / Visual Studio 2026 builds for
Windows x86, x64 and ARM64. This maintenance release improves
Driver Studio workflow clarity and safety following operator feedback
on stable 4.3.22.

## Driver Studio: read-only comparison by default

- **No automatically selected INF packages.** After Scan/Compare, all
  import checkboxes start unchecked, including New and Newer packages.
  Scanning and comparing drivers never creates a WIM write plan.
- **Select New / Newer** marks eligible driver packages only when you
  explicitly request it, then **Clear** can remove all checks.
- **Audit selected** and **Audit all INF** are grouped side by side
  above the comparison table. Both actions are read-only.
  Audit selected checks marked INF packages (or the highlighted INF
  when none are marked); Audit all checks source packages, up to 30
  INF packages per audit batch.
- **Add selected to WIM...** replaces the misleading button text
  *Install selected INF*. It is an OPTIONAL, separately confirmed
  write operation that stages selected, audited packages inside a
  mounted **read/write offline WIM**, not in the currently running
  Windows PC.
- Existing OEM driver versions are not deleted or overwritten by
  DISM Add-Driver; Windows PnP selects a driver using matching and
  ranking, not strictly the highest version string.
- Same version, Older, Review and Not checked packages remain
  ineligible for this selective import. Package signature and full
  source-manifest audit, WIM registration, architecture and inventory
  checks still fail safely. No automatic Mount, Commit, Discard,
  Remove-Driver or ForceUnsigned actions are introduced.
- The Package details area no longer hosts a detached Audit button,
  leaving more room for long INF details, especially at high DPI.

## Validation

- Tested on Windows x86, x64 and ARM64 build runners, including
  pre-build checks and portable EXE packaging.
- x64 Windows WIM and WinForms smoke tests, including explicit
  zero-selected default, opt-in New/Newer selection, Clear behavior,
  both audit commands grouped and simulated 125–200% font scaling.
- Signed OEM servicing on a hosted disposable Windows VM.
- The earlier signed Realtek ARM64 copy-only field acceptance from
  v4.3.22 remains valid; this release does not change OEM integration.

## Compatibility, notes and rollback

- Only the Driver Studio UI workflow, labels, documentation and
  related UI regression checks changed from 4.3.22. Driver
  installation engine behavior, security preflight and WIM contents
  are unchanged.
- The legacy **Driver tools → Add drivers** command still uses the
  selected source folder (with Recurse if enabled) without the
  selective New/Newer filter. Use Driver Studio for selective WIM
  staging when needed.
- `v4.3.22` and its original download archives remain published
  as an unchanged fallback:
  https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.22
- Manual validation on production images and physical machines
  remains recommended before deployment.

Original DISMGUI attribution and redistribution notices remain intact.
