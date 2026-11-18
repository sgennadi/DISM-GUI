# Four-stage acceptance: DISM-GUI 4.3.21 candidate

This page distinguishes automated evidence from operator-only field checks.
CI success is not proof that a damaged WIMMount registration has been repaired,
or that Windows 11 / Server installation media were actually tested.

## Stage 1 — Mount Recovery Assistant

Tools -> WIM Mount Recovery (also in WIM 360 -> Mount health).

- Inspect the selected WIM/ESD index with WIMGAPI; compare the documented
  DISM mount state to read-only WIMMount registry metadata.
- Find file locks queries Windows Restart Manager for a bounded sample of
  regular files. It cannot guarantee detection of directory or kernel handles.
- Record restart check persists a checkpoint locally. After a **manual
  Windows Restart**, Verify after restart re-checks the mount. A boot-time
  comparison is a hint, not cryptographic proof of a system restart.
- Even if registration disappears, the assistant does not conclude an
  abandoned mount folder is safe to delete.

**No operations** to automatically stop WIMMount, edit the registry, take
ownership, delete mount folders, Cleanup-Mountpoints, Remount-Image,
Commit or Discard. An uncertain earlier Commit must always be reviewed.

Known real incident: a 4.3.21 user ran /Unmount-Wim /Discard, got DISM
0xC1420117 after 100% progress, then 0xC1420123 from /Remount-Image;
Cleanup-Mountpoints returned 0 but DISM still reported Invalid.
The on-host result after an actual Windows Restart is NOT available.
The wizard does not claim to resolve that mount.

### Existing-mount collision (0xC1420127)

A later real field test of `boot.wim`, index 1, reported
`0xC1420127` on a duplicate R/W mount. The read-only
`DISM /English /Get-MountedWimInfo` output identified the same WIM/index
and folder, `Mounted Read/Write : Yes`, `Status : Invalid`.
This is NOT a new blank mount and is NOT a recovered WIM.

The main Mount button now checks WIMGAPI for the exact WIM/index and
system-wide registered mount directories before launching DISM, including
one final recheck in its worker. A normal existing mount or an
Invalid/Needs Remount/Mounting registration blocks the command and offers
read-only WIM 360 Mount health. No automatic unmount, remount, registry
cleanup, folder deletion or write action is performed. Mounting a WIM
into another image's reserved mount folder is also blocked.

Operator sequence: preserve the WIM and folder, close programs using
the mount, retain the DISM log, inspect state before/after a proper
Windows Restart, and use the Recovery Assistant only for read-only
assessment until the state and any uncommitted changes are understood.
Microsoft's documented `/Remount-Image` and `/Cleanup-Mountpoints`
are separate actions, not automatic recovery; `/Cleanup-Mountpoints`
can remove corrupt mount resources and must not be run blindly when
uncommitted changes might need preserving.

Official Microsoft reference:
https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-image-management-command-line-options-s14

## Stage 2 — Operation Center and Smart Preflight

Tools -> Operation Center (also accessible from WIM 360).

Available allowlisted steps:
- EnvironmentPreflight: read-only environment checks
- WimMetadata: WIMGAPI inspection
- MountHealth: WIMGAPI + local journal read-only
- MountedInventory: read-only for an already verified mounted index
- ExportIndexNewWim: writes exactly ONE index into a **NEW** output WIM.
  This step requires fresh, typed interactive approval on EVERY run.

A full plan is checked before execution; steps run sequentially, stop on
failure and record status locally. One queue at a time may use the same
local Operation Center store, including across application instances.
The queue lock is held through final journal persistence and is released
by Windows when its owning process exits; the persistent lock file is not
itself evidence of an active queue. History refresh does not label a live
queue as interrupted. Cancellation may be deferred while
Windows is performing an operation; interrupted exports can leave a
uniquely named .partial.wim for manual review.

No arbitrary commands, no unattended mount, Commit, Discard, driver
injection or package modification. These existing servicing workflows
retain their separate interactive confirmations and image-operation locks.

## Stage 3 — Profiles and write checkpoints

Profiles can contain at most 16 steps; the app retains at most 24
profiles and up to the last 100 run entries within a 512 KiB history budget.
When the history reaches that budget, the oldest complete runs are removed;
the newest run's checkpoint and diagnostic messages are retained intact.
Local storage is under
%LOCALAPPDATA%\DISMGUI\OperationCenter. Schema validation rejects
unknown commands and overwrite destinations. Profile files NEVER
store the approval for a write step.

Before a DISM new-WIM export starts, the queue writes a checkpoint to
local history, takes an exclusive cooperative image-writer lease and
revalidates the WIM/index, target availability and free space. A source
SHA-256 is calculated before and after export. Output is verified with
WIMGAPI before the unique temporary WIM is renamed to its new target.
The original WIM is never overwritten, mounted or serviced.

An interrupted or uncertain run is shown as REVIEW_REQUIRED on the
next launch. No automatic retry or deletion of partial WIMs is allowed.
The user must review the output and DISM log.

## Stage 4 — automated and field acceptance

### Automated CI

- Portable Windows x86, x64 and ARM64 builds.
- Regression checks for Mount Recovery and explicit export approval.
- Real DISM capture, read-only mount/unmount and export of a disposable
  test WIM. Original SHA-256 and produced one-index WIM are checked.
- GUI smoke tests with screenshots for 125%, 150%, 175% and simulated
  200% font scaling.

### Manual field tests (NOT covered by hosted CI)

A. On the affected Windows machine, complete a full Restart and then run
   DISM /Get-MountedImageInfo to determine whether Invalid persists.
   Use Mount Recovery to inspect; do not remove registry entries or
   folders merely because cleanup returned success.

B. On a physical display configured for **Windows 200% scaling**,
   check Mount Recovery, Mount Health, Operation Center, WIM 360
   Compare Indexes, Driver Studio and all dialogs. Verify buttons, long
   source paths, scrollbars and text under the actual monitor DPI. CI
   simulated font scaling does not substitute for this field test.

C. Supply authorized Windows 11, Server 2022 and Server 2025 install.wim
   media, plus a compatible vendor-signed INF/CAT package for each target.
   Review all WIM indexes with DISM /Get-WimInfo and create matrix.json
   as documented in docs/REAL-WIM-OEM-INTEGRATION.md.

D. Extract the complete **RealOemIntegration-win-x64** artifact from the
   matching successful CI run into a test tools folder. It includes
   RealOemIntegration.exe, Invoke-FourStageAcceptance.cmd and both acceptance
   guides together; no repository checkout, PowerShell or .NET SDK is required.

   First run read-only (CMD; no PowerShell required):

~~~cmd
Invoke-FourStageAcceptance.cmd "C:\OEMTests\matrix.json" "D:\WIMTests"
~~~

   Then, only with explicit approval to service **disposable test
   copies**, run:

~~~cmd
Invoke-FourStageAcceptance.cmd "C:\OEMTests\matrix.json" "D:\WIMTests" TEST_COPY_ONLY
~~~

   Review FIELD-*/mounted-before.txt, mounted-after.txt, media-probe.txt,
   matrix-plan.txt, matrix-execute.txt (if run) and individual
   oem-validation-report.json files. These reports stay local.

### Release criteria

Do not mark all four stages complete simply because CI passes.
At minimum, complete the manual matrix and physical 200%-DPI tests,
verify the user's actual Invalid mount after reboot, and obtain a
successful clean branch and post-merge CI. Do not overwrite an existing
published 4.3.21 stable ZIP with a different binary; version bump and
release approval must be explicit and separately verified.
