# DISM GUI 4.3.0

## WIM 360 — read-only Image Studio (first stage)

- New **WIM 360** workspace appears at the top of the application's left navigation, without removing or renumbering the nine existing servicing sections.
- Browse a Windows WIM or ESD image, or reuse the WIM file path from Mount & Inspect.
- Inspect an image using Windows **WIMGAPI** metadata-query-only access, rather than parsing localized DISM output or mounting the image.
- Display all indexes with image names, Windows versions/builds, edition, architecture, product, installation type, languages and compressed-file/uncompressed-index sizes when present in metadata.
- Show each index's mount state and path using the native mounted-image inventory. An unavailable mount inventory is shown as **Not checked**, never misleadingly as Not mounted.
- **Use index in Mount** copies only the chosen image path and index into the original Mount form. It does **not** mount, commit, discard, or modify the WIM.
- **Copy report** produces an easy-to-share text inventory for troubleshooting.
- Image metadata is loaded off the UI thread; bad paths, unsupported split SWM files, and invalid WIM files show actionable errors.
- Safer XML parsing: DTDs and external entities disabled, payload size bounded, duplicate/invalid indexes rejected.

## Included from earlier releases

- Existing nine responsive DISM pages, read-only until an explicit existing servicing action is chosen.
- Resizable DISM Output with saved height; saved main window size/position/maximized state.
- Update Center with scrollable GitHub release changelog, SHA-256-verified self-updater and Tools new-version indicator.
- **Email Support** in About / Tools, addressed to sgennadi@hotmail.com.
- Self-contained portable .NET 10 Windows x86/x64/ARM64 ZIPs, no PDB or legacy config.

## Verification and scope

- Windows x64 UI smoke test exercises WIM 360 navigation, two-index sample XML, index transfer to Mount, metadata rendering, malformed/XXE XML rejection, and existing nine servicing pages.
- Build and packaging are tested on x86, x64 and ARM64.
- The WIM 360 first stage is read-only. Driver/package counts, Smart Driver Detection, preflight, Mount Manager, and bulk jobs are separate planned improvements **not yet claimed complete**.
- Real boot.wim/install.wim scans and DPI/virtualization hardware behaviors must still be verified on Windows with representative images.

Original DISM GUI by Mike Celone (mikecel79/DISMGUI). Attribution retained.
