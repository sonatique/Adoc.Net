# Beta.20 Design — Final Asciidoctor Parity

## 1. Conditional Attribute Substitution

### Overview

Add `{foo?yes}` and `{foo!no}` syntax to `InlineParser.ExpandAttributes`.
This is Asciidoctor's inline conditional attribute substitution.

### Syntax

| Pattern | Meaning | Output when `foo` defined | Output when `foo` undefined |
|---------|---------|--------------------------|----------------------------|
| `{foo?yes}` | If-set | `"yes"` | `""` (empty) |
| `{foo!no}` | If-unset | `""` (empty) | `"no"` |
| `{foo?}` | If-set, empty value | `""` | `""` |
| `{foo!}` | If-unset, empty value | `""` | `""` |

### Implementation in ExpandAttributes (InlineParser.cs:846)

Insert a new check **after** counter expansion (line 917) and **before** the
`IsValidAttributeName` + `TryGetValue` lookup (line 919).

```
// Existing: counter expansion check
// NEW: conditional operator check
// Existing: normal attribute lookup
```

Logic:
1. Extract `name` from between `{` and `}` (already done at line 872).
2. Check if `name` contains `?` or `!`.
3. If `?` found at index `qIdx`:
   - `attrName = name[..qIdx]`
   - `valueIfSet = name[(qIdx + 1)..]`
   - Validate `attrName` via `IsValidAttributeName`.
   - If valid and `attributes.ContainsKey(attrName)`: emit `valueIfSet`.
   - If valid and NOT defined: emit nothing (empty string).
   - If `attrName` invalid: fall through (leave `{...}` literal).
4. If `!` found at index `bangIdx`:
   - `attrName = name[..bangIdx]`
   - `valueIfUnset = name[(bangIdx + 1)..]`
   - Validate `attrName` via `IsValidAttributeName`.
   - If valid and `attributes.ContainsKey(attrName)`: emit nothing.
   - If valid and NOT defined: emit `valueIfUnset`.
   - If `attrName` invalid: fall through.
5. Use `IndexOf('?')` and `IndexOf('!')` — check `?` first (Asciidoctor precedence).

### Edge Cases

- `{foo?}` — empty value-if-set: emit empty string when defined, empty when not.
- `{my-attr?yes}` — hyphenated attribute name: valid, `IsValidAttributeName` allows hyphens.
- `{_priv!fallback}` — underscore-prefixed name: valid.
- `{2bad?yes}` — name starting with digit: invalid, falls through to literal.
- `{foo?{bar}}` — nested braces in value: NOT supported. The `}` at position of `bar}`
  closes the outer `{`, so `name` = `foo?{bar`. The `?` split gives attrName=`foo`,
  value=`{bar` — the `{bar` is emitted literally. This matches Asciidoctor behavior
  (no recursive expansion in conditional values).
- `\{foo?yes}` — backslash-escaped: handled by existing escape logic before we reach this code.
- `{counter:x?y}` — counter prefix: handled by counter check first, never reaches conditional.

### IsValidAttributeName — NO CHANGES

The `?` and `!` characters are NOT added to valid attribute name chars.
Instead, the conditional check extracts the operator and validates only the
attribute name portion (before the operator).

## 2. Attribute Value Line Continuation

### Overview

When an attribute value ends with ` \` (space + backslash), the next line is
appended to the value, separated by a space. This continues until a line does
not end with ` \`.

### Asciidoctor Behavior

```asciidoc
:description: This is a \
long description that \
spans three lines
```
Result: `description` = `"This is a long description that spans three lines"`

Rules:
- Continuation marker is ` \` (space followed by backslash) at end of line.
- The ` \` is stripped from the value.
- The next line is trimmed of leading/trailing whitespace and appended with a single space.
- Multiple continuations chain.
- A bare `\` without preceding space is NOT a continuation marker (it's a literal backslash).

### Implementation Location

The continuation logic goes in the **main parse loop** in `BlockParser.Parse()`,
at the points where `TryParseAttribute` succeeds — lines 240-246 (header) and
lines 316-325 (body).

**Approach**: After `TryParseAttribute` returns `value`, check if `value` ends with ` \`.
If so, enter a continuation loop:

```csharp
// After TryParseAttribute succeeds and returns value:
while (value.EndsWith(" \\"))
{
    value = value[..^2]; // strip " \"
    i++;                 // advance to next line
    if (i >= lines.Length)
        break;           // EOF: stop continuation
    var nextLine = lines[i].TrimStart();
    if (nextLine.Length == 0)
        break;           // empty line: stop continuation
    value = value + " " + nextLine;
    lineNumber++;
}
```

### Header State (line 240)

After `TryParseAttribute` succeeds with `allowFlagStyle: true`:
- Apply continuation loop to `value`.
- Then call `ExpandAttributeValue` on the final joined value.
- Then `document.SetAttribute`.

### Body State (line 316)

After `TryParseAttribute` succeeds:
- Apply continuation loop to `value`.
- Then call `ExpandAttributeValue`.
- Then `document.SetAttribute`.

### Shared Helper

Extract a private method to avoid duplication:

```csharp
private static string ApplyLineContinuation(
    string value,
    string[] lines,
    ref int lineIndex,
    ref int lineNumber)
```

Both header and body paths call this helper.

### Edge Cases

- Value ends with `\` (no preceding space): NOT a continuation. Literal backslash preserved.
- Continuation to empty line: stop continuation, value ends with what's accumulated so far.
- Continuation at EOF: stop, value is what's accumulated.
- Multiple continuation lines: chain (3+ lines become one value).
- Continuation line is itself an attribute entry (`:name: value`): treated as plain text
  continuation — continuation does not parse the next line as an attribute.
- Flag-style attribute (`:name`): value is empty string, cannot end with ` \`, so
  continuation never triggers.
- Value is exactly ` \` (space + backslash): continuation triggered, value becomes
  empty + next line content.

### Line Index Management

The main parse loop uses `for (int i = 0; i < lines.Length; i++)` with `lineNumber`
tracked separately. The continuation helper must advance both `i` and `lineNumber`
for each consumed continuation line so the main loop doesn't re-process those lines.

## 3. Level-0 Part Rendering (Book Doctype)

### Overview

When `:doctype: book` is set, level-0 sections (below the document title) are
book **parts**. They should render as `<h1>` with a "Part I", "Part II" prefix
using Roman numerals.

### Level Switch Change (HtmlSectionRenderer.cs:14)

```csharp
var tag = section.Level switch
{
    0 => "h1",   // NEW
    1 => "h2",
    2 => "h3",
    3 => "h4",
    4 => "h5",
    _ => "h6",
};
```

This is unconditional — level 0 always maps to `<h1>` regardless of doctype.
The part prefix is doctype-conditional.

### Part Counter

Add `public int PartCounter { get; set; }` to `HtmlRenderState` (HtmlRenderer.cs:21).

### Part Prefix Logic

In `RenderSection`, after the tag is determined, before the existing appendix check:

```csharp
if (section.Level == 0
    && state.DocumentAttributes.TryGetValue("doctype", out var dt)
    && string.Equals(dt, "book", StringComparison.OrdinalIgnoreCase))
{
    state.PartCounter++;
    sb.Append("Part ");
    sb.Append(ToRoman(state.PartCounter));
    sb.Append(". ");
}
```

### Roman Numeral Conversion

Private static helper method `ToRoman(int number)`:

```csharp
private static string ToRoman(int number)
{
    if (number <= 0) return number.ToString();
    var sb = new StringBuilder();
    ReadOnlySpan<(int value, string numeral)> table =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];
    foreach (var (value, numeral) in table)
    {
        while (number >= value)
        {
            sb.Append(numeral);
            number -= value;
        }
    }
    return sb.ToString();
}
```

### Interaction with Existing Features

- **Appendix counter**: Part prefix and appendix prefix are mutually exclusive.
  A section is either a part (level 0 in book doctype) or an appendix (`[appendix]` style).
  The existing `if (Style == "appendix")` check handles appendix. The new part check
  runs before it (level 0 sections won't have `[appendix]` style).
- **Section numbering**: Parts are not numbered by `secCtx.Advance()`. The part prefix
  replaces section numbering for level-0 sections.
- **:sectanchors: / :sectlinks:**: These work unchanged — they depend on section ID, not level.
- **Article doctype**: Level-0 sections in article doctype get `<h1>` tag but NO part prefix.

## 4. Regression Test Plan

### Before modifying ExpandAttributes (InlineParser.cs)

Lock these existing behaviors:
1. `{name}` with defined attribute → value substituted
2. `{name}` with undefined attribute → literal `{name}` preserved
3. `\{name}` → literal `{name}` (backslash escape)
4. `{counter:x}` → counter increments and outputs value
5. `{counter2:x}` → counter increments, outputs nothing
6. Multiple attributes in one line → all expanded
7. Empty braces `{}` → literal `{}`
8. Name starting with digit `{2ver}` → literal (invalid name)

**Existing coverage**: AttributeTests.cs + InlineParserTests.cs + SubstitutionTests.cs
already cover items 1-8. No new regression tests needed — these exist.

### Before modifying attribute entry parsing (BlockParser.cs)

Lock these existing behaviors:
9. `:name: value` → attribute set to "value"
10. `:!name:` → attribute unset
11. `:name!:` → attribute unset
12. `:name:` (empty value) → attribute set to empty string
13. `:name` (flag-style, header only) → attribute set to empty string
14. Attribute with `{ref}` in value → expanded via ExpandAttributeValue
15. Body attribute at block boundary → parsed
16. Body attribute mid-paragraph → treated as paragraph text

**Existing coverage**: AttributeTests.cs covers 9-14. Need new regression tests for 15-16.

### Before modifying HtmlSectionRenderer

Lock these existing behaviors:
17. Level 1 → `<h2>`
18. Level 2 → `<h3>`
19. Level 3 → `<h4>`
20. `[appendix]` section → "Appendix A: " prefix
21. `:sectanchors:` → anchor before heading
22. `:sectlinks:` → heading wrapped in link
23. Numbered sections → prefix from secCtx

**Existing coverage**: HtmlRendererTests.cs + HtmlRendererRegressionTests.cs + Beta19ParityTests.cs
cover 17-23. No new regression tests needed.

### New regression tests to add (before implementation)

- Body attribute at block boundary is parsed correctly (item 15)
- Body attribute mid-paragraph falls through to text (item 16)

## 5. Explicit Non-Goals

The following are **NOT** in scope for beta.20:

1. **Nested ternary conditionals** — `{foo?{bar?a:b}:c}` is not supported.
   Asciidoctor itself does not support nested conditionals in inline substitution.

2. **Attribute value includes** — `{include::file.adoc[]}` is not supported as an
   inline substitution. Include directives are preprocessor-level, not inline-level.

3. **Part numbering in non-HTML converters** — DocBook, EPUB, PDF, Man, Reveal.js
   converters are NOT modified. Part rendering is HTML-only in beta.20.

4. **Custom part label** — Asciidoctor supports `:part-label:` attribute to customize
   "Part" text. Not in scope — hardcoded to "Part".

5. **Recursive expansion in conditional values** — `{foo?{bar}}` does not expand
   `{bar}` inside the conditional value. The value is emitted literally.

6. **Continuation in attribute unset** — `:!name:` cannot have continuation lines
   (there is no value to continue). This is a no-op.

7. **Complex continuation edge cases** — Continuation across include boundaries,
   continuation inside conditional blocks, etc. are not supported.
