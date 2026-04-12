# Beta.17 Design — Converters, Templates, and New Formats

## 1. Man Page Converter

### Project Structure
```
src/AdocNet.Converters.Man/
    AdocNet.Converters.Man.csproj
    ManRenderer.cs
    RoffWriter.cs          — low-level roff text builder (escaping, directives)
```

### ManRenderer Class
```csharp
public sealed class ManRenderer : DocumentRendererBase
{
    public override string Format => "man";
    protected override void RenderDocument(RenderContext context, Stream output) { ... }
}
```

Uses a `RoffWriter` helper (similar to how DocBook uses `XmlWriter`) to handle:
- Directive emission (`.TH`, `.SH`, `.PP`, etc.)
- Font escape sequences (`\fB`, `\fI`, `\fR`)
- Special character escaping (`\-` for hyphens, `\\` for backslashes)
- No-fill mode toggling (`.nf`/`.fi`)

### Roff Mapping Table

| AST Node | Roff Output | Notes |
|----------|-------------|-------|
| `DocumentNode` | `.TH NAME "section" "date" "source" "manual"` | Uses `:manmanual:`, `:mansource:` attributes |
| `SectionNode` level 1 | `.SH TITLE` | Uppercased title (roff convention) |
| `SectionNode` level 2 | `.SS Title` | Subsection, not uppercased |
| `SectionNode` level 3+ | `.PP\n\fBTitle\fR` | Bold paragraph (no deeper nesting in roff) |
| `ParagraphNode` | `.PP\ntext` | |
| `ListNode` (unordered) | `.IP "\\(bu" 2\nitem text` | Bullet character + 2-char indent |
| `ListNode` (ordered) | `.IP "N." 3\nitem text` | Sequential number |
| `ListItemNode` | Content after `.IP` or `.TP` | |
| `DescriptionListNode` | Series of `.TP` entries | |
| `DescriptionItemNode` | `.TP\n\fBterm\fR\ndescription` | Term in bold, description indented |
| `DelimitedBlockNode` (Source/Listing) | `.nf\ncontent\n.fi` | No-fill mode preserves whitespace |
| `DelimitedBlockNode` (Literal) | `.nf\ncontent\n.fi` | Same as source |
| `DelimitedBlockNode` (Quote) | `.RS\n.PP\ncontent\n.RE` | Indented block |
| `DelimitedBlockNode` (Example) | `.RS\n(children)\n.RE` | |
| `AdmonitionNode` | `.PP\n\fBTYPE:\fR text` | e.g., `\fBNOTE:\fR` prefix |
| `TableNode` | Custom `.TS`/`.TE` or `.nf` table | tbl preprocessor or manual formatting |
| `BlockImageNode` | `.PP\n[Image: alt (path)]` | Images not renderable in man pages |
| `ThematicBreakNode` | (skip) | No roff equivalent |
| `PageBreakNode` | `.bp` | Break page |
| `TocNode` | (skip) | Man pages don't have TOC |
| `StemBlockNode` | `.nf\nformula\n.fi` | Render verbatim (no MathJax in man) |

### Inline Mapping

| Inline Node | Roff Output |
|-------------|-------------|
| `TextInlineNode` | Escaped text |
| `StrongInlineNode` | `\fBtext\fR` |
| `EmphasisInlineNode` | `\fItext\fR` |
| `MonospaceInlineNode` | `\fBtext\fR` (bold by convention) |
| `LinkInlineNode` | `\fIurl\fR` |
| `InlineLinkMacroNode` | `\fIlabel\fR (\fIurl\fR)` or `\fIurl\fR` |
| `CrossReferenceInlineNode` | `\fIlabel\fR` or `\fItarget\fR` |
| `SuperscriptInlineNode` | `^content` (no native superscript in roff) |
| `SubscriptInlineNode` | `_content` |
| `PassthroughInlineNode` | Content verbatim |
| `FootnoteInlineNode` | `[N]` reference + footnotes section at end |
| `InlineMacroNode` (kbd) | `\fBkey\fR` |
| `StemInlineNode` | Content verbatim |

### .TH Header Format
```
.TH "NAME" "SECTION" "DATE" "SOURCE" "MANUAL"
```
- **NAME**: Extracted from document title (first part before ` - ` if Asciidoctor manpage doctype)
- **SECTION**: From document title (e.g., `COMMAND(1)` extracts `1`), default `1`
- **DATE**: From `:revdate:` attribute, or current date formatted as `"Month YYYY"`
- **SOURCE**: From `:mansource:` attribute, default empty
- **MANUAL**: From `:manmanual:` attribute, default empty

### Man Page Structure Detection
When `:doctype: manpage` is set:
1. Parse document title as `NAME(section)` → extract name and section number
2. First section must be NAME → `.SH NAME\nname \- description`
3. Remaining sections rendered normally

When `:doctype:` is not `manpage`:
- Render as generic man page with document title as `.TH` name
- All sections rendered normally without NAME/SYNOPSIS structure requirement

### CLI Integration
- `OutputFormat.Man` added to enum
- `-b man` / `--backend man` parsing
- `FormatExtension`: `Man => ".1"` (section 1 man page by convention)
- New specialized tool: `src/AdocNet.Cli.Man/` with `adocnet-man` command
- Help text updated with `man` format option

### Special Character Escaping
The `RoffWriter` must escape:
- `\` → `\\` (backslash)
- `-` → `\-` (minus/hyphen in arguments)
- `.` at start of line → `\&.` (prevent directive interpretation)
- `'` at start of line → `\&'` (prevent request interpretation)

## 2. Converter Templates

### INodeTemplate Interface
Located in `src/AdocNet.Core/INodeTemplate.cs`:
```csharp
/// <summary>
/// A custom rendering template for specific AST nodes. Templates are checked
/// before the built-in renderer — the first template whose <see cref="CanRender"/>
/// returns true produces the output for that node.
/// </summary>
public interface INodeTemplate
{
    /// <summary>
    /// Returns true if this template can render the given node.
    /// The predicate can match on any node property: Kind, type, roles, level, etc.
    /// </summary>
    bool CanRender(AstNode node);

    /// <summary>
    /// Renders the node to an HTML string. Called only when <see cref="CanRender"/>
    /// returned true.
    /// </summary>
    string Render(AstNode node, RenderContext context);
}
```

### Registration on HtmlRenderOptions
```csharp
// Added property to HtmlRenderOptions:
public IReadOnlyList<INodeTemplate>? Templates { get; init; }
```

### Rendering Flow

The template check is inserted at the top of the existing `RenderBlock` and
`RenderInline` methods in `HtmlRenderer`:

```csharp
// In RenderBlock (before switch):
private void RenderBlock(StringBuilder sb, AstNode node, ...)
{
    // Template hook — check before built-in rendering
    if (TryRenderTemplate(sb, node, state))
        return;

    switch (node)
    {
        case SectionNode section: ...
        // existing cases unchanged
    }
}

// In RenderInline (before switch):
private void RenderInline(StringBuilder sb, InlineNode node, ...)
{
    if (TryRenderTemplate(sb, node, state))
        return;

    switch (node)
    {
        case TextInlineNode n: ...
        // existing cases unchanged
    }
}
```

The `TryRenderTemplate` helper:
```csharp
private bool TryRenderTemplate(StringBuilder sb, AstNode node, HtmlRenderState state)
{
    var templates = (context.Options as HtmlRenderOptions)?.Templates;
    if (templates is null) return false;

    foreach (var template in templates)
    {
        if (template.CanRender(node))
        {
            sb.Append(template.Render(node, context));
            return true;
        }
    }
    return false;
}
```

### Key Design Decisions

1. **First match wins**: Templates are checked in list order; the first `CanRender()==true`
   produces the output. No fallthrough, no chaining.

2. **Predicate-based matching**: `CanRender(AstNode)` can match on any property.
   This is more flexible than a `Dictionary<AstNodeKind, Func<...>>` because it allows:
   - Paragraphs with a specific role
   - Sections at a specific level
   - Code blocks with a specific language
   - Any combination of properties

3. **HTML only**: Templates only apply to HtmlRenderer. Other renderers (PDF, DocBook,
   Man, Reveal.js) don't support templates — their output formats don't lend themselves
   to user-customizable string substitution in the same way.

4. **No default templates registered**: `Templates` defaults to `null` (no overhead).
   Zero templates = identical output to beta.16.

5. **Templates get full RenderContext**: They can access document attributes, options,
   and per-render state via `context.GetOrCreate<T>()`.

### Example Templates
```csharp
// Custom lead paragraph rendering
class LeadParagraphTemplate : INodeTemplate
{
    public bool CanRender(AstNode node)
        => node is ParagraphNode p && p.Roles.Contains("lead");

    public string Render(AstNode node, RenderContext context)
    {
        var p = (ParagraphNode)node;
        return $"<p class=\"lead\">{p.Text}</p>\n";
    }
}

// Custom source block for a specific language
class MermaidBlockTemplate : INodeTemplate
{
    public bool CanRender(AstNode node)
        => node is DelimitedBlockNode b
           && b.BlockKind == DelimitedBlockKind.Source
           && b.Language == "mermaid";

    public string Render(AstNode node, RenderContext context)
    {
        var b = (DelimitedBlockNode)node;
        return $"<div class=\"mermaid\">{b.Content}</div>\n";
    }
}

// Usage:
var options = new HtmlRenderOptions
{
    Templates = [new LeadParagraphTemplate(), new MermaidBlockTemplate()]
};
```

## 3. Reveal.js Slides Converter

### Project Structure
```
src/AdocNet.Converters.Revealjs/
    AdocNet.Converters.Revealjs.csproj
    RevealjsRenderer.cs
    RevealjsRenderOptions.cs   — CDN URL, theme, transition config
```

### RevealjsRenderer Class
```csharp
public sealed class RevealjsRenderer : DocumentRendererBase
{
    public override string Format => "revealjs";
    protected override void RenderDocument(RenderContext context, Stream output) { ... }
}
```

### Section-to-Slide Mapping

| AST Section Level | Reveal.js Mapping |
|-------------------|-------------------|
| Document title (h1) | Title slide: `<section><h1>title</h1></section>` |
| Level 1 (`== Title`) | Horizontal slide: `<section><h2>title</h2>content</section>` |
| Level 2 (`=== Title`) | Vertical slide: nested `<section><h3>title</h3>content</section>` |
| Level 3+ | Sub-heading within current slide: `<h4>title</h4>` (no new slide) |

### Slide Wrapping Algorithm
```
foreach top-level child in document:
    if SectionNode level 1:
        if section has level-2 subsections:
            emit <section>   // vertical slide group
            emit <section><h2>title</h2>level-1 content</section>
            foreach level-2 child:
                emit <section><h3>title</h3>level-2 content</section>
            emit </section>
        else:
            emit <section><h2>title</h2>all content</section>
    else:
        // Non-section content before first section → title slide
        accumulate into title slide
```

### Speaker Notes
The `[.notes]` role on open/sidebar blocks becomes `<aside class="notes">`:
```csharp
if (node is DelimitedBlockNode block && block.Roles.Contains("notes"))
{
    sb.Append("<aside class=\"notes\">\n");
    RenderChildren(sb, block, ...);
    sb.Append("</aside>\n");
}
```

### Reveal.js Attribute Support

| Document Attribute | Default | Maps To |
|-------------------|---------|---------|
| `:revealjs_theme:` | `black` | Theme CSS link |
| `:revealjs_transition:` | `slide` | `Reveal.initialize({ transition: '...' })` |
| `:revealjs_slideNumber:` | `false` | `Reveal.initialize({ slideNumber: ... })` |
| `:revealjs_controls:` | `true` | `Reveal.initialize({ controls: ... })` |
| `:revealjs_progress:` | `true` | `Reveal.initialize({ progress: ... })` |
| `:revealjs_center:` | `true` | `Reveal.initialize({ center: ... })` |
| `:revealjs_width:` | `960` | `Reveal.initialize({ width: ... })` |
| `:revealjs_height:` | `700` | `Reveal.initialize({ height: ... })` |
| `:revealjs_customtheme:` | (none) | Custom theme CSS URL instead of CDN |

### RevealjsRenderOptions
```csharp
public sealed class RevealjsRenderOptions : RenderOptions
{
    public static new RevealjsRenderOptions Default { get; } = new();

    /// <summary>Reveal.js CDN base URL. Default uses jsDelivr CDN.</summary>
    public string CdnBase { get; init; } =
        "https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist";
}
```

### HTML Output Structure
```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{document title}</title>
    <link rel="stylesheet" href="{cdnBase}/reveal.css">
    <link rel="stylesheet" href="{cdnBase}/theme/{theme}.css">
</head>
<body>
    <div class="reveal">
        <div class="slides">
            {slide sections}
        </div>
    </div>
    <script src="{cdnBase}/reveal.js"></script>
    <script>
        Reveal.initialize({
            transition: '{transition}',
            slideNumber: {slideNumber},
            controls: {controls},
            ...
        });
    </script>
</body>
</html>
```

### Inline and Block Rendering
Within slides, standard HTML elements are used:
- Paragraphs → `<p>`
- Lists → `<ul>/<ol>/<li>`
- Images → `<img>` (centered)
- Source blocks → `<pre><code class="language-X">`
- Tables → `<table>/<tr>/<td>`
- Admonitions → `<div class="admonition admonition-type">`
- STEM blocks → MathJax-compatible elements (same as HtmlRenderer)
- Strong/emphasis/monospace → `<strong>/<em>/<code>`

### CLI Integration
- `OutputFormat.Revealjs` added to enum
- `-b revealjs` / `--backend revealjs` parsing
- `FormatExtension`: `Revealjs => ".html"` (reveal.js output is HTML)
- New specialized tool: `src/AdocNet.Cli.Revealjs/` with `adocnet-revealjs` command
- Help text updated

## 4. New Projects Checklist

### Converter Projects

**AdocNet.Converters.Man**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework />
    <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <RootNamespace>AdocNet.Converters.Man</RootNamespace>
    <Description>Man page (roff) renderer for the Adoc.Net library.</Description>
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

**AdocNet.Converters.Revealjs**
Same pattern, `RootNamespace` = `AdocNet.Converters.Revealjs`,
description = `Reveal.js slides renderer for the Adoc.Net library.`

### CLI Tool Projects

**AdocNet.Cli.Man**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>adocnet-man</AssemblyName>
    <RootNamespace>AdocNet.Cli.Man</RootNamespace>
    <Description>AsciiDoc to man page converter.</Description>
    <PackageId>AdocNet.Man</PackageId>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>adocnet-man</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdocNet.Cli\AdocNet.Cli.csproj" />
  </ItemGroup>
</Project>
```

**AdocNet.Cli.Revealjs** — same pattern with `adocnet-revealjs` tool name.

### Solution Registration (AdocNet.slnx)
Add to `/src/` folder:
```xml
<Project Path="src/AdocNet.Converters.Man/AdocNet.Converters.Man.csproj" />
<Project Path="src/AdocNet.Converters.Revealjs/AdocNet.Converters.Revealjs.csproj" />
```
Add to `/tools/` folder:
```xml
<Project Path="src/AdocNet.Cli.Man/AdocNet.Cli.Man.csproj" />
<Project Path="src/AdocNet.Cli.Revealjs/AdocNet.Cli.Revealjs.csproj" />
```

### TFMs and Dependencies
- Converter projects: `netstandard2.0;net10.0` (matching existing converters)
- CLI tool projects: inherit from `Directory.Build.props` (`net10.0`)
- Zero NuGet dependencies for both new converters
- Both reference `AdocNet.Core`, `AdocNet.Ast`, `AdocNet.Parser`

### AdocNet.Cli Changes
Add `<ProjectReference>` to both new converter projects so `ConvertCommand` can
instantiate them.

## 5. Testing Strategy

### Man Page Converter Tests
Location: `tests/AdocNet.Tests/Converters/Man/`

**Unit tests** (`ManRendererTests.cs`):
- Simple paragraph → verify `.PP\n` prefix
- Section level 1 → verify `.SH TITLE` (uppercased)
- Section level 2 → verify `.SS Title`
- Bold/italic/monospace inlines → verify font escapes
- Source block → verify `.nf`/`.fi` wrapping
- Description list → verify `.TP` output
- Unordered list → verify `.IP "\\(bu"` output
- Ordered list → verify `.IP "N."` output
- Admonition → verify `\fBTYPE:\fR` prefix
- Table → verify structured output
- Special character escaping (backslash, leading dot, leading apostrophe)
- Nested lists → verify `.RS`/`.RE` indentation
- Document with `:doctype: manpage` → verify `.TH` header with NAME section
- Empty document → verify minimal valid output

**Integration tests** (`ManRendererIntegrationTests.cs`):
- Full document round-trip: parse AsciiDoc → render man → compare to expected roff string
- Man page doctype detection
- Attribute handling (`:manmanual:`, `:mansource:`, `:revdate:`)

### Reveal.js Converter Tests
Location: `tests/AdocNet.Tests/Converters/Revealjs/`

**Unit tests** (`RevealjsRendererTests.cs`):
- Level 1 section → verify `<section>` wrapping
- Level 2 section → verify nested `<section>` (vertical slide)
- Title slide generated from document title
- Speaker notes (`[.notes]` role) → verify `<aside class="notes">`
- Theme attribute → verify CDN link
- Transition attribute → verify `Reveal.initialize()` config
- Paragraph in slide → verify `<p>` inside `<section>`
- Source block in slide → verify `<pre><code>`
- Image in slide → verify `<img>`
- Multiple level-1 sections → verify separate horizontal slides
- Empty document → verify minimal valid HTML

**Integration tests** (`RevealjsRendererIntegrationTests.cs`):
- Full document → verify complete HTML with `<div class="reveal">`
- Custom attributes → verify they appear in `Reveal.initialize()`
- STEM content in slides → MathJax script inclusion

### Converter Template Tests
Location: `tests/AdocNet.Tests/Converters/Html/`

**Unit tests** (`NodeTemplateTests.cs`):
- Template registered → matching node uses template output
- Template registered → non-matching node uses default output
- Multiple templates → first match wins
- No templates (null) → default rendering (no overhead)
- Block template → intercepts block rendering
- Inline template → intercepts inline rendering
- Template with RenderContext access → can read document attributes

### Output Comparison Approach
All tests compare rendered output to expected strings:
```csharp
[Test]
public void Paragraph_renders_as_PP()
{
    var doc = new DocumentNode { Children = { new ParagraphNode { Text = "Hello world" } } };
    var output = RenderToString(doc);
    Assert.That(output, Does.Contain(".PP\nHello world"));
}
```

For integration tests, use multi-line expected strings with exact roff/HTML output.
No golden file approach needed — expected output is small enough to inline.

## 6. Explicit Non-Goals

### Not in scope for beta.17

1. **Asciidoctor.js compatibility**: We do NOT aim for byte-identical output with
   Asciidoctor's man page or reveal.js converters. The goal is correct roff and
   valid reveal.js HTML, not replication of Asciidoctor's exact formatting choices.

2. **Browser-based rendering**: The reveal.js converter produces static HTML files
   that reference CDN-hosted reveal.js. We do NOT embed a browser, start a web server,
   or provide a live preview for slides.

3. **WYSIWYG editing**: Templates are a rendering customization, not an editing
   feature. There is no template editor, no live preview of template changes.

4. **Template engines**: We do NOT integrate Razor, Scriban, Liquid, or any template
   engine. Templates are C# code implementing `INodeTemplate`. This is deliberate:
   zero dependencies, full type safety, no string templating.

5. **PDF/DocBook templates**: `INodeTemplate` only applies to HtmlRenderer.
   PDF and DocBook have fundamentally different output models (binary PDF objects,
   XML elements) that don't map to simple string substitution.

6. **Man page preprocessing**: We do NOT invoke `tbl`, `eqn`, or other roff
   preprocessors. Tables are rendered with manual formatting, not `.TS`/`.TE`.

7. **Reveal.js plugins**: We do NOT support reveal.js plugins (highlight.js,
   markdown, notes server, etc.). Only the core reveal.js framework is loaded.

8. **Custom slide layouts**: No support for custom CSS, custom slide dimensions
   beyond what `Reveal.initialize()` config provides, or custom HTML wrappers.

9. **Incremental builds for new converters**: No caching integration for man/revealjs
   converters. These are simple single-pass renderers.

10. **Man page validation**: We do NOT validate output against `mandoc -Tlint` or
    `groff` during rendering. Tests verify structural correctness.
