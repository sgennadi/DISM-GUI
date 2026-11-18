# DISM GUI 4.3.27 — Stable

Portable x86, x64 and ARM64 Windows builds using Visual Studio 2026
and self-contained .NET 10. Maintenance release for the **Mount**
workspace following operator screenshots showing the Choose folder
caption clipped in a small window, while preserving the v4.3.26
Driver Studio chooser/scan swap.

## Mount: readable paths and actions on compact / high-DPI screens

- Removed fixed 157px width assumptions for **Choose WIM** and
  **Choose folder**. Their layout cells are measured from the
  actual button captions and current UI font.
- The right **Mount WIM**, **Dismount WIM**, **Open folder** and
  **Display WIM info** action area also sizes for full captions.
- At narrow window widths and/or large text scaling, keeping two
  side-by-side columns could shrink the WIM path textbox to 1px and
  overlap its Browse button. The Mount workspace now adapts:
  WIM path, image index and mount folder occupy the upper full-width
  source area; the four existing actions move below in a readable
  two-column/two-row layout, with vertical scrolling when needed.
- Wide windows keep the familiar actions column on the right.
- The original DISM button instances, click handlers, selected path,
  image index, read-only option and Mount/Dismount confirmations
  remain unchanged.

## Driver Studio (included from 4.3.26)

- **Choose folder** is at the top-right workspace header and
  **Scan / Refresh** is adjacent to the source INF field (or directly
  below on high-DPI/narrow screens). The path box has adequate height.
- Scan/Refresh remains read-only, automatically comparing verified
  already-mounted WIM indexes; no automatic import, Mount, Commit or
  Discard occurs.

## Validation

- Windows x86/x64/ARM64 builds: PASS.
- Hosted x64 real WIM and WinForms layout smoke: PASS.
- Actual simulated client-size tests of **765×580**, 900×650 and
  1180×760, each at standard and simulated 125%, 150%, 175%, 200%
  UI font scaling: PASS. Test asserts readable Choose WIM / Choose
  folder captions, no overlapping WIM and mount folder fields, and
  responsive Mount controls inside the viewport.
- OEM signed driver execution and Windows WIM servicing code are
  unchanged. The separate hosted OEM VM workflow is intentionally
  path-filtered for DriverStudio/OEM changes and is not triggered by
  this layout-only Mount update.
- The test scaling is simulated; manual verification on the user's
  actual Windows display remains recommended.

## Safety and rollback

The fix changes only layout and accessibility. No alteration to
WIMMount registry operations, driver addition/removal, COMMIT/DISCARD,
admin authorization, image safety checks or fallback recovery.

The original [DISM GUI 4.3.26](https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.26)
stable ZIP files remain published for rollback.

Upstream attribution and licensing remain intact.
