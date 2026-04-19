# Beta.21 Design Document — Drop-in Asciidoctor Compatibility

## 1. Front Matter Stripping

### Activation
Front matter stripping is activated when `:skip-front-matter:` is present in
`ParseOptions.Attributes` (API-provided attributes). The attribute value is ignored —
its presence alone enables the feature (flag-style attribute).

### Detection Algorithm
YAML front matter is recognized when:
1. The **first line** of the source text is exactly `---` (three hyphens, no leading/trailing
   whitespace except optional trailing newline)
2. A subsequent line is exactly `---` (closing fence)
3. Everything between these two lines is the front matter content

If the first line is NOT `---`, there is no front matter — skip entirely.
If the opening `---` is found but no closing `---` exists, treat the entire
remainder as front matter (Asciidoctor behavior) — but emit a warning diagnostic.

### Extraction and Storage
When `:skip-front-matter:` is set and front matter is detected:
1. Strip the opening `---`, the content, and the closing `---` from the source text
2. Store the content (without the `---` fences) as `:front-matter:` document attribute
3. The attribute is injected into the document's attribute map so it's accessible via
   `DocumentNode.Attributes["front-matter"]`

### Pipeline Location
**Step 0** in `AdocParser.Parse()` — before include expansion (Step 1).

```
Step 0: Front matter stripping  ← NEW
Step 1: Include expansion
Step 2: Conditional preprocessing
Step 3: Block + inline parsing
```

Implementation in `AdocParser.Parse()`:
```csharp
// ── Front matter stripping (step 0) ──
if (options.Attributes?.ContainsKey("skip-front-matter") == true)
{
    var (stripped, frontMatter) = StripFrontMatter(sourceText);
    sourceText = stripped;
    if (frontMatter is not null)
        frontMatterContent = frontMatter;
}
```

After parsing, inject `:front-matter:` into the document attributes if content was found.

### New Method: StripFrontMatter
Location: `AdocParser.cs` (private static method).

```csharp
private static (string Text, string? FrontMatter) StripFrontMatter(string text)
```

Returns the text with front matter removed, plus the extracted front matter content
(null if no front matter found).

### Edge Cases
- Empty front matter (`---\n---`): valid, stores empty string
- Front matter with only whitespace: valid, stores the whitespace
- No closing `---`: strip to end, emit Warning diagnostic
- `:skip-front-matter:` not set: do nothing, `---` lines are literal text
- Document starts with blank lines then `---`: NOT front matter (must be line 1)

## 2. CSS Attributes (:stylesheet:, :linkcss:, :stylesdir:)

### Current Behavior (baseline)
In `HtmlDocumentRenderer.AppendDocumentPrologue()`:
- Theme CSS resolved via `HtmlThemeCss.GetCss(options.Theme)`
- Both theme CSS and `options.CustomCss` embedded inline in `<style>` block
- No document-attribute-driven CSS behavior

### New Behavior

#### Precedence Rules (Decision D2)
When determining CSS content:
1. **`HtmlRenderOptions.CustomCss` non-null** → use it, ignore `:stylesheet:`
2. **`:stylesheet:` document attribute set** → use the named stylesheet file
3. **Neither** → use built-in theme CSS (current behavior)

When `:stylesheet:` is empty string (`""`) — Asciidoctor treats this as "no stylesheet",
suppress all CSS output (no `<style>` block, no `<link>` tag).

#### Delivery Mechanism: `:linkcss:` attribute
- **`:linkcss:` NOT set** (default): embed CSS inline in `<style>` block (current behavior)
- **`:linkcss:` IS set**: emit `<link rel="stylesheet" href="...">` instead

#### Path Resolution: `:stylesdir:` attribute
When `:linkcss:` is set and a stylesheet filename is determined:
- If `:stylesdir:` is set: `href = "{stylesdir}/{filename}"`
- If `:stylesdir:` is not set: `href = "./{filename}"`
- If `:stylesheet:` is an absolute URL (starts with `http://` or `https://`): use as-is

#### When `:linkcss:` is set but no `:stylesheet:`:
Use the theme name as filename: `asciidoctor.css` for Default theme, skip for None theme.

### Implementation in AppendDocumentPrologue

Replace the current CSS block (lines 26-35) with:

```csharp
// Determine CSS source
string? cssContent = null;
string? cssFilename = null;
bool suppressCss = false;

if (options.CustomCss is not null)
{
    cssContent = options.CustomCss;  // API wins
}
else if (document.Attributes.TryGetValue("stylesheet", out var stylesheetVal))
{
    if (stylesheetVal.Length == 0)
        suppressCss = true;  // empty = no CSS
    else
        cssFilename = stylesheetVal;
}

if (!suppressCss)
{
    var themeCss = HtmlThemeCss.GetCss(options.Theme);
    bool useLink = document.Attributes.ContainsKey("linkcss");

    if (useLink)
    {
        // Link mode: <link rel="stylesheet" href="...">
        var href = ResolveStylesheetHref(cssFilename, document.Attributes);
        if (href is not null)
        {
            sb.Append("<link rel=\"stylesheet\" href=\"");
            EscapeTo(sb, href);
            sb.Append("\">\n");
        }
    }
    else
    {
        // Embed mode: <style>...</style>
        var embedContent = cssContent ?? themeCss;
        if (embedContent is not null)
        {
            sb.Append("<style>\n");
            sb.Append(embedContent).Append('\n');
            sb.Append("</style>\n");
        }
    }
}
```

### Helper: ResolveStylesheetHref
```csharp
private static string? ResolveStylesheetHref(
    string? filename,
    IReadOnlyDictionary<string, string> attributes)
```
- If `filename` starts with `http://` or `https://`: return as-is
- Get `stylesdir` from attributes (default: `.`)
- Return `$"{stylesdir}/{filename ?? "asciidoctor.css"}"`

### Edge Cases
- `:stylesheet:` with absolute URL + `:linkcss:`: use URL directly
- `:stylesheet:` without `:linkcss:`: currently we can't read the file to embed —
  fall back to theme CSS with a warning (reading arbitrary files from renderer is unsafe)
- `:linkcss:` without `:stylesheet:`: link to default `asciidoctor.css`

## 3. $$ Stem Delimiters

### Scoping Rule (Decision D3) — CRITICAL
`$$` as a stem delimiter is **ONLY active when `:stem:` document attribute is set**.
Without `:stem:`, `$$` is literal text — two dollar signs. This is the most
important invariant in beta.21.

### Block Form: `$$` on its own line

#### Detection
A line consisting of exactly `$$` (two dollar signs, no other content except
optional trailing whitespace) opens or closes a latexmath stem block.

#### Behavior
- `$$` on its own line → open a stem block (like `[latexmath]\n--`)
- Content lines collected until next `$$` line
- Closing `$$` → close the stem block
- Result: `StemBlockNode { StemType = "latexmath", Content = collected_content }`

#### Implementation Location
In `BlockParser.Parse()` main loop, add a check after the existing delimiter checks.
Since `$$` is only 2 characters, it cannot use `IsDelimiterLine()` (which requires 4+).
Add a dedicated check:

```csharp
// $$ stem block delimiter (only when :stem: is set)
if (IsStemDelimiterLine(line) && document.Attributes.ContainsKey("stem"))
{
    // Handle $$ block open/close
}
```

New helper:
```csharp
private static bool IsStemDelimiterLine(string line)
{
    var trimmed = line.TrimEnd();
    return trimmed == "$$";
}
```

#### State Tracking
Use `inStemBlock` boolean + `stemBlockLines` list (similar to how open blocks track content).
When `$$` opens: set `inStemBlock = true`, start collecting.
When `$$` closes: create `StemBlockNode`, reset state.

### Inline Form: `$$formula$$`

#### Detection
Within flowing text, `$$` opens an inline stem expression. The next `$$` closes it.
Content between the two `$$` pairs becomes a `StemInlineNode`.

#### Behavior
- `$$E=mc^2$$` → `StemInlineNode { Content = "E=mc^2", StemType = "latexmath" }`
- `$$` must appear as a pair. Unclosed `$$` at end of line → literal text
- Empty `$$$$` → valid but empty StemInlineNode (matches Asciidoctor)

#### Implementation Location
In `InlineParser`, add detection in the character scan loop. When current char is `$`
and next char is `$`, and `:stem:` attribute is set:
1. Mark position after opening `$$`
2. Scan for closing `$$`
3. If found: create `StemInlineNode`, advance past closing `$$`
4. If not found: emit literal `$$`, continue

```csharp
// $$ inline stem (only when :stem: is set)
if (pos < text.Length - 1 && text[pos] == '$' && text[pos + 1] == '$'
    && attributes?.ContainsKey("stem") == true)
{
    if (TryParseDollarStem(text, pos, out var stemNode, out var endPos))
    {
        // emit stemNode
        pos = endPos;
        continue;
    }
}
```

#### Nesting Rules
- `$$` inside `$$` is not supported (no nesting)
- `$$` inside backtick/monospace: literal (backtick wins — already handled)
- `$$` inside passthrough: literal (passthrough wins — already handled)
- `\$$` (backslash-escaped): literal `$$`, not a delimiter

### Interaction with Existing Stem Syntax
- `stem:[formula]` — existing macro syntax, unchanged
- `latexmath:[formula]` — existing macro syntax, unchanged
- `[stem]\n--\ncontent\n--` — existing block syntax, unchanged
- `$$formula$$` — NEW inline syntax, equivalent to `latexmath:[formula]`
- `$$\ncontent\n$$` — NEW block syntax, equivalent to `[latexmath]\n--\ncontent\n--`

All forms produce the same AST nodes. The renderer doesn't care about source syntax.

### Collision Avoidance
The scoping rule prevents collision:
- Document without `:stem:` → `$$` is literal `$$` (e.g., dollar amounts "$$50")
- Document with `:stem:` → `$$` is a stem delimiter
- Users who need literal `$$` in a STEM document can use `\$$` (backslash escape)
  or `pass:[$$]` (passthrough)

## 4. :max-include-depth: Document Attribute

### Where to Read
In `AdocParser.Parse()`, check `options.Attributes` for `:max-include-depth:` before
calling `IncludeExpander.Expand()`.

The attribute could also appear in the document text itself (`:max-include-depth: 5`),
but since include expansion happens first, a document-level attribute definition
would be too late to affect include expansion. Therefore, only API-provided attributes
(`ParseOptions.Attributes`) and pre-scanned attributes in `IncludeExpander.BuildAttributeMap()`
are relevant.

### Capping Rule (Decision D4)
```
effective_depth = min(ParseOptions.IncludeMaxDepth, document_attribute_value)
```

The document attribute can only **lower** the depth, never raise it above the API max.
This prevents a malicious document from increasing recursion beyond the caller's intent.

### Implementation

#### Option A: In AdocParser.Parse() (preferred)
Before calling `IncludeExpander.Expand()`:
```csharp
var effectiveMaxDepth = options.IncludeMaxDepth;
if (options.Attributes?.TryGetValue("max-include-depth", out var midStr) == true
    && int.TryParse(midStr, out var midVal) && midVal >= 0)
{
    effectiveMaxDepth = Math.Min(effectiveMaxDepth, midVal);
}
```
Pass `effectiveMaxDepth` instead of `options.IncludeMaxDepth` to `Expand()`.

#### Option B: In IncludeExpander.BuildAttributeMap()
After building the attribute map, check for `max-include-depth` and cap the depth.
This is more self-contained but less visible.

**Chosen: Option A** — simpler, more explicit, and the cap is visible at the call site.

### Invalid Value Handling
- Non-numeric value: ignore attribute, use API default
- Negative value: ignore attribute, use API default
- Zero: valid — disables all includes (depth 0 = no expansion)
- Value > API max: capped to API max (the min() rule handles this)
- Attribute not present: use API default (10)

### Edge Cases
- `:max-include-depth: 0` → no includes expanded at all
- `:max-include-depth: 100` with API max 10 → effective 10
- `:max-include-depth: abc` → ignored, API default used
- `:max-include-depth: -1` → ignored, API default used

## 5. Regression Test Plan

### Critical Regression: $$ as Literal Text
This is the highest-priority regression test. Currently `$$` in document text
is treated as literal characters. After beta.21, `$$` becomes a stem delimiter
ONLY when `:stem:` is set. Documents WITHOUT `:stem:` must preserve `$$` as literal.

#### Tests to add BEFORE modifying parser:
1. `$$` in paragraph text without `:stem:` → literal `$$` in output
2. `$$50` in paragraph without `:stem:` → literal `$$50`
3. `They paid $$100 for it` → preserved as-is
4. `$$` on its own line without `:stem:` → literal `$$` paragraph
5. Multiple `$$` in text without `:stem:` → all literal

#### Tests to add AFTER implementing:
6. `$$` with `:stem:` → stem delimiter behavior
7. `$$formula$$` with `:stem:` → StemInlineNode
8. `$$` block with `:stem:` → StemBlockNode
9. `\$$` with `:stem:` → escaped, literal `$$`
10. Mixed: `stem:[x]` and `$$y$$` in same document

### Front Matter Regression
Before modifying AdocParser:
1. Document without `---` on line 1 → parsed normally
2. Document starting with `= Title` → no front matter detected
3. Document with `---` in middle (not line 1) → not front matter

### CSS Embedding Regression
Before modifying HtmlDocumentRenderer:
1. Theme CSS embedded in `<style>` → preserved
2. CustomCss appended after theme → preserved
3. No theme, no CustomCss → no `<style>` block

### Include Max-Depth Regression
Before modifying include path:
1. Default max depth (10) → works for 10 levels
2. Custom API max depth → enforced
3. Depth exceeded → diagnostic + directive preserved

### Test Count Target
- At least 10 regression tests (locking existing behavior)
- At least 15 new feature tests
- Total: 25+ new tests across all 4 features

## 6. Explicit Non-Goals

The following are explicitly OUT OF SCOPE for beta.21:

1. **`:compat-mode:` attribute** — Asciidoctor's legacy compatibility mode for AsciiDoc
   Python syntax. Not relevant for a new implementation.

2. **Markdown tables** — GFM-style `| col | col |` tables without `|===` delimiters.
   AdocNet supports AsciiDoc tables with `|===`. Markdown tables are a separate parser
   feature with significant complexity.

3. **Full CommonMark compatibility** — AdocNet is an AsciiDoc processor, not a Markdown
   processor. The `#` headings and `>` blockquotes (beta.18) are the extent of
   Markdown-compatible syntax.

4. **YAML front matter parsing** — Beta.21 strips front matter and stores it as a raw
   string. It does NOT parse the YAML content. Consumers who need parsed YAML can
   use a YAML library on the `:front-matter:` attribute value.

5. **Custom stylesheet file reading** — When `:stylesheet: custom.css` is set without
   `:linkcss:`, the renderer would need to read the file to embed its content. This is
   not supported — in embed mode, only theme CSS and API-provided `CustomCss` are embedded.
   `:stylesheet:` is primarily useful with `:linkcss:` (link mode).

6. **`:copycss:` attribute** — Asciidoctor's attribute to copy the stylesheet to the
   output directory. This is a file I/O operation outside renderer scope.

7. **MathML output** — Asciidoctor can output MathML directly. AdocNet delegates to
   MathJax (client-side rendering). No server-side MathML generation.

8. **`$` single-dollar inline math** — Some LaTeX-adjacent tools use single `$` for
   inline math. Asciidoctor does NOT support this (only `$$`). AdocNet follows
   Asciidoctor behavior.

## 7. File Change Summary

| File | Change Type | Description |
|------|-------------|-------------|
| `src/AdocNet.Parser/AdocParser.cs` | Modify | Add front matter stripping (Step 0), max-include-depth cap |
| `src/AdocNet.Parser/BlockParser.cs` | Modify | Add `$$` block delimiter detection |
| `src/AdocNet.Parser/InlineParser.cs` | Modify | Add `$$` inline delimiter detection |
| `src/AdocNet.Converters.Html/HtmlDocumentRenderer.cs` | Modify | CSS attribute handling |
| `tests/AdocNet.Tests/Parser/FrontMatterTests.cs` | New | Front matter stripping tests |
| `tests/AdocNet.Tests/Parser/StemDelimiterTests.cs` | New | $$ delimiter tests |
| `tests/AdocNet.Tests/Rendering/CssAttributeTests.cs` | New | CSS attribute tests |
| `tests/AdocNet.Tests/Parser/MaxIncludeDepthTests.cs` | New | Max-depth attribute tests |
| `tests/AdocNet.Tests/Parser/StemDelimiterRegressionTests.cs` | New | $$ literal regression tests |

## 8. Implementation Phase Mapping

| Feature | Phase | Estimated Tests |
|---------|-------|-----------------|
| Front matter stripping | P02 | 8-10 |
| CSS attributes | P02 | 8-10 |
| $$ block delimiters | P03 | 8-10 |
| $$ inline delimiters | P03 | 6-8 |
| :max-include-depth: | P03 | 5-6 |
| Differential test fixtures | P04 | 10+ golden files |
