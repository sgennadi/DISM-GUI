# DISM GUI 4.2.8

## Email Support / Contact
- Added an **Email Support** button and a clickable **Support: sgennadi@hotmail.com** contact address in the About dialog.
- Added **Tools > Email Support...** to the main window.
- Launches the user's default mail client with the recipient, subject (`DISM GUI Support - 4.2.8`), installed version, process architecture, and an empty issue-description field.
- No email is sent automatically. If no default mail client is configured, a fallback offers to copy the support email address to the clipboard.
- Address is maintained centrally in the application to keep About and Tools consistent.

## Existing functionality
- Inline release notes / changelog in the Update Center; GitHub Releases updates with verified SHA-256.
- Responsive layout for all nine DISM sections, light UI and embedded Windows executable icon.
- Resizable DISM output with Expand/Restore and saved height.
- Remembers main-window size and position, with off-screen recovery.
- Portable Windows x86, x64 and ARM64 builds without legacy config and debug symbols.

## Checks
- Windows CI compiles all three architectures and smoke-tests About/Tools support controls and the generated mailto URL.
- Actual default mail client handling and operations on real WIM images still need manual testing on Windows.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI). Attribution preserved.
