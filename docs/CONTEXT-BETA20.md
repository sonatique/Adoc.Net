# Context — Beta.20 (Final Parity)

## 1. InlineParser.ExpandAttributes (InlineParser.cs:846-941)

### Signature
```csharp
internal static string ExpandAttributes(string text, IReadOnlyDictionary<string, string> attributes)
```

### Behavior
1. **Fast path**: if no `{` in text, return unchanged (line 849).
2. Scans character-by-character. On `{`:
   - **Backslash escape** (line 858-866): if preceded by `\`, emit literal `{`, skip expansion.
   - Find matching `}` (line 869). If found with content between:
     - **Counter expansion** (line 874-917): `{counter:name}` / `{counter2:name}` / `{counter:name:seed}`.
       Increments value in mutable dictionary. `counter2` is silent (no output).
     - **Normal lookup** (line 919-928): validates name via `IsValidAttributeName`, then `TryGetValue`.
       If found: emit value. If not: leave `{name}` literal in output.
3. If no substitutions occurred (`segmentStart == 0`), return original string (zero allocation).

### IsValidAttributeName (line 947-961)
- First char: letter or underscore.
- Subsequent chars: letter, digit, underscore, hyphen.
- Does **NOT** allow `?` or `!` — these will fail validation, meaning `{foo?yes}` is currently
  treated as an invalid attribute name and left as literal text.

### Key fact for beta.20
The conditional substitution (`{foo?yes}` / `{foo!no}`) must be checked **before** the
`IsValidAttributeName` + `TryGetValue` path. The name part (before `?`/`!`) must still
pass `IsValidAttributeName`. The operator splits the content between `{` and `}`.

## 2. BlockParser Attribute Entry Parsing

### Header state (BlockParser.cs:226-249)
- Triggered when `line[0] == ':'` during header parsing.
- First tries `TryParseAttributeUnset` (`:!name:` or `:name!:` → remove attribute).
- Then tries `TryParseAttribute` with `allowFlagStyle: true` (header allows `:name` without trailing colon).
- On success: calls `ExpandAttributeValue(value, document.Attributes)` then `document.SetAttribute(name, value)`.
- Respects `lockedAttributes` (CLI-provided attributes that cannot be overridden).
- Malformed attribute in header emits a diagnostic warning.

### Body state (BlockParser.cs:299-328)
- Triggered when `paragraphLines.Count == 0 && line[0] == ':'` (only at block boundaries).
- Same flow: `TryParseAttributeUnset`, then `TryParseAttribute` (without `allowFlagStyle`).
- On success: calls `ExpandAttributeValue`, then `document.SetAttribute`.
- Malformed body attributes silently fall through to paragraph text (no diagnostic).

### TryParseAttribute (BlockParser.cs:3370-3419)
- Expects `:name: value` format (line[0] == ':' guaranteed by caller).
- Finds closing colon at `line.IndexOf(':', 1)`.
- Validates: space required after closing colon if value present (`:name:value` is invalid).
- Flag-style (`allowFlagStyle=true`): `:name` without trailing colon → sets to empty string.
- Returns `name` and `value` (trimmed).

### TryParseAttributeUnset (BlockParser.cs:4399-4425)
- Two forms: `:!name:` and `:name!:`.
- Both require trailing colon, optional trailing whitespace.

### ExpandAttributeValue (BlockParser.cs:4484-4489)
- Simple wrapper: delegates to `InlineParser.ExpandAttributes(value, attributes)`.
- Fast path if no attributes or no `{` in value.

### No line continuation
- Each attribute entry is parsed from a **single line**.
- No handling of ` \` (space + backslash) at end of line to continue on the next line.
- Beta.20 must add this: strip ` \`, read next line, append with space separator.
- Applies in **both** header and body attribute parsing states.

## 3. HtmlSectionRenderer.RenderSection (HtmlSectionRenderer.cs:11-73)

### Level switch (line 14-21)
```csharp
var tag = section.Level switch
{
    1 => "h2",
    2 => "h3",
    3 => "h4",
    4 => "h5",
    _ => "h6",  // ← Level 0 falls through here!
};
```
**Confirmed: level 0 → `h6`** via the `_` wildcard case. This is wrong for book parts.

### Appendix counter (line 54-60)
- When `section.Style == "appendix"`: emits "Appendix A: ", "Appendix B: ", etc.
- Uses `state.AppendixCounter++` (char arithmetic starting from 'A').

### Section numbering (line 23-24)
- `SectnumsEnabled` checked per-section, falls back to `secCtx.Enabled`.
- `secCtx.Advance(section.Level)` returns "1. ", "1.1. " etc. prefix.

### :sectanchors: (line 37-43)
- When section has an ID and `:sectanchors:` attribute set.
- Emits `<a class="anchor" href="#id"></a>` before heading content.

### :sectlinks: (line 45-67)
- When section has an ID and `:sectlinks:` attribute set.
- Wraps heading content in `<a class="link" href="#id">...</a>`.

### Beta.20 change needed
- Add `0 => "h1"` to the level switch.
- When `:doctype: book` and level == 0: add "Part N" prefix with Roman numeral numbering.
- Similar to the appendix counter pattern (use `state` for a part counter).

## 4. Existing Test Coverage

### Attribute expansion tests
- **AttributeTests.cs**: 15+ tests covering:
  - `{name}` substitution in paragraphs, section titles, list items, table cells
  - Multiple attributes in one line
  - Backslash-escaped `\{name}` → literal
  - Undefined attribute → literal `{name}` preserved
  - Flag-style attributes, body attributes, locked attributes
  - Nested braces, unclosed braces, closing brace without opening
  - Name with digits valid, name starting with digit invalid
  - Attribute value not recursively expanded
- **InlineParserTests.cs**: counter expansion tests (`{counter:num}`, `{counter2:hidden}`, letter seed)
- **SubstitutionTests.cs**: attribute expansion with invalid names (`{123}`, `{no space}`)
- **Integration fixture**: `conditionals-and-attributes.adoc` — basic substitution + conditionals
- **No tests for `{foo?yes}` or `{foo!no}`** — this is the new feature.
- **No tests for attribute value line continuation** — this is the new feature.

### Section rendering tests
- **HtmlRendererTests.cs**: Level 1→h2, Level 2→h3, Level 3→h4, section with children
- **HtmlRendererRegressionTests.cs**: Level 1→h2, numbered sections with prefix
- **Beta19ParityTests.cs**: appendix style, showtitle, sectanchors, sectlinks
- **Integration fixtures**: large-document with h3/h4/h5 headings
- **No tests for level 0 rendering** — this is the new feature (book parts).
- **No tests for "Part I", "Part II" numbering**.

## 5. Files to Modify (beta.20)

| File | Change |
|------|--------|
| `src/AdocNet.Parser/InlineParser.cs` | Add `?`/`!` conditional operator parsing in `ExpandAttributes` |
| `src/AdocNet.Parser/BlockParser.cs` | Add ` \` line continuation in attribute entry parsing (header + body) |
| `src/AdocNet.Converters.Html/HtmlSectionRenderer.cs` | Add `0 => "h1"`, book part rendering with Roman numerals |

## 6. Risk Areas

- **ExpandAttributes is called from both InlineParser (inline text) and BlockParser (attribute values)**.
  Changes must be backward-compatible: existing `{name}` behavior must not change.
- **Line continuation in BlockParser** touches both header and body states — must handle edge cases
  (continuation at end of file, continuation to empty line, continuation to attribute unset line).
- **Part counter** needs a new field on `HtmlRenderState` (like `AppendixCounter`).
