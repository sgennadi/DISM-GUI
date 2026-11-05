# DISM GUI 4.3.10 RC1 (test release)

This is a **pre-release** for testing on Windows. It does **not** replace or auto-update the installed stable DISM GUI 4.3.9. Extract the ZIP to a separate folder. Run the application as administrator for DISM servicing operations.

## WIM 360: compare several indexes
- Compare 2-8 indexes from the same WIM or ESD side by side. Select indexes 1, 2 and 3 to find paths unique to one index, missing from another, common to all, or different.
- List files and folders directly from unmounted images using the Windows DISM PowerShell API, with Unicode-safe handling of Russian, Hebrew and other filenames.
- Compare metadata without mounting; compare file sizes and verify individual file SHA-256 after mounting; compare drivers, packages, Features and Capabilities after complete servicing audits.
- Filters by file type, missing/present index, differences and free text; filtered CSV export and CSV/JSON index metadata export.
- Source-WIM full SHA-256 is checked before and after offline listing. Windows may update its timestamp even for read-only enumeration; the original timestamp is restored only when the full hashes match.
- Cancellable comparison and SHA-256 operations, progress reporting, reduced memory use for large index catalogs.

## Interface
- Shared font-aware button sizing, clearer labels, scrollable dialogs and updated layouts across WIM 360, Driver Studio, Diagnostics, Test Center and Update Center.
- Three compared indexes and all Compare Indexes actions remain visible in compact high-font layouts.
- Shortened the sidebar caption to **01 Mount**; removed the leading `v` from user-visible version labels.
- Resizable DISM Output area and readable toolbar; window size and position persistence.
- More explicit Drivers handoff and error/support dialogs; support contact `sgennadi@hotmail.com`.

## Servicing reliability
- Corrected malformed `/Index` quoting for DISM Mount-WIM and Apply-Image commands (Error 87).
- Driver Studio duplicate/same/older/newer INF checks and guarded selective import into verified mounted indexes.

## Validation and limitations
- Tested three-index WIM comparisons including real DISM-generated images, Russian and Hebrew paths, missing files, empty folders, and unchanged source SHA-256.
- Tested a synthetic >1 GiB, three-index WIM and WinForms layouts using simulated 125%, 150%, and 175% UI font scales. This does not replace manual hardware/DPI testing.
- No automatic mounting, committing, or destructive image operations from Compare Indexes.
- GitHub App device authorization and direct issue submission from end-user machines are in separate draft PR #9 and not part of this version.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.


## Recommended RC checks
1. Open WIM 360, Inspect a copy of `install.wim`, select indexes 1/2/3 in Compare Indexes, then choose **Files + folders (no mounting)**.
2. Test **Missing from Index 2**, **Only in Index 3**, **Different**, **Exists in all**, search and CSV export.
3. Inspect Driver Studio, packages, settings, Update Center and Diagnostics at your actual Windows display scaling.
4. Mount a disposable image and confirm `/Index:1` and `/MountDir:` are passed correctly (DISM Error 87 regression).
5. Leave production images unchanged until this RC has been manually verified.

This release is based on GitHub Actions smoke tests and screenshot review; real Windows DPI profiles and large production install.wim files still need field testing.
