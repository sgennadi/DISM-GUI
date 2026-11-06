# DISM GUI 4.3.14 — Stable

Portable, self-contained Windows builds for **x64, x86 and ARM64**,
built with Visual Studio 2026 / .NET 10. Release 4.3.13 remains available.

## WIM 360 — bounded batch SHA-256 comparison

- In **WIM 360 → Compare indexes → Files + sizes (already mounted)**,
  click **SHA-256...** to choose either **Verify selected file** or
  **Verify filtered same-size files**. This does not add an extra cramped
  footer button, including at simulated 175% font scaling.
- Batch verification reads files present in all **2–8 selected indexes**
  with identical reported sizes under the current Type / Result / Search
  filters. This detects content differences that simple size comparison
  cannot detect.
- **50 paths / 1 GiB total read budget**, including all selected indexes.
  If the active filters exceed either limit, the app asks the operator to
  narrow them; it never truncates silently.
- The operator explicitly confirms before batch hashing. Status shows
  per-file progress. **Cancel scan** requests cancellation, including while
  reading a large file. Completed hash rows are applied atomically only
  after the whole batch succeeds; cancellation, inaccessible paths or a
  changed file do not display partial results.
- Fresh mount registration checks, path traversal protection, reparse-point
  checks and file length / last-write-time validation reduce stale or
  misleading results. The image is **not** mounted, changed, committed or
  discarded by hashing. Existing single-file SHA-256 remains available.

## Interface / tests

- Short **SHA-256...** footer caption avoids 175% high-DPI truncation; an
  explicit regression test checks horizontal readability.
- Added synthetic tests for equal-size selection, missing/different-size
  exclusions, 50-file limit and 1 GiB total-read budget.
- Windows CI tests all x86/x64/ARM64 packages; x64 runs read-only WIM mount
  integration and 125%, 150%, 175% simulated WinForms layout tests.
- A real multi-index Windows image with several mounted indexes should still
  be tested manually on a disposable copy before production servicing.

**Accuracy note:** the offline WIM source SHA-256 check protects the source
container, but it is not a per-file comparison. Batch SHA-256 is only offered
for already-mounted file trees. No background GitHub diagnostics upload,
external required runtime, or automatic DISM write action was introduced.

Original DISMGUI by Mike Celone (mikecel79/DISMGUI); original attribution
and existing distribution notices are retained.
