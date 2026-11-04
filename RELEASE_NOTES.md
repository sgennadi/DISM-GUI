# DISM GUI 4.3.1

## Driver Studio — Smart Driver Detection (preview)

- A dedicated **Driver Studio** workspace alongside WIM 360, without changing the original nine servicing sections.
- Scan a driver folder, subdirectories or a single INF and read its **Provider, Class, DriverVer date/version, hardware IDs and INF SHA-256**.
- Detect duplicate source INF files.
- Compare against the installed drivers of one **already mounted** WIM index using structured **Get-WindowsDriver** output.
- Before comparison, verify that WIM file, image index and mounted folder all refer to the same image.
- Show **New, Newer, Same version, Older, Review, Duplicate source** or **Not checked** with an explanation; default plan selects only appropriate New/Newer entries.
- A driver with a newer version but older DriverVer date is flagged **Review** due to Windows driver ranking.
- Per-model INF DriverVer overrides are conservatively flagged Review.
- Offline/unmounted/unknown inventory is **Not checked**, never incorrectly shown as all new drivers.
- Read-only comparison grid with adjustable selection checkboxes, detail pane, Copy comparison report.
- **Open Drivers section** warns that the original Add drivers action imports the entire folder, regardless of skip choices.

**This version does not selectively install drivers.** Driver Studio's selections currently form a preview only, not an import list consumed by the original Add drivers command. Checking the same version does not establish identical signatures or binaries. This design deliberately avoids modifying images based on incomplete comparisons.

## UI and prior features
- Compacter Driver Studio navigation and results layout for smaller windows.
- WIM 360 read-only image metadata, index selection and Mount handoff.
- Original nine DISM sections preserved, resizable output log, saved window size/position.
- Tools update indicator, Update Center with changelog and SHA-256 verification.
- About / Tools Email Support: sgennadi@hotmail.com.
- Portable self-contained .NET 10 x64, x86 and ARM64 Windows builds.

## Tests and scope
- Windows CI tests INF parsing, provider variables, date/version comparison, duplicate sources, ambiguous/incomplete data, inventory JSON, native WIM 360 inspection and live WinForms layout.
- Native Get-WindowsDriver queries require the selected image index to be mounted. No automatic mount, install, commit or discard operations.
- Real-world driver catalogs, mounted image access and 150-200% DPI still require hands-on testing on the target workstation.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
