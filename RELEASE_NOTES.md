# DISM GUI 4.3.24 — Stable

Portable Windows release for x86, x64 and ARM64, built using Visual
Studio 2026 and self-contained .NET 10. This maintenance release
simplifies Driver Studio after feedback on stable 4.3.23.

## Simplified Driver Studio

- Removed **Open Drivers section** and its confirmation dialog. The
  existing **Driver tools** sidebar item performs that navigation
  and automatically carries over the selected driver source folder.
- Removed the separate **Compare with mounted index** command.
  One always-visible **Scan / Refresh** button reads the source INF
  files and automatically compares versions whenever the selected
  WIM file/index is already mounted at the expected path.
- Only the exact WIMGAPI-verified WIM/index/mount registration can
  supply an installed-driver inventory. If a WIM is not mounted,
  Scan / Refresh still scans INF sources but labels results
  **Not checked** or **Review**. It never assumes missing installed
  drivers or marks packages eligible for import.
- Returning from **Mount** to Driver Studio rechecks a previously
  scanned source automatically. Even if the same WIM is mounted
  again at the same path, the prior comparison is not trusted.
  If an image is unmounted or discarded, previously verified
  results are invalidated, and checkboxes are disabled until a
  new verified comparison completes.
- Removed the redundant second **Scan INF** button. The single
  **Scan / Refresh** action remains visible in the workspace header,
  with sufficient width for simulated 125–200% DPI.
- **Stop after current** is displayed only during an active
  selective WIM driver import. Removed redundant Commit/Discard
  warning text, retaining one concise optional-write note.
- The read-only **Copy comparison** action remains separate from
  the application's **Copy log**, since they export different data.

## What remains intentionally explicit

- **Select New / Newer** only marks eligible INF entries if the
  user requests it; default selection is empty.
- **Clear** removes the selection; **Audit selected** and
  **Audit all INF** validate source files without WIM changes.
- **Add selected to WIM...** is an optional, separately confirmed
  operation requiring signed-package checks, fresh WIM inventory,
  a read/write mount and administrator approval. It stages driver
  packages in the OFFLINE WIM, not the running Windows PC, and
  does not remove old driver versions or automatically Commit.
- The legacy **Driver tools → Add drivers** remains an independent
  whole-folder command without Driver Studio's selective New/Newer
  filtering. Use it only when intentional, especially with Recurse.

## Acceptance and limitations

- Windows CI x86, x64, ARM64: successful compiled portable builds.
- x64 WinForms/WIM smoke and simulated 125%, 150%, 175%, 200% UI
  scaling: successful, including real Scan / Refresh on an
  **unmounted** WIM, stale-state invalidation and single-button UI.
- Hosted Windows VM signed OEM / WIM verification: successful.
- Automatic comparison for a newly re-mounted WIM relies on the
  existing WIMGAPI-verified inventory bridge. The specific visual
  flow on physical user equipment should still be confirmed after
  updating; no real-device driver binding is claimed.
- No automatic DISM Mount, Unmount, Commit, Discard, Remove-Driver,
  WIMMount cleanup or ForceUnsigned actions are introduced.
- No change to selective-import write engine or its postflight
  verification logic.

## Rollback

The original [v4.3.23 stable](https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.23)
and its published ZIPs remain intact if rollback is needed.

Original upstream DISMGUI attribution and licensing remain intact.
