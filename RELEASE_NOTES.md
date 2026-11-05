# DISM GUI 4.3.7

## Error Diagnostics and GitHub Reporting

- New **Tools > Report error to GitHub** and **About > Report error to GitHub** actions open a dedicated resizable support window with a sanitized, editable report preview.
- Includes application version, Windows/.NET architecture, display DPI and latest recorded WIM 360 error. DISM output is **opt-in** (last 60 lines), not automatically attached.
- Scrubs common credentials, BitLocker recovery passwords, URLs, IP/MAC addresses, emails, GUIDs and private paths. Human review checkbox is mandatory, and editing the report revokes approval.
- Opens a GitHub Issue **draft**, copying the sanitized report to clipboard. User signs in and submits in the browser; no unattended application uploads or GitHub credentials.
- Remembers support dialog placement independently and retains direct email support at sgennadi@hotmail.com.

## Automatic GitHub CI Error Analysis

- **Pre-build diagnostics** run before code generation/build on x86, x64 and ARM64 to catch missing modules, invalid versions, unsafe WIM code changes and known VB.NET compile regressions.
- Added a dedicated GitHub workflow that runs **after failed builds and every day**.
- Groups failed-step names and compiler error codes by signature across independent failed runs in the preceding 14 days. Creates or updates one recurring-failure GitHub Issue only when a signature occurs in two or more builds.
- Issue contents include only allowlisted step labels, compiler error codes and investigation guidance; never raw logs or machine details.
- Added parser/redaction and WinForms regression tests. CI error analysis uses the repository's ephemeral GitHub token exclusively in GitHub Actions.

## WIM 360 — Resilient Partial Audit

- Independent driver, Windows package, optional-feature and capability reads; failures in one category preserve successful inventories from the others.
- Visible **PARTIAL AUDIT** warnings instead of misleading zero counts, with per-stage progress and timings.
- Sanitized error diagnostics appear in the inventory and JSON snapshots. Snapshot comparison suppresses false removals from incomplete categories.
- Existing read-only image/index/mount revalidation, Driver Studio write guards, and real disposable WIM mount tests remain unchanged.

## Testing and limitations

- WinForms and test logic run in Windows x64 CI; portable EXE builds cover x86, x64, ARM64.
- Full real Windows installation servicing, alternate monitor scaling and SWM support still require further testing/development.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
