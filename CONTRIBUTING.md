# Contributing to Adoc.Net

Thanks for your interest in improving Adoc.Net. This guide covers the basics for
building, testing, and submitting changes.

## Prerequisites

- **.NET 10 SDK** (the repository pins the SDK band via `global.json`). The
  libraries multi-target `netstandard2.0`, `net8.0`, and `net10.0`, but the
  build, tests, CLI tools, and examples require the .NET 10 SDK.
- Optional, for parity work: a local install of [Asciidoctor](https://asciidoctor.org/)
  (the conformance/parity tests compare against it when present).

## Build and test

```bash
dotnet build AdocNet.slnx -c Release
dotnet test  AdocNet.slnx -c Release
```

The build treats warnings as errors. Please keep the build warning-free.

The test suite is the source of truth — add or update tests for any behavior
change. Parity-sensitive changes (HTML/DocBook/Reveal.js output) are checked
against the `spec/conformance/` corpus; run them locally before opening a PR.

## Making changes

1. Branch from `main` (e.g. `fix/issue-123-short-description`).
2. Keep commits focused; use [Conventional Commits](https://www.conventionalcommits.org/)
   style messages (`fix(parser): …`, `feat(html): …`, `docs: …`).
3. Add tests that fail before your change and pass after.
4. Run the full build + test suite.
5. Open a pull request describing the change and linking any related issue.

## Project layout

See the **Architecture** section of [README.md](README.md) for the assembly map.
In short: `src/` holds the libraries, converters, CLI tools, and language server;
`tests/` the test suites; `spec/` the conformance corpus; `docs/` the guides;
`examples/` and `samples/` runnable demonstrations.

## Reporting bugs and requesting features

Open an issue at <https://github.com/sonatique/Adoc.Net/issues>. For security
vulnerabilities, please follow the process in [SECURITY](.github/SECURITY.md)
instead of filing a public issue.
