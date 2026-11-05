# DISM GUI 4.3.8

## Integrated Test Center — test everything together

- Added **Tools > Run complete safe tests...** and **WIM 360 > Run complete safe tests**.
- One resizable, separately remembered WinForms window tests local Windows/.NET environment, administrator rights, physical display DPI, DISM availability, and selected WIM/ESD/SWM image metadata.
- Optional **already-mounted** servicing audit reads driver packages, Windows packages, optional features and capabilities; it is OFF by default. The Test Center never mounts, edits, commits, discards, installs, or changes images.
- Consolidated PASS/WARN/FAIL/SKIP results with progress, tooltips, cancel/close, local Copy and Save report (sanitized UTF-8), and the reviewed GitHub Issue-draft workflow. No automatic desktop uploads.
- Integrated Test Center is accessible directly from both the main Tools menu and WIM 360.

## WIM 360 — read-only split SWM metadata

- Accepts first or later split parts such as \`install.swm\` and \`install2.swm\`.
- Validates every required part's WIM signature, spanned flag, header part number/count and matching set identity; identifies missing, mismatched or incomplete sets.
- Uses query-only WIMGAPI access to the first SWM part for image-index metadata, description, architecture and build information; no write/reference operation is performed.
- Displays the complete part count and combined size. **Mount**, **Use index in Mount**, and mounted servicing are disabled for split sets; this release does **not** edit or apply SWM images.
- New integration test uses a disposable real DISM /Split-Image set, selects both parts, verifies missing-part detection and asserts stable SHA-256 checksums.

## Regression and limitations

- Existing Error Diagnostics (user-reviewed GitHub Issue drafts), CI recurring-error diagnostics, WIM 360 partial audit and Driver Studio safety remain intact.
- Windows CI validates the WinForms Test Center layout, DPI reporting at sample scales, sanitized report output, WIM/SWM metadata and disposable read-only WIM mount; portable archives cover x86, x64 and ARM64.
- Header/metadata inspection is not equivalent to a complete corruption scan. Windows 11/Server install.wim driver/package enumeration and 125%-200% live DPI should be tested on a separate disposable workstation before production use.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
