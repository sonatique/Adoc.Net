# Beta.18 Context Discovery — Final Asciidoctor Parity

> Generated from actual codebase at commit 4474b60 (beta.17).

## 1. Heading Detection Logic (BlockParser.cs)

### Current `=` heading detection (lines 327-358)

```csharp
var equalsCount = CountLeadingEquals(line);
if (equalsCount >= 2 && line.Length > equalsCount && line[equalsCount] == ' ')
```

- `CountLeadingEquals()` (line 3052-3057) counts consecutive `=` characters from line start.
- Requires `equalsCount >= 2` (so `==` is minimum — level 1 section).
- Requires `line[equalsCount] == ' '` — a space must follow the `=` characters.
- Title text extracted as `line[(equalsCount + 1)..].Trim()`.
- Level mapping: `equalsCount - 1` (== is level 1, === is level 2, etc.).
- After detection: flushes paragraph, clears list/dl frames, clears all pending block state.
- Supports discrete headings (`[discrete]`) and auto-ID generation.

### Hook point for `#` heading detection

The `#` heading detection should be placed immediately before or after the `=` heading
detection (around line 327). The logic is parallel:
- Count leading `#` characters.
- `#` = level 0 (document title), `##` = level 1, `###` = level 2, etc.
- Require `#` followed by a space (bare `#` without space = not a heading).
- Produces the same `SectionNode` as `=` headings.
- No new AST types needed.

**Key concern**: `#` is also used for inline anchors (`[[#id]]`) and attribute IDs
(`[#myid]`). The parser must only match `#` at the start of a body line, followed by
a space. The existing attribute-line parsing (which runs before heading detection) will
consume `[#id]` lines first, so there's no conflict there. The concern is lines like
`#not-a-heading` (no space) — these must NOT be treated as headings.

### CountLeadingEquals helper (lines 3052-3057)

```csharp
private static int CountLeadingEquals(string line)
{
    int count = 0;
    while (count < line.Length && line[count] == '=') count++;
    return count;
}
```

A parallel `CountLeadingHashes()` helper will be needed.

## 2. Quote Block Parsing (`____` Delimiter)

### Delimited block detection (lines 1376-1450)

The parser detects delimited blocks via `TryGetDelimiterKind()` (line 2623-2634):

```csharp
if (IsDelimiterLine(line, '_')) { delimChar = '_'; kind = DelimitedBlockKind.Quote; return true; }
```

`IsDelimiterLine()` (line 2636-2643) requires 4+ consecutive identical characters:

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

So `____` (4+ underscores) = quote block delimiter.

### Quote block styles

- `[quote]` attribute (line 680-697): sets `hasPendingQuote = true`, parses optional
  attribution and citation from `[quote, Author, Source]` syntax.
- `[verse]` attribute (line 700-718): same pattern, sets `hasPendingVerse = true`.
- When a `____` delimiter is encountered with `hasPendingVerse`, the block is promoted
  to `DelimitedBlockKind.Verse` (line 1410-1411).

### Paragraph-style quote blocks (lines 2301-2340)

When `pendingQuoteAttribution` is set and no `____` delimiter follows, the parser
collects paragraph lines as quote content:

```csharp
if (pendingQuoteAttribution is not null && paragraphLines.Count == 0)
```

This creates a `DelimitedBlockNode` with `BlockKind = Quote` from paragraph text.

### Hook point for `>` blockquote detection

The `>` blockquote detection should be added in the body parsing section, likely
after the section title detection and before or near the paragraph-style quote check.
Key considerations:
- Lines starting with `> ` (greater-than + space) form blockquote content.
- Consecutive `>` lines form a single quote block.
- Blank line or non-`>` line ends the blockquote.
- Output: same `DelimitedBlockNode` with `BlockKind = Quote`.
- Optional: `> -- Author` at end = attribution (Asciidoctor convention).
- This is purely a parser change — produces the same AST as `____` quote blocks.

## 3. DescriptionListNode — No Style Property

**Confirmed**: `DescriptionListNode` (src/AdocNet.Ast/DescriptionListNode.cs) has NO
`Style` property:

```csharp
public sealed class DescriptionListNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.DescriptionList;
}
```

It inherits from `BlockNode` which has `Id`, `Reftext`, `Roles`, `Substitutions` — but
no `Style` field. The `[horizontal]` style attribute is parsed and consumed (line 649-660
in BlockParser.cs) but never stored on the node.

**Beta.18 action**: Add `string? Style { get; init; }` property to `DescriptionListNode`.
This enables both `[qanda]` and `[horizontal]` styles.

## 4. Current Description List Rendering (HtmlListRenderer.cs)

### RenderDescriptionList method (lines 124-182)

Current rendering always produces:
```html
<dl>
  <dt class="hdlist1">Term</dt>
  <dd>
    <p>Description</p>
  </dd>
</dl>
```

Key observations:
- Always emits `<dl>` tag with optional `id` attribute (lines 126-132).
- Always emits `<dt class="hdlist1">` — hardcoded class regardless of style.
- Renders term inlines and description inlines with full inline formatting.
- Supports nested content in `<dd>`: nested description lists, admonitions,
  paragraphs, delimited blocks, and nested lists (lines 150-177).
- Does NOT check for any style (qanda, horizontal) — one rendering path for all.

### Beta.18 changes needed

For `[qanda]` style:
- Render as `<ol class="qanda">` with `<li><p>Question</p><p>Answer</p></li>`.

For `[horizontal]` style:
- Render as `<div class="hdlist"><table>` layout (Asciidoctor format).

Both require checking `DescriptionListNode.Style` (the new property).

## 5. Include Attribute Parsing (IncludeExpander.cs)

### Current bracket attribute parsing (lines 178-217)

```csharp
string? linesValue = null;
string? tagValue = null;
string? tagsValue = null;
int? levelOffset = null;
bool hasUnsupportedAttributes = false;

if (bracketContent.Length > 0)
{
    var attrs = ParseIncludeAttributes(bracketContent);
    if (attrs.TryGetValue("lines", out var lv)) linesValue = lv;
    if (attrs.TryGetValue("tag", out var tv)) tagValue = tv;
    if (attrs.TryGetValue("tags", out var tsv)) tagsValue = tsv;
    if (attrs.TryGetValue("leveloffset", out var lo))
    {
        if (int.TryParse(lo, out var parsed))
            levelOffset = parsed;
    }
}
```

Supported attributes: `lines`, `tag`, `tags`, `leveloffset`.

The unsupported-attribute check (lines 201-208) warns about any attribute key that
is NOT one of these four — meaning `indent=` currently triggers a warning diagnostic.

### Processing order for included content

1. Tag/lines filtering (applied first)
2. `leveloffset` application (line 504-508, via `ApplyLevelOffset()`)
3. Recursive expansion of nested includes

**`indent=` is NOT handled.** Any `indent=N` attribute is flagged as unsupported.

### Beta.18 action

- Parse `indent=N` from bracket attributes.
- `indent=0`: strip all leading whitespace from each line.
- `indent=N` (N > 0): prepend N spaces to each line.
- Apply AFTER tag/lines filtering but BEFORE leveloffset (per Asciidoctor semantics).

## 6. Current Description List Test Coverage

### File: tests/AdocNet.Tests/DescriptionListTests.cs (102 lines)

**Parsing tests** (5 tests):
1. `Simple_description_list` — basic `CPU:: The brain.` parsing
2. `Description_list_items_group_into_single_list` — 3 items in one list
3. `Description_list_followed_by_paragraph` — blank-line separation
4. `Description_list_with_inline_formatting` — `*bold*:: _italic_` parsing
5. `Two_separate_description_lists_with_paragraph_between` — list splitting

**Rendering tests** (2 tests):
6. `Renders_dl_dt_dd` — verifies `<dl>`, `<dt class="hdlist1">`, `<dd>` output
7. `Renders_inline_formatting_in_term_and_description` — inline format in output

**Coverage gaps** (relevant to beta.18):
- No tests for `[horizontal]` style
- No tests for `[qanda]` style (doesn't exist yet)
- No tests for nested description lists in rendering
- No tests for description list with continuation blocks
- No tests for description items with empty description + next-line description

### Other files with description list references

- `tests/AdocNet.Tests/ConformanceTests.cs` — conformance suite
- `tests/AdocNet.Tests/MacroTests.cs` — macro-related tests
- `tests/AdocNet.Tests/PdfRendererTests.cs` — PDF rendering
- `tests/AdocNet.Tests/ManRendererTests.cs` — man page converter
- `tests/AdocNet.Tests/RevealjsRendererTests.cs` — Reveal.js converter
- `tests/AdocNet.Layout.Tests/LayoutBuilderTests.cs` — Avalonia layout
- `tests/fixtures/integration/long-paragraphs.adoc` — integration fixture

## 7. Summary of Changes Needed

| Feature | Files to modify | New types/properties |
|---------|----------------|---------------------|
| `#` headings | BlockParser.cs | None (same SectionNode) |
| `>` blockquotes | BlockParser.cs | None (same DelimitedBlockNode/Quote) |
| Q&A list style | DescriptionListNode.cs, BlockParser.cs, HtmlListRenderer.cs | `Style` property on DescriptionListNode |
| Include `indent=` | IncludeExpander.cs | None |

## 8. Risk Assessment

- **`#` headings**: Low risk. Parallel to `=` headings. Must not break existing `=` heading tests.
- **`>` blockquotes**: Medium risk. New parsing path in the large BlockParser. Must not
  interfere with existing paragraph parsing or `>` characters in other contexts.
- **Q&A style**: Low risk. Additive property on AST node + new rendering branch.
- **`indent=`**: Low risk. Small addition to IncludeExpander. Existing tag/lines tests
  must remain passing.
