# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.0.0-beta.2] - 2026-03-20

### Added
- Zero-config API: `Adoc.ToHtml()`, `Adoc.ToPdf()`, `Adoc.ToStyledHtml()` one-liners
- Stream overloads: `Adoc.ToHtml(input, stream)`, `Adoc.ToPdf(input, stream)`, `Adoc.ConvertFile(path, stream)`
- Specialized CLI tools: `adocnet-pdf`, `adocnet-epub`, `adocnet-docbook`
- Asciidoctor-compatible CLI flags: `-b`, `-D`, `-a`, `-n`, `-e`
- Default output to file (matching Asciidoctor behavior), `-o -` for stdout
- Built-in document attributes: `{docyear}`, `{docdate}`, `{nbsp}`, `{cpp}`, etc.
- PDF text justification (paragraphs and table cells)
- Real per-character Helvetica AFM font metrics for accurate PDF text measurement
- Allocation guard tests for CI regression detection
- Asciidoctor compatibility documentation
- Comprehensive test fixtures (special characters, long paragraphs, anchors)
- **AdocNet.Layout** — UI-agnostic layout model (netstandard2.0 + net10.0) with LayoutBuilder for AST-to-layout conversion; supports paragraphs, headings, lists, code blocks, admonitions, tables, description lists, TOC, and all inline formatting
- **AdocNet.Avalonia** — Avalonia renderer (net10.0) that converts layout trees to stock Avalonia controls with rich inline formatting, clickable links, table grid rendering, and colored admonitions
- **Sample Avalonia Viewer** — minimal desktop app (`samples/AdocNet.Avalonia.Viewer`) for opening and rendering `.adoc` files with Fluent theme
- Parser: non-indented description list continuation (`Term::\nDescription`), `[horizontal]` attribute consumption, `[#id,reftext]` block anchors before description list items

### Fixed
- PDF paragraphs and code blocks running off right margin (text wrapping)
- Non-ASCII characters (copyright, accented) rendering as `?` in PDF
- `{docyear}` and other built-in attributes not substituted
- `[[id,reftext]]` anchor syntax putting comma+reftext in the ID
- Block anchors `[[id,reftext]]` dropped when preceding definition lists
- Cross-reference link text showing `[id]` instead of reftext
- PDF table column auto-sizing (equal-weight columns treated as auto-sized)

### Changed
- CLI default output changed from stdout to file
- Removed legacy CLI flags (`--styled`, `-f`, `--out-dir`)

## [1.0.0-beta.1] - 2026-03-19

Initial public beta.

### Features
- Full AsciiDoc parser with 94% Asciidoctor conformance (202/215 test cases)
- Four output renderers: HTML5, PDF 1.4, DocBook 5.0, EPUB 3.0
- CLI tools: `adocnet`, `adocnet-pdf`, `adocnet-epub`, `adocnet-docbook` with Asciidoctor-compatible flags
- Watch mode, live preview server, and project configuration
- LSP server with diagnostics, symbols, hover, go-to-definition, completion
- Extension architecture: custom renderers and include readers
- Multi-targeting: netstandard2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and net10.0 (optimized)
- Symbol packages (.snupkg) for all NuGet packages
- CI/CD with GitHub Actions (3-platform matrix, automated NuGet publishing)
