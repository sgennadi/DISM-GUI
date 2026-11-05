# DISM GUI 4.3.8 — Full Operator Test Plan

**Purpose:** verify the entire release in one pass before using it with production WIMs.
Download only the ZIP for your Windows architecture; extract the ZIP fully.
Use a disposable copy of \`boot.wim\` or \`install.wim\` and keep a backup.
No test in this plan requires modifying or committing your source WIM.

## 1. Run all basic checks from one screen

1. Start \`DISM GUI.exe\` as administrator.
2. Select **Tools > Run complete safe tests...**.
3. Leave the image field blank and leave the optional mounted-index checkbox unchecked.
4. Press **Run all safe checks**. Check Environment, Administrator rights,
   DPI/Layout and DISM mounted-image query. Image/audit checks should show SKIP.
5. Press **Copy report** and **Save report**; confirm only sanitized data is
   included. Close and reopen the test window to verify saved size and position.
6. Repeat using **WIM 360 > Run complete safe tests**.

## 2. Inspect an actual Windows WIM

1. Choose a disposable \`boot.wim\` or \`install.wim\`.
2. In **WIM 360**, press **Inspect** and confirm all image indexes, architectures,
   names, versions and mount state are shown.
3. Repeat in **Test Center**. Expect PASS for WIM metadata.
4. For a disposable WIM you already mounted in Windows, optionally enable
   **Scan servicing of an ALREADY mounted index** in the Test Center. The scan
   is slower, reads all four inventory categories, and never mounts or writes.
5. In WIM 360, use **Scan mounted index**, check partial-query warnings,
   then **Inventory details** and export a full JSON and filtered CSV.
6. After making an independent change to a *separate test copy* of the image,
   export another full JSON and run **Compare JSON snapshots**. Check
   Added/Removed/Changed and incomplete-inventory protections.

## 3. Inspect a split SWM image

1. Copy \`install.swm\`, \`install2.swm\`, ... into the same test folder.
2. In WIM 360 choose the first part, click **Inspect**, and check the total
   part count, combined size and image index metadata. All results are read-only.
3. Choose the second part and confirm WIM 360 resolves to the first part.
4. Copy the split set to another temporary folder, remove part 2 from **that
   copy only**, and confirm that WIM 360 reports the missing part instead of
   implying the image is complete.
5. Confirm **Use index in Mount** and **Scan mounted index** remain disabled for
   SWM; no SWM modifications or application are supported.

## 4. Driver Studio and support

1. Scan an INF folder containing two copies of the same version and an older
   version. Confirm the engine identifies existing/same/older drivers.
2. Confirm selective installation is blocked for Same/Older/Unverified entries.
   Do not actually install into a production WIM.
3. Open **Tools > Report error to GitHub...**. Confirm DISM output is unchecked,
   a sanitized preview is shown, and the GitHub button cannot be used before
   checking the explicit review checkbox.
4. Confirm changing the report clears approval. Selecting report submission
   opens a draft in your browser; nothing is transmitted until you click Submit
   on GitHub. You can close the draft without posting.
5. Check **Tools > Check for updates**, changelog, email support and program icon.

## 5. DPI and all application screens

Check Windows display scale **100%, 125%, 150%, 200%** when available.
At each scale inspect the Mount, Drivers, Packages, Features, Capture/Apply,
WIM 360, Driver Studio, Test Center, Support, Update and About windows.
Verify no labels/buttons overlap, all options are reachable by scrolling,
and DISM output can be expanded/restored without hiding buttons.
Restore the last manually chosen window size and position after restart.

## Reporting a failure

Use **Test Center > Report issue...** or **Tools > Report error to GitHub...**.
Only the last 60 DISM output lines are optionally included. Always review
redaction before posting; do not upload raw BitLocker recovery keys, LAPS data,
secrets, share names, or private customer files to a public issue.
Use the built-in **Email Support** address if you prefer private correspondence.

**Automated CI versus manual coverage:** GitHub CI compiles x86/x64/ARM64,
tests read-only WIM mount, creates a real split SWM fixture, validates
missing-part detection and unchanged SHA-256, and captures WinForms screenshots.
It cannot fully replicate an operator's production DISM image inventory, every
monitor/DPI configuration, or external hardware and Samba setups.
