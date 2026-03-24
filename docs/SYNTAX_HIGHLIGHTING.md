# Syntax Highlighting

AdocNet provides built-in server-side syntax highlighting for source code blocks.
No external JavaScript libraries or runtime dependencies are required.

## Supported Languages

| Language | Identifiers | Key patterns highlighted |
|----------|-------------|------------------------|
| C# | `csharp`, `cs`, `c#` | Keywords, strings, comments, numbers, attributes, preprocessor |
| Java | `java` | Keywords, strings, comments, numbers, annotations |
| JavaScript | `javascript`, `js` | Keywords, strings (including template literals), comments, numbers |
| Python | `python`, `py` | Keywords, strings (including triple-quoted), comments, decorators |
| JSON | `json` | Strings, numbers, true/false/null, punctuation |
| XML/HTML | `xml`, `html` | Tags, attributes, strings, comments |
| SQL | `sql` | Keywords (case-insensitive), strings, comments, types |

## Token Categories

Each token is classified into one of 9 categories:

| Category | HTML CSS class | Description |
|----------|---------------|-------------|
| Plain | *(none)* | Unclassified text, identifiers, whitespace |
| Keyword | `hl-kw` | Language keywords (`class`, `if`, `return`) |
| String | `hl-s` | String literals (`"hello"`, `'c'`) |
| Comment | `hl-c` | Comments (`//`, `/* */`, `#`) |
| Number | `hl-n` | Numeric literals (`42`, `3.14`, `0xFF`) |
| Type | `hl-t` | Type names (`int`, `String`, `List`) |
| Punctuation | `hl-p` | Operators and punctuation (`{`, `}`, `=`) |
| Attribute | `hl-a` | Annotations (`[Test]`, `@Override`) |
| Preprocessor | `hl-pp` | Preprocessor directives (`#if`, `#include`) |

## Configuration

### HTML

Enable server-side highlighting with `EnableSyntaxHighlighting`:

```csharp
var options = new HtmlRenderOptions
{
    Theme = HtmlTheme.Default,
    EnableSyntaxHighlighting = true
};
```

When enabled, source blocks with a supported language emit `<span>` elements
with CSS classes for each token category. The built-in themes include matching
color rules.

When `:source-highlighter: highlight.js` is set in the document, server-side
highlighting is automatically disabled to defer to the client-side library.

### PDF

Enable highlighting by setting a `SyntaxColorScheme`:

```csharp
var options = new PdfRenderOptions
{
    SyntaxColors = SyntaxColorScheme.Default
};
```

The default color scheme maps token categories to colors suitable for
light-background PDF documents. Custom schemes can override any color:

```csharp
var options = new PdfRenderOptions
{
    SyntaxColors = new SyntaxColorScheme
    {
        Keyword = new PdfColor(0.8f, 0f, 0f),
        String = new PdfColor(0f, 0.5f, 0f),
        Comment = new PdfColor(0.5f, 0.5f, 0.5f),
        // ... other categories
    }
};
```

## Quality

The tokenizer targets **80% accuracy for common patterns**. Known limitations:
- Nested string interpolation in C# may not nest correctly
- Regex literals in JavaScript are not distinguished from division
- Multi-line strings in Python use heuristics
- Generic type parameters are not always detected as types

This is by design — the tokenizer is intentionally simple (regex-based) to avoid
external dependencies while providing good-enough highlighting for documentation.

## Programmatic API

The tokenizer is available as a public API in `AdocNet.Highlighting`:

```csharp
using AdocNet.Highlighting;

var tokens = SyntaxTokenizer.Tokenize("class Foo { }", "csharp");
bool supported = SyntaxTokenizer.IsLanguageSupported("python");
string? cssClass = SyntaxTokenizer.GetCssClass(TokenKind.Keyword); // "hl-kw"
```
