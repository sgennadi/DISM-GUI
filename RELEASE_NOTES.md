# DISM GUI 4.3.25 — Stable

Portable Windows x86, x64 and ARM64 release built with Visual Studio
2026 and self-contained .NET 10. This is a Driver Studio UI and
usability maintenance update over v4.3.24.

## More coherent Driver Studio actions

- **Add selected to WIM...** is now on the action bar above the INF
  comparison table, directly alongside **Select New / Newer**, **Clear**
  and the two read-only **Audit** actions. It no longer lives as a
  detached large button at the bottom of the page.
- During a real selective WIM import, **Stop after current** replaces
  the Add action in the same toolbar slot. Stop is not shown when idle.
- **Select New / Newer** is available immediately once an eligible,
  verified WIM comparison finishes. When all eligible packages are
  already checked, the caption changes to **Deselect New / Newer**.
  Clicking again visibly clears them; no need to click an INF row first.
  The independent **Clear** action still clears a partial/manual plan.
- Changing the driver source immediately resets all selection, audit
  and import controls; stale WIM comparisons cannot authorize writes.
- **Copy comparison** now sits beside **INF DETAILS** rather than
  consuming its own large footer row. Source package detail content
  remains scrollable and export remains read-only.
- Removed two unused bottom action rows. The driver table and INF
  details expand with available window height instead of leaving a
  large unused bottom area.
- The action bar now adapts to window width and Windows font scaling:
  one row on wide screens; three legible rows in compact/high-DPI
  windows, keeping Audit and Add actions reachable.

## Safety remains unchanged

- **Scan / Refresh** automatically compares only with an already
  mounted, WIMGAPI-verified exact WIM/index, without modifying it.
- New/Newer packages are never selected automatically by scanning.
  A checkbox or Select action only creates a prospective plan; it does
  not call DISM Add-Driver by itself.
- **Add selected to WIM...** remains an optional, explicitly confirmed
  write to the **mounted offline image** (R/W). It never silently
  installs drivers in the running PC, removes older drivers,
  commits/discards a WIM or changes WIMMount registry entries.
- Signature/manifests, selected target/index, architecture, source INF,
  fresh inventory, administrator rights and writer lease still
  gate the import. Existing preflight and postflight validations are
  unchanged.
- Legacy **Driver tools → Add drivers** retains independent
  whole-folder semantics; use selective Driver Studio staging when
  you do not want to stage older/same-version packages.

## Validation

- Windows CI x86, x64, ARM64: success.
- Real disposable WIM servicing and WinForms interaction tests: success.
- Simulated font scaling 125%, 150%, 175%, 200%: success, including
  compact toolbar access, button captions, optional write placement
  and source-change selection-state reset.
- Hosted Windows VM signed OEM/WIM validation: success.
- Physical machine use, including an actual user-mounted WIM with
  production drivers, still merits local acceptance testing.

## Rollback

Stable [DISM GUI v4.3.24](https://github.com/sgennadi/DISM-GUI/releases/tag/v4.3.24)
and its original portable ZIP files remain unchanged and available.

Original upstream attribution and project licensing remain intact.
