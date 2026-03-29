# Adoc.Net

A pure managed C# AsciiDoc library for .NET. No external runtime dependencies.

Adoc.Net parses AsciiDoc into a typed AST and renders it to **HTML5**, **PDF**, **DocBook 5.0**, or **EPUB 3.0**. It targets both **.NET 10** (optimized) and **.NET Standard 2.0** (broad compatibility: .NET Framework 4.6.1+, .NET Core 2.0+, Mono, Unity, Xamarin).

## Installation

```
dotnet add package AdocNet
```

For the CLI tools:

```
dotnet tool install --global AdocNet.Tool         # adocnet (HTML default)
dotnet tool install --global AdocNet.Pdf          # adocnet-pdf
dotnet tool install --global AdocNet.Epub         # adocnet-epub
dotnet tool install --global AdocNet.DocBook      # adocnet-docbook
```

## Quick Start

```csharp
using AdocNet;

// One line — that's it
string html = Adoc.ToHtml("= Hello\n\nThis is *bold* text.");
```

### More control

```csharp
// Styled full HTML document with CSS theme
string page = Adoc.ToStyledHtml(source, HtmlTheme.Asciidoctor);

// PDF output
byte[] pdf = Adoc.ToPdf(source);

// Write directly to a stream (no intermediate string)
using var file = File.Create("output.html");
Adoc.ToHtml(source, file);

// Convert a file with include resolution
Adoc.ConvertFile("docs/chapter.adoc", file);

// Parse with diagnostics
var result = Adoc.Parse(source);
if (result.HasErrors)
    foreach (var d in result.Diagnostics) Console.WriteLine(d);
```

### Full API (when you need options)

```csharp
using AdocNet.Parser;
using AdocNet.Converters.Html;

var result = AdocParser.Parse(source, new ParseOptions
{
    SourceFilePath = "docs/chapter.adoc",  // enables include resolution
    Attributes = new Dictionary<string, string> { ["version"] = "2.0" },
});

var html = new HtmlRenderer().RenderToString(result.Document,
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

## CLI Tools

```bash
# General-purpose (default: HTML)
adocnet input.adoc                            # → input.html
adocnet input.adoc -b pdf                     # → input.pdf
adocnet input.adoc -o -                       # → stdout

# Specialized tools (same flags, different default format)
adocnet-pdf input.adoc                        # → input.pdf
adocnet-epub input.adoc                       # → input.epub
adocnet-docbook input.adoc                    # → input.xml

# Common options (all tools)
adocnet input.adoc -o custom.html             # explicit output file
adocnet input.adoc -a version=2.0             # set document attribute
adocnet docs/ -r -D build/                    # convert directory
adocnet docs/ --watch -v                      # watch and rebuild
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

### Rendering Features

| Feature | HTML | PDF |
|---------|------|-----|
| Built-in themes (Default, Asciidoctor, Clean, Github) | 4 themes | Style presets |
| Syntax highlighting (C#, Java, JS, Python, JSON, XML, SQL) | Server-side `<span>` classes | Per-token color operators |
| Hyphenation (English, Liang/Knuth algorithm) | N/A (browser CSS) | Enabled via option |
| Custom styling | Custom CSS override | Color/spacing properties |
| TrueType font embedding with Unicode | N/A | Full Unicode support |

### Processing Extensions

| Feature | Status |
|---------|--------|
| Document processors (`IDocumentProcessor`) | Supported |
| Block processors (`IBlockProcessor`) | Supported |
| Inline processors (`IInlineProcessor`) | Supported |
| Diagram blocks (PlantUML, Mermaid, Ditaa, Graphviz) | Supported (external tool) |
| Node replacement and removal (`NodeReplacements`) | Supported |
| Warning callback (`OnWarning`) | Supported |
| Dynamic extension loading (`LoadExtension`, `--extensions`) | Supported |
| Extension packaging (`ext install`, `ext list`, `ext remove`) | Supported |

### Not yet supported

- Stem/math blocks (MathJax, LaTeX)

## Architecture

Nine assemblies, each with a single responsibility:

| Assembly | Namespace | Description |
|----------|-----------|-------------|
| AdocNet.Ast | `AdocNet.Ast` | Typed AST node classes |
| AdocNet.Core | `AdocNet` | Diagnostics, options, renderer framework |
| AdocNet.Parser | `AdocNet.Parser` | Block and inline parsing |
| AdocNet.Converters.Html | `AdocNet.Converters.Html` | HTML5 renderer with themes |
| AdocNet.Converters.Pdf | `AdocNet.Converters.Pdf` | Pure managed PDF 1.4 renderer with TrueType font embedding and Unicode support |
| AdocNet.Converters.DocBook | `AdocNet.Converters.DocBook` | DocBook 5.0 renderer |
| AdocNet.Converters.Epub | `AdocNet.Converters.Epub` | EPUB 3.0 renderer |
| AdocNet.Layout | `AdocNet.Layout` | UI-agnostic layout model and AST-to-layout builder |
| AdocNet.Avalonia | `AdocNet.Avalonia` | Avalonia UI renderer (layout tree to controls) |

The data flow for the Avalonia viewer is strictly layered: `AST → Layout → Avalonia`. The Layout library has zero UI dependencies and targets netstandard2.0, making it consumable by any .NET UI framework.

## Documentation

| Guide | Description |
|-------|-------------|
| [USAGE.md](docs/USAGE.md) | Library usage, parsing, rendering workflows |
| [CLI.md](docs/CLI.md) | CLI reference and examples |
| [RENDERERS.md](docs/RENDERERS.md) | Renderer guide (HTML, PDF, DocBook, EPUB) |
| [PDF_RENDERER.md](docs/PDF_RENDERER.md) | PDF renderer: fonts, images, links, tables, configuration |
| [EXTENSIONS.md](docs/EXTENSIONS.md) | Processing extensions, custom renderers, and include readers |
| [DYNAMIC_EXTENSIONS.md](docs/DYNAMIC_EXTENSIONS.md) | Loading extensions from external DLLs at runtime |
| [EXTENSION_PACKAGING.md](docs/EXTENSION_PACKAGING.md) | Extension packaging, installation, and automatic loading |
| [DIAGRAMS.md](docs/DIAGRAMS.md) | Diagram block processing with external tools |
| [COMPATIBILITY.md](docs/COMPATIBILITY.md) | Asciidoctor conformance and known differences |
| [SECURITY.md](docs/SECURITY.md) | Security considerations for untrusted input |

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
