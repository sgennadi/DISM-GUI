# UniversalDiagnostics embedded integration — DISM GUI 4.3.10

DISM GUI includes the canonical .NET source from
[UniversalDiagnocticsModule](https://github.com/sgennadi/UniversalDiagnocticsModule/tree/main/embedded/dotnet)
**inside the application EXE**. There is no independent diagnostics collector
to install, no subprocess, and no network interaction during local capture.
The DISM GUI version stays **4.3.10**. This integration is included in unified draft
[PR #13](https://github.com/sgennadi/DISM-GUI/pull/13);
it does not overwrite the already published 4.3.10 release.

## Usage

1. Open **Tools → Report error** (or About → Report error).
2. Review the diagnostics preview. Existing optional DISM output and local
   history remain available only when their checkboxes are selected.
3. Check the explicit **I reviewed...** box.
4. Click **Save locally (built-in)**. Review the exact sanitized JSON summary
   in the confirmation dialog, then click **Yes** to capture.
5. The dialog shows the generated report ID.
6. Open **Saved diagnostics** to inspect the local queue. Choose an event
   and click **Open GitHub draft...** to review a second confirmation.
   This copies a sanitized JSON report and opens an Issue draft in the browser.
   The operator must sign in and click **Submit** on GitHub. An opened draft
   does not change the local report's delivery status.

The embedded core creates an atomic UTF-8 event:
\`%LOCALAPPDATA%\UniversalDiagnostics\<project-hash>\<report-id>.json\`

The saved record follows \`universal-diagnostics/v1\`:
\`{"event":{...,"fingerprint":"..."},"delivery":{"status":"pending","url":""}}\`.
No \`excerpt\`, DISM output, Windows username, machine identity, local file
paths, environment variables or GitHub tokens are included.

**Nothing is uploaded automatically**. The existing manual browser-based
GitHub Issue draft works independently. A saved queue item also offers a
separate, manually reviewed browser Issue draft; it never sends in the background. The universal repository's optional
standalone CLI/EXE can later inspect and submit these same queue records,
with explicit authorization/review, but it is NOT needed for normal app use.

## Architecture and updating

- Canonical source: \`UniversalDiagnocticsModule/embedded/dotnet/UniversalDiagnosticsEmbedded.vb\`.
- Vendored identical source: \`DISM-GUI/tools/UniversalDiagnosticsEmbedded.vb\`.
- App-specific, redacting UI adapter: \`DISM-GUI/tools/UniversalDiagnosticsBridge.vb\`.
- Build preparation copies both files into the Windows Forms project.
- CI tests create temporary queue records, assert schema/fingerprint,
  verify redaction and that no additional EXE is needed.
- For new embedded core versions, update the central file first and sync the
  exact source into each consuming project in its next test PR.

Other projects use the same canonical schema and per-project directory hash.
Native C++ builds use the equivalent canonical source under
\`UniversalDiagnocticsModule/embedded/cpp\`.

No installer, separately deployed DLL, private token, GitHub App client or
background telemetry is required. DISM servicing/image functionality and
its existing local diagnostics are unchanged.
