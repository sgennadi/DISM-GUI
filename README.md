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
mount state. Right after **Inspect**, the new **Automatic index overview**
summarizes all indexes' edition, Windows build, architecture and languages
without mounting the image. It identifies shared builds, explicitly lists
differences and recommends up to three pairs for **Compare indexes**, such
as Home vs Pro on the same build. If metadata is missing, the overview
does not guess that indexes are the same. It also warns that matching
metadata is not proof of identical file contents. The overview is included
in **Copy report**; it never changes the WIM.
The **Compare suggested pair** action opens the existing index comparison
with the two suggested indexes already selected and the **Differences only**
filter active. The first preview compares metadata only. To compare actual
file/folder presence without mounting, choose **Files + folders (no mounting)**
and click **Compare selected indexes**. Filters are retained across re-runs
when they still apply; **Reset** clears them.
**Use index in Mount** transfers the selected WIM path and
index to the original Mount form but **never mounts or modifies the image**.
Use **Copy report** to copy the read-only text report. The new
**Export indexes CSV** and **Export indexes JSON** buttons save an unmounted
image's index metadata directly to a local file (including WIM/ESD or a
verified split SWM). These exports include all image indexes, architecture,
edition, build, languages, size and mount status; they never mount the image.
CSV text is protected against spreadsheet formula injection. The JSON format
has `ExportKind: Wim360IndexMetadata` and is **not** an inventory snapshot
for **Compare JSON snapshots**; those require an actual mounted-index audit.

### Compare 2–8 indexes inside one WIM or ESD (test RC)

After **Inspect**, choose **Compare indexes**. Two or three indexes are
selected by default; select any 2–8 using the checkbox list.

- **Index metadata** works immediately, without a mount: name, description,
  edition, build, languages, architecture, installation type, image size.
- **Files + folders (no mounting)** uses the built-in Windows
  Windows DISM PowerShell `Get-WindowsImageContent` against each selected WIM/ESD index.
  The result contains all *paths* (files, folders and empty folders) in
  adjacent columns: `Only in Index N`, `Missing from Index N`,
  `Present in all`, etc. It does not mount or modify the image, and cannot
  prove file bytes or sizes are the same. Truncated/failed DISM output is
  rejected instead of being reported as a missing file. Direct split SWM
  path enumeration is not enabled; SWM metadata inspection remains available.
  To protect image integrity, this mode computes a full SHA-256 of the
  **source WIM before and after** enumeration and restores its original file
  modification timestamp if Windows DISM changed only that timestamp.
  On multi-gigabyte WIM files this verification adds substantial disk reads;
  the Compare button switches to **Cancel scan** during the operation.
- **Files + sizes (already mounted)** requires each selected index to be
  mounted in a **different folder** (prefer read-only mounts); refresh
  **Inspect** after mounting. This mode compares file paths, lengths and
  empty-folder presence. Reparse points are skipped. File SHA-256 verification
  is available for an individually selected path.
- **Drivers / packages / features** requires a completed **Scan mounted
  index** for every selected index. If any scan is partial, comparison
  refuses to report possibly false missing items.

Use the Type, Result, and Search filters to find missing or changed items.
**Exists in all** includes items present in every selected index even if
their versions or sizes differ; **Present in all** means reported values
are identical across all selected indexes.
The selected indexes are shown as adjacent columns; horizontal scrolling
is available. **Export filtered CSV** saves only visible rows. To check
actual bytes, select one file in the mounted-files mode and choose **Verify SHA-256**.
Equal file lengths alone do not establish equal content, and the SHA-256
operation can take time for large files. All operations are read-only.

For example, for `install.wim` with indexes 1, 2 and 3, first use
**Index metadata** to compare editions and builds. Select
**Files + folders (no mounting)** to compare all paths under
`Windows\System32` immediately. Filter by `Missing from Index 2`,
`Only in Index 3`, or `Present in all`. To check different file lengths
or calculate SHA-256, mount the selected indexes in separate read-only
directories and switch to **Files + sizes (already mounted)**.

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

### Real WIM mount integration test (4.3.6)

The Windows x64 CI runner creates a disposable WIM using `dism.exe
/Capture-Image`, then mounts **index 1 in read-only mode** with `/Mount-Image
/ReadOnly`. WIM 360 verifies the actual mounted-image registration and
matching WIM/index/path; the test reads the mounted file without modifying
the source. A `/Unmount-Image /Discard` in a `finally` block and a SHA-256
comparison verify safe cleanup. Test code **never mounts a production image**,
never commits, and never installs a driver/package. Failures block release.

The test WIM only contains fixture files and a dummy `Windows` directory.
It **cannot** validate Windows driver/package/feature servicing APIs. To
check those, open a disposable copy of a real Windows `install.wim` on an
elevated Windows workstation, run **Scan mounted index**, review feature
warnings and export an unfiltered JSON snapshot. This remains a manual test.

For local testing the read-only mount check is opt-in; in an elevated
PowerShell session set `$env:DISMGUI_TEST_WIM_MOUNT='1'` before running
`dotnet run --project tests/LayoutSmoke/LayoutSmoke.csproj`.
It is enabled automatically on the x64 GitHub CI runner.

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

## Embedded UniversalDiagnostics local queue (4.3.10 integration)

The canonical `universal-diagnostics/v1` source is compiled **into DISM GUI**.
In **Tools → Report error**, review the preview and use
**Save locally (built-in)** to write a redacted report to the shared
per-project queue. The **Saved diagnostics** window can search, copy,
export, or open a reviewed report as a prefilled **GitHub Issue draft** after
explicit confirmation. The sanitized JSON is copied to the clipboard;
the operator must sign in and press **Submit** on GitHub. Opening a draft
does not mark the local event as submitted. No background upload occurs.

There is **no required collector EXE, DLL, installer or GitHub token**.
The original DISM diagnostics, support contact and manual Issue draft remain.
See [embedded integration details](docs/UNIVERSAL-DIAGNOSTICS.md).
DISM GUI continues to report version **4.3.10**.

## Update Center high-DPI layout (4.3.10 test branch)

The **Updates** dialog keeps its installed/latest version columns and the
Check, Update/Reinstall, Releases and Close actions within the window width
at simulated 125%, 150% and 175% font scaling. Long changelogs scroll
vertically; horizontal scrolling is not required.

## Error diagnostics and GitHub issue workflow (4.3.7)

Use **Tools > Report error to GitHub** or **About > Report error to GitHub**.
DISM GUI prepares a **multiline**, readable local diagnostic preview with
app version, .NET/Windows environment, architecture and DPI. Windows CRLF
line endings are normalized so the headings, log excerpts and checklist
do not collapse into one long line in the WinForms preview. Recent DISM output is **off by default**,
and including the last 60 lines requires an explicit checkbox. The preview
redacts recognizable credentials, recovery passwords, identities, addresses,
URLs and local/network paths. You must still inspect it yourself, check the
review checkbox, and click **Open GitHub Issue draft**. The application copies
the sanitized report and opens the new-Issue page in your browser; you sign
in and click Submit. **Nothing is uploaded automatically from the desktop.**
The existing support email remains available: sgennadi@hotmail.com.

CI now runs fail-fast source checks **before** building each architecture.
The separate GitHub Actions workflow **Recurring CI Diagnostics** runs after
a failed build and daily. It aggregates only known failed-step names and
compiler error codes over the past 14 days. The same signature must occur
in at least two separate builds before a single recurring-diagnostics Issue
is created or updated. It never publishes raw GitHub Actions logs. A private
source/build log must not be pasted into a public Issue without review.

## WIM 360 partial audit (4.3.7)

Driver, Windows package, optional-feature and capability reads report errors
independently. A failure of one category no longer hides the others or
misrepresents missing data as zero items. Scan progress, per-category timings,
sanitized warnings and a PARTIAL AUDIT indicator appear in the report.
Snapshot exports preserve driver/package error flags; comparison suppresses
false Added/Removed events from failed inventories. Mount validation failures
remain fatal, and no image is committed or discarded by the inventory viewer.


## One-click integrated testing (4.3.8)

Choose **Tools > Run complete safe tests...** or **WIM 360 > Run complete safe tests**.
The new Test Center runs read-only checks for Windows/.NET/runtime, elevation,
display DPI/window dimensions, DISM mounted-image query, a selected WIM/ESD/SWM
image, and split-image part consistency. It shows PASS/WARN/FAIL/SKIP results
in one resizable window. **Copy report** and **Save report** export a locally
sanitized summary; **Report issue...** opens the existing human-reviewed
GitHub support form. **No automatic upload or telemetry is performed.**

A selected image is optional for basic environment testing. For a more complete
test, choose a disposable copy of \`boot.wim\` or \`install.wim\`. If the image
is **already mounted**, enable **Scan servicing of an ALREADY mounted index**
before pressing **Run all safe checks**. This optional query can take several
minutes. It checks drivers, Windows packages, optional features and Windows
capabilities; the Test Center **never mounts, unmounts, commits or changes**
a WIM. Run as administrator to test the full servicing APIs.

### Split SWM support and limitations

WIM 360 now accepts \`install.swm\`, \`install2.swm\` etc. It resolves the
first part, verifies the names, count, GUID and part numbers of the complete
set from each Microsoft WIM header, and reads image metadata from the first part with query-only WIMGAPI.
All members of a split set must be in the same directory.

This is **view-only**: WIM 360 disables **Use index in Mount** and mounted
servicing for SWM images. Split images cannot be edited in place. The header
comparison and metadata read do **not** guarantee full data integrity; they
do detect missing or mismatched part headers. Never use the standard Mount
or Driver Studio write actions on a SWM path.

CI creates an independent random-data WIM, splits it into multiple SWM parts,
inspects both part 1 and part 2, verifies missing-part rejection, and compares
SHA-256 checksums after testing. Real production OS-image servicing and
interactive DPI scaling at 125%, 150% and 200% still require an operator test.

## Runtime diagnostics from installed DISM GUI copies (4.3.9)

DISM GUI records real application failures for the current Windows profile in
%LOCALAPPDATA%\DISMGUI\Diagnostics\events.log. It records failed DISM exit
codes from Mount, Unmount and command workers, WIM 360 errors, and unhandled
UI/process/task exceptions. Entries are redacted before writing to disk and
contain timestamp, version, operation, error code and coarse signature.
No full DISM console is collected automatically. Journals rotate at
256 KiB, keeping current and previous files only.

In Tools > Report error to GitHub, enable Include 15 local errors to
include an optional sanitized excerpt. This is independent of
Include last 60 DISM lines, also off by default. Open log folder lets
users view locally recorded errors, and Clear history erases them after
confirmation. Editing the report or changing either checkbox revokes
prior approval. User must sign in and click Submit on GitHub. No automatic
user diagnostics upload or embedded GitHub token is present.

CI error aggregation now ignores compiler warnings and isolated error-code
mentions; only actual compiler errors and failed build steps count.

Test checklist: TESTING_4.3.9.md. Windows 11/Server servicing and physical
125%-200% DPI still require manual workstation testing.
