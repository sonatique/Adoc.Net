# Beta.4 Context Discovery — Renderers + Core

> Generated 2026-03-24 during Phase P00 — Context Discovery.
> Read-only exploration of the HTML renderer, PDF renderer (post-beta.3), and AdocNet.Core.

---

## 1. HTML Renderer — File Inventory

Directory: `src/AdocNet.Converters.Html/`

| File | Description |
|------|-------------|
| `HtmlRenderer.cs` | Main renderer (~1800 lines). Extends `DocumentRendererBase`. Dispatches all block/inline AST nodes to HTML output via `StringBuilder`. |
| `HtmlRenderOptions.cs` | Options class: `Theme`, `CustomCss`, `FullDocument`, `Title`, `ExtraHead`. Inherits `RenderOptions`. |
| `HtmlTheme.cs` | Enum with 4 values: `None`, `Default`, `Asciidoctor`, `Clean`. |
| `HtmlThemeCss.cs` | Static class mapping `HtmlTheme` → raw CSS string constants (3 embedded themes, ~270 lines of CSS). |

### HtmlRenderer Architecture

- Extends `DocumentRendererBase` (from Core).
- Single `RenderDocument()` entry point builds a `StringBuilder`, optionally wraps in full HTML document (`<!DOCTYPE html>`, `<head>`, `<body>`).
- Per-render state stored in `HtmlRenderState` via `RenderContext.GetOrCreate<T>()`:
  - `IdTitles` / `TitleIds` — maps for cross-reference resolution.
  - `DocumentAttributes` — for attribute expansion in verbatim blocks.
  - `TableCounter`, `FigureCounter`, `ExampleCounter` — auto-numbering.
- `SectionNumberingContext` tracks `:sectnums:` state.
- `FootnoteState` collects footnotes during rendering, emits `<div id="footnotes">` at end.
- No syntax highlighting — source blocks emit `<pre class="highlight"><code class="language-X">` and rely on external JS (highlight.js) when `:source-highlighter:` attribute is set.

### HtmlTheme Mechanism

The theme system is simple enum → CSS mapping:

1. `HtmlRenderOptions.Theme` selects a built-in theme (or `None`).
2. When theme ≠ `None`, output is wrapped in a full HTML document.
3. `HtmlThemeCss.GetCss(theme)` returns the raw CSS string.
4. CSS is embedded in a `<style>` block in `<head>`.
5. `CustomCss` property allows appending additional CSS after the theme CSS.
6. `ExtraHead` allows injecting arbitrary `<head>` content (e.g., `<link>` tags).

**Current CSS themes are hardcoded string constants** — there is no CSS parsing, no variable system, no programmatic theme model. Each theme is a standalone CSS block covering:
- Body typography (font-family, font-size, line-height, color, max-width)
- Headings (h1–h6 sizing, color, margins)
- Links, code/monospace, pre blocks
- Blockquotes (`.quoteblock`)
- Tables (border-collapse, striping via `.stripes-odd`/`.stripes-even`)
- Admonitions (`.admonitionblock` with type-specific colors: note, tip, warning, caution, important)
- Images (`.imageblock`)
- Sidebar, example, listing blocks
- TOC (`#toc`)
- Footnotes (`#footnotes`)
- Highlights (`mark`, `.highlight`)
- Verse blocks

**No syntax-highlighting CSS exists** — the themes rely on external highlight.js for colorizing source blocks.

---

## 2. PDF Renderer — File Inventory (Post-Beta.3)

Directory: `src/AdocNet.Converters.Pdf/`

| File | Lines | Description |
|------|-------|-------------|
| `PdfRenderer.cs` | ~456 | Main renderer (partial class). `RenderDocument()` entry point, document/section/paragraph/list/image/admonition/description-list/footnotes rendering, inline segment building. |
| `PdfRenderer.Blocks.cs` | ~485 | Partial class: table rendering (auto-column sizing, multi-line cells, header repeat on page break), block image embedding, bibliography entries. |
| `PdfWriter.cs` | ~512 | Low-level PDF 1.4 writer. Object management, page management, font allocation, text operations (plain, justified, segments, wrapped), link annotations, image XObject embedding, text measurement, word wrapping, final PDF assembly (xref table, trailer). |
| `PdfWriter.Rendering.cs` | ~494 | Partial class: verbatim text wrapping, cursor movement, drawing operations (lines, rects, fill/stroke colors), link annotations, image drawing, text measurement, word wrapping with `NoStartChars` punctuation rules, segment wrapping, justified segment writing, `ToBytes()` final assembly, helper methods. |
| `PdfRenderOptions.cs` | ~108 | Options: page geometry (A4/Letter), font paths (regular/bold/italic/mono TTF), typography (fontSize, headingScale, lineSpacing), headers/footers (templates with `{page}`/`{pages}`), images (BaseDirectory), visual styling (LinkColor, CodeBackground, AdmonitionBorderWidth, RepeatTableHeader). |
| `PdfFontEmbedder.cs` | ~193 | TrueType font embedding: subsetting, CIDFont creation, CIDToGIDMap, ToUnicode CMap, glyph ID encoding, code point tracking, zlib compression. |
| `TrueTypeParser.cs` | ~300+ | Minimal TrueType parser: extracts cmap, hmtx, head, OS/2, name tables. Maps Unicode → glyph ID, glyph → advance width. |
| `TrueTypeSubsetter.cs` | ~200+ | Font subsetter: creates minimal TTF containing only used glyphs. Handles composite glyph resolution, table rebuilding. |
| `ImageParser.cs` | ~200+ | JPEG and PNG header parser. Extracts dimensions, components, bits. PNG: decompresses IDAT, separates RGB/alpha for PDF SMask. |
| `HelveticaMetrics.cs` | ~100+ | AFM character widths for Helvetica, Helvetica-Bold, and Courier (standard PDF fonts). |

### PDF Renderer Architecture

- Extends `DocumentRendererBase` (from Core).
- **Pure managed PDF 1.4 writer** — no external PDF library dependencies.
- `PdfWriter` builds PDF objects incrementally: allocates objects, tracks page streams, assembles xref table.
- Font system: 4 standard fonts (Helvetica, Helvetica-Bold, Helvetica-Oblique, Courier) + optional TrueType embedding via `PdfFontEmbedder`.
- Font keys: F1=Regular, F2=Bold, F3=Italic, F4=Mono. Embedded fonts get F5+ keys.
- Text rendering via PDF content stream operators: `BT`, `Tf`, `Td`, `Tj`, `Tw`, `ET`.
- Mixed-style text via `TextSegment` records (font + fontSize + optional linkUri).
- Image embedding: JPEG via DCTDecode, PNG via FlateDecode with optional SMask for alpha.
- Link annotations: stored per-page, emitted as `/Annot /Link` objects.
- Headers/footers: template-based with `{page}` and `{pages}` placeholders, centered at top/bottom margins.
- **No syntax highlighting** — source blocks rendered as plain monospace text with optional language label.
- **No theming/styling system** — colors are hardcoded or come from `PdfRenderOptions` properties.

### Source Block Rendering in PDF

Source blocks (`DelimitedBlockKind.Source`) are handled in `RenderVerbatimBlock()`:
1. Background rectangle drawn using `CodeBackground` color.
2. Language label in italic 8pt at top.
3. Content rendered line-by-line via `WriteWrappedVerbatimText()` in monospace font.
4. Callout list appended after the block.

**No tokenization or syntax coloring occurs** — all source text is rendered in a single monospace font.

---

## 3. AdocNet.Core — File Inventory and Extension Points

Directory: `src/AdocNet.Core/`

| File | Description |
|------|-------------|
| `IDocumentRenderer.cs` | Core interface: `string Format`, `void Render(DocumentNode, Stream, RenderOptions)`. |
| `DocumentRendererBase.cs` | Abstract base with virtual dispatch methods for every AST block/inline type. Both HTML and PDF renderers extend this. |
| `DocumentRendererExtensions.cs` | Convenience: `RenderToString()`, `RenderToBytes()`. |
| `RenderContext.cs` | Per-render state bag. Holds `Document`, `Options`, typed state via `GetOrCreate<T>()`. |
| `RenderOptions.cs` | Base options class. `HtmlRenderOptions` and `PdfRenderOptions` inherit from this. |
| `AdocEngine.cs` | High-level facade: combines parser + renderer. `Convert()` and `ConvertFile()`. |
| `TextUtility.cs` | Internal: `SplitLines()`, `TrimmedEndLength()`. |
| `Diagnostic.cs` | Record: `Severity`, `Message`, `Range`, `FilePath`. |
| `DiagnosticSeverity.cs` | Enum: `Error`, `Warning`, `Info`. |
| `IIncludeReader.cs` | Interface for resolving `include::` targets. |
| `ParseOptions.cs` | Options for the parser (not renderer). |

### Where Shared Code Should Go

`AdocNet.Core` is the natural home for shared abstractions consumed by multiple renderers. Its dependency graph:

```
AdocNet.Core → AdocNet.Ast (only)
```

Both renderers already depend on Core:
```
AdocNet.Converters.Html → AdocNet.Core, AdocNet.Ast, AdocNet.Parser
AdocNet.Converters.Pdf  → AdocNet.Core, AdocNet.Ast
```

**For beta.4, shared code should go in `AdocNet.Core`:**
- **Syntax tokenizer**: a language-agnostic tokenizer that produces `(TokenKind, string)` tuples. Both renderers consume the token list — HTML wraps tokens in `<span>` elements, PDF changes font/color per token.
- **Theme model**: if a shared theme abstraction is needed (e.g., color palettes), it would live in Core as plain data records (no CSS, no PDF-specific types).
- **Critical rule**: shared modules must not depend on consumer-specific types. They consume and produce plain data: strings, token lists, style records.

---

## 4. Source Block AST Representation

Source blocks are represented as `DelimitedBlockNode` with `BlockKind = DelimitedBlockKind.Source`.

### AST Type: `DelimitedBlockNode`

Defined in `src/AdocNet.Ast/DelimitedBlockNode.cs`:

```csharp
public sealed class DelimitedBlockNode : BlockNode
{
    public required DelimitedBlockKind BlockKind { get; init; }
    public string? Content { get; init; }       // Raw source text
    public string? Title { get; init; }          // Optional .Title line
    public string? Language { get; init; }       // e.g. "csharp", "python"
    public string? Attribution { get; init; }    // For quote blocks
    public string? CitationSource { get; init; } // For quote blocks
    public string? Style { get; init; }          // e.g. "abstract"
    public IReadOnlyList<CalloutEntry>? Callouts { get; init; }
}
```

### `DelimitedBlockKind` Enum

```csharp
public enum DelimitedBlockKind
{
    Literal, Listing, Source, Example, Quote, Sidebar, Passthrough, Open, Verse
}
```

### How Source Blocks Reach Renderers

1. **Parser** creates `DelimitedBlockNode` with `BlockKind = Source`, `Language = "csharp"` (etc.), `Content = "..."`.
2. **DocumentRendererBase.RenderBlock()** dispatches to `RenderDelimitedBlock()`.
3. **HTML renderer** (`RenderDelimitedBlock` → case `Source`):
   - Emits `<pre class="highlight"><code class="language-X" data-lang="X">`.
   - If `:source-highlighter: highlight.js` attribute is set, adds `highlightjs`/`hljs` CSS classes.
   - Content is HTML-escaped and rendered as plain text (no server-side tokenization).
   - Callouts emitted via `<b class="conum">(N)</b>` markers.
4. **PDF renderer** (`RenderDelimitedBlock` → `RenderVerbatimBlock`):
   - Draws code background rect.
   - Renders language label in italic.
   - Renders each line in monospace font via `WriteWrappedVerbatimText()`.
   - No tokenization, no coloring.

### Available Attributes on Source Blocks

- `Language` — the source language (from `[source,csharp]` syntax).
- `Content` — raw source text between delimiters.
- `Title` — optional title from `.Title` line.
- `Callouts` — list of `CalloutEntry(Number, Text, LineNumber)` for callout annotations.
- `Style` — block style attribute.
- Inherited from `BlockNode`: `Id`, `Roles`, `Attributes`, `Substitutions`, `Reftext`.

---

## 5. Rendering Test Inventory

### HTML Renderer Tests

| File | Test Count | Coverage |
|------|-----------|----------|
| `HtmlRendererTests.cs` | 78 | Documents, sections, paragraphs, lists, tables, delimited blocks, admonitions, images, videos, audio, description lists, footnotes, cross-references, inline formatting, bibliography, thematic/page breaks, TOC, index. |
| `HtmlThemeTests.cs` | 10 | Fragment vs full document, all 3 themes, custom CSS, ExtraHead, title precedence, Styled convenience. |
| `CrossRendererTests.cs` | 15 | Verifies HTML and PDF renderers produce output for the same AST inputs. |

### PDF Renderer Tests

| File | Test Count | Coverage |
|------|-----------|----------|
| `PdfRendererTests.cs` | 86 | PDF structure (header, EOF, determinism), sections, paragraphs, lists (ordered/unordered/nested), tables (header, alignment, multi-page, column sizing), source blocks, admonitions, images (JPEG/PNG), links, footnotes, headers/footers, page numbers, options (margins, fonts, typography), inline formatting (bold, italic, monospace, mixed). |
| `TrueTypeFontTests.cs` | 10 | TrueType parser, glyph mapping, text measurement, subsetter, CIDToGIDMap, ToUnicode CMap. |

### Summary

- **Total rendering tests: ~199** (78 HTML + 86 PDF + 10 Theme + 15 CrossRenderer + 10 TrueType)
- PDF tests verify structure (valid PDF header/trailer, deterministic output) and content presence (string matching in ASCII-decoded PDF bytes).
- HTML tests verify exact string output.
- No visual regression tests (screenshot comparison).
- No syntax highlighting tests (neither renderer tokenizes source code).

---

## 6. Dependency Graph

```
AdocNet.Ast              (no dependencies — leaf)
    ↑
AdocNet.Core             → AdocNet.Ast
    ↑
AdocNet.Parser           → AdocNet.Core (→ AdocNet.Ast transitively)
    ↑
AdocNet.Converters.Html  → AdocNet.Core, AdocNet.Ast, AdocNet.Parser
AdocNet.Converters.Pdf   → AdocNet.Core, AdocNet.Ast
```

**Key observation**: The HTML renderer depends on `AdocNet.Parser` (for `BlockParser.Parse` usage in verbatim content substitution handling). The PDF renderer does NOT depend on Parser.

**For beta.4 shared abstractions**: New code in `AdocNet.Core` will be automatically available to both renderers without adding new project references.

---

## 7. Current Gaps (Beta.4 Scope)

Based on this discovery, the following capabilities are missing:

1. **Syntax highlighting** — Neither renderer tokenizes source code. HTML relies on client-side highlight.js. PDF renders all code as plain monospace.
2. **HTML theming** — Themes are hardcoded CSS strings. No programmatic theme model, no variable system, no user-defined themes.
3. **PDF styling** — Visual properties (colors, fonts, spacing) come from `PdfRenderOptions` properties. No theme/preset system.
4. **Typography improvements (PDF)** — No hyphenation, no kerning, no ligatures, no optical margin alignment.
5. **Renderer alignment** — HTML and PDF diverge in feature coverage (HTML has more AST node support; PDF has more layout sophistication for the nodes it does support).
6. **Configuration expansion** — Options exist but are limited (e.g., no per-element color overrides, no style presets).
