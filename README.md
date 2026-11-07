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
chosen WIM index using **WIM 360 → Mount**. Then choose **Compare with
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
After reviewing the outcome, use **WIM 360 → Mount** to explicitly commit or
discard the image. The legacy Drivers section's Add drivers button still
imports an **entire folder**: its behavior is separate from the selective importer.

Use disposable copies of WIM images for initial real-world tests. DriverVer
equality does not prove identical binaries, package signatures or Windows
device ranking. An imported package may not be selected by PnP at boot.

### Driver Studio source package integrity audit (4.3.15)

Driver Studio has a new **Audit selected** action in the package-details header.
After comparing drivers with an already-mounted image, select up to 30
**New / Newer** INF files and audit them. The scan is read-only; it collects
SHA-256 checksums for the **complete source directory tree**, including
INF, CAT, SYS, DLL and all other files, and combines relative paths,
lengths and hashes into a reproducible package manifest SHA-256. To avoid
silently incomplete checks, a package with more than **1,600 files or 2 GiB**,
unreadable files or reparse points is rejected and must be separated into
smaller source folders.

The audit chooses the INF's generic or target-architecture-decorated
`CatalogFile` and verifies its **Authenticode signature** via Windows.
An absent catalog, invalid/untrusted signature, mismatch between the INF's
explicit architecture hint and the WIM, or changed source files blocks
**Install selected INF**. Windows DISM remains responsible for complete
signed-package membership, servicing compatibility and installation;
a valid CAT signature alone does **not** guarantee all driver payloads are
correct, or that a specific physical device will use the staged driver.
When metadata cannot prove OS/build or HWID compatibility, Driver Studio
does **not** claim that the driver will be selected by PnP.

Installation now requires a successful explicit audit of every selected INF
and **rechecks the whole folder manifest and CAT immediately before each
DISM command**. It never auto-mounts, auto-commits, auto-discards or adds
`/ForceUnsigned`. After successful DISM imports, it re-reads the mounted
inventory to check whether the original INF/provider/class/version/date is
found. That means **staged in the image**, not necessarily selected by PnP.
Unconfirmed or unavailable inventory checks are reported, not labeled as
confirmed installs. The operator must choose Commit or Discard explicitly.
The saved audit is a point-in-time check, not a lock on the source directory.

Released as **4.3.15** after automated x86/x64/ARM64 checks. Stable **4.3.14**
remains available unchanged. A signed OEM test package and writable OS image
still require manual workstation validation.

## WIM 360 — Image Studio

Select **WIM 360** at the top of the left navigation. Choose a `.wim` or
`.esd` file, or select one in **WIM 360 → Mount** first. Click **Inspect** to
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

### Compare TWO different WIM/ESD containers (4.3.16)

In **WIM 360 → Compare 2 files**, select two independent WIM or ESD
files, run **Inspect** for A and B, then choose one image index inside
each. This is separate from **Compare indexes**, which examines several
indexes inside **one** WIM. You can compare install.wim index 1 with an
entirely different install.wim or install.esd index 1; the two columns
are clearly labeled **A: filename [original index]** and **B: ...**.

- **Index metadata (no mounting)** compares architecture, edition,
  Windows build, languages, install type, image name and other WIMGAPI
  fields. No DISM mount or write operation occurs.
- **Files + folders (no mounting)** compares file and directory **paths**
  from both catalogs, preserving Unicode Russian/Hebrew filenames.
  The filters include Differences only, Only in A, Only in B, Different,
  Exists in both, Present in both, type and free-text search.
- **Export filtered CSV** includes explicit A/B source headers and
  protects filenames starting with spreadsheet formula characters.
- The offline catalog is accepted only after an independent, full-file
  **SHA-256 before/after** check of **each** source image and an index
  snapshot validation. A missing/unreadable/truncated catalog, changed
  source, checksum failure or cancellation **discards the comparison**
  instead of showing false missing files.
- A Windows DISM API call may update a source file's modification time
  even during a nominally read-only query. The original timestamp is
  restored only if a full SHA-256 proves the source bytes unchanged.
  Failed or interrupted operations cannot guarantee timestamp restoration.

**Critical distinction:** the offline comparison reports only *presence*
and *absence* of paths. Identical paths do **not** imply equal file sizes
or bytes; that would require mounted file comparison or additional
per-file hashing. No source is automatically mounted, applied, committed,
discarded, or converted. Split SWM sets are not supported by this
cross-container catalog mode. A real disposable-WIM Windows test is included
in CI; physical Windows/Server versions and production WIMs still need
manual validation before relying on reports for deployment decisions.

### Cross-image mounted sizes and SHA-256 (4.3.17)

In **WIM 360 → Compare 2 files**, choose two separate WIM/ESD sources,
select an image index in each, and choose **Files + sizes (already mounted)**.
Both indexes **must already be mounted** (preferably read-only) in separate
folders using normal Mount tools. The comparison verifies the active WIMGAPI
mount registrations, scans file paths, sizes, and empty directories, and
marks paths present in only A, only B, or both. It includes genuine WIM/WOF-backed reparse files used by Windows
DISM mounts, while skipping symbolic links and junctions. This also
corrects the older same-WIM mounted comparison, which previously skipped
such regular WIM-backed files. Incomplete/unreadable scans fail rather
than displaying false missing-file results. Each mount is limited to 250,000 entries.

For a selected file choose **SHA-256... → Verify selected file**, or
**Verify filtered same-size files** to hash file content across A and B.
Batch verification uses the active Type/Result/Search filters and refuses
batches above **50 paths / 1 GiB total reads**, rather than truncating them.
Bytes are read without writing or committing the images. Source paths,
file sizes and reparse points are checked again immediately before hashing.
No partial results are displayed when the operation fails or is canceled.
A successful hash result replaces size labels only for the verified paths;
all other rows remain a **size comparison, not proof of equal content**.

The comparison window keeps its footer controls visible at high DPI and
retains the A/B source filename and original WIM index in CSV exports.
**Commit/Discard remains fully manual**; this feature does not mount,
unmount, install, or modify images. Windows CI uses disposable mounted
WIMs and synthetic same-length/different-bytes fixtures. For real
deployment images, confirm mounted read-only image paths and result accuracy
on a safe Windows test machine before relying on the report.

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
  empty-folder presence. Symbolic links/junctions are skipped; WIM/WOF-backed files are included. File SHA-256 verification
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

### Bounded batch SHA-256 for mounted indexes (next candidate)

In **WIM 360 → Compare indexes**, select **Files + sizes (already mounted)**
then click **SHA-256...**. Choose **Verify selected file** for one
highlighted file, or **Verify filtered same-size files** for files present in
all selected indexes with equal reported sizes. The latter uses the active
Type / Result / Search filters. Narrow the filters to **at most 50 matching
files**, with **at most 1 GiB of total file reads across all indexes**. The
app rejects larger batches rather than silently truncating the results.

The user must confirm the batch; progress appears in the status line and
**Cancel scan** stops at the next cancellation check. No image is mounted,
modified, committed, or discarded by hashing. Verified rows replace file
sizes with SHA-256 fingerprints, and other rows continue to show path and
size differences. Batch results are applied only if all selected files
were successfully checked. If files disappear, change size during reading,
become unsupported reparse links or escape their mount folder, the whole
batch fails without displaying partial hashes. This protects against
misleading claims that equal file sizes prove equal file contents.

**Release 4.3.14:** these read-only hash options are part of the tested
portable app. Version 4.3.13 remains available unchanged as a fallback.

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

### SWM integrity audit and safe export to a NEW WIM (4.3.19)

**WIM 360 → SWM audit / export** inspects a complete split SWM set,
verifies every header and computes SHA-256 for the **entire byte stream
of every part**. The audit records each file's length, timestamp,
SHA-256 and a combined manifest SHA-256; it detects missing, renamed
or unrelated wildcard-matching parts and changes during hashing.
A canceled or partial audit is not reported as verified.

After a successful audit, select an image index and a **new, absent**
`.wim` destination. DISM GUI acquires cooperative locks on the source
SWM parts and destination, rehashes **every** source part immediately
before conversion, then runs DISM `/Export-Image` with
`/SWMFile:<matching-part-pattern>`, `/SourceIndex` and
`/CheckIntegrity`. DISM exports to a uniquely named temporary WIM
in the destination directory. After successful DISM exit, the source
hashes are rechecked and the output WIM must be a valid, standalone
single-index image. Only then is it moved to the requested filename;
pre-existing destinations are **never overwritten**. Failed runs
remove only their own partial output after DISM has actually exited.
No SWM part is mounted, written, deleted or renamed by this operation.

**Image-operation guard:** DISM GUI Mount and Commit/Discard now obtain
machine-local OS-released file locks on the WIM and its mount folder
for the full worker operation. This prevents *cooperating instances
of DISM GUI on the same computer* from launching simultaneous
conflicting commands. It does not stop external DISM processes,
other applications, remote machines, or all legacy servicing actions;
avoid modifying sources from other tools while a scan/export runs.

The SWM tool has an English, high-DPI WinForms UI, cancelable hashing,
clear progress and a permanent Audit / Export / Close footer.
Microsoft documents SWM as **read-only**; this feature does not
modify split image parts. CI tests a real DISM-created split image,
actual export and subsequent apply into a disposable directory,
no-overwrite behavior, altered/missing parts and SHA-256 preservation.
Production Windows install images remain subject to manual testing.

### Mount safety, Commit/Discard verification and recovery journal (4.3.18)

**WIM 360 → Mount health** is a read-only view of the current WIMGAPI
mount registration and the recent **local** Commit/Discard operation journal.
It remains accessible even when no WIM file is selected, so interrupted
operations can be reviewed. The health window can refresh and copy the
report, but cannot automatically clean, remount, commit or discard an image.

Before invoking legacy **Dismount WIM**, DISM GUI now validates the fresh
WIM/ESD source, numeric index, exact registered mount folder and
mount state. **Commit** requires a registered `Mounted (R/W)` index;
**Discard** requires an active valid R/W or read-only mount. Unknown
registration, changed selection, unsafe folder or missing source blocks
the operation without starting DISM.

After a user chooses Commit or Discard, a **Pending** operation record is
written atomically to a local JSON journal BEFORE DISM starts. Failure to
save this record prevents execution. After DISM exits, the WIMGAPI
registration is queried again. A success message requires DISM exit 0,
a confirmed dismount and a successfully updated journal. Failed or
interrupted operations remain **Review required** (or **Pending** if the
app/process stopped before completion). The history contains the action,
time, WIM path/index, mounted folder and exit status, not full DISM
console output. Files remain on the local computer; nothing is uploaded.

**Important:** Commit can change the underlying WIM before a power loss
or process termination. No application can guarantee atomic rollback.
The Mount health view therefore **never** retries Commit, runs automatic
DISM cleanup or discards images after an uncertain interruption.
Inspect registrations and logs and decide on recovery manually. Do
not delete a mount folder until DISM confirms it was dismounted.

This feature does not change the regular Mount/Apply/Driver Studio
commands. Automated tests use synthetic registration and local journal
fixtures, plus disposable Windows DISM images where available.
Physical Windows 11/Server servicing should still be tested with
non-production copies before deployment.

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

## Grouped workspaces and update indicator (4.3.13)

The original Mount and Drivers DISM commands remain available as nested
**Mount** under **WIM 360** and **Driver tools** under
**Driver Studio**. The legacy tab indices and handlers remain unchanged,
but the other numbered sections now run from **01 Packages** to
**07 Apply** without empty 01/02 navigation positions.

On smaller high-DPI screens, Driver Studio also exposes **Scan INF** in the
fixed workspace header, invoking the existing scan operation without scrolling.
Compare Indexes displays the complete SHA-256 disclaimer in its tooltip and
a short readable note above the buttons. The duplicate DISM Output **Exit**
button is hidden; the title-bar X remains the standard way to close.

When a newer stable release is verified at startup, the **Tools** button
turns deep orange with a high-contrast white exclamation mark. When the
check finds no newer release or is unavailable, Tools remains white.
The menu continues to show the exact stable release version and keeps
the manual update action.

## Unified local runtime diagnostics (4.3.12)

[PR #14](https://github.com/sgennadi/DISM-GUI/pull/14) routes real
DISM, WIM 360 and Driver Studio failures to the embedded
`universal-diagnostics/v1` local queue. Automatic local capture stores
only coarse category, error type, numeric code and summary. It suppresses
repeated fingerprints for 24 hours and bounds total local records.
Exception messages, image paths, full DISM output and identifiers are
excluded. **No report is sent automatically.**

Saved Diagnostics adds **Mark submitted...** and **Mark pending** for users
who manually created an Issue in their browser. The official Issue link is
validated and stored locally. Neither action posts to GitHub. The
previous stable 4.3.11 release files remain available unchanged.

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
Current stable version: **4.3.19**. Earlier releases remain available.

## Other dialogs: 175% horizontal-scroll fixes (4.3.10 test branch)

Integrated Test Center, WIM 360 Detailed Inventory and WIM 360 Snapshot
Comparison now fit their actual viewport width at simulated 125%, 150% and
175% font scaling. Inventory and Snapshot filters use proportional columns
so the search field stays wide enough to type and read search terms. The test center uses four proportionally sized action
buttons. Long row contents retain their own DataGridView horizontal scroll,
while the surrounding forms use vertical scrolling for tall content. CI
asserts that primary and Close actions remain reachable without scrolling
the entire dialog horizontally.

## About window high-DPI labels (4.3.10 test branch)

The About dialog uses short, readable **Updates**, **Email** and
**Report error** buttons. The report action still opens the same
reviewed diagnostics/GitHub workflow, and the support email link remains
visible. LayoutSmoke measures text widths at simulated font scales.

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
do not collapse into one long line in the WinForms preview. The diagnostics
window uses a responsive width at simulated 125%, 150% and 175% font
scaling, with vertical rather than horizontal scrolling. Recent DISM output is **off by default**,
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
