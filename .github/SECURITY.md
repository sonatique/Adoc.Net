# Security Policy

## Reporting a vulnerability

If you discover a security vulnerability in Adoc.Net, please report it
**privately** rather than opening a public issue.

- Preferred: use GitHub's [private vulnerability reporting](https://github.com/sonatique/Adoc.Net/security/advisories/new)
  ("Report a vulnerability" on the repository's **Security** tab).
- We aim to acknowledge reports within a few business days and to provide a
  remediation timeline after triage.

Please include enough detail to reproduce the issue (a minimal AsciiDoc input
and the API/CLI invocation used, where applicable).

## Supported versions

Security fixes are released against the latest `1.0.x` line.

| Version | Supported |
|---------|-----------|
| 1.0.x (latest) | ✅ |
| < latest 1.0.x | ❌ (upgrade to the latest patch) |

## Hardening guidance

For guidance on processing untrusted documents safely (safe mode, includes,
passthrough/XSS, data-uri, Avalonia links), see [docs/SECURITY.md](../docs/SECURITY.md).
Key default: `ParseOptions.SafeMode` defaults to `SafeMode.Safe`, which confines
`include::` resolution to the document's base directory.
