# Security Policy

## Supported releases

Automatic installation of GitHub release ZIP files is disabled until the updater verifies a publisher-controlled cryptographic signature with a trusted public key embedded in the application. SHA-256 digests from the same GitHub release only verify integrity, not authenticity.

Existing releases, including 4.3.27, must not be described as cryptographically authenticated. No security fix is considered shipped until a signed build is tested and released.

## Reporting vulnerabilities

Please report vulnerabilities privately through GitHub's **Report a vulnerability** feature when enabled, or contact the maintainer at sgennadi@hotmail.com. Do not publish proof-of-concept privilege-escalation details before coordination.

Include the affected version, reproduction steps, expected/actual behavior and sanitized diagnostics. Do not include credentials, WIM contents, personal information or machine identifiers.

## Release requirements

- Obtain and document permission/license status for upstream code before redistributing new binaries.
- Build and test on Windows x86, x64 and ARM64.
- Verify update package signatures using an embedded trusted public key; fail closed on missing, invalid or unknown signatures.
- Use administrator-only staging directories, reject reparse points and untrusted ACLs, and revalidate staged executable content before replacement.
- Complete real WIM mount, read-only inspection, servicing and rollback acceptance, plus 200% DPI GUI checks.
- Publish preview artifacts for testing; publish stable only through explicit maintainer approval.
