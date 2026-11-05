# DISM GUI 4.3.5

## WIM 360 — Offline Snapshot Comparison

- Added **Compare JSON snapshots**, a separate resizable high-DPI viewer for two locally saved WIM 360 inventory JSON files; no WIM mounting or DISM servicing.
- Displays **Added**, **Removed**, and **Changed** driver packages, Windows packages, optional features, and Windows capabilities, including before/after states, versions, providers and changed-field descriptions.
- Interactive virtualized comparison table, filters by change/type, free-text search, column sorting, and **Export CSV / Export JSON** for the currently displayed changes.
- Snapshot exports upgraded from schema 1 to **schema 2**, which includes the source's total inventory row count and an independently verified **IsCompleteInventory** flag.
- **Safety:** Added/Removed detection requires *both* snapshots to contain the complete inventory. Legacy schema-1 and filtered snapshots suppress membership changes rather than guessing missing records are deleted.
- Unsupported optional feature/capability queries suppress membership comparisons for those categories individually; version/state comparisons of shared records still work.
- Duplicate inventory identities are flagged as ambiguous; inputs have size, type, index and schema checks, and spreadsheet-formula-safe CSV output.
- The comparison dialog saves its size and position separately, shows warnings and is available even without a mounted image.
- Extended CI tests cover legacy and filtered exports, before/after version and state changes, added/removed counts, partial category warnings, ambiguous identities, exports, and real WinForms interaction/layout.

## Preserved functionality

- **WIM 360 / Scan mounted index** remains read-only and requires a verified existing mount.
- **Inventory details** retains searchable driver/package/feature/capability results and CSV/JSON export.
- **Driver Studio** selective per-INF installation remains separate and requires explicit confirmation.
- No implicit Mount / Unmount / Commit / Discard.

## Validation and limitations

- Windows CI builds portable .NET 10 versions for x86, x64 and ARM64 and tests the WinForms UI.
- Live component and driver inventory must still be verified on disposable mounted Windows/WinPE WIMs. Diff tests use representative saved inventories.
- WIM 360 still does not support split .swm image sets.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
