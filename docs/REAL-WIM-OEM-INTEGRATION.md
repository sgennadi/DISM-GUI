# Real Windows WIM + signed OEM INF validation (opt-in)

This is an **operator-run integration test**, not part of the automatic
installation of DISM GUI. It exercises the actual Driver Studio reader,
full-package SHA-256 + catalog audit, inventory comparison, selective
installation and postflight checks with a genuine Windows WIM and one
independently supplied, signed OEM driver. No proprietary WIM or vendor
driver is bundled with the repository.

## Scope and safety

- Test **only** on an elevated, disposable Windows test machine with .NET 10
  SDK, Windows DISM and sufficient local scratch disk space.
- The input may be a real Windows 11 / Windows Server **install.wim**
  (not .esd or .swm); choose one valid Windows OS index.
- Use one extracted, **legitimately signed** OEM driver package. Point to its
  original .inf, retaining its .cat, .sys and any dependent files in a
  normal directory tree. Avoid a huge multi-driver extraction directory:
  package audits deliberately stop above 1,600 files / 2 GiB.
- The original WIM is opened read-only, SHA-256 checked, and **copied** to a
  fresh local per-run directory before any DISM mounts/servicing.
- The per-run **copy** is mounted read/write; only a New/Newer selected INF
  can be staged. An unchanged source package and signed CAT are required.
- A successful staged-driver inventory is mandatory **before** committing to
  the copy. The copy is then remounted read-only to verify persistence, then
  unmounted without changes. The original source is SHA-256 verified again.
- On failure, the isolated copy and a JSON report are kept for inspection.
  The runner attempts Discard **only** for its own known mount.
  Never interrupt DISM or run another servicing tool on the same WIM concurrently.
- This does **not** prove that Plug and Play selects the driver on a physical
  machine, that every CAT file covers every payload, or that an OEM platform
  firmware/device is compatible. No driver is installed on the running host.
- **No upload** occurs. The local report contains source paths; inspect it
  before sharing. The universal updater and diagnostic upload remain separate.

## Windows setup

Start an elevated Windows Terminal / PowerShell session. Either use a
repository checkout with **.NET 10 SDK** or download the self-contained
**RealOemIntegration-win-x64** ZIP from this branch's successful GitHub
Actions build (Artifacts). The latter requires no .NET SDK and is deliberately
not included in DISM GUI's stable release.

For the portable executable, replace the dotnet-run project prefix in the
examples below with .\\RealOemIntegration.exe and use the same arguments.
Verify the ZIP originated from a successful run of the expected GitHub
commit. The runner requires normal elevated Windows servicing privileges.

Create a dedicated scratch directory on a **local fixed disk** with enough
free space. A conservative minimum of 16 GiB is enforced; larger images may
require considerably more. Choose a scratch path that does **not** contain
either source and does **not** live inside the OEM driver's package folder.

For example, using a Server 2022/2025 install.wim, replace all paths and
the selected image index with values from your actual media.

### 1. Read-only plan (no mount, no image copies)

~~~powershell
dotnet run --project tests/RealOemIntegration/RealOemIntegration.csproj --configuration Release -- --plan --wim "C:\Images\install.wim" --index 2 --inf "C:\OEM\NetAdapter\netadapter.inf" --work-root "D:\DismOemTest"
~~~

The plan reads WIM index metadata, verifies the INF architecture, performs
a full-package SHA-256 manifest and Windows CAT Authenticode check, and
checks available disk space. If the catalog is not reported **Valid**,
the runner refuses to proceed. The plan does not create a WIM copy, mount
the image or perform any DISM modifications.

### 2. Explicitly validate against a disposable copy

~~~powershell
dotnet run --project tests/RealOemIntegration/RealOemIntegration.csproj --configuration Release -- --execute --confirm TEST_COPY_ONLY --wim "C:\Images\install.wim" --index 2 --inf "C:\OEM\NetAdapter\netadapter.inf" --work-root "D:\DismOemTest"
~~~

The copy and the report are kept under:

~~~text
D:\DismOemTest\OEM-<unique-id>\test-copy.wim
D:\DismOemTest\OEM-<unique-id>\oem-validation-report.json
~~~

The test does not rewrite or replace the original WIM. A successful
report states the selected WIM index, target architecture, valid CAT,
baseline inventory fingerprint, successful staged-driver presence,
presence after committed-copy remount, and original source hashes.
A failure report marks the copy as requiring investigation; check
DISM /Get-MountedImageInfo before manually cleaning up any mount.

### 3. CI-only non-destructive self-test

~~~powershell
dotnet run --project tests/RealOemIntegration/RealOemIntegration.csproj --configuration Release -- --self-test
~~~

The normal Windows x64 GitHub Actions build runs **only this self-test**.
It checks argument safety, path boundaries, the copy-only confirmation,
and conservative disk-space calculations using dummy local files. GitHub
Actions does **not** have actual Microsoft install.wim or the licensed,
signed vendor driver package and must not claim that a real OEM import
has been validated.

## Test acceptance

A genuine OEM scenario passes only if all of these are verified:

1. The original WIM SHA-256 is identical before and after the run.
2. The CAT is Valid, architecture matches, source manifest remains intact.
3. The live mounted inventory's New/Newer decision and baseline snapshot
   agree with the reviewed values.
4. The selective INF import returned DISM success.
5. The original INF identity/version is present in the mounted image
   inventory **after staging**.
6. After committing **only the test copy**, the INF is still present in the
   read-only remounted image.
7. All owned test mounts are unmounted without automatic modifications to
   any production image.

Run once per OS family/architecture and representative OEM driver package.
Do not use this tool directly against the production WIM, a mounted image
being used by another operator, or a driver source package that can change
while the test executes.
