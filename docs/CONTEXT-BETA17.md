# Beta.17 Context — Converters, Templates, and New Formats

## 1. Existing Converter Project Structure

### Project Layout
Each converter lives in `src/AdocNet.Converters.<Format>/`:
```
src/AdocNet.Converters.DocBook/
    AdocNet.Converters.DocBook.csproj
    DocBookRenderer.cs
```

### csproj Pattern
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework />
    <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <RootNamespace>AdocNet.Converters.DocBook</RootNamespace>
    <Description>DocBook 5.0 renderer for the Adoc.Net library.</Description>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdocNet.Core\AdocNet.Core.csproj" />
    <ProjectReference Include="..\AdocNet.Ast\AdocNet.Ast.csproj" />
    <ProjectReference Include="..\AdocNet.Parser\AdocNet.Parser.csproj" />
  </ItemGroup>
</Project>
```

### Renderer Class Pattern
Every renderer:
1. Extends `DocumentRendererBase` (from AdocNet.Core)
2. Overrides `string Format => "formatname";`
3. Overrides `void RenderDocument(RenderContext context, Stream output)`
4. Uses `RenderBlock(...)` / `RenderInline(...)` dispatch via `switch` on node type
5. DocBookRenderer: uses `XmlWriter` for output
6. HtmlRenderer: uses `StringBuilder` for output, writes bytes at end

### DocumentRendererBase (AdocNet.Core)
```csharp
public abstract class DocumentRendererBase : IDocumentRenderer
{
    public abstract string Format { get; }
    public void Render(DocumentNode document, Stream output, RenderOptions options)
    {
        var context = new RenderContext(document, options);
        RenderDocument(context, output);
    }
    protected abstract void RenderDocument(RenderContext context, Stream output);
    // Also provides: RenderBlock(BlockNode, RenderContext) and
    //                RenderInline(InlineNode, RenderContext) dispatch methods
}
```

### IDocumentRenderer Interface
```csharp
public interface IDocumentRenderer
{
    string Format { get; }
    void Render(DocumentNode document, Stream output, RenderOptions options);
}
```

### Solution Registration
Projects are added to `AdocNet.slnx` under the appropriate folder:
- Converters: `/src/` folder
- CLI tools: `/tools/` folder
- Tests: `/tests/` folder

Current solution structure:
```xml
<Folder Name="/src/">
  <Project Path="src/AdocNet.Converters.Html/..." />
  <Project Path="src/AdocNet.Converters.DocBook/..." />
  <Project Path="src/AdocNet.Converters.Pdf/..." />
  <Project Path="src/AdocNet.Converters.Epub/..." />
  <!-- New: AdocNet.Converters.Man, AdocNet.Converters.Revealjs -->
</Folder>
<Folder Name="/tools/">
  <Project Path="src/AdocNet.Cli/..." />
  <Project Path="src/AdocNet.Cli.Pdf/..." />
  <Project Path="src/AdocNet.Cli.Epub/..." />
  <Project Path="src/AdocNet.Cli.DocBook/..." />
  <!-- New: AdocNet.Cli.Man, AdocNet.Cli.Revealjs (optional) -->
</Folder>
```

## 2. CLI Dispatch Pattern

### Specialized CLI Tools
Each specialized tool (e.g., `adocnet-pdf`) is a one-liner `Program.cs`:
```csharp
// src/AdocNet.Cli.Pdf/Program.cs
return AdocNet.Cli.Program.Run(args, AdocNet.Cli.OutputFormat.Pdf, "adocnet-pdf");
```

csproj for specialized tools:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>adocnet-man</AssemblyName>
    <RootNamespace>AdocNet.Cli.Man</RootNamespace>
    <PackageId>AdocNet.Man</PackageId>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>adocnet-man</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdocNet.Cli\AdocNet.Cli.csproj" />
  </ItemGroup>
</Project>
```

### Format Selection in ConvertCommand
`ConvertCommand.RenderOutput()` (line ~142) creates the renderer:
```csharp
IDocumentRenderer renderer = run.Format switch
{
    OutputFormat.Html    => new HtmlRenderer(),
    OutputFormat.Pdf     => new PdfRenderer(),
    OutputFormat.DocBook => new DocBookRenderer(),
    OutputFormat.Epub    => new EpubRenderer(),
    _ => new HtmlRenderer(),
};
```

### OutputFormat Enum
```csharp
public enum OutputFormat { Html, Pdf, DocBook, Epub }
```
Beta.17 adds: `Man`, `Revealjs`.

### CLI Argument Parsing
`Program.ParseArguments()` handles `-b/--backend` flag:
```csharp
format = formatStr switch
{
    "html" or "html5" => OutputFormat.Html,
    "pdf"            => OutputFormat.Pdf,
    "docbook" or "docbook5" or "xml" => OutputFormat.DocBook,
    "epub"           => OutputFormat.Epub,
    _ => OutputFormat.Html,
};
```
Beta.17 adds: `"man"` and `"revealjs"` cases.

### Format Extension Mapping
```csharp
private static string FormatExtension(OutputFormat format) => format switch
{
    OutputFormat.Html    => ".html",
    OutputFormat.Pdf     => ".pdf",
    OutputFormat.DocBook => ".xml",
    OutputFormat.Epub    => ".epub",
    _ => ".html",
};
```
Beta.17 adds: `Man => ".1"` (or `.man`), `Revealjs => ".html"`.

## 3. Template Hook Points in HtmlRenderer

### Where Templates Would Intercept
The `HtmlRenderer.RenderBlock()` method (line ~397) dispatches via switch:
```csharp
private void RenderBlock(StringBuilder sb, AstNode node, bool useIconFont,
    FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
{
    switch (node)
    {
        case SectionNode section: RenderSection(...); break;
        case ParagraphNode paragraph: RenderParagraph(...); break;
        // ...17 more cases
    }
}
```

**Template hook point**: Before the switch, check if any registered `INodeTemplate`
matches via `CanRender(node)`. If yes, use its `Render()` output. If no template
matches, fall through to the built-in switch.

### INodeTemplate Interface (to be added to AdocNet.Core)
```csharp
public interface INodeTemplate
{
    bool CanRender(AstNode node);
    string Render(AstNode node, RenderContext context);
}
```

### HtmlRenderOptions Extension
```csharp
// Add to HtmlRenderOptions:
public IReadOnlyList<INodeTemplate>? Templates { get; init; }
```

### Template Resolution Logic
For each node being rendered:
1. If `Templates` is non-null, iterate templates
2. Call `CanRender(node)` on each — first match wins
3. If match: append `template.Render(node, context)` to StringBuilder, skip built-in
4. If no match: fall through to built-in rendering

Templates match on any node property (not just Kind), enabling:
- "All paragraphs with role 'lead'" → custom rendering
- "Sections at level 2 only" → custom rendering
- "Code blocks with language 'mermaid'" → diagram rendering

## 4. Man Page Roff Format Basics

### Core Structure
```roff
.TH COMMAND "1" "April 2026" "Source 1.0" "Manual Title"
.SH NAME
command \- short description
.SH SYNOPSIS
.B command
[\fIoptions\fR] \fIfile\fR ...
.SH DESCRIPTION
Main description text.
.SH OPTIONS
.TP
\fB\-v\fR, \fB\-\-verbose\fR
Enable verbose output.
```

### Key Directives
| Directive | Purpose |
|-----------|---------|
| `.TH name section date source manual` | Title header — defines the man page identity |
| `.SH HEADING` | Section heading (NAME, SYNOPSIS, DESCRIPTION, etc.) |
| `.SS subheading` | Subsection heading |
| `.TP` | Tagged paragraph — next line is the tag, rest is indented body |
| `.IP "bullet" indent` | Indented paragraph (lists) |
| `.PP` | Plain paragraph break |
| `.nf` / `.fi` | No-fill / fill — toggle literal mode (for code blocks) |
| `.RS` / `.RE` | Relative indent start/end (nesting) |
| `.BR word word` | Alternating bold/roman |
| `.BI word word` | Alternating bold/italic |

### Font Escapes
| Escape | Meaning |
|--------|---------|
| `\fB` | Switch to bold |
| `\fI` | Switch to italic |
| `\fR` | Switch to roman (normal) |
| `\fP` | Previous font |
| `\-` | Minus sign (literal hyphen) |
| `\\` | Literal backslash |

### AsciiDoc-to-Man Mapping
| AsciiDoc | Man roff |
|----------|----------|
| Document title | `.TH` directive |
| `= Section` (level 1) | `.SH SECTION` |
| `== Subsection` (level 2) | `.SS Subsection` |
| Paragraph | `.PP` + text |
| `*bold*` | `\fBbold\fR` |
| `_italic_` | `\fIitalic\fR` |
| `` `mono` `` | `\fBmono\fR` (bold by convention) |
| Source block | `.nf` / `.fi` |
| Description list | `.TP` entries |
| Unordered list | `.IP "\\(bu" 2` entries |
| `:doctype: manpage` | Triggers man page structure |
| `:manmanual:` | Manual title in `.TH` |
| `:mansource:` | Source field in `.TH` |

### Man Section Numbers
| Section | Content |
|---------|---------|
| 1 | User commands |
| 2 | System calls |
| 3 | Library functions |
| 5 | File formats |
| 7 | Miscellaneous |
| 8 | System admin commands |

## 5. Reveal.js Slide Structure

### Basic HTML Structure
```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Presentation Title</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.css">
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/theme/black.css">
</head>
<body>
    <div class="reveal">
        <div class="slides">
            <section><!-- Horizontal slide 1 --></section>
            <section>
                <section><!-- Vertical slide 2a --></section>
                <section><!-- Vertical slide 2b --></section>
            </section>
        </div>
    </div>
    <script src="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.js"></script>
    <script>Reveal.initialize();</script>
</body>
</html>
```

### Slide Mapping from AsciiDoc
| AsciiDoc Element | Reveal.js Output |
|-----------------|------------------|
| Level 1 section (`== Title`) | `<section>` — horizontal slide |
| Level 2 section (`=== Title`) | Nested `<section>` — vertical slide |
| Paragraph | `<p>` inside current `<section>` |
| Unordered list | `<ul><li>` inside slide |
| `[.notes]` block | `<aside class="notes">` (speaker notes) |
| Image | `<img>` centered in slide |
| Source block | `<pre><code>` in slide |

### Reveal.js Configuration Attributes
| AsciiDoc Attribute | Reveal.js Config |
|-------------------|-----------------|
| `:revealjs_theme:` | Theme CSS file (black, white, league, beige, sky, night, serif, simple, solarized, blood, moon) |
| `:revealjs_transition:` | Slide transition: none, fade, slide, convex, concave, zoom |
| `:revealjs_slideNumber:` | Show slide numbers: true/false/c/h.v |
| `:revealjs_controls:` | Show navigation arrows: true/false |
| `:revealjs_progress:` | Show progress bar: true/false |
| `:revealjs_center:` | Center slide content: true/false |
| `:revealjs_width:` | Presentation width (default 960) |
| `:revealjs_height:` | Presentation height (default 700) |

### CDN URLs (no local installation needed)
```
https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.css
https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/theme/{theme}.css
https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.js
```

### Speaker Notes
```html
<section>
    <h2>Slide Title</h2>
    <p>Slide content</p>
    <aside class="notes">
        Speaker notes go here — visible in speaker view (press S).
    </aside>
</section>
```

## 6. Key Integration Points for Beta.17

### New OutputFormat Enum Values
Add `Man` and `Revealjs` to `OutputFormat` in `Program.cs`.

### ConvertCommand Changes
Add cases to:
1. `RenderOutput()` — renderer creation switch
2. `FormatExtension()` — extension mapping
3. `ParseArguments()` — `-b man` and `-b revealjs` parsing
4. Help text — document new formats

### AdocNet.Cli References
`AdocNet.Cli.csproj` must add `<ProjectReference>` to new converter projects.

### INodeTemplate Location
New interface in `src/AdocNet.Core/` (e.g., `INodeTemplate.cs`).
Core already has zero external deps — this is just an interface, no new deps needed.

### Existing AST Nodes Used
Man page converter needs: `DocumentNode`, `SectionNode`, `ParagraphNode`,
`ListNode`, `ListItemNode`, `DescriptionListNode`, `DescriptionItemNode`,
`DelimitedBlockNode`, `AdmonitionNode`, `TableNode`, `BlockImageNode`,
`InlineMacroNode`, all inline types (TextInlineNode, StrongInlineNode, etc.).

Reveal.js converter needs: same plus `StemBlockNode`, `StemInlineNode` (from beta.16).

### Document Attributes for Man Pages
- `:doctype: manpage` — triggers man page structure detection
- `:manmanual:` — manual title (right side of header)
- `:mansource:` — source/version (left side of footer)
- `:man-linkstyle:` — link rendering style

### Document Attributes for Reveal.js
- `:revealjs_theme: black` — theme selection
- `:revealjs_transition: slide` — transition type
- `:revealjs_slideNumber: true` — show slide numbers
- Plus many more `:revealjs_*:` attributes mapped to Reveal.initialize() config
