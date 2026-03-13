# Adoc.Net

A pure managed C# AsciiDoc library for .NET. No external runtime dependencies.

Adoc.Net parses AsciiDoc into a typed AST and renders it to **HTML5**, **PDF**, **DocBook 5.0**, or **EPUB 3.0**. It targets both **.NET 10** (optimized) and **.NET Standard 2.0** (broad compatibility: .NET Framework 4.6.1+, .NET Core 2.0+, Mono, Unity, Xamarin).

## Installation

```
dotnet add package AdocNet
```

For the CLI tool:

```
dotnet tool install --global AdocNet.Tool
```

## Quick Start

```csharp
using AdocNet.Parser;
using AdocNet.Converters.Html;

var source = """
    = My Document

    == Introduction

    This is *bold* and _italic_ text with a link: https://example.com

    * First item
    * Second item with `inline code`
    """;

var result = AdocParser.Parse(source);
var html = new HtmlRenderer().RenderToString(result.Document);
```

### Styled HTML with theme

```csharp
string styledHtml = new HtmlRenderer().RenderToString(result.Document,
    new HtmlRenderOptions { Theme = HtmlTheme.Asciidoctor });
```

### Render to PDF

```csharp
using AdocNet.Converters.Pdf;

byte[] pdf = new PdfRenderer().RenderToBytes(result.Document);
File.WriteAllBytes("output.pdf", pdf);
```

### Render to DocBook

```csharp
using AdocNet.Converters.DocBook;

string xml = new DocBookRenderer().RenderToString(result.Document);
```

### Render to EPUB

```csharp
using AdocNet.Converters.Epub;

byte[] epub = new EpubRenderer().RenderToBytes(result.Document);
File.WriteAllBytes("output.epub", epub);
```

### Parse with includes

```csharp
var text = File.ReadAllText("book.adoc");
var result = AdocParser.Parse(text, new ParseOptions
{
    SourceFilePath = "book.adoc",
});
```

### Check for errors

```csharp
if (result.HasErrors)
{
    foreach (var diag in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine(diag);
}
```

## CLI Tool

```
adocnet input.adoc                            # render HTML to stdout
adocnet input.adoc -o output.html             # render HTML to file
adocnet input.adoc --styled --theme asciidoctor -o out.html
adocnet input.adoc -f pdf -o out.pdf          # render PDF
adocnet input.adoc -f docbook -o out.xml      # render DocBook
adocnet input.adoc -f epub -o out.epub        # render EPUB
adocnet input.adoc --dump-ast                 # print AST tree
adocnet docs/ --out-dir build/                # convert entire directory
adocnet docs/ --watch                         # auto-rebuild on changes
adocnet preview input.adoc                    # live preview with hot reload
```

See [docs/CLI.md](docs/CLI.md) for the full reference.

## Supported AsciiDoc Features

### Block-level

| Feature | Status |
|---------|--------|
| Document title, author, revision | Supported |
| Section headings (`==` through `======`) | Supported |
| Paragraphs | Supported |
| Unordered, ordered, description, and nested lists | Supported |
| Checklists | Supported |
| Tables (header/footer, column specs, spans, alignment, cell styles) | Supported |
| Source blocks with language and callouts | Supported |
| Listing, literal, example, open, sidebar, verse, quote blocks | Supported |
| Admonitions (NOTE, TIP, WARNING, IMPORTANT, CAUTION) | Supported |
| Include directives (files, partial includes, leveloffset) | Supported |
| Conditional directives (ifdef, ifndef, ifeval) | Supported |
| Document attributes (`:name: value`) | Supported |
| Table of contents (`:toc:`) | Supported |
| Block images, video, audio macros | Supported |
| Anchors, cross-references, inter-document xrefs | Supported |
| Footnotes with back-references | Supported |
| Bibliography sections | Supported |
| Page breaks, horizontal rules | Supported |

### Inline

| Feature | Status |
|---------|--------|
| Bold, italic, monospace, highlight | Supported |
| Nested formatting (`*_bold italic_*`) | Supported |
| Bare URLs, link macros, email links | Supported |
| Image macros | Supported |
| Attribute references (`{name}`) | Supported |
| Passthrough (`+text+`, `pass:[text]`) | Supported |
| Cross-references (`<<id>>`) | Supported |
| Footnotes | Supported |
| Superscript (`^text^`), subscript (`~text~`) | Supported |
| Smart punctuation (em/en dash, ellipsis, curly quotes) | Supported |
| Inline macros (`kbd:[]`, `btn:[]`, `menu:[]`) | Supported |

### Not yet supported

- Stem/math blocks (MathJax, LaTeX)

## Architecture

Seven assemblies, each with a single responsibility:

| Assembly | Namespace | Description |
|----------|-----------|-------------|
| AdocNet.Ast | `AdocNet.Ast` | Typed AST node classes |
| AdocNet.Core | `AdocNet` | Diagnostics, options, renderer framework |
| AdocNet.Parser | `AdocNet.Parser` | Block and inline parsing |
| AdocNet.Converters.Html | `AdocNet.Converters.Html` | HTML5 renderer with themes |
| AdocNet.Converters.Pdf | `AdocNet.Converters.Pdf` | Pure managed PDF 1.4 renderer |
| AdocNet.Converters.DocBook | `AdocNet.Converters.DocBook` | DocBook 5.0 renderer |
| AdocNet.Converters.Epub | `AdocNet.Converters.Epub` | EPUB 3.0 renderer |

## Documentation

| Guide | Description |
|-------|-------------|
| [USAGE.md](docs/USAGE.md) | Library usage, parsing, rendering workflows |
| [CLI.md](docs/CLI.md) | CLI reference and examples |
| [RENDERERS.md](docs/RENDERERS.md) | Renderer guide (HTML, PDF, DocBook, EPUB) |
| [EXTENSIONS.md](docs/EXTENSIONS.md) | Building custom renderers and include readers |

## Building

```
dotnet build
dotnet test
```

## Target Frameworks

All core libraries target `netstandard2.0` and `net10.0`. The CLI and LSP server target `net10.0` only.

| Consumer | Resolved TFM |
|----------|-------------|
| .NET Framework 4.6.1+ | netstandard2.0 |
| .NET Core 2.0+ | netstandard2.0 |
| .NET 5-9 | netstandard2.0 |
| .NET 10+ | net10.0 (optimized) |

## License

MIT
