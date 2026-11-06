# DISM GUI 4.3.12 — Stable

DISM GUI 4.3.12 is a standalone, self-contained Windows release for x86, x64 and ARM64.
This is a successor to **4.3.11**, whose release assets remain available unchanged.

## Unified local diagnostics

- Automatic recording of real DISM failures, WIM 360 inspection/servicing errors,
  and Driver Studio INF scan/comparison/selective import errors into the existing
  embedded `universal-diagnostics/v1` local queue.
- Automatic records contain **only coarse component category, exception type,
  numeric code, generic summary, app version and anonymous fingerprint**.
  No full exception messages, paths to images or drivers, machine/user identity,
  source logs, credentials or private tokens are included in automatic events.
- A matching error fingerprint is recorded only once per 24-hour window,
  including across application restarts. The local queue is capped at 1,500
  records without silently deleting historic reports. An unavailable legacy
  journal no longer prevents independent universal queue capture.
- The existing rotating local runtime log and manually reviewed report preview
  remain available for compatible workflows.
- All diagnostics use the canonical shared embedded .NET source in the
  portable EXE. **No helper executable, DLL, GitHub token, or background uploader.**

## Saved Diagnostics user interface

- **Open GitHub draft...** still requires explicit confirmation, browser
  sign-in, review and manual GitHub Issue submission.
- **Mark submitted...** lets the operator enter a link to an Issue already
  created in the DISM-GUI GitHub repository. The link is strictly validated
  before updating the **local-only** delivery status and URL.
- **Mark pending** reverses the local status and clears the associated link.
  Neither operation contacts GitHub or asserts that the server verified delivery.
- Responsive queue footer and row/header heights at simulated 125%, 150%, 175%
  font scaling. Shortened button captions, balanced grid columns and compact UTC
  dates improve readability. The full validated timestamp remains in JSON and
  the row tooltip rather than being incorrectly redacted as an IPv6 address.

## Testing and safeguards

- GitHub Actions compiles portable win-x86, win-x64 and win-arm64 EXEs and ZIPs.
  x64 WinForms and real disposable read-only WIM tests verify comparison,
  diagnostics, redaction, deduplication, local status changes and cleanup.
- Smoke tests use isolated temporary diagnostics folders, never mixing fixture
  errors with user data. Failure of either diagnostics store does not interrupt
  Windows imaging or driver servicing.
- **No automatic error report upload to GitHub.** Direct user-to-GitHub API
  submission remains a separately deferred feature, requiring an authorized
  design and explicit consent. Local Mark submitted is not an upload.
- Real Windows workstation verification remains recommended for physical DPI,
  protected file system conditions and production-specific WIM servicing.

Original application by Mike Celone (mikecel79/DISMGUI); attribution preserved.
