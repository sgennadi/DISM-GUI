# DISM GUI 4.2.6

## Fully responsive Windows image servicing sections
- Rebuilt all nine workspaces with responsive layouts rather than overlapping fixed legacy coordinates.
- Drivers: add/delete drivers, recurse and unsigned options, driver information actions.
- Packages: dedicated full-width folder input with a **Choose package folder** button, remove by name/path, get/add package actions.
- Features, Editions, Unattend, Applications, Capture and Apply: dedicated labels, input fields, browse controls, checkboxes and clearly sized action buttons.
- All original DISM controls and event handlers are retained; long action labels wrap instead of being clipped. Fields/actions scroll independently at tight window sizes.
- Mount uses the previously rebuilt WIM/image-index grid.

## Window placement and preferences
- The main window remembers normal size, location and maximized state on close; no manual reconfiguration on next launch.
- If a saved position lies on an unplugged monitor or outside the current desktop, it reopens in a visible working area.
- Preferences stored under `HKCU\Software\DISMGUI\UserInterface`, alongside the resizable DISM output height.
- Drag the DISM output splitter or use **Expand / Restore** to change the log height.

## Appearance, updates and portable binaries
- Nipkg Creator-inspired light palette, restrained action colors, consistent native icon.
- Main, About and Update Center titles show the installed version without a `v` prefix.
- Tools menu quietly indicates a newer SHA-256 verified stable release, when available.
- Portable .NET 10 Windows EXE ZIP packages for x64, x86, ARM64; no unnecessary PDB or DISM GUI.dll.config.

## Validation and limits
- GitHub Actions compiles for all three Windows architectures.
- Windows x64 UI smoke tests exercise **all nine sections** at two window sizes (900x650 and 1180x760), check for visible control overlap and clipped button/label text, and capture screenshots.
- Window placement persistence and off-screen recovery are tested using native WinForms forms.
- UI tests do not replace manual validation at 150%/200% DPI or functional image servicing on a real Windows system. Test DISM operations with disposable WIM copies first.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI). Attribution preserved. Confirm upstream redistribution rights before broad redistribution.
