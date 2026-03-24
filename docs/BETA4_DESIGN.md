# AdocNet v1.0.0-beta.4 — Design Document

> Created 2026-03-24 during Phase P01.
> This document describes all beta.4 features before any code is written.

---

## 1. Syntax Highlighting Tokenizer

### Placement Decision

The tokenizer lives in **AdocNet.Core** (namespace `AdocNet.Highlighting`).

**Justification**: Both renderers already depend on Core. Core depends only on
AdocNet.Ast, has no renderer-specific types, and the shared-code rules explicitly
state that shared abstractions belong in Core. A separate project would add build
complexity for no benefit — the tokenizer is small (~200-400 lines) and has no
external dependencies.

### Tokenizer Interface

```csharp
namespace AdocNet.Highlighting;

/// <summary>
/// Categories for syntax-highlighted tokens.
/// </summary>
public enum TokenKind
{
    Plain,       // unclassified text
    Keyword,     // language keywords (if, class, return, etc.)
    String,      // string literals ("...", '...')
    Comment,     // single-line and multi-line comments
    Number,      // numeric literals (42, 3.14, 0xFF)
    Type,        // type names (in languages where detectable)
    Punctuation, // operators and punctuation ({, }, =, +, etc.)
    Attribute,   // annotations/attributes ([Obsolete], @Override)
    Preprocessor // preprocessor directives (#include, #if)
}

/// <summary>
/// A single highlighted token: a run of text with a classification.
/// </summary>
public readonly record struct SyntaxToken(TokenKind Kind, string Text);

/// <summary>
/// Tokenizes source code for syntax highlighting.
/// Input: source string + language identifier.
/// Output: list of tokens covering the entire input (no gaps, no overlaps).
/// </summary>
public static class SyntaxTokenizer
{
    /// <summary>
    /// Tokenizes source code in the given language. Returns a flat list of tokens
    /// whose concatenated Text equals the original source.
    /// Unknown languages return a single Plain token containing the full source.
    /// </summary>
    public static List<SyntaxToken> Tokenize(string source, string? language);

    /// <summary>
    /// Returns true if the given language identifier is supported for highlighting.
    /// </summary>
    public static bool IsLanguageSupported(string language);
}
```

### Token Categories (9 total)

| Category | CSS class | PDF color (default) | Examples |
|----------|-----------|---------------------|----------|
| `Plain` | (none) | black | identifiers, whitespace |
| `Keyword` | `hl-kw` | dark blue (#0000C0) | `class`, `if`, `return`, `def` |
| `String` | `hl-s` | dark red (#A31515) | `"hello"`, `'c'` |
| `Comment` | `hl-c` | green (#008000) | `// ...`, `/* ... */`, `# ...` |
| `Number` | `hl-n` | dark cyan (#098658) | `42`, `3.14f`, `0xFF` |
| `Type` | `hl-t` | teal (#267F99) | `int`, `String`, `List<T>` |
| `Punctuation` | `hl-p` | dark gray (#505050) | `{`, `}`, `=`, `=>` |
| `Attribute` | `hl-a` | purple (#8B008B) | `[Test]`, `@Override` |
| `Preprocessor` | `hl-pp` | gray (#808080) | `#include`, `#if` |

### Supported Languages (7 initial)

| Language ID(s) | Language | Key patterns |
|----------------|----------|--------------|
| `csharp`, `cs`, `c#` | C# | Keywords, `//`/`/* */`, strings, `$""`, `@""`, attributes `[...]`, preprocessor `#` |
| `java` | Java | Keywords, `//`/`/* */`, strings, annotations `@` |
| `python`, `py` | Python | Keywords, `#`, strings (`"""`, `'''`, `"`, `'`), decorators `@` |
| `javascript`, `js` | JavaScript | Keywords, `//`/`/* */`, strings, template literals `` ` `` |
| `json` | JSON | Strings, numbers, `true`/`false`/`null`, punctuation |
| `xml`, `html` | XML/HTML | Tags `<...>`, attributes, strings, comments `<!-- -->`, CDATA |
| `sql` | SQL | Keywords (SELECT, FROM, WHERE...), strings, `--`/`/* */` comments |

**Language detection**: reads `DelimitedBlockNode.Language` directly. Aliases
(e.g., `cs` → `csharp`) are mapped via a static dictionary.

### Quality Ceiling

The goal is **80% correct highlighting for common patterns**. Known limitations:
- Nested string interpolation in C# (`$"outer {$"inner"}"`) may not nest correctly.
- Regex literals in JS won't be distinguished from division operators.
- Multi-line strings in Python will use heuristics.
- Generic type parameters (`List<T>`) won't always be classified as types.

This is acceptable. We do NOT attempt IDE-grade precision.

### Implementation Approach

Each language is a set of regex patterns tried in priority order. The tokenizer
walks the source string, at each position tries patterns in order, takes the
first match, emits a token, and advances. Unmatched characters accumulate as `Plain`.

**Extensibility**: Adding a new language requires adding a new entry to a
`Dictionary<string, LanguageDefinition>` where `LanguageDefinition` contains
ordered regex patterns mapped to `TokenKind` values. No interface to implement,
no subclass to create — just data.

```csharp
internal readonly record struct LanguageDefinition(
    IReadOnlyList<(Regex Pattern, TokenKind Kind)> Rules);
```

### HTML Integration

When `:source-highlighter:` is NOT set to `highlight.js`, and the tokenizer
supports the language, the HTML renderer wraps each non-Plain token in a `<span>`:

```html
<pre class="highlight"><code class="language-csharp" data-lang="csharp">
<span class="hl-kw">class</span> <span class="hl-t">Foo</span> { }
</code></pre>
```

The theme CSS includes syntax highlighting color rules for `.hl-kw`, `.hl-s`, etc.

When `:source-highlighter: highlight.js` IS set, the renderer emits plain text
as before (letting client-side JS handle highlighting). This preserves backward
compatibility.

### PDF Integration

The PDF renderer uses the tokenizer to produce colored text segments:

1. Tokenize source content.
2. Map each `TokenKind` → color from a `SyntaxColorScheme` (configurable via options).
3. Build `TextSegment` list with per-token colors.
4. Render via existing `WriteTextSegments()`.

The monospace font is used for all tokens — only the color changes.

---

## 2. Typography Improvements (PDF)

### Hyphenation Approach

**Algorithm**: Liang/Knuth pattern-based hyphenation.

**Justification**: This is the standard approach used by TeX, LibreOffice, and
most professional typesetting systems. It is well-documented, deterministic,
compact (patterns are ~50-100KB per language), and produces high-quality results.
Dictionary-based approaches would require much larger data files and still miss
compound words. The algorithm is straightforward to implement (~100-150 lines).

### Pattern Source and License

Patterns come from the **TeX hyphenation repository** (CTAN `hyph-utf8`).
These patterns are licensed under **LPPL** (LaTeX Project Public License),
which permits redistribution and is compatible with MIT-style licenses.

Patterns are converted to a simple text format at build time and embedded as
C# string constants (similar to HelveticaMetrics) to avoid external data files.

### Supported Natural Languages

**Beta.4**: English only (`en-us`).

English covers the vast majority of technical documentation use cases. Adding
more languages (French, German, Spanish) is trivial — just add their pattern
files — but is deferred to avoid scope creep.

### Pattern Data Size and Embedding Strategy

English hyphenation patterns are approximately **30-40KB** as compressed text.

**Embedding**: patterns are stored as a `const string` in a static class
(`HyphenationPatterns.cs`) inside `AdocNet.Converters.Pdf`. The class is
internal — hyphenation is a PDF-specific concern (HTML relies on browser
hyphenation via CSS `hyphens: auto`).

**Design decision**: Hyphenation lives in the PDF renderer, NOT in Core.
Rationale: HTML has native CSS hyphenation; only the PDF renderer needs
algorithmic hyphenation. Putting it in Core would violate the "no unused
abstractions" principle.

### Hyphenation Interface

```csharp
namespace AdocNet.Converters.Pdf;

internal static class Hyphenator
{
    /// <summary>
    /// Returns possible hyphenation points for a word.
    /// Each int is a character index where a hyphen may be inserted.
    /// Returns empty if the word is too short or cannot be hyphenated.
    /// </summary>
    internal static List<int> GetBreakPoints(string word);
}
```

### Improved Justification with Hyphenation

The existing word-wrapping algorithm (`WrapText` / `WrapSegments`) is extended:

1. When a word doesn't fit on the current line, attempt hyphenation.
2. If a break point yields a fragment that fits, break there with a trailing hyphen.
3. The remainder continues on the next line.
4. Minimum fragment length: 2 characters before hyphen, 3 after (avoids ugly breaks).

Justification (word spacing distribution) remains the same algorithm from beta.3.
Hyphenation just provides more line-break opportunities, reducing the need for
excessive word spacing.

### Paragraph Spacing and Line Height

Beta.3 already has configurable `LineSpacing` multiplier and fixed `ParagraphSpacing`.
Beta.4 adds:

- `ParagraphSpacingBefore` and `ParagraphSpacingAfter` (replacing the single constant).
- These are configurable via `PdfRenderOptions` with defaults matching beta.3 behavior.

No changes to the fundamental line-height model (leading = fontSize × lineSpacing).

---

## 3. HTML Theming System

### Theme Abstraction

The existing enum-based approach is **kept and extended** — no new interface or class hierarchy.

**Justification**: The current system (enum → CSS string) is simple, works, and
produces deterministic output. A complex theme object model would be overengineering
for a library that generates static HTML. Users who need custom themes use the
`CustomCss` property.

### Changes to Existing Themes

The 3 existing themes (`Default`, `Asciidoctor`, `Clean`) are updated to include:

1. **Syntax highlighting CSS rules** — colors for `.hl-kw`, `.hl-s`, `.hl-c`, etc.
2. **Line-number styling** — for future line-number support (reserved CSS classes).

Each theme gets its own syntax color palette that matches its overall aesthetic.

### Built-in Themes (4 total — existing 3 + 1 new)

| Theme | Description | Code style |
|-------|-------------|------------|
| `Default` | Clean sans-serif, muted colors | VS Code–inspired dark-on-light |
| `Asciidoctor` | Serif body, Asciidoctor colors | Asciidoctor-compatible highlighting |
| `Clean` | Minimal Georgia serif | Subdued monochrome highlighting |
| `Github` | **NEW** — GitHub-flavored styling | GitHub syntax colors |

### Custom Theme Support

Users customize via the existing `CustomCss` property:

```csharp
var options = new HtmlRenderOptions
{
    Theme = HtmlTheme.Default,
    CustomCss = ".hl-kw { color: #FF0000; }" // override keyword color
};
```

This is already supported — no new API needed. The `CustomCss` is appended after
the theme CSS, so it naturally overrides theme rules.

### HTML Document Template

No new template system. The existing `FullDocument` / `ExtraHead` / `Title`
properties on `HtmlRenderOptions` provide sufficient customization for header/footer.

### HtmlRenderOptions Changes

One new property:

```csharp
/// <summary>
/// When true and a supported language is specified, source blocks are highlighted
/// server-side using the built-in tokenizer. When false, source blocks are emitted
/// as plain text (for client-side highlighting). Default: true.
/// Ignored when :source-highlighter: highlight.js is set (always defers to client).
/// </summary>
public bool EnableSyntaxHighlighting { get; init; } = true;
```

---

## 4. PDF Styling System

### Approach

PDF styling extends the existing `PdfRenderOptions` with additional properties.
No separate "PdfTheme" class — the options object IS the theme.

**Justification**: `PdfRenderOptions` already contains page geometry, font paths,
typography settings, and visual styling. Adding more properties follows the
established pattern. A separate theme class would just duplicate options with
an extra indirection layer.

### New PdfRenderOptions Properties

```csharp
// ── Syntax highlighting ──────────────────────────────────────────────
/// <summary>Color scheme for syntax highlighting in source blocks. Null = no highlighting.</summary>
public SyntaxColorScheme? SyntaxColors { get; init; } = SyntaxColorScheme.Default;

// ── Heading colors ───────────────────────────────────────────────────
/// <summary>Color for heading text. Null = black (default).</summary>
public PdfColor? HeadingColor { get; init; }

// ── Typography ───────────────────────────────────────────────────────
/// <summary>Enable hyphenation in body text. Default: false.</summary>
public bool EnableHyphenation { get; init; }

/// <summary>Spacing before paragraphs in points. Default: 0.</summary>
public float ParagraphSpacingBefore { get; init; } = 0f;

/// <summary>Spacing after paragraphs in points. Default: 8 (matches beta.3).</summary>
public float ParagraphSpacingAfter { get; init; } = 8f;
```

### SyntaxColorScheme

```csharp
namespace AdocNet.Converters.Pdf;

/// <summary>
/// Maps token categories to PDF colors for syntax highlighting.
/// </summary>
public sealed class SyntaxColorScheme
{
    public PdfColor Keyword { get; init; }
    public PdfColor String { get; init; }
    public PdfColor Comment { get; init; }
    public PdfColor Number { get; init; }
    public PdfColor Type { get; init; }
    public PdfColor Punctuation { get; init; }
    public PdfColor Attribute { get; init; }
    public PdfColor Preprocessor { get; init; }

    public static SyntaxColorScheme Default { get; } = new()
    {
        Keyword      = new(0f, 0f, 0.75f),      // dark blue
        String       = new(0.64f, 0.08f, 0.08f), // dark red
        Comment      = new(0f, 0.5f, 0f),        // green
        Number       = new(0.04f, 0.53f, 0.34f),  // dark cyan
        Type         = new(0.15f, 0.5f, 0.6f),   // teal
        Punctuation  = new(0.31f, 0.31f, 0.31f), // dark gray
        Attribute    = new(0.55f, 0f, 0.55f),     // purple
        Preprocessor = new(0.5f, 0.5f, 0.5f),    // gray
    };
}
```

### PDF Style Presets

Convenience factory methods on `PdfRenderOptions`:

```csharp
public static PdfRenderOptions Compact => new()
{
    FontSize = 10f, LineSpacing = 1.25f,
    ParagraphSpacingAfter = 6f, MarginTop = 54f, MarginBottom = 54f
};

public static PdfRenderOptions Presentation => new()
{
    TitleFontSize = 30f, FontSize = 14f, LineSpacing = 1.5f,
    HeadingColor = new PdfColor(0f, 0f, 0.6f)
};
```

---

## 5. Renderer Alignment Strategy

### Aligned Properties (should be visually consistent)

| Element | Aligned property | How |
|---------|-----------------|-----|
| Headings | Relative sizing hierarchy | Both use same scale ratio |
| Source blocks | Syntax highlighting colors | Same token categories, similar default colors |
| Admonitions | Type labels (NOTE, TIP, etc.) | Same text labels |
| Tables | Column alignment (left/center/right) | Both respect `TableColumnSpec.Alignment` |
| Lists | Bullet/number style | Both use `-` or `1.` prefix |
| Links | Visual distinction | Both color links (blue-ish) |

### Explicitly NOT Aligned

| Feature | Reason |
|---------|--------|
| Font families | PDF uses system/embedded fonts; HTML uses CSS font stacks |
| Page layout | PDF has fixed pages; HTML flows |
| Margins/padding | PDF in points; HTML in CSS units |
| Interactive features | HTML has hover states; PDF is static |
| Admonition styling | HTML uses CSS boxes; PDF uses border lines |
| Table styling | HTML uses CSS borders; PDF draws lines manually |
| Theme names | HTML has named themes; PDF has options properties |

---

## 6. Configuration Model

### Options Hierarchy

```
RenderOptions (base)
  ├── HtmlRenderOptions
  │     ├── Theme (enum)
  │     ├── CustomCss (string)
  │     ├── EnableSyntaxHighlighting (bool)
  │     └── [existing: FullDocument, Title, ExtraHead]
  └── PdfRenderOptions
        ├── SyntaxColors (SyntaxColorScheme)
        ├── EnableHyphenation (bool)
        ├── HeadingColor (PdfColor)
        ├── ParagraphSpacingBefore/After (float)
        └── [existing: all beta.3 options unchanged]
```

There is no global → renderer-specific → per-element hierarchy. Each renderer
has its own flat options object. This is intentional — simplicity over flexibility.

### New Options Summary

| Option | Type | Default | Renderer |
|--------|------|---------|----------|
| `EnableSyntaxHighlighting` | `bool` | `true` | HTML |
| `SyntaxColors` | `SyntaxColorScheme?` | `SyntaxColorScheme.Default` | PDF |
| `HeadingColor` | `PdfColor?` | `null` (black) | PDF |
| `EnableHyphenation` | `bool` | `false` | PDF |
| `ParagraphSpacingBefore` | `float` | `0f` | PDF |
| `ParagraphSpacingAfter` | `float` | `8f` | PDF |

### Backward Compatibility

All new options have defaults that produce output identical to beta.3.
`EnableHyphenation` defaults to `false`. `SyntaxColors` defaults to a scheme,
but source blocks in beta.3 were always plain monospace — with syntax coloring,
they will look *better* but different. If byte-identical PDF output is required,
set `SyntaxColors = null` to disable highlighting.

---

## 7. Testing Strategy

### Syntax Highlighting Tests

**Deterministic token comparison**:

```csharp
[Test]
public void CSharp_class_keyword_is_tokenized()
{
    var tokens = SyntaxTokenizer.Tokenize("class Foo { }", "csharp");
    Assert.That(tokens[0], Is.EqualTo(new SyntaxToken(TokenKind.Keyword, "class")));
    Assert.That(tokens[1], Is.EqualTo(new SyntaxToken(TokenKind.Plain, " ")));
    Assert.That(tokens[2], Is.EqualTo(new SyntaxToken(TokenKind.Plain, "Foo")));
}
```

Test categories:
- Per-language keyword recognition (≥ 1 test per supported language)
- String literal handling (single/double quotes, escapes)
- Comment handling (single-line, multi-line)
- Number literal recognition
- Round-trip: concatenated token text == original source
- Unsupported language returns single Plain token

### Theme Application Tests

**HTML**: verify output contains expected CSS classes:

```csharp
[Test]
public void Source_block_with_highlighting_emits_span_classes()
{
    // Parse [source,csharp] block, render with Default theme
    // Assert output contains <span class="hl-kw">
}
```

**PDF**: verify PDF content stream contains color-change operators when
syntax highlighting is enabled (search for `rg` color operators within
source block regions).

### Cross-Renderer Consistency Tests

- Same source block tokenized identically for both renderers (same token list).
- Both renderers support the same set of languages (query `IsLanguageSupported`).
- Token category count is consistent.

### Hyphenation Tests

- Known English words produce expected break points.
- Short words (< 5 chars) are not hyphenated.
- Hyphenation is deterministic (same input → same break points).

---

## 8. Explicit Non-Goals

The following features are deliberately **deferred to future releases**:

| Feature | Reason for deferral |
|---------|-------------------|
| PDF bookmarks / outlines | Useful but not critical for beta.4 scope |
| Table of contents in PDF | Requires bookmark infrastructure first |
| Source block line numbers | Adds complexity to both renderers; deferred |
| Knuth-Plass optimal line-breaking | Current greedy algorithm is adequate for documents |
| Full CSS engine | Out of scope — themes are predefined CSS strings |
| Multi-column layout | Complex layout feature for future major release |
| Stem/math block rendering | Requires math typesetting engine (MathJax/KaTeX level) |
| Kerning and ligatures | Font-level feature requiring OpenType layout table parsing |
| PDF/A compliance | Archival PDF format requires additional metadata/structure |
| Custom PDF fonts per element | All headings use the same bold font; per-element fonts deferred |
| Syntax highlighting for > 7 languages | Additional languages are easy to add but not beta.4 scope |
| User-defined syntax language definitions | Runtime language definitions deferred |
| CSS `hyphens: auto` in HTML themes | Browser support is inconsistent; leave to users |

---

## Appendix: Phase Execution Order

1. **P02 — Typography (PDF)**: Hyphenation engine, improved paragraph spacing.
2. **Check A — Typography Integrity**: Verify hyphenation correctness.
3. **P03 — Syntax Highlighting**: Tokenizer in Core, integration in both renderers.
4. **P04 — HTML Theming**: Add syntax CSS to existing themes, add Github theme.
5. **P05 — PDF Styling**: SyntaxColorScheme, heading colors, style presets.
6. **Check B — Cross-Renderer Integrity**: Verify both renderers highlight consistently.
7. **P06 — Renderer Alignment**: Ensure visual consistency where specified.
8. **P07 — Configuration**: New options, backward compatibility verification.
9. **P08 — Rendering Tests**: Comprehensive test suite for all new features.
10. **P09 — Documentation**: API docs, getting-started guide, changelog.
