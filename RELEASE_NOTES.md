# DISM GUI 4.3.13 — Stable

Portable, self-contained Windows builds for **x64, x86 and ARM64**.
This release focuses on the actual WinForms layout problems reported with
DISM GUI 4.3.12. The previous stable release stays available unchanged.

## Navigation and driver workflows

- **WIM 360** includes a nested **Mount** entry and **Driver Studio**
  includes a nested **Driver tools** entry. These retain the original
  Mount/Dismount, driver addition, removal and export DISM handlers.
  The navigation-only grouping does not automatically mount, commit,
  discard or install packages.
- Other sections have been renumbered without gaps: **01 Packages,
  02 Features, 03 Editions, 04 Unattend, 05 Apps, 06 Capture, 07 Apply**.
- Driver Studio exposes **Scan INF** in the persistent workspace header,
  so a user on a smaller laptop at 175% scaling can initiate the same
  existing scan even when the detailed source controls are below the fold.
  The source pane also has more height when its fields stack vertically.

## Display and high-DPI fixes

- **Tools** gets a high-contrast dark-orange background, white lettering,
  an exclamation mark and a gold border when a newer verified stable
  release is available. It returns to the normal white style when the
  startup check finds no newer release or cannot verify one.
  The update still requires the user's explicit action.
- In **WIM 360 Compare Indexes**, the brief source WIM SHA-256 verification
  and timestamp note remains visible above the action buttons.
  The full explanation of offline name/presence-only comparison is
  preserved in the report and tooltip; verifying the WIM container
  checksum is **not** per-file hash comparison.
- The **Compare indexes**, **Verify SHA-256**, **Export CSV** and **Close**
  actions remain visible with a readable results grid at simulated
  font scaling of 125%, 150% and 175%.
- The Mount workspace has more space around **Choose folder**, and
  **Open mounted folder** has a compact **Open folder** caption with an
  accessible description.
- The redundant **Exit** button was removed from the **DISM Output**
  toolbar. The standard Windows title-bar X still closes the application;
  Expand, Copy log and Clear remain available.

## Build and compatibility

- Existing WIM 360 index inspection, driver comparison, DISM operations,
  portable packaging, self-update and local UniversalDiagnostics remain.
- GitHub Actions performs Windows x86/x64/ARM64 builds, read-only WIM tests,
  diagnostics checks, plus simulated 125/150/175% WinForms layout tests.
- Real operating-system servicing and physical DPI testing on a workstation
  remain recommended before applying changes to production images.
- No automatic GitHub diagnostic upload and no new external runtime process.

Based on DISMGUI by Mike Celone (mikecel79/DISMGUI); original attribution
and licensing remain intact.
