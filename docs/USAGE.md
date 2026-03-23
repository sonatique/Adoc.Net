# AdocNet Library Usage Guide

AdocNet is a pure managed C# AsciiDoc engine. This guide covers using it
as a library in your .NET applications.

## Installation

### Library (NuGet)

Add the umbrella package (includes parser, HTML, and PDF renderers):

```xml
<PackageReference Include="AdocNet" Version="1.0.0" />
```

Or reference only what you need:

```xml
<PackageReference Include="AdocNet.Parser" Version="1.0.0" />
<PackageReference Include="AdocNet.Converters.Html" Version="1.0.0" />
```

### CLI tools

```bash
dotnet tool install --global AdocNet.Tool         # adocnet (HTML default)
dotnet tool install --global AdocNet.Pdf          # adocnet-pdf
dotnet tool install --global AdocNet.Epub         # adocnet-epub
dotnet tool install --global AdocNet.DocBook      # adocnet-docbook
```

### Target frameworks

AdocNet targets both `netstandard2.0` and `net10.0`. It works on .NET Framework 4.6.1+,
.NET Core 2.0+, and modern .NET. The `net10.0` target includes Span-based optimizations;
`netstandard2.0` uses string-based fallbacks with identical behavior.

## Quick Start

### Zero-config API

The simplest way to use AdocNet — one `using`, one line:

```csharp
using AdocNet;

string html = Adoc.ToHtml("= Hello\n\nThis is *bold* text.");
```

More options without additional `using` directives:

```csharp
// Styled full HTML document
string page = Adoc.ToStyledHtml(source, HtmlTheme.Asciidoctor);

// PDF
byte[] pdf = Adoc.ToPdf(source);

// Write directly to a stream (no intermediate allocation)
using var file = File.Create("output.html");
Adoc.ToHtml(source, file);

// Convert a file (resolves includes automatically)
Adoc.ConvertFile("docs/chapter.adoc", file);

// Parse with error checking
var result = Adoc.Parse(source);
if (result.HasErrors)
    foreach (var d in result.Diagnostics) Console.WriteLine(d);
```

### Full API

For advanced scenarios (custom options, include readers, render options), use the
component API directly:

```csharp
using AdocNet.Parser;
using AdocNet.Converters.Html;

var result = AdocParser.Parse(text, new ParseOptions { SourceFilePath = "chapter.adoc" });
var html = new HtmlRenderer().RenderToString(result.Document,
    new HtmlRenderOptions { Theme = HtmlTheme.Asciidoctor });
```

## Parsing

### Basic parsing

```csharp
var result = AdocParser.Parse(sourceText);
```

Returns a `ParseResult` with:
- `Document` — the root AST node (`DocumentNode`)
- `Diagnostics` — errors and warnings from parsing
- `HasErrors` / `HasWarnings` — convenience checks

### Parsing with options

```csharp
var result = AdocParser.Parse(sourceText, new ParseOptions
{
    // File path — enables include resolution relative to this file
    SourceFilePath = "docs/intro.adoc",

    // Pre-set document attributes
    Attributes = new Dictionary<string, string>
    {
        ["author"] = "Jane Doe",
        ["version"] = "2.0",
    },

    // Attributes the document cannot override
    LockedAttributes = new HashSet<string> { "version" },

    // Custom include resolver (default: reads from filesystem)
    IncludeReader = new MyCustomReader(),
});
```

### ParseOptions reference

| Property | Default | Description |
|----------|---------|-------------|
| `SourceFilePath` | null | File path for includes and diagnostics |
| `BaseDirectory` | from SourceFilePath | Base directory for relative includes |
| `IncludeMaxDepth` | 10 | Maximum include nesting depth |
| `ExpandIncludes` | auto | Explicitly enable/disable include expansion |
| `Attributes` | null | Pre-populated document attributes |
| `LockedAttributes` | null | Attributes that can't be overridden by the document |
| `IncludeReader` | FileIncludeReader | Custom include file resolver |
| `AllowUriRead` | false | Allow `include::https://...` URIs |

### Parsing files with includes

When `SourceFilePath` is set, the parser automatically expands `include::` directives
relative to that file's directory:

```csharp
var path = "docs/book.adoc";
var text = File.ReadAllText(path);
var result = AdocParser.Parse(text, new ParseOptions
{
    SourceFilePath = path,
});
```

Partial includes are supported:

```asciidoc
include::chapter.adoc[lines=5..20]
include::chapter.adoc[tags=setup]
include::chapter.adoc[leveloffset=+1]
```

## Rendering

### Stream API

All renderers implement `IDocumentRenderer`:

```csharp
var renderer = new HtmlRenderer();
using var stream = File.Create("output.html");
renderer.Render(result.Document, stream, RenderOptions.Default);
```

### Convenience methods

```csharp
// Render to string (UTF-8)
string html = renderer.RenderToString(result.Document);
string html = renderer.RenderToString(result.Document, options);

// Render to byte array
byte[] bytes = renderer.RenderToBytes(result.Document);
byte[] bytes = renderer.RenderToBytes(result.Document, options);
```

### HTML

```csharp
using AdocNet.Converters.Html;

// Fragment (no <html> wrapper)
string fragment = new HtmlRenderer().RenderToString(doc);

// Full document with theme
string page = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions
{
    FullDocument = true,
    Theme = HtmlTheme.Asciidoctor,
    Title = "My Page",
    CustomCss = "body { max-width: 800px; margin: auto; }",
    ExtraHead = "<link rel=\"icon\" href=\"/favicon.ico\">",
});
```

Available themes: `HtmlTheme.None`, `HtmlTheme.Default`, `HtmlTheme.Asciidoctor`, `HtmlTheme.Clean`.

### PDF

```csharp
using AdocNet.Converters.Pdf;

byte[] pdf = new PdfRenderer().RenderToBytes(doc);
File.WriteAllBytes("output.pdf", pdf);

// With custom layout
byte[] pdf = new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions
{
    PageWidth = 612f,    // US Letter in points
    PageHeight = 792f,
    MarginLeft = 72f,    // 1 inch margins
    ShowPageNumbers = true,
    HeaderText = "My Document",
    FontPath = "/fonts/Custom-Regular.ttf",
    BoldFontPath = "/fonts/Custom-Bold.ttf",
});

// Presets
byte[] a4 = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.A4);
byte[] letter = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.Letter);
```

### DocBook

```csharp
using AdocNet.Converters.DocBook;

string xml = new DocBookRenderer().RenderToString(doc);
```

### EPUB

```csharp
using AdocNet.Converters.Epub;

byte[] epub = new EpubRenderer().RenderToBytes(doc);
File.WriteAllBytes("output.epub", epub);
```

## Working with the AST

The parsed document is a tree of typed `AstNode` objects:

```csharp
var doc = result.Document;

// Document-level properties
Console.WriteLine(doc.Title);                  // from "= Title" header
Console.WriteLine(doc.Attributes["author"]);   // from ":author: Name"

// Walk the tree
foreach (var child in doc.Children)
{
    switch (child)
    {
        case SectionNode section:
            Console.WriteLine($"Section L{section.Level}: {section.Title}");
            break;
        case ParagraphNode para:
            Console.WriteLine($"Paragraph: {para.Text}");
            foreach (var inline in para.Inlines)
            {
                if (inline is StrongInlineNode strong)
                    Console.WriteLine($"  Bold text found");
            }
            break;
    }
}
```

### Pretty-printing

For debugging or test assertions:

```csharp
using AdocNet.Ast;

string tree = AstPrettyPrinter.Print(result.Document);
Console.WriteLine(tree);
```

Output:

```
Document [1:1-8:1]
  Title="My Document"
  Section [3:1-8:1]
    Level=1 Title="Introduction"
    Paragraph [5:1-5:42]
      Text: This is bold text.
      Strong [5:9-5:15]
        Text: bold
```

### Key node types

| Node | Kind | Key Properties |
|------|------|----------------|
| `DocumentNode` | Document | `Title`, `Attributes`, `Children` |
| `SectionNode` | Section | `Level`, `Title`, `Id`, `Children` |
| `ParagraphNode` | Paragraph | `Text`, `Inlines`, `Title`, `Id` |
| `ListNode` | List | `ListKind`, `Children` |
| `ListItemNode` | ListItem | `Text`, `Inlines`, `Checked`, `Children` |
| `TableNode` | Table | `Columns`, `Header`, `Body`, `Footer` |
| `DelimitedBlockNode` | DelimitedBlock | `BlockKind`, `Content`, `Language` |
| `AdmonitionNode` | Admonition | `AdmonitionType`, `Text`, `Inlines` |

### Source locations

Every node carries source position information (1-based):

```csharp
var section = doc.Children.OfType<SectionNode>().First();
Console.WriteLine($"Starts at line {section.Source.Start.Line}, column {section.Source.Start.Column}");
```

## Handling Diagnostics

```csharp
foreach (var diag in result.Diagnostics)
{
    // diag.Severity — Info, Warning, or Error
    // diag.Message — human-readable description
    // diag.Range — source position (line, column)
    // diag.FilePath — originating file (set for include errors)

    var level = diag.IsError ? "ERROR" : "WARN";
    Console.WriteLine($"[{level}] {diag.Message} at line {diag.Range.Start.Line}");
}

if (result.HasErrors)
{
    // The AST may be incomplete — stop processing
    return;
}
```

## AdocEngine Facade

For pipelines that wire parsing and rendering together:

```csharp
var engine = new AdocEngine(
    renderer: new HtmlRenderer(),
    parser: text => AdocParser.Parse(text).Document
);

// Convert string input
using var output = File.Create("output.html");
engine.Convert(sourceText, output);

// Convert a file (reads from disk, resolves includes)
engine.ConvertFile("input.adoc", output);
```

## Common Workflows

### Batch convert a directory

```csharp
var renderer = new HtmlRenderer();
var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };

foreach (var file in Directory.GetFiles("docs", "*.adoc"))
{
    var text = File.ReadAllText(file);
    var result = AdocParser.Parse(text, new ParseOptions { SourceFilePath = file });
    if (result.HasErrors) continue;

    var outPath = Path.ChangeExtension(file, ".html");
    File.WriteAllText(outPath, renderer.RenderToString(result.Document, options));
}
```

### Embed rendered HTML in a web app

```csharp
// Render a body fragment for embedding inside your page layout
var fragment = new HtmlRenderer().RenderToString(doc);
// Returns: <div class="sect1"><h2>...</h2>...</div>

// Render a complete standalone page
var page = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions
{
    FullDocument = true,
    Theme = HtmlTheme.Clean,
    Title = doc.Title ?? "Untitled",
});
// Returns: <!DOCTYPE html><html>...<body>...</body></html>
```

### Convert with error handling

```csharp
static string SafeConvert(string adocText, string? filePath = null)
{
    var options = filePath is not null
        ? new ParseOptions { SourceFilePath = filePath }
        : ParseOptions.Default;

    var result = AdocParser.Parse(adocText, options);

    if (result.HasErrors)
    {
        var errors = string.Join("\n",
            result.Diagnostics.Where(d => d.IsError).Select(d => d.ToString()));
        throw new InvalidOperationException($"Parse failed:\n{errors}");
    }

    return new HtmlRenderer().RenderToString(result.Document);
}
```

### Render multiple formats from one parse

```csharp
var result = AdocParser.Parse(text, new ParseOptions { SourceFilePath = path });
var doc = result.Document;

File.WriteAllText("output.html", new HtmlRenderer().RenderToString(doc));
File.WriteAllBytes("output.pdf", new PdfRenderer().RenderToBytes(doc));
File.WriteAllText("output.xml", new DocBookRenderer().RenderToString(doc));
File.WriteAllBytes("output.epub", new EpubRenderer().RenderToBytes(doc));
```

## Thread Safety

- `AdocParser.Parse` is stateless and thread-safe — call it concurrently
- Renderer instances can be reused across calls (they are stateless)
- AST nodes are immutable after parsing
- `RenderContext` provides per-render state isolation via `GetOrCreate<T>()`

## See Also

- [CLI Reference](CLI.md) — command-line tools (adocnet, adocnet-pdf, adocnet-epub, adocnet-docbook)
- [Renderers Guide](RENDERERS.md) — renderer options and details
- [Extensions Guide](EXTENSIONS.md) — writing custom renderers and include readers
