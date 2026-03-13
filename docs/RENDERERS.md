# Renderers

Adoc.Net includes four output renderers, all sharing a common framework.

## Renderer Framework

All renderers implement `IDocumentRenderer`:

```csharp
public interface IDocumentRenderer
{
    string Format { get; }
    void Render(DocumentNode document, Stream output, RenderOptions options);
}
```

Convenience extensions simplify common usage:

```csharp
string html = new HtmlRenderer().RenderToString(document);
byte[] pdf  = new PdfRenderer().RenderToBytes(document);
```

### DocumentRendererBase

All four renderers extend `DocumentRendererBase`, which provides:

- Full type-dispatch across ~30 block types and ~18 inline types
- Per-render `RenderContext` with a type-safe state bag (`GetOrCreate<T>`)
- Thread-safe, reentrant rendering (no static or ThreadLocal state)

### RenderContext

Each render call receives a `RenderContext` that provides access to the document, options, and a per-render state bag:

```csharp
var state = context.GetOrCreate(() => new MyState());
```

This replaces the old `[ThreadStatic]` pattern and enables safe concurrent rendering with a single renderer instance.

## HTML Renderer

**Format:** `html` | **Assembly:** `AdocNet.Converters.Html`

Renders semantic HTML5 with CSS classes matching the Asciidoctor convention.

### Basic usage

```csharp
var renderer = new HtmlRenderer();
string html = renderer.RenderToString(document);
```

### Fragment vs. full document

By default, the HTML renderer produces a **fragment** (no `<!DOCTYPE>`, `<html>`, `<head>`, or `<body>` tags). This is ideal for embedding in existing pages or CMS templates.

For standalone documents, use `HtmlRenderOptions`:

```csharp
var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
string html = renderer.RenderToString(document, options);
```

This wraps the output in a complete HTML document with embedded CSS.

### Themes

Three built-in themes are available:

| Theme | Description |
|---|---|
| `HtmlTheme.Default` | Modern sans-serif with muted colors, 960px max-width |
| `HtmlTheme.Asciidoctor` | Serif body with red-brown headings, matching Asciidoctor's classic look |
| `HtmlTheme.Clean` | Narrow-width Georgia for maximum readability, minimal decoration |

### Custom CSS

Append custom CSS after the theme:

```csharp
var options = new HtmlRenderOptions
{
    Theme = HtmlTheme.Default,
    CustomCss = "body { max-width: 1200px; } h1 { color: navy; }",
};
```

### All options

| Property | Type | Default | Description |
|---|---|---|---|
| `Theme` | `HtmlTheme` | `None` | Built-in theme to embed |
| `FullDocument` | `bool` | `false` | Wrap in `<!DOCTYPE html>..` even without a theme |
| `CustomCss` | `string?` | `null` | Additional CSS appended after theme |
| `Title` | `string?` | `null` | Override `<title>` (defaults to document title) |
| `ExtraHead` | `string?` | `null` | Extra content injected into `<head>` |

## PDF Renderer

**Format:** `pdf` | **Assembly:** `AdocNet.Converters.Pdf`

Pure managed PDF 1.4 writer with no external dependencies.

Uses standard PDF fonts (Helvetica, Courier) by default. Custom TrueType fonts
can be embedded via options.

### Basic usage

```csharp
var renderer = new PdfRenderer();
byte[] pdf = renderer.RenderToBytes(document);
File.WriteAllBytes("output.pdf", pdf);
```

### Options

```csharp
var options = new PdfRenderOptions
{
    PageWidth = 612f,     // Letter width in points
    PageHeight = 792f,    // Letter height
    MarginTop = 72f,      // 1 inch margins
    ShowPageNumbers = true,
    HeaderText = "My Document",
    FooterText = "Page {page}",
    FontPath = "/path/to/regular.ttf",
    BoldFontPath = "/path/to/bold.ttf",
};

renderer.Render(document, stream, options);
```

### All PDF options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PageWidth` | `float` | 595 (A4) | Page width in points (72 pts = 1 inch) |
| `PageHeight` | `float` | 842 (A4) | Page height in points |
| `MarginLeft` | `float` | 72 | Left margin in points |
| `MarginRight` | `float` | 72 | Right margin in points |
| `MarginTop` | `float` | 72 | Top margin in points |
| `MarginBottom` | `float` | 72 | Bottom margin in points |
| `ShowPageNumbers` | `bool` | false | Show page numbers in footer |
| `HeaderText` | `string?` | null | Text to display in page header |
| `FooterText` | `string?` | null | Text to display in page footer |
| `BaseDirectory` | `string?` | null | Base directory for resolving images |
| `FontPath` | `string?` | null | Path to regular TrueType font (.ttf) |
| `BoldFontPath` | `string?` | null | Path to bold TrueType font |
| `ItalicFontPath` | `string?` | null | Path to italic TrueType font |
| `MonoFontPath` | `string?` | null | Path to monospace TrueType font |

Presets: `PdfRenderOptions.Default` (A4), `PdfRenderOptions.A4`, `PdfRenderOptions.Letter`.

## DocBook Renderer

**Format:** `docbook` | **Assembly:** `AdocNet.Converters.DocBook`

Renders DocBook 5.0 XML with the CALS table model and XLink for hyperlinks.

### Basic usage

```csharp
var renderer = new DocBookRenderer();
string xml = renderer.RenderToString(document);
```

### Output characteristics

- UTF-8 encoding without BOM
- Root element: `<article xmlns="http://docbook.org/ns/docbook" version="5.0">`
- Tables: CALS model (`<tgroup>`, `<colspec>`, `<thead>`, `<tbody>`)
- Links: XLink attributes (`xlink:href`)
- Admonitions: native DocBook elements (`<note>`, `<tip>`, `<warning>`, `<caution>`, `<important>`)
- LF-only line endings for cross-platform determinism

## EPUB Renderer

**Format:** `epub` | **Assembly:** `AdocNet.Converters.Epub`

Renders EPUB 3.0 archives (ZIP format) reusing the HTML renderer for content generation.

### Basic usage

```csharp
var renderer = new EpubRenderer();
byte[] epub = renderer.RenderToBytes(document);
File.WriteAllBytes("output.epub", epub);
```

### Archive structure

```
mimetype
META-INF/
  container.xml
OEBPS/
  content.opf      (package document with metadata)
  toc.xhtml         (navigation document)
  content.xhtml     (document body)
  style.css         (embedded stylesheet)
```

### Determinism

EPUB output is deterministic: fixed UUID (`Guid.Empty`), fixed timestamps, and consistent ZIP entry ordering. Two renders of the same document produce byte-identical output.

## AdocEngine Facade

`AdocEngine` wires a parser and renderer together for one-step conversion:

```csharp
var engine = new AdocEngine(
    renderer: new HtmlRenderer(),
    parser: text => AdocParser.Parse(text).Document
);

using var output = File.Create("output.html");
engine.Convert("= Hello\n\nWorld", output);
engine.ConvertFile("input.adoc", output);
```

The `Func<string, DocumentNode>` delegate decouples the parser from the core assembly.

## Thread Safety

All renderers are **thread-safe** and **reentrant**. A single renderer instance can be shared across threads:

```csharp
var renderer = new HtmlRenderer();
Parallel.ForEach(documents, doc =>
{
    string html = renderer.RenderToString(doc);
    // ...
});
```

This is guaranteed by the `RenderContext` pattern — all per-render state lives in the context, not in static or instance fields.

## See Also

- [Usage Guide](USAGE.md) — parsing and rendering API
- [Extensions Guide](EXTENSIONS.md) — building custom renderers
- [CLI Reference](CLI.md) — command-line tool
