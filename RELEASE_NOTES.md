# DISM GUI 4.3.4

## WIM 360 — Detailed Mounted Inventory

- Added an **Inventory details** window for an already-scanned WIM index, kept separate from destructive servicing.
- Full driver table (INF, provider, driver class, published INF, version/date), Windows package table (identity, release type, state), optional Windows features and Windows capabilities (names/states).
- Uses read-only Get-WindowsOptionalFeature -Path and Get-WindowsCapability -Path queries. Failures are isolated per optional component category and a visible warning preserves partial inventory.
- Responsive, resizable high-DPI inventory window; virtualized table, horizontal scrolling and tooltips for long values. Window position and dimensions are saved independently of the main window.
- Combined Type/State/substring filtering, column-header sorting and Clear filters.
- Export displayed filtered rows to **CSV** (UTF-8 BOM, quote escaping and spreadsheet-formula safeguards) or **JSON** (versioned schema, source image/index, UTC timestamp and partial scan warnings).
- Shows features/capabilities counts in the WIM 360 summary and copied report.
- Adds Windows CI regression coverage for parsing, filtering, invalid JSON, exports, complete/partial results and the inventory window.

## Retained from 4.3.3

- Image/index/mount validation before and after scanning; read-only driver/package inventory.
- Driver Studio selective per-INF write remains separate and requires explicit user confirmation.

## Testing and limitations

- The real mounted-image audit still requires testing against disposable Windows/WinPE WIM files; Windows CI validates mocks, compilation and the WinForms UI.
- Some image types do not expose feature/capability information. Partial results are labeled; do not treat an absent list as proof the image has no features.
- Split .swm files are not supported in WIM 360; re-scan after changing an image.
- Attribution: Mike Celone (mikecel79/DISMGUI).
