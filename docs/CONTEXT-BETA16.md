# Beta.16 Context Discovery — Asciidoctor Parity Features

> Generated during P00 phase. Read-only exploration of the codebase.

---

## 1. HTML Renderer — Head Injection Point

**File**: `src/AdocNet.Converters.Html/HtmlRenderer.cs`

### Render Flow (lines 155-179)

```
state setup → fullDoc check → AppendDocumentPrologue() → RenderDocumentBody() → RenderFootnotesSection() → AppendDocumentEpilogue()
```

### AppendDocumentPrologue (lines 184-215)

Builds the `<head>` section in this order:
1. `<!DOCTYPE html>`, `<html lang="en">`, `<head>`
2. `<meta charset>`, `<meta viewport>`
3. `<title>` — from `options.Title ?? document.Title ?? "Untitled"`
4. Theme CSS — `HtmlThemeCss.GetCss(options.Theme)` in a `<style>` block
5. Custom CSS — `options.CustomCss` appended to the same `<style>` block
6. `options.ExtraHead` — injected verbatim just before `</head>`
7. `</head>`, `<body>`

**Head injection point for beta.16**: Between the `<style>` block and `</head>`.
Currently `ExtraHead` is the only extension point. Beta.16 features (Font Awesome CDN,
MathJax script, docinfo header) should be injected in this area, either by extending
the prologue logic or by composing into ExtraHead.

### AppendDocumentEpilogue (lines 220-224)

Simply emits `</body>\n</html>\n`. No injection point for footer content.
Beta.16 docinfo footer injection needs to be added before `</body>`.

---

## 2. HTML Renderer — Image Rendering

**File**: `src/AdocNet.Converters.Html/HtmlRenderer.cs`

### Block Images (RenderBlockImage, line 1326)

```html
<div class="imageblock">
  <img src="{Target}" alt="{Alt}">
  <div class="title">Figure N. {Title}</div>  <!-- if title present -->
</div>
```

- `image.Target` is emitted directly as `src` attribute (HTML-escaped)
- No base64/data URI conversion
- No file reading or path resolution
- `FigureCounter` incremented per block image with a title

### Inline Images (line 1558)

```html
<span class="image"><img src="{Target}" alt="{Alt}"></span>
```

- Same pattern: literal path in `src`, no data URI support

**For beta.16 `:data-uri:` feature**: When the document attribute `data-uri` is set,
the renderer must read image files from disk, base64 encode them, and emit
`<img src="data:image/{type};base64,{data}">` instead. Needs base directory
from ParseOptions or RenderOptions for path resolution. Fallback to literal path
if file not found.

---

## 3. HTML Renderer — Icon Handling

**File**: `src/AdocNet.Converters.Html/HtmlRenderer.cs`

### Detection (line 282)

```csharp
bool useIconFont = document.Attributes.TryGetValue("icons", out var iconsValue)
    && string.Equals(iconsValue, "font", StringComparison.OrdinalIgnoreCase);
```

`useIconFont` is passed to admonition rendering and controls icon output mode.

### Admonition Icons (line 900-907)

When `icons=font`: `<i class="fa icon-{type}" title="{Type}"></i>`
Otherwise: text label.

### Icon Macro (RenderIconMacro, line 1737)

Three modes based on `icons` attribute:
1. **`icons=font`**: `<i class="fa fa-{name}"></i>` with optional size/rotate/flip classes
2. **`icons=image`**: `<img src="{iconsdir}/{name}.png" alt="{name}">`
3. **No icons attr**: `[{name}]` plain text

**Missing for beta.16**: No Font Awesome CDN stylesheet injection in `<head>`.
The renderer outputs FA class names but never injects the CSS link. Users must
manually add it via `ExtraHead`. Beta.16 should auto-inject:
```html
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css">
```

---

## 4. HTML Renderer — Delimited Block Rendering

**File**: `src/AdocNet.Converters.Html/HtmlRenderer.cs` (line 613)

### RenderDelimitedBlock

Handles all `DelimitedBlockKind` values via switch:
- `Literal` → `<pre>` (no wrapper for plain literal, `<div class="literalblock">` if roles)
- `Listing` → `<pre>` (with optional `<div class="listingblock">` wrapper)
- `Source` → `<pre><code>` with highlight.js or built-in syntax highlighting
- `Example` → `<div class="exampleblock">` with "Example N." numbered caption
- `Quote` → `<blockquote>` with optional attribution
- `Sidebar` → `<div class="sidebarblock">`
- `Passthrough` → raw content
- `Open` → `<div class="openblock">` (or `abstract` variant)
- `Verse` → `<div class="verseblock"><pre>`

**No collapsible support**: No `<details>/<summary>` output for any block kind.
No `IsCollapsible` property on `DelimitedBlockNode`.

---

## 5. HtmlRenderOptions — All Properties

**File**: `src/AdocNet.Converters.Html/HtmlRenderOptions.cs`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Theme` | `HtmlTheme` | `None` | Built-in theme (None = bare fragment) |
| `CustomCss` | `string?` | `null` | Appended after theme CSS |
| `FullDocument` | `bool` | `false` | Wrap in full HTML document |
| `Title` | `string?` | `null` | `<title>` override |
| `ExtraHead` | `string?` | `null` | Verbatim `<head>` content |
| `EnableSyntaxHighlighting` | `bool` | `false` | Server-side highlighting |
| `EnableIncrementalMarkers` | `bool` | `false` | Section comment markers |

Internal: `IsFullDocument` → `FullDocument || Theme != HtmlTheme.None`

Static: `Default` (bare fragment), `Styled` (Default theme)

---

## 6. DelimitedBlockNode — Fields

**File**: `src/AdocNet.Ast/DelimitedBlockNode.cs`

| Property | Type | Description |
|----------|------|-------------|
| `BlockKind` | `DelimitedBlockKind` (required) | Type of block |
| `Content` | `string?` | Raw text for verbatim blocks, null for structural |
| `Title` | `string?` | Block title from `.Title` line |
| `Language` | `string?` | Source language for Source blocks |
| `Attribution` | `string?` | Quote attribution |
| `CitationSource` | `string?` | Quote citation source |
| `Style` | `string?` | Block style from `[style]` |
| `Callouts` | `IReadOnlyList<CalloutEntry>?` | Callout entries |

**No `IsCollapsible` property.** No `Options` list.

From `BlockNode` base: `Id`, `Reftext`, `Roles`, `Substitutions`.

---

## 7. ParseOptions — All Properties

**File**: `src/AdocNet.Core/ParseOptions.cs`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SourceFilePath` | `string?` | `null` | Document file path |
| `BaseDirectory` | `string?` | `null` | Base dir for includes |
| `IncludeMaxDepth` | `int` | `10` | Max include nesting |
| `ExpandIncludes` | `bool?` | `null` | Whether to expand includes |
| `Attributes` | `IReadOnlyDictionary<string, string>?` | `null` | Pre-populated attributes |
| `LockedAttributes` | `IReadOnlySet<string>?` | `null` | Unoverridable attributes |
| `IncludeReader` | `IIncludeReader?` | `null` | Custom include reader |
| `AllowUriRead` | `bool` | `false` | Allow URL includes |

**No `SafeMode` property.** No safe mode enforcement anywhere.

Internal methods: `ResolveBaseDirectory()`, `ShouldExpandIncludes()`.

---

## 8. IncludeExpander — Include Flow

**File**: `src/AdocNet.Parser/IncludeExpander.cs`

### Flow
1. `Expand()` entry point: takes text, baseDirectory, reader, maxDepth, attributes, allowUriRead
2. Builds attribute map from document + API attributes
3. `ExpandRecursive()`: line-by-line processing
   - Handles conditional directives (ifdef/ifndef/endif/ifeval) for include skipping
   - Matches `include::path[attrs]` pattern
   - **URL includes**: checks `allowUriRead` flag, uses shared `HttpClient`
   - **File includes**: resolves relative to baseDirectory, reads via `IIncludeReader`
   - Supports: `lines=`, `tag=`, `tags=`, `leveloffset=` attributes
   - Circular detection via visited path set
   - Depth guard via `maxDepth`

### Security-relevant for SafeMode
- `AllowUriRead` already controls URL includes (false by default)
- No path traversal guard — includes can reference `../` paths
- No restriction on absolute paths
- No filesystem I/O restriction mechanism

**For beta.16 Safe Modes**:
- `Safe`: should disallow `../` parent traversal in includes
- `Server`: should disallow all file I/O except explicitly allowed
- `Secure`: should disable all includes entirely

---

## 9. AstNodeKind Enum — 38 Values

**File**: `src/AdocNet.Ast/AstNodeKind.cs`

All current values: Document, Section, Paragraph, List, ListItem, DelimitedBlock,
Table, TableRow, TableCell, InlineText, InlineEmphasis, InlineStrong, InlineMonospace,
InlineLink, InlineLinkMacro, InlineImage, BlockImage, DescriptionList, DescriptionItem,
Admonition, InlinePassthrough, InlineCrossReference, InlineFootnote, BibliographyEntry,
InlineMacro, InlineSuperscript, InlineSubscript, InterDocumentXref, Toc, InlineAnchor,
InlineHighlight, Video, Audio, PageBreak, ThematicBreak, IndexTerm, IndexTermHidden, Index.

**No `StemBlock` or `StemInline`.**

---

## 10. DelimitedBlockKind Enum — 9 Values

**File**: `src/AdocNet.Ast/DelimitedBlockKind.cs`

Values: Literal, Listing, Source, Example, Quote, Sidebar, Passthrough, Open, Verse.

**No `Stem` kind.**

---

## 11. Block Option Parsing in Parser

**File**: `src/AdocNet.Parser/BlockParser.cs`

- `pendingBlockOptions` (List<string>?) collects `[%option]` entries
- Known options: `hardbreaks`, `header`, `footer`, `autowidth`
- Options stored in `BlockAttributes.Options` from the attribute line parser
- `[%hardbreaks]` handled specially for paragraphs (line 514)
- Other options accumulated in `pendingBlockOptions` for block macros (video/audio)
- **No `collapsible` option recognition** — would need to be added

---

## 12. Confirmed Absence of Beta.16 Features

| Feature | Grep verification | Status |
|---------|-------------------|--------|
| StemBlockNode / StemInlineNode | No matches in `src/AdocNet.Ast/` | **ABSENT** |
| SafeMode enum / property | No matches in `src/` | **ABSENT** |
| Collapsible blocks | No matches in `src/` | **ABSENT** |
| Data URI / base64 image embedding | No matches in `src/` | **ABSENT** |
| Docinfo injection | No matches in `src/` | **ABSENT** |
| Font Awesome CDN link | No matches in `src/` | **ABSENT** |
| MathJax script injection | No matches in `src/` | **ABSENT** |

---

## 13. Key Architecture Points for Implementation

### Parser Changes Needed
- Recognize `[%collapsible]` option on delimited blocks → set `IsCollapsible` on node
- Recognize `[stem]` block style on delimited blocks → create `StemBlockNode`
- Recognize `stem:[]`, `latexmath:[]`, `asciimath:[]` inline macros → create `StemInlineNode`
- Add `SafeMode` to `ParseOptions`, enforce in `IncludeExpander`

### AST Changes Needed
- Add `IsCollapsible` property to `DelimitedBlockNode`
- Add `StemBlockNode` (new AstNodeKind.StemBlock)
- Add `StemInlineNode` (new AstNodeKind.StemInline)
- Add `Stem` to `DelimitedBlockKind` (or use existing Open block with style)

### Renderer Changes Needed
- Collapsible: `<details><summary>` output for IsCollapsible blocks
- Data URI: base64 encode images when `:data-uri:` attribute set
- Font Awesome: inject CDN `<link>` in `<head>` when `icons=font`
- Docinfo: read `docinfo.html`/`docinfo-footer.html`, inject in head/before body close
- MathJax: inject MathJax `<script>` in `<head>` when `:stem:` attribute set
- Stem blocks/inlines: render math content in MathJax-compatible elements

### New Types Needed
- `SafeMode` enum (Unsafe, Safe, Server, Secure)
- `StemBlockNode` AST node
- `StemInlineNode` AST node

---

## 14. Existing Infrastructure That Can Be Leveraged

- `ExtraHead` on HtmlRenderOptions — could compose FA/MathJax links here
- `document.Attributes` already available in renderer — can check `:data-uri:`, `:icons:`, `:stem:`
- `InlineMacroNode` with Name/Target/Content — stem macros fit this model (or new node type)
- `IncludeExpander` already has `allowUriRead` pattern — SafeMode can extend this
- `ParseOptions.BaseDirectory` — needed for docinfo file resolution and data-uri image reading
- `BlockAttributes.Options` — already collects `[%option]` values
