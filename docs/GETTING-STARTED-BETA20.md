# AdocNet v1.0.0-beta.20 — Getting Started

## Prerequisites

- Beta.19 must be merged and stable
- Create a branch: `git checkout -b beta20`

## What Beta.20 Adds — Final 3 Parity Items

1. **Conditional attribute substitution** — `{foo?yes}` (if set) / `{foo!no}` (if not set)
2. **Attribute value line continuation** — `:desc: Long \` + next line → multi-line value
3. **Book doctype level-0 part rendering** — `<h1>` with "Part I", "Part II" numbering

After this release, AdocNet has **complete Asciidoctor core processor feature parity**.
No remaining known syntax, parser, or rendering gaps.

## Phase Sequence

```
/b20-p00         Context Discovery           (8 criteria)
/b20-p01         Design Document             (8 criteria)
/b20-p02         Conditional Attributes      (13 criteria) <- ExpandAttributes modification
/b20-p03         Continuation + Parts        (15 criteria) <- BlockParser + SectionRenderer
/b20-reflect     Self-Reflection
/b20-check-a     System Integrity            (18 criteria) <- GATE
/b20-p05         Documentation               (6 criteria)
/b20-check-c     Final Validation            (33 criteria) <- GATE
```

## Tips

- P02 modifies `ExpandAttributes` — a method used by BOTH InlineParser and BlockParser's
  `ExpandAttributeValue`. Changes here have wide blast radius. The 8 regression tests in
  Step 0 are critical — they lock `{name}`, `{counter:x}`, `\{escape}`, and mixed text.
- P03 Step 1 modifies attribute parsing in BlockParser in BOTH header and body states.
  The continuation loop must handle: chained continuation, blank line termination, next
  attribute entry termination.
- P03 Step 3 modifies the level switch in HtmlSectionRenderer — adding `0 => "h1"` is
  the smallest change, but the "Part I" prefix requires a PartCounter on HtmlRenderState.
- Minimum 35 new tests total (8+8 regression from Step 0s, 11 conditional, 7 continuation, 6 parts).
