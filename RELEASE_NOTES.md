# DISM GUI 4.3.2

## Driver Studio — Selective INF Import

- **Install selected INF** adds only checked **New / Newer** packages to one already-mounted, read/write Windows image index. No automatic whole-folder import.
- Blocks Same version, Older, Review, Duplicate source and Not checked; those rows cannot be selected for installation.
- **Smart Preflight:** immediately before writing, validates the exact WIM file/index/mount triplet and read/write status, re-reads installed drivers, re-scans driver sources and validates INF SHA-256/provider/class/date/version against the user-reviewed plan.
- If the installed inventory or an INF changes after comparison, the entire batch is refused until a new comparison. INF files are checked again before each command.
- Uses `dism.exe /Image:<mounted-folder> /Add-Driver /Driver:<one-inf>` with separated arguments for each selected INF. No `/Recurse` or `/ForceUnsigned`; DISM handles driver/package signature checks.
- Requires Administrator privileges, shows an explicit confirmation listing WIM/index/mount and cautions that the command **modifies** the mounted image.
- **Stop after current** completes the active DISM operation before stopping the remaining list; avoids interrupting an in-progress image write.
- Displays per-INF exit results in the interface and includes them in the Copy comparison report. Stops after a failure; does not silently roll back.
- Never automatically mounts, unmounts, commits or discards a WIM. Review results and explicitly commit/discard using the original Mount section.
- Import choices expire after the operation, requiring a new Compare before another import.

## From 4.3.1 / 4.3.0

- WIM 360 read-only image inventory and index handoff; separate Driver Studio source metadata and version-comparison preview.
- Original nine servicing pages retained, DPI-aware scrollable panels, resizable output, saved window placement.
- Update Center with release changelog and SHA-256-verified updates; About / Tools email support: sgennadi@hotmail.com.
- Portable self-contained .NET 10 Windows x64, x86 and ARM64 packages.

## Testing and limitations

- Windows CI verifies safe command arguments, allowed/blocked statuses, source-hash and inventory invalidation, import controls, and the existing WIM 360/UI tests.
- CI does **not** install real drivers or simulate an interrupted image write. Validate first using disposable mounted WIM copies on Windows.
- The source-INF hash does not cryptographically cover a driver's additional payload files or prove that Windows PnP will select the driver at boot. Driver compatibility/signing remains the responsibility of the operator and DISM.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI); attribution retained.
