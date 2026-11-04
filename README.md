# DISM GUI — VS 2026 Modernized

Based on https://github.com/mikecel79/DISMGUI with original author credit.

## Build
Visual Studio 2026; .NET 10 Windows WinForms; VB.NET. Targets: win-x86, win-x64, win-arm64.

GitHub Actions downloads upstream source, applies modern-dialog fixes, and publishes portable architecture-specific ZIP artifacts. Upstream is not modified.

## Improvements
- Modern Windows folder picker, owned by main window.
- Remember separate recent WIM/mount/driver/capture/apply paths.
- Commit / Discard / Cancel on UI thread.
- Owned foreground success/failure message after unmount.

## Local build
Run PowerShell: ./Build.ps1 -Runtime win-x64 (requires .NET 10 SDK and Python 3).

Test using disposable WIM copies. Native ARM64 hardware testing requires Windows ARM64.

## Attribution
Original project: mikecel79/DISMGUI. Verify upstream license terms before redistributing binary releases.
