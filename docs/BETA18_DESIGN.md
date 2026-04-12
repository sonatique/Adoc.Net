# Beta.18 Design Document — Final Asciidoctor Parity

> 4 features: Markdown headings, Markdown blockquotes, Q&A list style, include indent=

## 1. Markdown-Compatible Headings

### Syntax

```
# Document Title          → level 0 (equivalent to = Title)
## Section Level 1        → level 1 (equivalent to == Section)
### Section Level 2       → level 2 (equivalent to === Section)
#### Section Level 3      → level 3 (equivalent to ==== Section)
##### Section Level 4     → level 4 (equivalent to ===== Section)
###### Section Level 5    → level 5 (equivalent to ====== Section)
```

### Level mapping

| Markdown | Asciidoc | Level |
|----------|----------|-------|
| `#`      | `=`      | 0 (document title) |
| `##`     | `==`     | 1 |
| `###`    | `===`    | 2 |
| `####`   | `====`   | 3 |
| `#####`  | `=====`  | 4 |
| `######` | `======` | 5 |

Level = hashCount - 1 (parallel to equalsCount - 1 for `=` headings).

### Detection logic

```
CountLeadingHashes(line) → int
if (hashCount >= 1 && line.Length > hashCount && line[hashCount] == ' ')
    → heading detected
```

New helper `CountLeadingHashes()` parallels existing `CountLeadingEquals()`.

### Integration points

**Header state (document title)**:
- Current `IsDocTitle()` checks `line[0] == '=' && line[1] == ' '`.
- Add parallel check: `line[0] == '#' && line[1] == ' '` for single `#` document title.
- This enables `# My Document Title` in header state.

**Body state (section headings)**:
- After the existing `equalsCount` check (line 327-358), add a parallel `hashCount` check.
- Same code path: flush paragraph, clear pending state, create SectionNode.
- `hashCount >= 2` for body sections (## = level 1, ### = level 2, etc.).
- Single `#` in body = level 0, which is document title — should be treated same as `=` in body.

**Helper methods to update**:
- `IsSectionHeader()` (line 4309): add `#` pattern matching alongside `=`.
- `IsDocTitle()` (line 2986): add `#` pattern.

### Safety constraints

- `#` must be followed by a space. `#notheading` is NOT a heading.
- `[#id]` lines (attribute anchors) are parsed before heading detection — no conflict.
- Both `=` and `#` headings coexist. A document can mix them freely.
- No new AST types. Same `SectionNode` output.
- Discrete headings (`[discrete]`) work with `#` headings too.

## 2. Markdown-Compatible Blockquotes

### Syntax

```
> This is a blockquote.
> It can span multiple lines.
>
> Blank lines within the blockquote start new paragraphs.
> -- Author Name
```

### Detection logic

A line starts a blockquote when:
1. First character is `>` AND
2. Second character is ` ` (space) OR line is exactly `>` (empty blockquote line)

Consecutive `> ` lines accumulate into one blockquote.
A blank line (no `>` prefix) or a non-`>` line terminates the blockquote.

Lines within the blockquote that are just `>` (no trailing content) act as
paragraph separators within the blockquote content.

### Attribution detection

If the last non-empty line of the blockquote matches `> -- Author`:
- Strip `-- ` prefix.
- Set as `Attribution` on the `DelimitedBlockNode`.
- Remove the attribution line from the blockquote content.

### AST output

Same `DelimitedBlockNode` with `BlockKind = DelimitedBlockKind.Quote`.
The content is parsed recursively (same as `____` quote blocks with structural parsing).

```csharp
var quoteBlock = new DelimitedBlockNode
{
    BlockKind = DelimitedBlockKind.Quote,
    Attribution = detectedAttribution,  // from "-- Author" line
    Title = pendingBlockTitle,
    // Children populated from recursive parse of stripped content
};
```

### Parser location

Add blockquote detection in the body loop, after section title detection (line ~440)
and before the description list detection. The check runs only when `paragraphLines`
is empty (no pending paragraph text accumulating).

```csharp
// Markdown blockquote: > text
if (paragraphLines.Count == 0 && line.Length >= 2 && line[0] == '>' && line[1] == ' '
    || (paragraphLines.Count == 0 && line == ">"))
{
    // Accumulate lines, strip > prefix, detect attribution, parse recursively
}
```

### Edge cases

- `>` at line start inside a paragraph → NOT a blockquote (paragraphLines.Count > 0).
- `> ` inside a delimited block → handled by the delimiter's own content parsing.
- Empty blockquote: just `>` on a line → treated as empty content.
- Nested `>>` → NOT supported (Asciidoctor doesn't support Markdown-style nesting via `>>`).
  Treat `>>` as content within the outer blockquote.

## 3. Q&A Description List Style

### AST change

Add `Style` property to `DescriptionListNode`:

```csharp
public sealed class DescriptionListNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.DescriptionList;

    /// <summary>
    /// Optional style: "qanda" for Q&amp;A, "horizontal" for horizontal layout.
    /// Null for default description list style.
    /// </summary>
    public string? Style { get; set; }
}
```

Must also override `MixAdditionalState()` and include Style in `GetProperties()` for
structural hashing (beta.15 requirement).

### Parser changes

**`[qanda]` detection**: Add a check parallel to the existing `[horizontal]` check
(line 649-660). When `blockAttrs.Style == "qanda"`, store as pending style.

**Propagation**: When creating a `DescriptionListNode` (line 1746), set
`Style = pendingQandaStyle` or `Style = pendingHorizontalStyle`.

Currently `[horizontal]` is detected (line 649) but the style is consumed and lost.
Beta.18 fixes this by storing both `[qanda]` and `[horizontal]` in a `pendingDlStyle`
variable and applying it when the `DescriptionListNode` is created.

```csharp
string? pendingDlStyle = null;

// In attribute detection:
if (blockAttrs is not null && (
    string.Equals(blockAttrs.Style, "qanda", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(blockAttrs.Style, "horizontal", StringComparison.OrdinalIgnoreCase)))
{
    pendingDlStyle = blockAttrs.Style.ToLowerInvariant();
    // ... existing ID/role handling ...
}

// When creating DescriptionListNode:
dl = new DescriptionListNode();
if (pendingDlStyle is not null)
{
    dl.Style = pendingDlStyle;
    pendingDlStyle = null;
}
```

### HTML rendering

**Default style** (Style == null): current rendering unchanged (`<dl>` / `<dt>` / `<dd>`).

**Q&A style** (Style == "qanda"):
```html
<div class="qlist qanda">
<ol>
<li>
<p><em>Question text?</em></p>
<p>Answer text.</p>
</li>
</ol>
</div>
```

Asciidoctor wraps Q&A in `<div class="qlist qanda">` with an `<ol>`.
Each description item becomes an `<li>` with the term as question (in `<em>`) and
description as answer. Both wrapped in `<p>`.

**Horizontal style** (Style == "horizontal"):
```html
<div class="hdlist">
<table>
<tr>
<td class="hdlist1">Term</td>
<td class="hdlist2"><p>Description</p></td>
</tr>
</table>
</div>
```

Asciidoctor renders horizontal lists as a table within `<div class="hdlist">`.

### Rendering dispatch

In `RenderDescriptionList()`:
```csharp
if (list.Style == "qanda")
    RenderQandaList(sb, list, ...);
else if (list.Style == "horizontal")
    RenderHorizontalList(sb, list, ...);
else
    // existing <dl> rendering
```

## 4. Include `indent=` Attribute

### Attribute parsing

In `IncludeExpander.cs`, add `indent` to the recognized attributes:

```csharp
int? indentValue = null;

if (attrs.TryGetValue("indent", out var iv))
{
    if (int.TryParse(iv, out var parsed) && parsed >= 0)
        indentValue = parsed;
}
```

Update the unsupported-attribute check to also exclude `"indent"`.

### Application logic

New method `ApplyIndent(string content, int indent)`:

```csharp
private static string ApplyIndent(string content, int indent)
{
    var lines = content.Split('\n');
    var sb = new StringBuilder();
    for (int i = 0; i < lines.Length; i++)
    {
        if (i > 0) sb.Append('\n');
        if (indent == 0)
        {
            // Strip all leading whitespace
            sb.Append(lines[i].TrimStart());
        }
        else
        {
            // Prepend N spaces
            sb.Append(' ', indent);
            sb.Append(lines[i]);
        }
    }
    return sb.ToString();
}
```

### Processing order

1. Tag/lines filtering (existing)
2. **indent= application** (NEW — inserted here)
3. leveloffset application (existing)
4. Recursive expansion (existing)

This matches Asciidoctor semantics: indent adjusts the raw included text before
level offset modifies heading levels.

### Both file and URL includes

The indent logic must be applied in both code paths:
- File includes (around line 500-510)
- URL includes (around line 310-315)

In both cases, insert the `ApplyIndent()` call after tag/lines filtering and before
the `ApplyLevelOffset()` call.

### Edge cases

- `indent=0` on a file with no leading whitespace: no change (TrimStart on already-trimmed).
- `indent=0` on a code block included with `tag=`: strips indentation from tagged region.
- Negative indent values: ignored (constraint: `parsed >= 0`).
- Non-numeric indent values: ignored (int.TryParse fails).
- Empty included content: no crash (loop over zero lines).

## 5. Regression Test Plan

### Before modifying BlockParser.cs

Lock existing heading behavior:
- `= Title` document title in header state → still parsed correctly
- `== Section` through `===== Section` → all levels still work
- Discrete headings with `[discrete]` → still work
- Auto-ID generation on headings → still works
- `IsSectionHeader()` returns correct results for existing patterns

Lock existing quote block behavior:
- `[quote]` + `____` delimiter → still produces Quote block
- `[quote, Author, Source]` → attribution/citation still parsed
- `[verse]` + `____` → still produces Verse block
- Paragraph-style quote (`[quote, Author]` + paragraph) → still works

### Before modifying DescriptionListNode.cs

Lock current DescriptionListNode behavior:
- Simple description list parsing → unchanged AST
- Description list rendering → unchanged HTML output (`<dl>/<dt>/<dd>`)
- `[horizontal]` attribute is parsed (consumed) without error

### Before modifying HtmlListRenderer.cs

Lock current description list rendering:
- Default style → `<dl>` with `<dt class="hdlist1">` and `<dd>`
- Nested description lists → rendered correctly
- Description items with inline formatting → rendered correctly
- Description items with continuation blocks → rendered correctly

### Before modifying IncludeExpander.cs

Lock existing include behavior:
- `lines=` filtering → still works
- `tag=` / `tags=` filtering → still works
- `leveloffset=` → still applied correctly
- Unsupported attributes → still emit warning diagnostic
- URL includes with tag/lines → still work

## 6. Explicit Non-Goals

The following Markdown features are **NOT** in scope for beta.18:

1. **Markdown fenced code blocks** (` ``` `) — Asciidoctor already uses `----` / `[source]`
   for code blocks. Markdown fenced code blocks are a separate feature.

2. **Markdown-style links** (`[text](url)`) — Asciidoctor uses `link:url[text]` and
   `url[text]`. Markdown link syntax is incompatible with Asciidoctor's attribute syntax.

3. **Full Markdown compatibility** — Only headings (`#`) and blockquotes (`>`) are
   implemented. These are the two features Asciidoctor officially supports for
   Markdown migration. Other Markdown syntax (emphasis with `_`, lists with `-`, etc.)
   already overlaps with native Asciidoctor syntax.

4. **Nested blockquotes** (`>>`, `>>>`) — Asciidoctor does not support Markdown-style
   nested blockquotes. Nested quoting uses Asciidoctor's native `[quote]` blocks.

5. **Markdown tables** — Asciidoctor has its own table syntax (`|===`) which is more
   capable than Markdown tables. No Markdown table parsing.

6. **ATX heading closing hashes** (`## Title ##`) — Asciidoctor does not support
   trailing `#` characters on Markdown headings. Only leading `#` is recognized.

7. **Markdown setext headings** (underline-style: `Title\n=====`) — Not supported by
   Asciidoctor's Markdown compatibility mode.

8. **Q&A nested content** — Q&A list items will support simple term/description pairs.
   Nested blocks within Q&A items (list continuations, etc.) are rendered but may not
   match Asciidoctor's exact nesting behavior in all edge cases.
