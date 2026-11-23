# Independent DISM GUI — work in progress

This project is a newly authored .NET 11 Windows Forms application. It does not download, patch, compile or link the mikecel79/DISMGUI source tree.

Current implemented functionality: choose a local image and run a read-only DISM /Get-WimInfo command, and inspect mounted WIM images with /Get-MountedWimInfo. The UI is DPI-aware. All other DISM GUI features remain to be independently implemented or provenance-reviewed before reuse.

The legacy project and its upstream-fetching scripts remain elsewhere in the repository for comparison only. They are NOT part of this independent project's build. Do not redistribute a repository-wide source archive as independently licensed until the legacy material is removed or rights are established.

No stable release is authorized by this scaffold.
