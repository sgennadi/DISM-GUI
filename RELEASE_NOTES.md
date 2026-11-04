# DISM GUI v4.2.0

Windows Image Toolkit, modernized for .NET 10 and Visual Studio 2026.

- Light, colorful user interface based on the Nipkg Creator visual palette, with responsive controls across all nine DISM sections.
- High-DPI-friendly About and Update Center dialogs with full labels, native multi-resolution icon and readable fonts.
- GitHub Releases-based update checker: **Update** for newer releases; **Reinstall** for matching version. Downloads verified by SHA-256.
- Modern file/folder pickers with saved recent locations, safe WIM unmount confirmation and foreground result dialogs.
- Portable Windows x64, x86 and ARM64 ZIP files. No unnecessary PDB or legacy DISM GUI.dll.config files.

**Testing:** GitHub Actions verifies all three Windows architecture builds and embedded icons. Actual image mounting, DISM operations, and DPI on physical Windows hardware need real-device validation. Test on disposable WIM images first.

Based on original DISM GUI by Mike Celone (mikecel79/DISMGUI), with attribution preserved. Confirm redistribution rights to upstream code before wider distribution.
