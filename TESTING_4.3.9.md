# DISM GUI 4.3.9 — Real-User Diagnostic Testing

Run on a real Windows workstation using the portable ZIP from GitHub Releases.
No GitHub account or internet connection is required to collect local errors.

## Confirm real-user diagnostic capture

1. Run \`DISM GUI.exe\` normally or as administrator.
2. Open **Tools > Report error to GitHub...**. The two options
   **Include last 60 DISM lines** and **Include 15 local errors**
   must both be unchecked by default.
3. Click **Open log folder**. The application opens
   \`%LOCALAPPDATA%\DISMGUI\Diagnostics\`.
4. Run **Test Center > Run all safe checks** and review PASS/WARN/FAIL/SKIP.
5. Trigger a harmless failure (for example, select a non-existent image
   in WIM 360 and press Inspect). Reopen the support window.
6. Check **Include 15 local errors**. Confirm the new entry is visible
   in the sanitized preview, with version, category and a coarse error
   signature. It must not contain machine names, passwords, local
   usernames, recovery keys or network shares.
7. Uncheck the option. The local history must disappear from the preview.
8. If the failure happened in a normal DISM operation, confirm its exit
   code appears in local history; successful exit code 0 and reboot-needed
   code 3010 must never create error entries.
9. Close the app, reopen it and confirm that local history is retained.
10. Press **Clear history**, approve the confirmation and verify the
    \`events.log\` and \`events.previous.log\` entries disappear.

## Reporting and privacy

- **No desktop log is sent automatically to GitHub**.
- The DISM output checkbox and the local-history checkbox are independent,
  OFF by default. Both are opt-in.
- **Open GitHub Issue draft** stays disabled until the user reviews the
  current sanitized report and checks the explicit approval box.
- Changing the report or either checkbox invalidates the prior approval.
- GitHub requires the user to sign in and click **Submit** in the browser.
  Support email remains available from the Tools menu.
- Runtime log rotation caps each local journal file at approximately
  256 KiB; a maximum of two files is retained. Error entries contain
  version, UTC timestamp, category, exception/exit code and a coarse
  deduplication signature, not a full stack trace.
- Redaction is conservative and cannot guarantee all organizational
  secrets are removed. Review the complete report before submitting.

## Automated GitHub CI diagnostics

- Windows x86/x64/ARM64 compile with fail-fast checks.
- Windows x64 tests local persistence, redaction, clearing the history,
  opt-in report UI and the existing real WIM/SWM fixtures.
- The scheduled workflow groups only **actual compiler errors** and
  failed CI job steps. Compiler warnings are not incidents.
- CI errors are independent from end-user runtime error history.
  A separate authenticated intake backend would be required for
  unattended submissions from arbitrary end users.

## Additional manual UI tests

Verify 100%, 125%, 150%, and 200% display scaling. Check:
**Tools**, **About**, **Report error to GitHub**, **Test Center**, **WIM 360**,
**Driver Studio**, the updater and the resizable DISM output panel.
