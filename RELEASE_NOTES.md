# DISM GUI 4.3.11 — Stable

This is the stable successor to 4.3.10. Portable Windows builds: x86, x64 and ARM64.
The previous v4.3.10 release assets remain unchanged.

## WIM 360: clearer comparison of multiple indexes
- **Automatic Inspect overview:** read-only metadata summary of every index in a WIM/ESD, including editions, OS builds, architectures and languages, with suggested comparison pairs.
- **Compare suggested pair:** open the existing multi-index comparison window with the two recommended indexes preselected, and the new **Differences only** filter enabled.
- **Differences only** shows unique/missing files, changed values and ambiguous duplicate identities while hiding identical entries; other Present, Missing, Different, type and text filters remain available.
- Filters are retained when comparisons are repeated where still applicable. File and folder comparisons use the existing non-mounting path; metadata inspection alone does not claim file hashes or contents were checked.

## UniversalDiagnostics integration
- Embedded `universal-diagnostics/v1` support inside the existing portable EXE. No additional executable or DLL, service, token or background uploader.
- Manually reviewed, redacted reports can be saved in a local per-project queue. Saved Diagnostics supports searching, viewing, copying, exporting and deleting local records.
- A selected saved report can open a **prefilled GitHub Issue draft** only after explicit confirmation. GitHub sign-in and manual submission are still required; opening a draft does not mark a report as delivered.
- Existing local DISM log and Error Report tools are preserved. Sensitive paths, credentials, keys, emails and host identifiers are redacted, but users should review the entire preview before sharing.

## High-DPI and window layout improvements
- Improved WIM 360 Inventory and Snapshot Comparison filter/search widths, with fewer form-level horizontal scrollbars at simulated 125%, 150% and 175% scaling.
- Made Integrated Test Center, Error Diagnostics, About and Update Center buttons and content responsive. Error Diagnostics now uses native Windows line breaks for readable preview headings, descriptions and checklists.
- Version labels use numeric notation without a leading `v`; update changelog remains scrollable and accessible.
- Original DISM servicing controls, read-only inspection, window geometry persistence, and portable packaging are retained.

## Verification and safeguards
- GitHub Actions Windows builds run for **win-x86, win-x64 and win-arm64**, with automatic WinForms layout tests, read-only WIM smoke tests, multi-index comparisons, diagnostics queue/redaction tests and simulated font-scale checks.
- Image comparison does not automatically mount, commit, modify or replace WIM/ESD images.
- Do not share secrets in the report; inspect the sanitized preview and confirm the GitHub Issue body before submitting.
- Physical workstation testing at each DPI/architecture and production images remains recommended.

Originally based on Mike Celone's DISMGUI (mikecel79/DISMGUI); attribution preserved.
