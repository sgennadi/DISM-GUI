# DISM GUI 4.3.6

## WIM 360 — Real Read-only Mount Validation

- Adds an **actual DISM mount integration test** to Windows x64 CI instead of relying exclusively on mocked snapshot and metadata fixtures.
- Captures a small disposable WIM, mounts index 1 with **/ReadOnly**, verifies WIMGAPI identifies **Mounted (read-only)**, checks the WIM index/path and reads mounted contents.
- Performs **/Unmount-Image /Discard** in guaranteed cleanup and checks the source WIM SHA-256 remains unchanged. A mount, cleanup, or hash failure stops the release.
- Test WIM includes a dummy Windows directory to exercise selected-image path validation; **it is not an operating system image**.
- The same test is opt-in for local elevated Windows smoke runs through the **DISMGUI_TEST_WIM_MOUNT=1** environment variable.
- All existing WinForms layout, JSON comparison, Driver Studio INF and portable x86/x64/ARM64 build tests remain.

## Safety and limitations

- The CI mount test operates only on files created under a fresh temporary directory; it never mounts or alters customer WIMs.
- The artificial WIM does not support Get-WindowsDriver, Get-WindowsPackage, Get-WindowsOptionalFeature or Get-WindowsCapability. These operations still require a separate real Windows WIM test on a disposable copy.
- Read-only scan and snapshot comparison functions are unchanged; no automatic commit or package installation is introduced.
- UI scaling at non-default DPI still requires interactive testing on Windows at 125%, 150% and 200% as runner display settings are not representative.
- Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
