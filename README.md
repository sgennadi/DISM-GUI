# DISM GUI - Visual Studio 2026 / .NET 10

Modernized from [mikecel79/DISMGUI](https://github.com/mikecel79/DISMGUI). Original author credited.

## Builds
- Windows x64: `DISMGUI-VS2026-win-x64.zip`
- Windows ARM64: `DISMGUI-VS2026-win-arm64.zip`
- Windows x86: `DISMGUI-VS2026-win-x86.zip`

**Download**: [Actions -> latest successful build -> Artifacts](https://github.com/sgennadi/DISM-GUI/actions/workflows/build.yml). Each archive contains a portable self-contained application.

## Driver Studio — Smart Driver Detection

Driver Studio scans a folder or individual driver INF files, optionally
recursing through subfolders. It extracts the DriverVer date/version, INF
name, manufacturer, class, selected hardware IDs and source-INF SHA-256.

To check whether the *same version* is already present, first mount the
chosen WIM index using **Mount & Inspect**. Then choose **Compare with
mounted index** in Driver Studio. The tool verifies that the WIM file,
index, and mount directory match and queries structured
`Get-WindowsDriver -Path <mount-folder> -All` metadata. If the index is
not mounted, every unverified candidate is labeled **Not checked**.

**Same version** means the same INF name, provider, class, version and
date; this does not guarantee identical signed driver files. Driver Studio
flags per-model DriverVer directives and version/date ranking conflicts as
**Review**. After comparing a **read/write-mounted** index, select the desired **New** or
**Newer** packages and use **Install selected INF**. Driver Studio confirms
the WIM path and index, re-checks mount status, the installed-driver inventory
and source INF hashes, then invokes DISM separately for each selected INF.
**Same version, Older, Review, Duplicate source** and **Not checked** are
never eligible. A changed source or inventory invalidates the plan.

The **Stop after current** control finishes the active DISM command before
ending the batch to avoid interrupting a write. Driver Studio does **not**
mount, unmount, commit, discard or force unsigned packages automatically.
After reviewing the outcome, use Mount & Inspect to explicitly commit or
discard the image. The legacy Drivers section's Add drivers button still
imports an **entire folder**: its behavior is separate from the selective importer.

Use disposable copies of WIM images for initial real-world tests. DriverVer
equality does not prove identical binaries, package signatures or Windows
device ranking. An imported package may not be selected by PnP at boot.

## WIM 360 — Image Studio

Select **WIM 360** at the top of the left navigation. Choose a `.wim` or
`.esd` file, or select one in **Mount & Inspect** first. Click **Inspect** to
read all image-index metadata through the Windows WIMGAPI in read-only mode.
Select an index to view architecture, build, edition, languages and
mount state. **Use index in Mount** transfers the selected WIM path and
index to the original Mount form but **never mounts or modifies the image**.
Use **Copy report** to share the read-only inventory.

For an index that is **already mounted**, select it and click **Scan mounted
index**. WIM 360 validates the exact image file, index and mount directory,
then reads the driver inventory (including inbox drivers) and Windows package
inventory with the DISM PowerShell module. The details and copied report
show total/third-party/inbox drivers, common driver classes and providers,
installed/staged/pending packages, optional Windows features, Windows
capabilities and a scan timestamp. This is **read-only**,
works with read-only mounts, and can take several minutes. It never mounts,
installs, commits, discards or changes the image. Re-run **Inspect** after
external mount changes and re-scan after installing drivers or packages.

After scanning, open **Inventory details** for the full per-item catalog:
driver original/published INF, class, version and vendor; Windows package
state and release type; optional Windows features; and Windows capabilities.
The separate resizable window supports combined Type, State and text filters,
column sorting, and long-value tooltips. Export filtered results to CSV
(UTF-8 with BOM and safe spreadsheet escaping) or JSON (schema version, source
image/index, timestamp and partial-result warnings). Its size and location
are saved separately from the main DISM GUI window.

The optional features and Windows capabilities queries are independent.
Unsupported servicing APIs on specialized WinPE images produce a warning
instead of falsely reporting a complete inventory. Nothing in this viewer
changes the image. Run Scan mounted index again after servicing changes.

**Compare JSON snapshots** opens a separate read-only diff viewer. Choose a
baseline inventory JSON export and a newer export. The tool compares matching
drivers, packages, features and capabilities; shows Added, Removed and Changed
items with old/new state, version, provider and relevant fields; and supports
filtering, sorting and CSV/JSON export. The comparison is local/offline and
does not run DISM or require a WIM to be mounted.

**Important:** v4.3.5 exports use **snapshot schema 2**, which records total
inventory rows and whether the export contains the complete inventory.
Added/Removed are only reported when **both snapshots are complete**; filtered
exports and legacy v4.3.4 schema-1 exports can reliably compare shared
records but not membership. Missing optional features/capabilities are also
suppressed when the related inventory returned a warning. Duplicate identities
are reported as ambiguous, rather than silently treated as a change. Always
export with cleared Type/State/Search filters to get a complete comparison.

WIM 360 still cannot inspect split `.swm` sets. For actual image servicing,
use the existing DISM sections only after verifying image backups.

## Changes
- Modern Windows dialog for selecting mount, apply, driver, package and capture folders; matching file picker for WIM images.
- Last used paths stored per category in `HKCU\Software\DISMGUI\RecentPaths`.
- Cancel selection preserves previous path; mount and apply destinations require empty directories.
- Commit / Discard / Cancel on the main UI thread.
- Foreground, main-window-owned unmount notification on success or failure; includes error code.
- Capture DISM stdout and stderr without hanging on full pipes.

## Local Visual Studio 2026
Requires .NET 10 SDK and Python 3 on Windows.

1. Clone this repository.
2. Run `python tools/prepare_source.py` once to download the pinned upstream VB.NET source, apply the UI fixes and update the project to .NET 10.
3. Open `DISM GUI/DISM GUI.vbproj` in Visual Studio 2026.
4. Build in Visual Studio or publish via PowerShell: `./Build.ps1 -Runtime all`.

The source is downloaded from a **pinned upstream revision** at preparation time, rather than copied into this repository. GitHub Actions automatically performs preparation and publishes x86/x64/ARM64 portable ZIP artifacts.

## Test safely
CI tests compilation, not real WIM operations. Test with disposable WIM copies first. ARM64 runtime testing requires a Windows ARM64 machine.

**Attribution and redistribution:** Upstream license terms are not clearly stated. Verify redistribution rights before publishing modified binaries.

## Support
For DISM GUI questions or bug reports, use **About → Email Support** or
**Tools → Email Support...** in the application. The default email client
opens a draft addressed to **[sgennadi@hotmail.com](mailto:sgennadi@hotmail.com?subject=DISM%20GUI%20Support)**,
including the installed version and architecture. No message is sent
automatically.
