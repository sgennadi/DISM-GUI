# DISM GUI v4.2.3

Windows Image Toolkit — interface and update-notification improvements.

## Interface
- Rebuilt the main header using a responsive three-column layout: icon, title/subtitle and a real **Tools** dropdown button. The original menu commands remain available.
- Replaced alarming red Dismount/Remove actions with restrained blue-and-white controls. Red is reserved for actual error messages.
- Rebuilt the **Mount & Inspect** workspace using separate cells for the WIM file, mount folder, image index, and full-width operations. Controls no longer share overlapping absolute coordinates.
- Replaced the legacy blue-selected WIM image-index ComboBox with a numeric selector. The underlying DISM index value is synchronized with the original control.
- Fixed the output panel buttons with dedicated layout columns and proper padding.
- Improved About and Update Center headings, button spacing, and text readability.

## Updates
- **Tools** quietly checks the latest stable release once on startup. If a newer verified version exists for the current architecture, it shows a green indicator and an "Update available" menu item.
- **Update** means install a newer release; **Reinstall** means repair the current release. Update Center continues to verify the GitHub ZIP's SHA-256 before replacement.
- No recurring background monitoring, PowerShell, CMD, or surprise installation.

## Build and testing
- Portable self-contained Windows x86, x64 and ARM64 packages, with multi-resolution native EXE icon and no PDB / legacy DISM GUI.dll.config files.
- Windows CI compiles all three platforms and runs a **live WinForms layout smoke test** on x64. Layout captures include main window, About and Update Center.
- Functional DISM mount/apply tests and actual high-DPI checks on the user's Windows systems remain to be performed. Use disposable WIM files for initial testing.

Originally based on Mike Celone's DISMGUI project (mikecel79/DISMGUI); attribution preserved. Confirm upstream redistribution permissions before broad public distribution.
