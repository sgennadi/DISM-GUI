# DISM GUI 4.3.9

## Runtime diagnostics on real installed copies

- Added locally stored, redacted diagnostic history for normal DISM GUI sessions. Tracks unhandled UI/process/task exceptions, WIM 360 errors and nonzero DISM exit codes from the legacy Mount/Unmount/command workers.
- Each entry includes UTC timestamp, version, sanitized operation, error type and exit/HRESULT code plus a short coarse signature. Raw command lines, full DISM logs and credentials are not recorded automatically.
- Current and previous local journals rotate at 256 KiB each. No automatic uploads or embedded GitHub secrets.

## User-reviewed support reports

- Two independent opt-in checkboxes: include last 60 DISM output lines, or include last 15 locally recorded errors.
- Open log folder and confirmation-protected Clear history controls.
- Preview editing and checkbox changes revoke review approval. GitHub Issue submission still occurs from the user's authenticated browser.
- Shorter captions help the window fit at high DPI.

## CI failure diagnostics

- Counts actual compiler errors and failed CI steps, but not warnings or casual code references.
- Tests cover history persistence, redaction, clearing and UI opt-in behavior.
- All existing WIM/SWM, Test Center and x86/x64/ARM64 safety tests remain.

## Limitations

- Unattended forwarding of arbitrary end-user errors to GitHub still requires a separately hosted authenticated intake service with rate limiting, organizational consent and retention controls.
- Physical 125%/150%/200% DPI and real OS-image servicing require manual testing.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
