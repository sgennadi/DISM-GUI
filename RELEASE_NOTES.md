# DISM GUI v4.2.4

## Resizable DISM Output

- Replaced the fixed-height output panel with a **native horizontal splitter**. Drag the divider up or down to give DISM's text output more space while keeping the workspace usable.
- Added a clear **Expand / Restore** button alongside Copy log, Clear and Exit. The expansion remembers the prior panel height, and dragging remembers the selected height for next launch under `HKCU\Software\DISMGUI\UserInterface\OutputHeight`.
- Enlarged the output-toolbar row and separated its buttons into dedicated columns, preventing the prior overlap/clipping with the text area at Windows display scaling.

## Update notification

- **Tools** now contains an explicit, quiet update-status row and a manual **Check for updates...** command.
- When a newer *verified* GitHub stable release exists for the current CPU architecture, Tools gains a subtle green indicator and the dropdown displays the new version. No unsolicited popup, automatic download or repeated checks.
- Offline / API errors never block image servicing. Use the Update Center to see explicit check errors.

## Other improvements retained

- Responsive WIM Mount/Index navigation, light Nipkg Creator-inspired palette, native EXE icon and updated About/Update Center.
- .NET 10 WinForms portable x86/x64/ARM64 builds. Archives omit PDB and legacy DISM GUI.dll.config files.

## Verification

- GitHub Windows CI builds three architectures and smoke-tests the live UI on x64, including divider expansion/restoration and non-overlapping toolbar controls at multiple sizes.
- Mounting, servicing and actual Windows DPI behavior still need testing on a real workstation and disposable WIM images.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained. Confirm upstream redistribution rights for broad public distribution.
