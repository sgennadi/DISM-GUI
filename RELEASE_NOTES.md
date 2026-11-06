# DISM GUI 4.3.17 — Stable

Portable self-contained Visual Studio 2026 / .NET 10 builds for Windows
x64, x86 and ARM64. The previous stable 4.3.16 remains available.

## WIM 360 — cross-image mounted files and SHA-256

- **Compare 2 files** now offers a third mode:
  **Files + sizes (already mounted)**, alongside the existing
  read-only metadata and offline path-presence modes.
- Select one index in each of two **different** WIM/ESD files.
  Both indexes must already be mounted in separate directories.
  Prefer DISM **read-only** mounts. The application never mounts,
  commits, discards, unmounts or modifies either source image.
- Lists files, byte lengths, paths and empty folders for both
  mount roots. Classifies missing, unique, same-size, different-size
  results with A/B source and original-index labels.
- **SHA-256...** checks one selected file or bounded filtered batch.
  Maximum **50 paths / 1 GiB total reads** across both mounted images,
  with explicit confirmation and cancellation.
  Source paths, file sizes, reparse types and timestamps are checked
  before or after reading; verified rows are applied **atomically**
  only after the whole operation succeeds. Other rows remain a
  size/path comparison, not an assertion of equal content.

## Important WIMMount reparse fix

Real Windows DISM-mounted WIM files are regular content backed by
Microsoft **IO_REPARSE_TAG_WIM** (or **IO_REPARSE_TAG_WOF**).
Earlier generic reparse filtering skipped these real files, leaving
only directories in some mounted comparisons. The new shared
read-only walker recognizes these two safe content-backing types
while refusing other reparse files and all reparse directories
(including symlinks and junctions). This fix also applies to
**Compare indexes** within a single mounted WIM/ESD.

## Interface, safeguards and validation

- Cross-image Compare / SHA-256 / Export / Close stay in the
  bottom toolbar at simulated 125%, 150% and 175% font scaling.
  Adaptive A/B grid sizing and compact mode text avoid truncation.
  Window geometry is saved independently from the main DISM GUI.
- A scan refuses more than **250,000 entries per mount** and
  rejects inaccessible paths rather than silently showing a
  partial comparison. Unknown/untrusted reparse types are skipped.
- Windows CI creates and mounts two real, independent test WIMs
  using DISM **/ReadOnly**, validates differences and SHA-256
  for same-size, different-byte files, and dismounts with **/Discard**.
  Both image SHA-256 sums and original timestamps are verified.
- Synthetic unit tests also cover symlinks (where supported),
  traversal rejection, changed file sizes, canceled hash operations,
  same-WIM backward compatibility and protected CSV output.
- These tests cannot prove behavior for every real Microsoft Windows
  install.wim, machine-specific driver, or production storage setup.
  Validate representative test images before production use.

All other WIM 360, Driver Studio 4.3.15, diagnostics and update
workflows remain unchanged. No automatic diagnostic upload added.
