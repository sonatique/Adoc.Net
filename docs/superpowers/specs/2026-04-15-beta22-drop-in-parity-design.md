# AdocNet v1.0.0-beta.22 Design: Drop-in Parity

## Summary

Close the remaining 11 Asciidoctor spec gaps identified by auditing against the
official AsciiDoc syntax quick reference and document attributes reference.
After beta.22, AdocNet covers 100% of core AsciiDoc syntax and rendering attributes.

Single release. All changes in one beta.

## Gap Inventory

| # | Feature | Severity | Layer |
|---|---------|----------|-------|
| 1 | Image `width`/`height` attributes | Medium | AST + Parser + Renderer |
| 2 | Image `link=` attribute | Medium | AST + Parser + Renderer |
| 3 | `:idprefix:` / `:idseparator:` | Medium | Parser |
| 4 | Image role/positioning CSS | Low | Renderer |
| 5 | `:noheader:` attribute | Low | Renderer |
| 6 | `:reproducible:` attribute | Low | Renderer |
| 7 | `xrefstyle` attribute | Low | Renderer |
| 8 | Source line highlight | Low | AST + Parser + Renderer |
| 9 | `[#id]#text#` inline anchor | Low | AST + Parser + Renderer |
| 10 | Description list multiple terms | Low | AST + Parser + Renderer |
| 11 | Audio width attribute | Low | Renderer |

## Design Decisions

### D1: Image attributes — typed AST properties (not dictionary)

Add `Width`, `Height`, `Link` as `string?` properties to `BlockImageNode` and
`Width`, `Height` to `InlineImageNode`. These are stable, well-known attributes
from the spec. A generic dictionary would lose type safety for no benefit.

### D2: ID prefix/separator — pass attributes dictionary to GenerateSectionId

Change `GenerateSectionId(string title)` to
`GenerateSectionId(string title, IDictionary<string, string> attributes)`.
The method reads `:idprefix:` and `:idseparator:` from the dictionary.
Matches how other attribute-dependent behavior works throughout the parser.

---

## Layer 1: AST Changes

### BlockImageNode

```
+ string? Width { get; init; }
+ string? Height { get; init; }
+ string? Link { get; init; }
```

Update `GetProperties()` to yield Width, Height, Link when non-null.
Update `MixAdditionalState()` accordingly.

### InlineImageNode

```
+ string? Width { get; init; }
+ string? Height { get; init; }
```

Update `GetProperties()` and `MixAdditionalState()`.

### HighlightInlineNode

```
+ string? Id { get; init; }
```

For `[#id]#text#` shorthand anchors. Update `GetProperties()`.

### DescriptionItemNode

```
- string Term { get; init; }
+ IReadOnlyList<string> Terms { get; init; }
```

Breaking change to the node. All consumers (renderer, tests) updated.
Update `GetProperties()` to yield comma-joined terms.
Update `MixAdditionalState()`.

### DelimitedBlockNode

```
+ string? Highlight { get; init; }
```

For `[highlight="1,3,5-7"]` on source blocks. Update `GetProperties()`.

---

## Layer 2: Parser Changes

### 2a. Image attribute parsing

Replace `ParseImageAlt(string bracketContent) -> string` with:

```csharp
internal static ImageAttributes ParseImageAttributes(string bracketContent)
```

`ImageAttributes` is a readonly struct:

```csharp
internal readonly struct ImageAttributes
{
    public string Alt { get; init; }
    public string? Width { get; init; }
    public string? Height { get; init; }
    public string? Link { get; init; }
}
```

Parsing logic:
1. Split bracket content on commas.
2. Separate named attributes (`key=value`) from positional ones.
3. Positional order: alt (1st), width (2nd), height (3rd).
4. Named attributes override positional: `alt=`, `width=`, `height=`, `link=`.
5. Quoted values supported: `link="https://example.org"`.

Both block image (BlockParser line ~4067) and inline image (InlineParser line ~1147)
call `ParseImageAttributes` instead of `ParseImageAlt`.

### 2b. `:idprefix:` / `:idseparator:` in auto-ID generation

Change signature:

```
- internal static string GenerateSectionId(string title)
+ internal static string GenerateSectionId(string title, IDictionary<string, string> attributes)
```

Inside the method:
- Read `idprefix` from attributes. Default: `"_"`. Empty string means no prefix.
- Read `idseparator` from attributes. Default: `"_"`. Empty string means no separator
  (non-alphanumeric chars are dropped instead of replaced).
- Use these values instead of hardcoded `_`.

All call sites pass the document attributes dictionary. Call sites:
- Section heading parsing (multiple locations in BlockParser)
- Discrete heading parsing
- Any other location that calls `GenerateSectionId`

### 2c. `[#id]#text#` inline anchor

In `InlineParser`, where constrained highlight `#text#` is parsed (line ~820):
- Before parsing the highlight, check if the preceding text is `[#someId]`.
- If so, extract `someId`, consume the bracket prefix, and set
  `HighlightInlineNode.Id = someId`.
- The `[.role]#text#` path already exists (line ~656). The `[#id]` path is similar
  but targets the `Id` property instead of `Roles`.
- Combined syntax `[#id.role]#text#` should also work (Asciidoctor supports it).

### 2d. Multiple description list terms

In `BlockParser` description list parsing:
- When a line matches `term::` with no definition text AND the next line also starts
  a description term, push the current term onto a pending terms list.
- When the final term + definition line arrives, create a single `DescriptionItemNode`
  with `Terms = [all accumulated terms]`.
- Single-term items (the common case) create `Terms = [term]`.

### 2e. Source line highlight attribute

In `BlockParser`, when parsing block attributes for source blocks:
- Check for `highlight="..."` named attribute.
- Store the raw string on `DelimitedBlockNode.Highlight`.
- Parsing the range syntax (e.g., `"1,3,5-7"` into a set of line numbers) happens
  in the renderer, not the parser.

---

## Layer 3: Renderer Changes (HTML)

### 3a. Image dimensions and link

**Block image** (`HtmlImageRenderer.RenderBlockImage`):

```html
<!-- Without link -->
<div class="imageblock">
<div class="content">
<img src="photo.jpg" alt="Photo" width="640" height="480">
</div>
</div>

<!-- With link -->
<div class="imageblock">
<div class="content">
<a class="image" href="https://example.org"><img src="photo.jpg" alt="Photo" width="640" height="480"></a>
</div>
</div>
```

- Emit `width="N"` and `height="N"` on `<img>` when properties are set.
- When `Link` is set, wrap `<img>` in `<a class="image" href="...">`.
- Wrap content in `<div class="content">` (Asciidoctor does this).

**Inline image** (`HtmlInlineRenderer`):
- Emit `width` and `height` on `<img>` when set.

### 3b. Image role/positioning CSS

In `HtmlImageRenderer.RenderBlockImage`:
- When `image.Roles` has entries, append them to the wrapper div's class list:
  `<div class="imageblock left">` or `<div class="imageblock text-center">`.
- Use the existing `AppendRoles` helper.

### 3c. `:noheader:` attribute

In `HtmlDocumentRenderer`, when rendering in full-document mode:
- Check `document.Attributes.ContainsKey("noheader")`.
- If set, skip the entire `<div id="header">` section (title, author, revision).

### 3d. `:reproducible:` attribute

In `HtmlDocumentRenderer` footer rendering:
- Check `document.Attributes.ContainsKey("reproducible")`.
- If set, suppress any timestamp content in the footer.
- AdocNet currently doesn't emit timestamps, so this is a guard for correctness
  if footer timestamps are added later. Also suppress `:last-update-label:` output.

### 3e. `xrefstyle` attribute

In `HtmlInlineRenderer`, cross-reference rendering (xref case):
- When the xref has no explicit label AND `:xrefstyle:` is set:
  - `"basic"` — use the section title (current behavior, no change).
  - `"short"` — use "Section N.M" (section number only, requires numbering context).
  - `"full"` — use "Section N.M, "Title"" (number + quoted title).
- Only applies when section numbering is enabled (`:sectnums:`).
- Without `:sectnums:`, all styles fall back to title text.
- Requires passing section numbering state into inline rendering. The `HtmlRenderState`
  already has `IdTitles` — add a `IdNumbers` map (id -> "N.M" string) populated
  during section rendering.

### 3f. Source line highlight

In `HtmlBlockRenderer`, source block rendering:
- If `block.Highlight` is set, parse the range string into a `HashSet<int>` of
  line numbers (e.g., `"1,3,5-7"` -> {1, 3, 5, 6, 7}).
- When rendering source content line-by-line, wrap highlighted lines in
  `<span class="highlight">`.
- If source content is not rendered line-by-line (currently rendered as a single
  escaped block), split by newlines for this case.

### 3g. Audio width

`AudioNode` currently lacks a `Width` property (unlike `VideoNode` which has one).

- Add `string? Width { get; init; }` to `AudioNode` in the AST.
- Update `AudioNode.GetProperties()` to yield Width when non-null.
- In `HtmlImageRenderer.RenderAudio`, emit `width="N"` on `<audio>` tag when set.
- Update audio macro parsing in `BlockParser` to extract width from bracket content.

### 3h. `[#id]#text#` rendering

In `HtmlInlineRenderer`, highlight case:
- If `highlight.Id` is set, emit `id="..."` on the wrapping element
  (`<mark>` or `<span>`).

### 3i. Multiple description list terms

In `HtmlListRenderer.RenderDescriptionList`:
- For each `DescriptionItemNode`, render every term in `Terms` as its own `<dt>`:

```html
<dt class="hdlist1">Term 1</dt>
<dt class="hdlist1">Term 2</dt>
<dd><p>Shared definition</p></dd>
```

---

## Layer 4: Housekeeping

- Bump `Directory.Build.props` version to `1.0.0-beta.22`.
- Update golden fixture files for any affected existing tests.
- Add new test fixtures for each feature.

---

## Testing Strategy

### New tests per feature (minimum 25 total)

| Feature | Test count | What to test |
|---------|-----------|-------------|
| Image width/height | 3 | Positional, named, mixed |
| Image link | 2 | Block image with link, without link |
| idprefix/idseparator | 4 | Custom prefix, empty prefix, custom separator, empty separator |
| Image roles | 2 | Single role, multiple roles |
| :noheader: | 2 | With and without attribute |
| :reproducible: | 1 | Attribute set, no timestamp |
| xrefstyle | 3 | basic, short, full |
| Source highlight | 3 | Single line, range, multiple |
| [#id]#text# | 2 | With id, with id+role |
| Multiple dlist terms | 2 | Two terms, three terms |
| Audio width | 1 | Width attribute rendered |

### Regression tests

- Lock existing image rendering output before changes.
- Lock existing section auto-ID output before changes.
- Lock existing description list output before changes.
- Lock existing highlight inline output before changes.
- Run full 2531-test suite + 240 compatibility tests after all changes.

---

## Implementation Order

1. AST property additions (all nodes)
2. Parser: image attribute parsing
3. Parser: idprefix/idseparator
4. Parser: [#id]#text# anchor
5. Parser: multiple description list terms
6. Parser: source highlight attribute
7. Renderer: image dimensions + link + roles
8. Renderer: noheader, reproducible, xrefstyle
9. Renderer: source highlight, audio width, dlist terms, [#id] highlight
10. Golden files + full test suite validation
