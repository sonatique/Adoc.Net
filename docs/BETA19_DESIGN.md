# Beta.19 Design Document — Asciidoctor Parity Polish

> 13 features across 3 areas: parser (3), rendering attributes (10).

## 1. Fenced Code Blocks (```)

### Syntax

```
```java
public class Hello { }
```
```

Opening line: 3+ backtick characters, optionally followed by a language identifier.
Closing line: 3+ backtick characters (no language), matching the opening backtick count
or more.

### Detection

**New helper method** `TryParseFencedCodeBlock`:

```csharp
private static bool TryParseFencedCodeBlock(string line, out string? language)
```

- Count leading backticks. If fewer than 3, return false.
- After backticks: optional whitespace + optional language identifier.
- Language = remaining trimmed text after backticks (empty → null).

This is intentionally **not** added to `TryGetDelimiterKind` because:
1. Existing delimiters are pure char-repetition lines (4+ same char, nothing else)
2. Fenced blocks carry a language identifier on the opening line
3. The closing delimiter uses the same char but different semantics (no language)

### Integration with main parse loop

At BlockParser.cs ~line 1478, **before** the `TryGetDelimiterKind` call, insert:

```csharp
if (TryParseFencedCodeBlock(line, out var fencedLang))
{
    // Scan forward for closing ``` (3+ backticks, no language)
    // Collect content lines between
    // Create DelimitedBlockNode with BlockKind = Source, Language = fencedLang
    // Apply pending [source] language if fencedLang is null and hasPendingSource
}
```

### Interaction with [source] attribute

If `[source,ruby]` precedes a fenced block:
- `[source]` language takes precedence if fenced has no language
- If both specify a language, fenced language wins (matches Asciidoctor)
- If `[source]` is present, `hasPendingSource` is already true — block becomes Source

### Closing delimiter matching

The closing line must have 3+ backticks with no non-whitespace after.
`IsClosingFence(line)`: count leading backticks ≥ 3, remaining trimmed length = 0.

### Output

Same `DelimitedBlockNode` as `----` source blocks:
- `BlockKind = DelimitedBlockKind.Source`
- `Language = fencedLang ?? pendingSourceLang`
- `Content = collected lines joined by \n`

### Edge cases

- ` ``` ` with no language → Source block with null Language
- ` ```  ` (trailing spaces only) → treated as opening with null language
- Unclosed fenced block → consume to EOF with diagnostic (matches existing behavior)
- Nested fenced blocks: not supported (Asciidoctor doesn't support this either)

## 2. Book Doctype Mode (`:doctype: book`)

### Activation

Check `document.Attributes["doctype"]` during section parsing.
When value is `"book"`:
- Level 0 sections (after document title) are **parts** (Part I, II, etc.)
- Level 1 sections are **chapters**

### SectionNode.Style property

Add to `SectionNode`:

```csharp
/// <summary>
/// Optional section style for book doctype. Values: "appendix", "glossary",
/// "colophon", "dedication", "preface", or null for normal sections.
/// </summary>
public string? Style { get; init; }
```

Must override `GetProperties()` and `MixAdditionalState()` to include Style.

### Section style detection

In the block attribute parsing path (around line 603), after the `[discrete]` check,
add detection for section style names:

```csharp
private static readonly string[] SectionStyleNames =
    ["appendix", "glossary", "colophon", "dedication", "preface", "bibliography",
     "index", "abstract"];
```

When `blockAttrs.Style` matches one of these, set `pendingSectionStyle` and apply
it to the next `SectionNode`.

### HTML rendering

In `HtmlSectionRenderer.RenderSection`:
- When `Style == "appendix"`: prefix title with "Appendix A: ", "Appendix B: ", etc.
  Use a counter on `HtmlRenderState` for appendix lettering.
- Other styles (`glossary`, `colophon`, etc.): add CSS class to the section wrapper
  but no prefix.

### Part rendering (book doctype)

Level 0 sections (`=` single equals, level 0 in our model):
- Currently level 0 is the document title. In book mode, additional level 0 sections
  after the first are parts.
- This is complex and largely presentation-focused. For beta.19: store the Style
  on SectionNode but defer full part/chapter numbering to a future release.
- **Minimum viable**: recognize `[appendix]` style and other section styles. Parts
  require deeper section nesting changes.

### Scope decision

Full book doctype with part numbering (Part I, Part II) requires changes to section
nesting logic. For beta.19, we implement:
- `SectionNode.Style` property (AST change)
- `[appendix]`, `[glossary]`, `[colophon]`, `[dedication]`, `[preface]` recognition
- HTML rendering: appendix prefix with lettering, style-based CSS classes
- **Defer**: Part (level 0) sections, chapter numbering

## 3. toc::[] Block Macro

### Parsing

In `TryParseBlockMacro` (line 3664), add a branch for `"toc"`:

```csharp
if (macroName == "toc")
{
    // toc::[] is a placement marker — empty target and brackets
    node = new TocPlaceholderNode();
    return true;
}
```

**New AST node**: `TocPlaceholderNode` — a simple marker node.

```csharp
public sealed class TocPlaceholderNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.TocPlaceholder;
}
```

Add `TocPlaceholder` to `AstNodeKind` enum.

### Deferred insertion logic

After the main parse loop (line 2578), modify the TOC generation:

```csharp
if (document.Attributes.ContainsKey("toc"))
{
    // ... build entries as before ...
    if (placement == TocPlacement.Macro)
    {
        // Find TocPlaceholderNode and replace it with the real TocNode
        ReplaceTocPlaceholder(document, tocNode);
    }
    else
    {
        document.InsertChild(0, tocNode);
    }
}
```

`ReplaceTocPlaceholder`: walk children, find `TocPlaceholderNode`, replace with `TocNode`.
If no placeholder found: fall back to position 0 (defensive).

### Interaction with `:toc: macro`

- `:toc: macro` sets placement to Macro
- `toc::[]` in the document body inserts the placeholder
- Both must be present for macro-positioned TOC
- If `:toc: macro` but no `toc::[]`: TOC at position 0 (fallback)
- If `toc::[]` but `:toc:` not set: placeholder is ignored (no TOC generated)

## 4. :showtitle: — Embedded Mode Title Rendering

### Current behavior

`RenderDocumentBody` always renders `<h1>Title</h1>` when `document.Title is not null`.
This matches Asciidoctor's full-document mode.

### Design

In `RenderDocumentBody` (HtmlRenderer.cs:195):

```csharp
bool showTitle;
if (fullDoc)
    showTitle = document.Title is not null; // Full doc: always show title
else
    showTitle = document.Title is not null
                && document.Attributes.ContainsKey("showtitle");
```

The `fullDoc` boolean is already available in the calling `RenderDocument` method.
Pass it as a parameter to `RenderDocumentBody`, or read it from `state`.

### Backward compatibility

Current behavior: title always shown. After change:
- Full document mode: unchanged (title always shown)
- Embedded mode: title suppressed unless `:showtitle:` set
- This matches Asciidoctor behavior exactly

**Note**: The current codebase always shows the title. This is technically a behavior
change for embedded mode users who relied on the title being shown. Since there are
no users, this is acceptable.

## 5. :nofooter: — Footer Suppression

### Current state

No footer div (`<div id="footer">`) is currently rendered. The epilogue only has
docinfo footer + `</body></html>`.

### Design

Add a footer div in `AppendDocumentEpilogue` (full-document mode only):

```csharp
if (!document.Attributes.ContainsKey("nofooter"))
{
    sb.Append("<div id=\"footer\">\n");
    sb.Append("<div id=\"footer-text\">\n");
    // Last updated info if available
    var lastUpdateLabel = document.Attributes.TryGetValue("last-update-label", out var lul)
        ? lul : "Last updated";
    sb.Append(lastUpdateLabel);
    sb.Append('\n');
    sb.Append("</div>\n");
    sb.Append("</div>\n");
}
```

When `:nofooter:` is set: skip the footer div entirely.

### Scope

This is a minimal footer. Asciidoctor includes a timestamp — we omit the timestamp
for determinism (no `DateTime.Now` per beta.3 rules). The label text is controlled
by `:last-update-label:`.

## 6. :nofootnotes: — Footnote Section Suppression

### Current behavior

`RenderFootnotesSection` renders `<div id="footnotes">` when footnotes exist.

### Design

At the top of `RenderFootnotesSection`:

```csharp
if (state.DocumentAttributes.ContainsKey("nofootnotes")) return;
```

Simple guard. Inline footnote markers (`<sup>`) still render — only the
definitions section at the bottom is suppressed.

## 7. :source-language: — Default Language Fallback

### Current behavior

Source blocks without a Language property emit `<code>` with no class.

### Design

In `HtmlBlockRenderer.cs`, the source block rendering path:

```csharp
var effectiveLang = block.Language;
if (effectiveLang is null)
{
    state.DocumentAttributes.TryGetValue("source-language", out effectiveLang);
}

if (effectiveLang is not null)
{
    sb.Append(" class=\"language-");
    EscapeTo(sb, effectiveLang);
    // ...
}
```

Use `effectiveLang` everywhere `block.Language` was used in this code path.

### Parser-side vs renderer-side

Renderer-side is correct here. The parser should NOT bake `:source-language:` into
the AST — it's a rendering concern. The AST faithfully records what was explicitly
declared on each block.

## 8. :linkattrs: — Link Attribute Parsing

### Current behavior

`link:url[text, window=_blank]` → entire bracket content becomes the label.

### Design

**Approach**: Parse attributes in the InlineParser when `:linkattrs:` is set.

When `:linkattrs:` attribute is present in document attributes:
1. Split bracket content by `,` (respecting quotes)
2. First segment without `=` is the label text
3. Named segments (`window=_blank`, `role=external`) are attributes

### AST change

Add optional properties to `InlineLinkMacroNode`:

```csharp
/// <summary>Target window for the link. Set when :linkattrs: is enabled.</summary>
public string? Window { get; init; }

/// <summary>Additional CSS role for the link. Set when :linkattrs: is enabled.</summary>
public string? Role { get; init; }
```

### HTML rendering

In `HtmlInlineRenderer`, the `InlineLinkMacroNode` case:

```csharp
if (linkMacro.Window is not null)
{
    sb.Append(" target=\"");
    EscapeTo(sb, linkMacro.Window);
    sb.Append('"');
}
if (linkMacro.Role is not null)
{
    sb.Append(" class=\"");
    EscapeTo(sb, linkMacro.Role);
    sb.Append('"');
}
```

### Parsing location

In `InlineParser`, where `link:` macros are parsed. The `:linkattrs:` check:

```csharp
if (attributes.ContainsKey("linkattrs"))
{
    // Parse bracket content for named attributes
    ParseLinkAttributes(bracketContent, out label, out window, out role);
}
else
{
    label = bracketContent;
}
```

## 9. :sectanchors: — Anchor Icon Before Section Titles

### Design

In `HtmlSectionRenderer.RenderSection`, after the opening `<hN>` tag:

```csharp
if (section.Id is not null && state.DocumentAttributes.ContainsKey("sectanchors"))
{
    sb.Append("<a class=\"anchor\" href=\"#");
    EscapeTo(sb, section.Id);
    sb.Append("\"></a>");
}
```

This inserts an invisible anchor link before the heading content. CSS can style it
to show a link icon (e.g., `#` or paragraph symbol).

### Interaction with :sectlinks:

Both can be active simultaneously. Asciidoctor supports this:
- `:sectanchors:` adds the anchor icon before the title
- `:sectlinks:` wraps the title text in a self-link

Order: anchor icon first, then title content (which may be self-linked).

## 10. :sectlinks: — Self-Linking Section Titles

### Design

In `HtmlSectionRenderer.RenderSection`, wrap the heading content:

```csharp
if (section.Id is not null && state.DocumentAttributes.ContainsKey("sectlinks"))
{
    sb.Append("<a class=\"link\" href=\"#");
    EscapeTo(sb, section.Id);
    sb.Append("\">");
}

if (prefix is not null) sb.Append(prefix);
RenderInlines(sb, section.TitleInlines, section.Title, footnotes, state);

if (section.Id is not null && state.DocumentAttributes.ContainsKey("sectlinks"))
{
    sb.Append("</a>");
}
```

### Result

```html
<h2 id="my-section"><a class="link" href="#my-section">My Section</a></h2>
```

## 11. :hide-uri-scheme: — URI Scheme Stripping

### Design

In `HtmlInlineRenderer`, the `LinkInlineNode` case:

```csharp
case LinkInlineNode link:
    sb.Append("<a class=\"bare\" href=\"");
    EscapeTo(sb, link.Url);
    sb.Append("\">");
    var displayUrl = state.DocumentAttributes.ContainsKey("hide-uri-scheme")
        ? StripUriScheme(link.Url)
        : link.Url;
    EscapeTo(sb, displayUrl);
    sb.Append("</a>");
    break;
```

### StripUriScheme helper

```csharp
private static string StripUriScheme(string url)
{
    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return url[8..];
    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        return url[7..];
    return url;
}
```

### Scope

Only affects **bare auto-detected URLs** (`LinkInlineNode`).
`link:` macros with explicit labels are not affected (the label is custom text).
`link:` macros that are bare (label = url) should also be affected.

## 12. :webfonts: — Google Fonts Link Injection

### Design

In `AppendDocumentPrologue`, after Font Awesome injection:

```csharp
if (document.Attributes.TryGetValue("webfonts", out var webfontsUrl)
    && webfontsUrl.Length > 0)
{
    sb.Append("<link rel=\"stylesheet\" href=\"");
    EscapeTo(sb, webfontsUrl);
    sb.Append("\">\n");
}
else if (!document.Attributes.ContainsKey("webfonts!"))
{
    // Default: no font loading (unlike Asciidoctor which loads Open Sans + Noto Serif)
    // AdocNet does not load fonts by default — opt-in via :webfonts: URL
}
```

### Behavior

- `:webfonts: https://fonts.googleapis.com/css?family=Open+Sans` → inject that URL
- `:webfonts:` (empty) → use Asciidoctor default font URL
- `:!webfonts:` (unset) → skip font loading
- Not set at all → skip font loading (AdocNet default is no external fonts)

### Default font URL

When `:webfonts:` is set but empty, use:
`https://fonts.googleapis.com/css?family=Open+Sans:300,300italic,400,400italic,600,600italic%7CNoto+Serif:400,400italic,700,700italic%7CDroid+Sans+Mono:400,700`

This matches Asciidoctor's default.

## 13. :last-update-label: — Footer Label Customization

### Design

Used by the footer rendering (see section 5 above). The attribute controls the
label text shown before the last-updated timestamp.

```csharp
var lastUpdateLabel = document.Attributes.TryGetValue("last-update-label", out var lul)
    ? lul : "Last updated";
```

### Scope

Only relevant when footer is rendered (full document mode + `:nofooter:` not set).
Since AdocNet doesn't include timestamps for determinism, the label appears alone
unless external tools add timestamp information.

## 14. Regression Test Plan

### Before modifying BlockParser.cs

Lock existing delimiter parsing behavior:
- Test that `----` still produces a Listing block
- Test that `====` still produces an Example block
- Test that `....` still produces a Literal block
- Test that existing [source] + ---- produces a Source block with language
- Test that `--` open block delimiter still works

### Before modifying HtmlSectionRenderer.cs

Lock current heading output:
- Test that section headings produce correct `<hN>` tags
- Test that section IDs are correctly emitted
- Test that section numbering prefixes are correct

### Before modifying HtmlInlineRenderer.cs

Lock current bare URL rendering:
- Test that bare URLs produce `<a class="bare" href="url">url</a>`
- Test that `link:url[label]` produces correct output

### Before modifying HtmlDocumentRenderer.cs

Lock current prologue/epilogue output:
- Test full-document mode produces correct `<head>` structure
- Test that docinfo injection works correctly

### Before modifying HtmlBlockRenderer.cs

Lock source block rendering:
- Test source blocks with language produce correct class/data-lang
- Test source blocks without language produce bare `<code>`

## 15. Explicit Non-Goals

### NOT in scope for beta.19

1. **Markdown tables** — pipe tables already work. GitHub-flavored Markdown table
   syntax (different header separator) is not in scope.
2. **Markdown images** — `![alt](url)` syntax. AsciiDoc `image::` macro covers this.
3. **Full CommonMark compliance** — we add `#` headings, `>` blockquotes, and
   `` ``` `` fenced code blocks. That's the Asciidoctor-compatible Markdown subset.
4. **Part numbering** — Full `:doctype: book` part/chapter numbering (Part I, Part II).
   We add the `Style` property and section style recognition, but defer the complex
   section-level renumbering.
5. **Footer timestamps** — No `DateTime.Now` in output (determinism constraint).
   Footer shows the label but no timestamp.
6. **Custom TOC rendering** — `toc::[]` places the standard TOC at a custom position.
   Custom TOC templates or transformations are not in scope.
7. **Link attribute security** — `:linkattrs:` enables `window=_blank` but we do NOT
   add `rel="noopener"` automatically. That's a user/template responsibility.
8. **Sect-level wrapper divs** — Asciidoctor wraps sections in `<div class="sect1">`.
   AdocNet does not emit these wrappers. This is existing behavior, not changing.

## 16. Implementation Phases

### P02 — Parser features (fenced code blocks, book doctype styles, toc::[] macro)

Files modified:
- `src/AdocNet.Ast/SectionNode.cs` — add Style property
- `src/AdocNet.Ast/AstNodeKind.cs` — add TocPlaceholder
- `src/AdocNet.Ast/TocPlaceholderNode.cs` — new file
- `src/AdocNet.Parser/BlockParser.cs` — fenced code detection, section styles, toc::[] macro
- Tests: fenced code, book doctype, toc::[] macro

### P03 — Rendering Attributes I (showtitle, nofooter, nofootnotes, source-language, linkattrs)

Files modified:
- `src/AdocNet.Ast/InlineLinkMacroNode.cs` — add Window, Role properties
- `src/AdocNet.Parser/InlineParser.cs` — linkattrs parsing
- `src/AdocNet.Converters.Html/HtmlRenderer.cs` — showtitle guard
- `src/AdocNet.Converters.Html/HtmlDocumentRenderer.cs` — footer div, nofooter
- `src/AdocNet.Converters.Html/HtmlBlockRenderer.cs` — source-language fallback
- `src/AdocNet.Converters.Html/HtmlInlineRenderer.cs` — linkattrs rendering
- Tests: 5 rendering attributes

### P04 — Rendering Attributes II (sectanchors, sectlinks, hide-uri-scheme, webfonts, last-update-label)

Files modified:
- `src/AdocNet.Converters.Html/HtmlSectionRenderer.cs` — sectanchors, sectlinks
- `src/AdocNet.Converters.Html/HtmlInlineRenderer.cs` — hide-uri-scheme
- `src/AdocNet.Converters.Html/HtmlDocumentRenderer.cs` — webfonts, last-update-label
- Tests: 5 rendering attributes

## 17. File Change Summary

| File | Changes |
|------|---------|
| `src/AdocNet.Ast/SectionNode.cs` | Add `Style` property, update GetProperties/MixAdditionalState |
| `src/AdocNet.Ast/AstNodeKind.cs` | Add `TocPlaceholder` value |
| `src/AdocNet.Ast/TocPlaceholderNode.cs` | New file |
| `src/AdocNet.Ast/InlineLinkMacroNode.cs` | Add `Window`, `Role` properties |
| `src/AdocNet.Parser/BlockParser.cs` | Fenced code blocks, section styles, toc::[] |
| `src/AdocNet.Parser/InlineParser.cs` | linkattrs parsing |
| `src/AdocNet.Converters.Html/HtmlRenderer.cs` | showtitle guard, nofootnotes guard |
| `src/AdocNet.Converters.Html/HtmlSectionRenderer.cs` | sectanchors, sectlinks |
| `src/AdocNet.Converters.Html/HtmlInlineRenderer.cs` | hide-uri-scheme, linkattrs rendering |
| `src/AdocNet.Converters.Html/HtmlBlockRenderer.cs` | source-language fallback |
| `src/AdocNet.Converters.Html/HtmlDocumentRenderer.cs` | footer, nofooter, webfonts, last-update-label |
