# Asciidoctor Compatibility

AdocNet targets 94% output compatibility with Asciidoctor 2.0.26.
This document describes what matches, what differs, and what is unsupported.

## Conformance

Tested against 215 fixture files with Asciidoctor-generated expected HTML.

| Category | Count | Percentage |
|----------|-------|------------|
| Identical output | 202 | 94.0% |
| Minor differences | 13 | 6.0% |

## What matches Asciidoctor

- Document structure: title, author, revision, attributes
- All heading levels (= through ======)
- Section auto-IDs and custom IDs (`[#my-id]`)
- Paragraphs and inline formatting (bold, italic, monospace, highlight)
- Nested inline formatting (`*_bold italic_*`)
- Unordered, ordered, description, and nested lists
- Checklists
- Tables: header/footer rows, column specs, spans, alignment, cell styles
- CSV/DSV/TSV table formats
- All delimited blocks: source, listing, literal, example, sidebar, quote, verse, open, passthrough
- Admonitions (all 5 types, inline and block)
- Include directives with lines, tags, and leveloffset
- Conditional directives (ifdef, ifndef, ifeval)
- Cross-references, anchors, inter-document xrefs
- Footnotes with back-references
- Callout markers and lists
- Smart punctuation (em dash, en dash, ellipsis, curly quotes)
- Link and image macros
- Superscript and subscript
- Table of contents generation
- Bibliography entries
- Page breaks and thematic breaks
- Attribute references and counters

## Known differences

These produce valid output that differs from Asciidoctor in minor ways:

| Area | Difference |
|------|------------|
| CSS classes | Some wrapping `<div>` classes differ slightly |
| Whitespace | Minor whitespace differences in some block contexts |
| ID generation | Edge cases with special characters in auto-generated IDs |
| Table rendering | Some complex column spec edge cases |

These are tracked and documented in the conformance test suite. None affect
the rendered visual output in a browser.

## Not supported

| Feature | Reason |
|---------|--------|
| Stem/math blocks (MathJax, LaTeX) | Requires external rendering engine |

## Target framework compatibility

| Platform | Supported |
|----------|-----------|
| .NET 10+ | Yes (optimized) |
| .NET 5-9 | Yes (via netstandard2.0) |
| .NET Core 2.0+ | Yes (via netstandard2.0) |
| .NET Framework 4.6.1+ | Yes (via netstandard2.0) |
| Mono | Yes (via netstandard2.0) |
