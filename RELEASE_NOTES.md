# DISM GUI 4.2.7

## Release changelog inside Update Center
- **What's new:** Update Center now displays the actual changelog from the latest stable GitHub Release directly below the version status, before the Update or Reinstall button.
- **Scrollable release notes:** long descriptions can be read without obscuring buttons; resize the Updates window to give the changelog more space.
- **Full changelog on GitHub:** the heading links to the exact stable release's detailed notes. Content is displayed as plain text; Markdown links and headings are made easier to read.
- **Safe empty/offline states:** shows a clear message when a release has no notes or the GitHub API is unavailable; update installation still requires architecture-specific ZIP and SHA-256 verification.
- **Version labels:** when the same version is installed, Reinstall remains available, and its changelog remains visible. Newer versions show Update.

## Features retained from 4.2.6
- Responsive layout for all nine DISM image-servicing sections, including unclipped Package/Driver browse buttons.
- Resizable DISM output with Expand/Restore and saved panel height.
- Saved window size, position and maximized state with disconnected-monitor recovery.
- Light-color modern interface, embedded native icon, automatic discreet Tools update notification.
- Self-contained .NET 10 Windows x64, x86 and ARM64 builds; PDB and legacy config files excluded.

## Validation
- Windows CI builds x86/x64/ARM64 and smoke-tests the Update Center changelog controls and visual layout with live WinForms screenshots.
- Real-world DISM operations and 150%-200% DPI still require end-user Windows testing with disposable WIM copies.

Originally based on DISM GUI by Mike Celone (mikecel79/DISMGUI). Attribution preserved.
