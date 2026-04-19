# AdocNet v1.0.0-beta.18 — Getting Started

## Prerequisites

- Beta.17 must be merged and stable
- Create a branch: `git checkout -b beta18`

## What Beta.18 Adds — Final Asciidoctor Parity (4 Features)

1. **Markdown-compatible headings** — `#`, `##`, `###` through `######` parsed as section titles
2. **Markdown-compatible blockquotes** — `> ` prefix lines parsed as quote blocks
3. **Q&A description list style** — `[qanda]` renders as numbered Q&A list + `[horizontal]` fix
4. **Include indent= attribute** — `indent=N` prepends/strips whitespace on included content

After this release, AdocNet has full Asciidoctor core processor feature parity.

## Phase Sequence

```
/b18-p00         Context Discovery           (8 criteria)
/b18-p01         Design Document             (9 criteria)
/b18-p02         Markdown Headings + Quotes  (16 criteria) <- HIGH, parser modifications
/b18-p03         Q&A Lists + indent=         (16 criteria) <- AST + renderer + include
/b18-reflect     Self-Reflection
/b18-check-a     System Integrity            (15 criteria) <- GATE
/b18-p05         Documentation               (6 criteria)
/b18-check-c     Final Validation            (30 criteria) <- GATE
```

## Tips

- P02 and P03 each have a **mandatory Step 0** adding regression tests BEFORE modifications.
  This is non-negotiable — see engineering principles.
- P02 modifies BlockParser (4685 lines) — the riskiest file in the project. Step 0 locks
  existing heading and quote parsing behavior before any changes.
- `#` headings produce the same SectionNode as `=` headings — the AST doesn't change.
- `>` blockquotes produce the same Quote DelimitedBlockNode as `[quote]` blocks.
- DescriptionListNode gains a `Style` property — this is the only AST change in beta.18.
- Include `indent=` is applied AFTER tag/lines filtering but BEFORE leveloffset.
- Minimum 24 new tests expected (9 regression + 15+ feature tests).
- BlockParser is known tech debt at 4685+ lines — Check C exempts it from the 500-line rule.
