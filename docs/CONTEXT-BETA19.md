# Beta.19 Context Discovery

> Generated from codebase at commit c2a4bd7 (beta.18).

## 1. BlockParser — TryGetDelimiterKind() and IsDelimiterLine()

### TryGetDelimiterKind (line 2735)

Maps delimiter characters to `DelimitedBlockKind`:

```csharp
private static bool TryGetDelimiterKind(string line, out char delimChar, out DelimitedBlockKind kind)
{
    if (IsDelimiterLine(line, '.')) { delimChar = '.'; kind = Literal;      return true; }
    if (IsDelimiterLine(line, '-')) { delimChar = '-'; kind = Listing;      return true; }
    if (IsDelimiterLine(line, '=')) { delimChar = '='; kind = Example;      return true; }
    if (IsDelimiterLine(line, '_')) { delimChar = '_'; kind = Quote;        return true; }
    if (IsDelimiterLine(line, '*')) { delimChar = '*'; kind = Sidebar;      return true; }
    if (IsDelimiterLine(line, '+')) { delimChar = '+'; kind = Passthrough;  return true; }
    // ... returns false otherwise
}
```

**No backtick (`` ` ``) delimiter exists.** Fenced code blocks must be added here.

### IsDelimiterLine (line 2748)

```csharp
private static bool IsDelimiterLine(string line, char ch)
{
    int len = TextUtility.TrimmedEndLength(line);
    if (len < 4) return false;
    for (int i = 0; i < len; i++)
        if (line[i] != ch) return false;
    return true;
}
```

Requires **4+ consecutive characters**. For backtick fenced code blocks, we need a
separate check (3+ backticks, optionally followed by a language identifier).

### IsOpenBlockDelimiter (line 2771)

Two-dash `--` open block delimiter handled separately. Relevant pattern for
fenced code block detection — needs its own helper method.

### Hook point for fenced code blocks

At line 1478, `TryGetDelimiterKind` is called in the main parse loop. A new check
for backtick fenced lines should be inserted **before** or **after** this call.
The fenced block needs special handling because:
- The opening delimiter can carry a language identifier (`` ```java ``)
- The closing delimiter is just `` ``` `` (3+ backticks, no language)
- The result is a `DelimitedBlockNode` with `BlockKind = Source`

## 2. Block Macro Parsing Path

### TryParseBlockMacro (line 3658)

Parses `name::target[content]` pattern. Currently supports:
- `image::target[alt]`
- `video::target[attrs]`
- `audio::target[attrs]`

For `toc::[]`:
- The `name` would be `"toc"`, target empty, brackets empty
- A new branch in `TryParseBlockMacro` or at the call site (line 986) would
  detect `toc::[]` and produce a placeholder node

### TOC Generation (line 2578)

Currently, TOC is generated **after** the main parse loop:
```csharp
if (document.Attributes.ContainsKey("toc"))
{
    var tocValue = document.Attributes["toc"];
    var placement = tocValue switch { ... };
    // ... build entries, insert at position 0
    document.InsertChild(0, tocNode);
}
```

When `:toc: macro` is set, placement is `TocPlacement.Macro`. Currently the TOC
is **always inserted at position 0** regardless of placement. For `toc::[]` macro
support, the code should:
1. During parsing: record the position where `toc::[]` was found (insert a placeholder)
2. After parsing: when placement is `Macro`, insert TocNode at that position
   instead of position 0

### TocPlacement enum

```csharp
public enum TocPlacement { Auto, Left, Right, Preamble, Macro }
```

`Macro` already exists. The infrastructure is ready — only the parser-side
placement and post-parse insertion logic need updating.

## 3. SectionNode — No Style Property

**Confirmed.** `SectionNode` (src/AdocNet.Ast/SectionNode.cs) has:
- `Level` (int, required)
- `Title` (string, required)
- `IsDiscrete` (bool)
- `SectnumsEnabled` (bool?)
- `TitleInlines` (IReadOnlyList<InlineNode>)

**No `Style` property.** Beta.19 needs to add `string? Style { get; init; }` for
book doctype section styles (`appendix`, `glossary`, `colophon`, `dedication`, `preface`).

## 4. HTML Prologue/Epilogue Flow

### AppendDocumentPrologue (HtmlDocumentRenderer.cs:11)

Builds `<head>` with:
1. DOCTYPE, html, meta tags
2. `<title>` from options or document
3. Theme CSS (embedded `<style>`)
4. Font Awesome CDN link (when `:icons: font`)
5. MathJax script (when `:stem:` set)
6. ExtraHead content
7. Docinfo header injection

**No `:webfonts:` check** — no Google Fonts loading currently.
**No `:showtitle:` check** — title rendering is in `RenderDocumentBody`.

### AppendDocumentEpilogue (HtmlDocumentRenderer.cs:76)

Builds:
1. Docinfo footer injection
2. `</body></html>`

**No footer div** — no `<div id="footer">` with "Last updated" info.
**No `:nofooter:` check** — no footer exists to suppress.
**No `:last-update-label:` handling** — no footer label rendered.

### RenderDocumentBody (HtmlRenderer.cs:195)

```csharp
if (document.Title is not null)
{
    sb.Append("<h1>");
    EscapeTo(sb, document.Title);
    sb.Append("</h1>\n");
}
```

Title is **always rendered** when present, regardless of FullDocument mode.
For `:showtitle:` support:
- In embedded mode (`FullDocument = false`): suppress title by default,
  render only when `:showtitle:` is set
- Current behavior: always renders → this is the Asciidoctor full-document behavior

### RenderFootnotesSection (HtmlRenderer.cs:254)

```csharp
if (footnotes.Footnotes.Count == 0) return;
sb.Append("<div id=\"footnotes\">\n");
sb.Append("<hr>\n");
// ... render each footnote
sb.Append("</div>\n");
```

**No `:nofootnotes:` check.** Footnotes are always rendered when present.
Hook point: add attribute check at the beginning of this method.

## 5. Section Heading Rendering

### RenderSection (HtmlSectionRenderer.cs:11)

```csharp
sb.Append('<');
sb.Append(tag);  // h2-h6
if (section.Id is not null)
{
    sb.Append(" id=\"");
    EscapeTo(sb, section.Id);
    sb.Append('"');
}
sb.Append('>');
if (prefix is not null) sb.Append(prefix);  // section numbering
RenderInlines(sb, section.TitleInlines, section.Title, footnotes, state);
sb.Append("</"); sb.Append(tag); sb.Append(">\n");
```

**No `:sectanchors:` support** — no `<a class="anchor" href="#id"></a>` before heading.
**No `:sectlinks:` support** — heading text is not wrapped in `<a class="link" href="#id">`.

Hook points:
- `:sectanchors:` → insert anchor element after `<hN id="...">` and before prefix/inlines
- `:sectlinks:` → wrap the heading content in `<a class="link" href="#id">...</a>`

## 6. Bare URL and Link Macro Rendering

### LinkInlineNode (bare URL) — HtmlInlineRenderer.cs:111

```csharp
case LinkInlineNode link:
    sb.Append("<a class=\"bare\" href=\"");
    EscapeTo(sb, link.Url);
    sb.Append("\">");
    EscapeTo(sb, link.Url);  // Full URL including scheme
    sb.Append("</a>");
    break;
```

**Always displays full URL including scheme** (e.g., `https://example.com`).
For `:hide-uri-scheme:`: strip `http://` or `https://` from the **display text** only,
keep full URL in `href`. Hook point is the second `EscapeTo(sb, link.Url)` call.

### InlineLinkMacroNode (link: macro) — HtmlInlineRenderer.cs:119

```csharp
case InlineLinkMacroNode linkMacro:
{
    bool isBare = string.IsNullOrEmpty(linkMacro.Label) ||
                  linkMacro.Label == linkMacro.Url;
    sb.Append("<a");
    if (isBare) sb.Append(" class=\"bare\"");
    sb.Append(" href=\"");
    EscapeTo(sb, linkMacro.Url);
    sb.Append("\">");
    EscapeTo(sb, isBare ? linkMacro.Url : linkMacro.Label);
    sb.Append("</a>");
    break;
}
```

**No attribute parsing from bracket content.** `link:url[text, window=_blank]`
treats everything as label text. For `:linkattrs:` support:
- Parse bracket content for `window=`, `role=`, etc.
- Add `target="_blank"` when `window=_blank` is found
- This could be done in the InlineParser or in the renderer

### InlineLinkMacroNode AST

```csharp
public sealed class InlineLinkMacroNode : InlineNode
{
    public required string Url { get; init; }
    public required string Label { get; init; }
}
```

**No attributes dictionary.** For `:linkattrs:` support, either:
- Add an optional `Attributes` dictionary to the node, or
- Parse and apply attributes in the renderer (simpler, less AST change)

## 7. Source Block Language Handling

### HtmlBlockRenderer.cs:137 — Source block rendering

```csharp
case DelimitedBlockKind.Source:
    sb.Append("<pre class=\"highlight\"><code");
    if (block.Language is not null)
    {
        sb.Append(" class=\"language-");
        EscapeTo(sb, block.Language);
        sb.Append("\" data-lang=\"");
        EscapeTo(sb, block.Language);
        sb.Append('"');
    }
    sb.Append('>');
```

**No `:source-language:` fallback.** When `block.Language` is null, no language class
is emitted. For `:source-language:` support:
- Check `state.DocumentAttributes["source-language"]` when `block.Language` is null
- Use the document-level default as fallback
- Hook point: after `if (block.Language is not null)`, add `else if` for attribute fallback

## 8. Test Coverage Assessment

- **Total tests**: ~2330 (from `--list-tests`)
- **Relevant test files**:
  - `MarkdownCompatTests.cs` — tests for `#` headings and `>` blockquotes (beta.18)
  - `QuoteBlockTests.cs` — quote block tests
  - `Beta16ParityTests.cs` — beta.16 parity features
  - `HtmlRendererTests.cs` — comprehensive HTML rendering tests
  - `BlockParserTests.cs` — parser tests
- **No existing tests** for any beta.19 features (fenced code blocks, book doctype,
  toc::[] macro, or any of the 10 rendering attributes)
- **No existing tests** reference `:showtitle:`, `:nofooter:`, `:sectanchors:`,
  `:sectlinks:`, `:hide-uri-scheme:`, `:source-language:`, `:linkattrs:`,
  `:webfonts:`, `:last-update-label:`, or `:nofootnotes:`

## 9. Key Types Summary

| Type | Location | Relevant Properties |
|------|----------|-------------------|
| `SectionNode` | AdocNet.Ast | Level, Title, IsDiscrete, SectnumsEnabled, TitleInlines — **no Style** |
| `DelimitedBlockNode` | AdocNet.Ast | BlockKind, Content, Title, Language, Style, IsCollapsible, Callouts |
| `DelimitedBlockKind` | AdocNet.Ast | Literal, Listing, Source, Example, Quote, Sidebar, Passthrough, Open, Verse |
| `TocNode` | AdocNet.Ast | Placement, Entries |
| `TocPlacement` | AdocNet.Ast | Auto, Left, Right, Preamble, **Macro** |
| `LinkInlineNode` | AdocNet.Ast | Url — bare auto-detected URL |
| `InlineLinkMacroNode` | AdocNet.Ast | Url, Label — explicit `link:` macro |
| `HtmlRenderOptions` | Html converter | Theme, FullDocument, Title, ExtraHead, CustomCss, EnableSyntaxHighlighting, EnableIncrementalMarkers, BaseDirectory, Templates |
| `HtmlRenderState` | Html converter | IdTitles, TitleIds, DocumentAttributes, DataUriEnabled, BaseDirectory, ImagesDir + counters |

## 10. Implementation Hook Points Summary

| Feature | File | Line | Hook |
|---------|------|------|------|
| Fenced code blocks | BlockParser.cs | ~1478 | New check before/after TryGetDelimiterKind |
| Book doctype | BlockParser.cs | section parsing | Check `:doctype:` attribute, set Style on SectionNode |
| toc::[] macro | BlockParser.cs | ~986 | Add to TryParseBlockMacro; fix post-parse insertion at ~2607 |
| :showtitle: | HtmlRenderer.cs | ~199 | Guard title rendering with attribute check |
| :nofooter: | HtmlDocumentRenderer.cs | ~76 | Guard footer div (when added) |
| :nofootnotes: | HtmlRenderer.cs | ~256 | Guard RenderFootnotesSection with attribute check |
| :source-language: | HtmlBlockRenderer.cs | ~143 | Fallback when block.Language is null |
| :linkattrs: | HtmlInlineRenderer.cs | ~119 | Parse bracket attrs on InlineLinkMacroNode |
| :sectanchors: | HtmlSectionRenderer.cs | ~27 | Insert anchor before heading content |
| :sectlinks: | HtmlSectionRenderer.cs | ~37 | Wrap heading content in self-link |
| :hide-uri-scheme: | HtmlInlineRenderer.cs | ~115 | Strip scheme from display text |
| :webfonts: | HtmlDocumentRenderer.cs | ~60 | Add Google Fonts link in prologue |
| :last-update-label: | HtmlDocumentRenderer.cs | ~76 | Customize footer label text |
