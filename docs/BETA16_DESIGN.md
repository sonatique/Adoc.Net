# Beta.16 Design Document — Asciidoctor Parity Features

> Version: `1.0.0-beta.16`. Builds on beta.15.

---

## 1. Collapsible Blocks

### Overview

Asciidoctor supports `[%collapsible]` on delimited blocks (typically example blocks).
The block renders as an HTML `<details>/<summary>` element, initially collapsed.

### AST Change

Add `IsCollapsible` property to `DelimitedBlockNode`:

```csharp
/// <summary>
/// Whether this block was marked with <c>[%collapsible]</c>.
/// When true, the HTML renderer wraps the block in &lt;details&gt;/&lt;summary&gt;.
/// </summary>
public bool IsCollapsible { get; init; }
```

Must also be included in `GetProperties()` and `MixAdditionalState()` for structural hashing.

### Parser Change

In `BlockParser.cs`, when building a `DelimitedBlockNode`, check if `blockAttrs.Options`
contains `"collapsible"`. If so, set `IsCollapsible = true` on the node.

The parser already recognizes `[%option]` syntax and collects options into
`BlockAttributes.Options`. The change is small: when creating the `DelimitedBlockNode`,
pass the collapsible flag through. This applies to all delimited block constructions
(there are ~5 `new DelimitedBlockNode` sites in BlockParser).

### HTML Rendering

In `RenderDelimitedBlock`, when `block.IsCollapsible` is true:

```html
<details>
<summary class="title">{Title or "Details"}</summary>
<div class="content">
  {normal block content}
</div>
</details>
```

If the block has a title, use it as `<summary>`. Otherwise use "Details".
The collapsible wrapper replaces the normal block wrapper (no double-wrapping).

The `IsCollapsible` flag works with any `DelimitedBlockKind`, but is most commonly
used with `Example` blocks.

### Edge Cases

- `[%collapsible]` without a title → `<summary>Details</summary>`
- `[%collapsible]` on a source block → `<details>` wraps the `<pre><code>` block
- Combined with roles: `[%collapsible.custom-role]` → roles applied to `<details>` element

---

## 2. Data URI Embedding

### Overview

When the `:data-uri:` document attribute is set, image `src` attributes should contain
base64-encoded data URIs instead of file paths.

### Approach

The HTML renderer reads the image file, detects MIME type from extension, base64 encodes
the content, and emits `<img src="data:{mime};base64,{data}">`.

### Image Path Resolution

1. Check `imagesdir` document attribute (default: empty string / current directory)
2. Resolve relative to the document's base directory
3. Base directory comes from a new `BaseDirectory` property on `HtmlRenderOptions`

```csharp
// In HtmlRenderOptions
/// <summary>
/// Base directory for resolving relative image paths (for data-uri embedding).
/// When null, data-uri falls back to literal path output.
/// </summary>
public string? BaseDirectory { get; init; }
```

### MIME Type Detection

Simple extension-based mapping (no file magic):

| Extension | MIME Type |
|-----------|-----------|
| `.png` | `image/png` |
| `.jpg`, `.jpeg` | `image/jpeg` |
| `.gif` | `image/gif` |
| `.svg` | `image/svg+xml` |
| `.webp` | `image/webp` |
| `.ico` | `image/x-icon` |
| `.bmp` | `image/bmp` |

Unknown extension → fall back to literal path with warning diagnostic.

### Helper Class

New static helper: `DataUriHelper` in `src/AdocNet.Converters.Html/DataUriHelper.cs`

```csharp
internal static class DataUriHelper
{
    public static string? TryConvertToDataUri(
        string imagePath, string? baseDirectory, string? imagesDir);
    public static string? GetMimeType(string path);
}
```

Returns null if file not found or type unknown → renderer falls back to literal path.

### Affected Render Methods

- `RenderBlockImage` — block images
- Inline image rendering (in `RenderInline` switch)
- Admonition icon images (when `icons=image`)

All three check `DocumentAttributes["data-uri"]` and call the helper.

### HtmlRenderState Addition

Add `bool DataUriEnabled` and `string? BaseDirectory` to `HtmlRenderState`,
populated from document attributes and options during render setup.

---

## 3. Font Awesome CSS Injection

### Overview

When `:icons: font` is set, the HTML renderer already emits FA class names
(`fa fa-{name}`) but does NOT inject the Font Awesome CSS stylesheet.
Users must manually add it. Beta.16 auto-injects it.

### Approach

In `AppendDocumentPrologue`, after theme CSS and before `ExtraHead`:

```csharp
if (document.Attributes.TryGetValue("icons", out var iconsVal)
    && string.Equals(iconsVal, "font", StringComparison.OrdinalIgnoreCase))
{
    sb.Append("<link rel=\"stylesheet\" href=\"https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css\">\n");
}
```

### Version Choice

Font Awesome 4.7.0 — matches Asciidoctor's default. The `fa` prefix (FA4) is already
used throughout the renderer. FA5/FA6 use different prefixes (`fas`, `far`, etc.).

### No New Options

This is automatic when `icons=font` is set. No additional option needed.
The `iconfont-cdn` document attribute could override the URL (Asciidoctor supports this):

```csharp
var cdnUrl = document.Attributes.TryGetValue("iconfont-cdn", out var customUrl)
    ? customUrl
    : "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css";
```

---

## 4. Docinfo Injection

### Overview

Asciidoctor supports injecting custom HTML from external files into the document head
and before the closing `</body>` tag. Files are looked up by naming convention.

### File Naming Convention

| File | Location | Injection Point |
|------|----------|-----------------|
| `docinfo.html` | Document base directory | End of `<head>` |
| `docinfo-footer.html` | Document base directory | Before `</body>` |
| `{docname}-docinfo.html` | Document base directory | End of `<head>` (private) |
| `{docname}-docinfo-footer.html` | Document base directory | Before `</body>` (private) |

`{docname}` is the document filename without extension.

### Control Attribute

`:docinfo:` attribute controls which docinfo files are loaded:

| Value | Behavior |
|-------|----------|
| not set | No docinfo |
| `shared` | Load `docinfo.html` and `docinfo-footer.html` |
| `private` | Load `{docname}-docinfo.html` and `{docname}-docinfo-footer.html` |
| `shared-head` | Load only `docinfo.html` (header) |
| `private-head` | Load only `{docname}-docinfo.html` (header) |
| `shared-footer` | Load only `docinfo-footer.html` (footer) |
| `private-footer` | Load only `{docname}-docinfo-footer.html` (footer) |
| `shared,private` | Load both shared and private |

### Helper Class

New static helper: `DocinfoHelper` in `src/AdocNet.Converters.Html/DocinfoHelper.cs`

```csharp
internal static class DocinfoHelper
{
    public static string? ReadHeaderDocinfo(
        IReadOnlyDictionary<string, string> attributes, string? baseDirectory);
    public static string? ReadFooterDocinfo(
        IReadOnlyDictionary<string, string> attributes, string? baseDirectory);
}
```

Returns concatenated content from matching files, or null if no docinfo.

### Injection Points

- **Header**: In `AppendDocumentPrologue`, after ExtraHead, before `</head>`
- **Footer**: In `AppendDocumentEpilogue`, before `</body>`

Both methods need the document and options/baseDirectory parameters.
`AppendDocumentEpilogue` currently takes only `StringBuilder` — needs
`DocumentNode document` and `string? baseDirectory` added.

### Safe Mode Interaction

- `Safe` mode: docinfo files restricted to base directory (no `../`)
- `Server` / `Secure`: docinfo injection disabled entirely

---

## 5. Safe Modes

### Overview

Asciidoctor safe modes control what the processor is allowed to do, restricting
potentially dangerous operations like file I/O, includes, and attribute overrides.

### SafeMode Enum

New file: `src/AdocNet.Core/SafeMode.cs`

```csharp
/// <summary>
/// Controls the security level of document processing.
/// Higher values are more restrictive.
/// </summary>
public enum SafeMode
{
    /// <summary>No restrictions. All features enabled. Default.</summary>
    Unsafe = 0,

    /// <summary>
    /// Prevents access to files outside the document's base directory.
    /// Disables include path traversal (../).
    /// </summary>
    Safe = 1,

    /// <summary>
    /// Disables filesystem features not explicitly enabled.
    /// Disables URI includes. Disables docinfo.
    /// </summary>
    Server = 10,

    /// <summary>
    /// Most restrictive. Disables all includes, all file I/O,
    /// and all macros that access the filesystem.
    /// </summary>
    Secure = 20,
}
```

### ParseOptions Addition

```csharp
/// <summary>
/// The safe mode for document processing. Default: <see cref="SafeMode.Unsafe"/>.
/// </summary>
public SafeMode SafeMode { get; init; } = SafeMode.Unsafe;
```

### Enforcement Points

| Feature | Unsafe | Safe | Server | Secure |
|---------|--------|------|--------|--------|
| Include (local) | Yes | Base dir only | No | No |
| Include (URL) | If AllowUriRead | If AllowUriRead | No | No |
| Docinfo injection | Yes | Base dir only | No | No |
| Data URI (file read) | Yes | Base dir only | No | No |
| Attribute override of sensitive attrs | Yes | No | No | No |
| Icon image (file read) | Yes | Yes | No | No |

### Implementation in IncludeExpander

`IncludeExpander.Expand()` receives a new `SafeMode` parameter (passed from ParseOptions).

- `Safe`: after resolving the include path, verify it's within `baseDirectory`
  using `Path.GetFullPath()` comparison. Reject with diagnostic if outside.
- `Server`: skip all includes (local and URL). Emit diagnostic.
- `Secure`: skip all includes. Emit diagnostic.

### Sensitive Attributes (Safe mode)

Attributes that could influence security if overridden by the document:
`icons`, `iconsdir`, `imagesdir`, `docinfo`, `data-uri`, `allow-uri-read`.

In `Safe` mode and above, these are treated as locked (cannot be overridden by document).
Implementation: in the parser or attribute merger, check safe mode and add these
to the effective `LockedAttributes` set.

### BaseDirectory Check Helper

```csharp
internal static class SafeModeHelper
{
    /// <summary>
    /// Returns true if the resolved path is within the base directory.
    /// </summary>
    public static bool IsWithinBaseDirectory(string resolvedPath, string baseDirectory);

    /// <summary>
    /// Returns the set of attribute names that should be locked for the given safe mode.
    /// </summary>
    public static IReadOnlySet<string>? GetLockedAttributes(SafeMode mode);
}
```

Location: `src/AdocNet.Core/SafeModeHelper.cs`

---

## 6. STEM/Math Support

### Overview

AsciiDoc supports mathematical notation via `stem`, `latexmath`, and `asciimath`
macros and blocks. MathJax renders them in the browser.

### New AST Nodes

**StemBlockNode** — block-level math:

```csharp
// src/AdocNet.Ast/StemBlockNode.cs
public sealed class StemBlockNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.StemBlock;

    /// <summary>The math formula content.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The stem type: "latexmath" or "asciimath". Default determined by :stem: attribute.
    /// </summary>
    public required string StemType { get; init; }

    /// <summary>Optional block title.</summary>
    public string? Title { get; init; }
}
```

**StemInlineNode** — inline math:

```csharp
// src/AdocNet.Ast/StemInlineNode.cs
public sealed class StemInlineNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.StemInline;

    /// <summary>The math formula content.</summary>
    public required string Content { get; init; }

    /// <summary>The stem type: "latexmath" or "asciimath".</summary>
    public required string StemType { get; init; }
}
```

### AstNodeKind Additions

```csharp
StemBlock,    // 39
StemInline,   // 40
```

### Parser Changes

**Block-level stem**: In `BlockParser.cs`, when a delimited block has style `"stem"`,
`"latexmath"`, or `"asciimath"` (from `[stem]`, `[latexmath]`, or `[asciimath]`),
create a `StemBlockNode` instead of a `DelimitedBlockNode`.

The stem type is determined by:
1. Explicit style: `[latexmath]` or `[asciimath]`
2. If just `[stem]`: use `:stem:` document attribute value (default: `"latexmath"`)

Stem blocks use open blocks (`--`) or example blocks (`====`) as delimiters.

**Inline stem macros**: Add `"stem"`, `"latexmath"`, `"asciimath"` to `KnownMacroNames`.
When the inline parser encounters `stem:[formula]`, `latexmath:[formula]`, or
`asciimath:[formula]`, create a `StemInlineNode` instead of `InlineMacroNode`.

This requires a change in `TryParseGenericMacro` or a new dedicated method:
after matching the macro name, if it's a stem name, return `StemInlineNode`
instead of `InlineMacroNode`.

Stem type for `stem:[]` determined by document `:stem:` attribute (default latexmath).
For `latexmath:[]` and `asciimath:[]`, the type is explicit.

### HTML Rendering

**Block stem**:

```html
<div class="stemblock">
<div class="title">{Title}</div>  <!-- if title present -->
<div class="content">
\[{formula}\]
</div>
</div>
```

The `\[...\]` delimiters are for MathJax display math (latexmath).
For asciimath, use `\$...\$` or the asciimath delimiter.

**Inline stem**:

```html
\({formula}\)
```

For latexmath inline. For asciimath: `\$...\$`.

**MathJax script injection**: In `AppendDocumentPrologue`, when `:stem:` attribute is set:

```html
<script src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js"></script>
```

Also add MathJax configuration for asciimath if `:stem: asciimath`:

```html
<script>
MathJax = {
  loader: {load: ['input/asciimath']},
  asciimath: {delimiters: [['\\$','\\$']]}
};
</script>
<script src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js"></script>
```

### Document Attribute

`:stem:` — enables math support. Values:
- `:stem:` or `:stem: latexmath` → LaTeX math (default)
- `:stem: asciimath` → AsciiMath

---

## 7. HtmlRenderer Refactoring

### Problem

The renderer is currently 1978 lines. Adding collapsible, data-uri, docinfo,
and stem rendering will push it well past 2000 lines (violating the 500-line guideline).

### Approach: Partial Classes

Use C# partial classes to split `HtmlRenderer` into logical files while keeping
a single class. This is the lightest-weight refactoring — no API changes,
no new abstractions, no behavior changes.

```
src/AdocNet.Converters.Html/
    HtmlRenderer.cs              — main Render(), prologue, epilogue, setup (~300 lines)
    HtmlRenderer.Blocks.cs       — block rendering methods (~500 lines)
    HtmlRenderer.Inlines.cs      — inline rendering methods (~400 lines)
    HtmlRenderer.Tables.cs       — table rendering (~300 lines)
    HtmlRenderer.Helpers.cs      — EscapeTo, AppendRoleClasses, etc. (~200 lines)
    HtmlRenderer.Stem.cs         — NEW: stem block/inline rendering (~80 lines)
    DataUriHelper.cs             — NEW: data-uri conversion (separate class)
    DocinfoHelper.cs             — NEW: docinfo file reading (separate class)
```

### When to Split

Split during P02/P03 as files grow past 500 lines. Don't pre-split — refactor
when it becomes necessary during feature implementation.

### What NOT to Do

- No new interfaces or abstractions
- No visitor pattern
- No strategy pattern
- No template method refactoring
- No breaking out into separate renderer classes

---

## 8. Testing Strategy

### Collapsible Blocks

| Test | Description |
|------|-------------|
| Parser: `[%collapsible]` on example block | `IsCollapsible = true` on AST |
| Parser: no collapsible option | `IsCollapsible = false` |
| HTML: collapsible example with title | `<details><summary>Title</summary>...</details>` |
| HTML: collapsible without title | `<summary>Details</summary>` |
| HTML: collapsible source block | `<details>` wraps `<pre><code>` |

### Data URI

| Test | Description |
|------|-------------|
| PNG image with data-uri | `src="data:image/png;base64,..."` in output |
| JPEG image with data-uri | `src="data:image/jpeg;base64,..."` |
| Missing image file | Falls back to literal path, warning diagnostic |
| No data-uri attribute | Normal `src="path"` |
| SVG image | `data:image/svg+xml;base64,...` |
| Inline image with data-uri | Same base64 treatment |

### Font Awesome

| Test | Description |
|------|-------------|
| icons=font in full doc mode | `<link rel="stylesheet"...font-awesome...>` in output |
| icons=font with custom CDN | Custom URL used instead of default |
| No icons attribute | No FA link injected |
| icons=image | No FA link injected |

### Docinfo

| Test | Description |
|------|-------------|
| docinfo=shared with header file | Content appears in `<head>` |
| docinfo=shared with footer file | Content appears before `</body>` |
| docinfo=private | Uses `{docname}-docinfo.html` |
| No docinfo attribute | No injection |
| Missing docinfo file | No error, no injection |
| Safe mode + docinfo | Respects restrictions |

### Safe Modes

| Test | Description |
|------|-------------|
| Unsafe: include with `../` | Allowed |
| Safe: include with `../` | Blocked with diagnostic |
| Server: any local include | Blocked |
| Secure: any include | Blocked |
| Safe: sensitive attribute override | Locked |
| Default (no safe mode) | Behaves as Unsafe |

### STEM/Math

| Test | Description |
|------|-------------|
| Parser: `[stem]` block | Creates `StemBlockNode` with latexmath type |
| Parser: `[latexmath]` block | Creates `StemBlockNode` with latexmath type |
| Parser: `[asciimath]` block | Creates `StemBlockNode` with asciimath type |
| Parser: `stem:[x^2]` inline | Creates `StemInlineNode` |
| Parser: `latexmath:[\\sum_i]` inline | Creates `StemInlineNode` |
| HTML: stem block | `<div class="stemblock">` with `\[...\]` |
| HTML: stem inline | `\(...\)` in output |
| HTML: `:stem:` attribute | MathJax script injected in `<head>` |
| HTML: `:stem: asciimath` | AsciiMath MathJax config + script |

### Round-Trip / Regression

- All existing tests must continue to pass unchanged
- Rendering without any beta.16 features produces byte-identical output to beta.15
- Structural hashing: new AST nodes implement `GetProperties()` and `MixAdditionalState()`

---

## 9. Explicit Non-Goals

The following are **NOT** in scope for beta.16:

| Feature | Why Not | When |
|---------|---------|------|
| Converter templates | Different architecture (template engine) | beta.17+ |
| Man page output | New renderer, low priority | beta.17+ |
| Reveal.js slides | Specialized renderer | beta.17+ |
| Custom backends | Plugin architecture needed | beta.17+ |
| Embedded MathJax | Would embed ~1MB JS; CDN is correct approach | Never |
| Full CSP-compatible mode | Would require embedded FA CSS too | beta.17+ |
| LaTeX rendering to images | Server-side rendering, complex | beta.17+ |
| PDF stem rendering | MathJax is browser-only; PDF math is very different | beta.17+ |
| Docinfo for non-HTML | Only HTML has `<head>` injection model | N/A |
| Custom safe mode levels | 4 levels match Asciidoctor exactly | N/A |
| Remote docinfo files | Security risk, not in Asciidoctor either | Never |

---

## 10. Implementation Phases

| Phase | Features | Key Files |
|-------|----------|-----------|
| P02 | Collapsible + Data URI + Font Awesome | AST, Parser, HtmlRenderer, DataUriHelper |
| P03 | Docinfo + Safe Modes | DocinfoHelper, SafeMode, SafeModeHelper, ParseOptions, IncludeExpander |
| P04 | STEM/Math | StemBlockNode, StemInlineNode, Parser, HtmlRenderer |

### Phase Dependencies

- P02 and P03 are largely independent (can be done in either order)
- P04 depends on no other beta.16 phase
- Safe mode (P03) affects data-uri behavior (P02), but the data-uri code can be
  written first and safe mode enforcement added on top

### File Change Summary

New files:
- `src/AdocNet.Core/SafeMode.cs`
- `src/AdocNet.Core/SafeModeHelper.cs`
- `src/AdocNet.Ast/StemBlockNode.cs`
- `src/AdocNet.Ast/StemInlineNode.cs`
- `src/AdocNet.Converters.Html/DataUriHelper.cs`
- `src/AdocNet.Converters.Html/DocinfoHelper.cs`

Modified files:
- `src/AdocNet.Ast/AstNodeKind.cs` — add StemBlock, StemInline
- `src/AdocNet.Ast/DelimitedBlockNode.cs` — add IsCollapsible
- `src/AdocNet.Core/ParseOptions.cs` — add SafeMode
- `src/AdocNet.Parser/BlockParser.cs` — collapsible detection, stem block creation
- `src/AdocNet.Parser/InlineParser.cs` — stem macro parsing
- `src/AdocNet.Parser/IncludeExpander.cs` — safe mode enforcement
- `src/AdocNet.Converters.Html/HtmlRenderer.cs` — collapsible, data-uri, FA, docinfo, stem
- `src/AdocNet.Converters.Html/HtmlRenderOptions.cs` — add BaseDirectory
