# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.0.0] - 2026-03-19

Initial release.

### Features
- Full AsciiDoc parser with 94% Asciidoctor conformance (202/215 test cases)
- Four output renderers: HTML5, PDF 1.4, DocBook 5.0, EPUB 3.0
- CLI tool with watch mode, live preview server, and project configuration
- LSP server with diagnostics, symbols, hover, go-to-definition, completion
- Extension architecture: custom renderers and include readers
- Multi-targeting: netstandard2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and net10.0 (optimized)
- 1427 tests including cross-TFM consistency and Asciidoctor conformance suites
- Symbol packages (.snupkg) for all NuGet packages
- CI/CD with GitHub Actions (3-platform matrix, automated NuGet publishing)
