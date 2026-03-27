# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.0.0-beta.6] - 2026-03-27

### Added
- Dynamic extension loading from external DLLs via `AdocEngine.LoadExtension(path)` and `AdocEngine.LoadExtensions(directory)`
- `IExtension` optional metadata interface for extension identification (Name, Version)
- `ExtensionLoader` public utility class for scanning assemblies for processor types
- CLI `--extensions <path>` flag for loading extension DLLs (repeatable)
- CLI `--extension-dir <dir>` flag for loading all DLLs from a directory (repeatable)
- Deterministic load ordering: alphabetical by DLL filename, sorted by type name within each assembly
- Error handling for invalid assemblies (`BadImageFormatException`), missing dependencies (`ReflectionTypeLoadException`), and types without parameterless constructors
- Documentation: DYNAMIC_EXTENSIONS.md guide

### Compatibility
- Zero extensions loaded = output identical to beta.5
- All new API is additive; no existing public API changed
- Uses `Assembly.LoadFrom` for netstandard2.0 compatibility (no AssemblyLoadContext)
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.5] - 2026-03-26

### Added
- Processing extension system: `IDocumentProcessor`, `IBlockProcessor`, `IInlineProcessor`
- `AdocEngine.RegisterDocumentProcessor()`, `RegisterBlockProcessor()`, `RegisterInlineProcessor()` with fluent API
- `AdocEngine.OnWarning` callback for non-fatal processor errors
- `NodeReplacements` for AST node replacement and removal during processing
- `ProcessingPipeline` with guaranteed FIFO execution order (document -> block -> inline)
- `IDiagramToolRunner` abstraction for external diagram tool invocation
- `ProcessDiagramToolRunner` implementation using `Process.Start` with deterministic output filenames
- `DiagramBlockProcessor` supporting PlantUML, Mermaid, Ditaa, Graphviz, and DOT languages
- Built-in example extensions: `IconMacroProcessor`, `DocumentMetadataProcessor`, `AutoIdBlockProcessor`
- 46 new extension tests (pipeline invocation, ordering, error handling, diagram, integration)
- Documentation: DIAGRAMS.md, updated EXTENSIONS.md with processor guide

### Compatibility
- Zero extensions registered = output identical to beta.4
- All new API is additive; no existing public API changed
- Registration freezes after first `Convert()` call (throws `InvalidOperationException`)
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.4] - 2026-03-24

### Added
- Syntax highlighting tokenizer in AdocNet.Core with 7 language support (C#, Java, JavaScript, Python, JSON, XML/HTML, SQL)
- 9 token categories: keyword, string, comment, number, type, punctuation, attribute, preprocessor, plain
- Server-side syntax highlighting for HTML source blocks (`EnableSyntaxHighlighting` option)
- Syntax highlighting CSS rules in all 4 built-in HTML themes
- PDF syntax highlighting via `SyntaxColorScheme` with configurable per-token colors
- Github theme (`HtmlTheme.Github`) — GitHub-flavored HTML styling
- Liang/Knuth hyphenation engine with TeX US English patterns (`EnableHyphenation` option)
- Hyphenation-aware line breaking in PDF text layout
- Configurable paragraph spacing: `ParagraphSpacingBefore` and `ParagraphSpacingAfter`
- Configurable section spacing: `SectionSpacing`
- Heading color: `HeadingColor` property for PDF
- Body text color: `BodyColor` property for PDF
- Table header background: `TableHeaderBackground` property for PDF
- Block indent: `BlockIndent` property for PDF
- PDF style presets: `PdfRenderOptions.Compact` and `PdfRenderOptions.Presentation`
- 84 new tests across 8 test files
- Documentation: THEMING.md, SYNTAX_HIGHLIGHTING.md, TYPOGRAPHY.md

### Changed
- Justification max spacing tightened to 1.5x when hyphenation enabled (was 2x)
- PdfWriter word-wrapping code extracted into PdfWriter.WordWrap.cs partial
- Highlighted verbatim rendering moved to PdfRenderer.Blocks.cs partial

### Compatibility
- All new options default to values matching beta.3 output
- Syntax highlighting defaults to disabled (opt-in) in both renderers
- Existing tests pass without modification

## [1.0.0-beta.3] - 2026-03-24

### Added
- TrueType font embedding with Unicode support (cmap format 4 and 12)
- Font subsetting — only used glyphs are embedded, reducing PDF size
- JPEG image embedding via DCTDecode filter
- PNG image embedding with RGBA alpha channel support via SMask
- Clickable hyperlinks via PDF link annotations
- Table header repetition on continuation pages
- Total page count placeholder `{pages}` in headers/footers
- Configurable typography: FontSize, CodeFontSize, TitleFontSize, HeadingScale, LineSpacing
- Visual styling options: LinkColor, CodeBackground, AdmonitionBorderWidth
- RepeatTableHeader option for table page breaks
- PdfColor type for RGB color configuration
- Line-start punctuation prevention (no line starts with `)`, `.`, etc.)
- Justification spacing cap at 2x normal word space
- PDF renderer documentation (`docs/PDF_RENDERER.md`)
- 27 new tests covering fonts, images, links, tables, headers, configuration, determinism

### Changed
- PdfRenderOptions: 10 new properties (backward compatible — all have defaults matching beta.2)
- Renderer constants (font sizes, leading) now configurable via PdfRenderOptions
- PdfWriter and PdfRenderer split into partial classes for maintainability

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
