# Beta.21 Context Discovery — Drop-in Asciidoctor Compatibility

## 1. Preprocessing Pipeline Order (AdocParser.Parse)

The pipeline in `AdocParser.Parse()` (src/AdocNet.Parser/AdocParser.cs:44-93) runs:

```
Step 1: Include expansion   (IncludeExpander.Expand)
Step 2: Conditional preprocessing  (ConditionalPreprocessor.Process)
Step 3: Block + inline parsing     (BlockParser.Parse)
```

**Front matter stripping must be Step 0** — before include expansion.
The raw source text arrives as `string text` at line 50.
Insert point: between `var sourceText = text;` (line 50) and the include
expansion block (line 53). Front matter is stripped from `sourceText` before
anything else sees it.

The `:skip-front-matter:` attribute must be checked in `ParseOptions.Attributes`.
The stripped content is stored as `:front-matter:` document attribute for API access.

YAML front matter format: `---` on line 1, content, `---` on a subsequent line.

## 2. CSS Embedding Logic (HtmlDocumentRenderer)

File: `src/AdocNet.Converters.Html/HtmlDocumentRenderer.cs:11-81`

Current CSS flow in `AppendDocumentPrologue()`:

```
1. Get theme CSS:  HtmlThemeCss.GetCss(options.Theme)
2. If theme or CustomCss non-null:
     <style>
       {themeCss}
       {options.CustomCss}
     </style>
3. Font Awesome link if :icons: font
4. MathJax script if :stem:
5. Google Fonts link if :webfonts:
6. ExtraHead content
7. Docinfo header injection
```

**`:stylesheet:` / `:linkcss:` / `:stylesdir:` hooks:**

Currently all CSS is embedded inline via `<style>`. There is no `:linkcss:` check.

Insert point: Inside the CSS block (lines 26-35). When `:linkcss:` is set,
replace `<style>...</style>` with `<link rel="stylesheet" href="...">`.

Precedence logic (from beta.21 rules D2):
- `HtmlRenderOptions.CustomCss` non-null → takes precedence, `:stylesheet:` ignored
- Only `:stylesheet:` set → use it as CSS source
- Neither → use theme CSS (current behavior)
- `:linkcss:` changes delivery: `<link>` instead of `<style>`
- `:stylesdir:` provides directory prefix for the stylesheet href

`HtmlRenderOptions` (src/AdocNet.Converters.Html/HtmlRenderOptions.cs):
- `CustomCss` (string?) — custom CSS string
- `Theme` (HtmlTheme) — built-in theme enum
- `BaseDirectory` (string?) — for data-uri and docinfo
- No `:stylesheet:` or `:linkcss:` properties — these are document attributes

## 3. Stem Block Parsing (BlockParser)

File: `src/AdocNet.Parser/BlockParser.cs`

### Block-level stem parsing (lines 720-741)
- `pendingStem` (string?) tracks pending stem type
- When `[stem]`, `[latexmath]`, or `[asciimath]` block attribute is parsed:
  - `[stem]` → resolves to document `:stem:` attribute value, defaults to `"latexmath"`
  - `[latexmath]` / `[asciimath]` → uses style name directly
  - Sets `pendingStem` to the resolved stem type
- When the next open block (`--` delimiter) is found (lines 1416-1433):
  - If `pendingStem is not null`: creates `StemBlockNode` with Content and StemType
  - Otherwise: creates normal `DelimitedBlockNode`

### No existing `$$` handling
- Searched entire `src/` tree for `$$` — **zero matches**
- `$$` is not recognized as a delimiter anywhere
- Currently `$$` in document text is treated as literal characters (two dollar signs)

### Where `$$` block delimiter hooks go:
In the main parsing loop, alongside other delimiter detection (e.g., `----`, `====`).
A `$$` line (two dollar signs alone on a line) should open/close a latexmath stem block,
equivalent to `[latexmath]\n--\n...\n--`.

**Critical scoping rule:** `$$` delimiters are ONLY active when `:stem:` attribute is set.
Without `:stem:`, `$$` is literal text.

The check belongs in the delimiter detection area, likely near `IsDelimiterLine()` or
`IsOpenBlockDelimiter()` (line ~4743+). Since `$$` is exactly 2 characters, it needs
its own detection (not via the 4+-char `IsDelimiterLine`).

## 4. Stem Inline Parsing (InlineParser)

File: `src/AdocNet.Parser/InlineParser.cs`

### Current inline stem parsing (lines 1225-1287)
- `StemMacroNames` = `["stem", "latexmath", "asciimath"]` (line 1226)
- Handled in `TryParseMacro()` (line 1276-1279):
  - If macro name is in `StemMacroNames`: creates `StemInlineNode`
  - `stem` → resolves to `"latexmath"` type
  - `latexmath` / `asciimath` → uses name directly
  - Content comes from bracket content: `stem:[E=mc^2]`

### No existing `$$` inline handling
- No `$$..$$` pattern detection in InlineParser

### Where `$$` inline delimiter hooks go:
In the main inline parsing loop, add detection for `$$` as an opening/closing
delimiter pair. When `:stem:` attribute is set:
- `$$formula$$` → `StemInlineNode { Content = "formula", StemType = "latexmath" }`
- Only scan for this when `:stem:` is confirmed set in document attributes

The check should go in the character-by-character scan, similar to how other
inline delimiters (like `pass:[]` or backtick) are detected. Check for two
consecutive `$` characters.

## 5. Include Max-Depth Enforcement (IncludeExpander)

File: `src/AdocNet.Parser/IncludeExpander.cs`

### Current max-depth mechanism
- `IncludeExpander.DefaultMaxDepth = 10` (line 23)
- `Expand()` accepts `int maxDepth` parameter (line 68, 83, 97)
- `AdocParser.Parse()` passes `options.IncludeMaxDepth` (line 57)
- `ParseOptions.IncludeMaxDepth` default = 10 (src/AdocNet.Core/ParseOptions.cs:25)

### Depth enforcement in ExpandRecursive (lines 366-377)
```csharp
if (currentDepth >= maxDepth)
{
    diagnostics.Add(new Diagnostic(...));
    // Emit directive as-is, don't expand
    continue;
}
```

### Where `:max-include-depth:` attribute would be read:
In `AdocParser.Parse()`, after building `condAttrs` (line 66-71) but before
calling `IncludeExpander.Expand()` (line 56-58).

The attribute value from the document must be compared with the API value:
```
effective_depth = min(ParseOptions.IncludeMaxDepth, document_attribute_value)
```

The attribute could be in `options.Attributes` (API-provided) or discovered
during include expansion itself. Since include expansion happens first,
we need to check `options.Attributes` for `:max-include-depth:` before calling
`Expand()`, and pass the effective depth.

Alternatively, inside `IncludeExpander.Expand()` or `BuildAttributeMap()` (line 786),
detect `:max-include-depth:` from the scanned attributes and cap the depth.
The `BuildAttributeMap` already scans document text for attribute definitions —
it could detect this attribute and apply the cap before recursive expansion begins.

## 6. Key Types and Properties

### StemBlockNode (AdocNet.Ast)
- `Content` (string) — the math formula content
- `StemType` (string) — "latexmath" or "asciimath"
- Standard BlockNode properties: Title, Id, Roles, Source

### StemInlineNode (AdocNet.Ast)
- `Content` (string) — inline formula
- `StemType` (string) — "latexmath" or "asciimath"

### ParseOptions (AdocNet.Core)
- `IncludeMaxDepth` (int, default 10)
- `Attributes` (IReadOnlyDictionary<string, string>?)
- `SafeMode` (SafeMode enum)
- `SourceFilePath`, `BaseDirectory`, etc.

### HtmlRenderOptions (AdocNet.Converters.Html)
- `Theme` (HtmlTheme)
- `CustomCss` (string?)
- `FullDocument` (bool)
- `BaseDirectory` (string?)
- `Templates` (IReadOnlyList<INodeTemplate>?)

## 7. Test Counts and Framework

- Framework: NUnit (`[Test]`)
- Test location: `tests/AdocNet.Tests/`
- Current test count: ~1200+ tests
- Differential testing: `AsciidoctorRunner` for golden file comparison (beta.21 requirement)

## 8. Summary of Changes Needed

| Feature | Files Modified | Key Hook Point |
|---------|---------------|----------------|
| Front matter | AdocParser.cs | Before include expansion (line 50) |
| CSS attributes | HtmlDocumentRenderer.cs | CSS embedding block (lines 26-35) |
| `$$` block delimiters | BlockParser.cs | Delimiter detection area |
| `$$` inline delimiters | InlineParser.cs | Inline scan loop |
| `:max-include-depth:` | AdocParser.cs or IncludeExpander.cs | Before/during Expand() call |
