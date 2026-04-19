# AdocNet v1.0.0-beta.19 — Getting Started

## Prerequisites

- Beta.18 must be merged and stable
- Create a branch: `git checkout -b beta19`

## What Beta.19 Adds — Asciidoctor Parity Polish (13 Features)

### Parser features (P02)
1. **Markdown fenced code blocks** — ` ``` ` and ` ```lang ` as listing/source delimiters
2. **Book doctype** — `:doctype: book` with parts, chapters, [appendix]/[glossary]/etc.
3. **toc::[] macro** — place TOC at arbitrary position with `:toc: macro`

### Rendering attributes I (P03)
4. **:showtitle:** — show document title in embedded mode
5. **:nofooter:** — suppress footer div
6. **:nofootnotes:** — suppress footnote section
7. **:source-language:** — default language for source blocks
8. **:linkattrs:** — enable attribute parsing on link macros

### Rendering attributes II (P04)
9. **:sectanchors:** — anchor icon before section titles
10. **:sectlinks:** — self-linking section titles
11. **:hide-uri-scheme:** — strip http:// from displayed URLs
12. **:webfonts:** — Google Fonts link injection
13. **:last-update-label:** — custom footer label

## Phase Sequence

```
/b19-p00         Context Discovery              (9 criteria)
/b19-p01         Design Document                (9 criteria) <- HIGH, 13 features
/b19-p02         Fenced Code + Book + toc::[]   (16 criteria) <- HIGH, parser mods
/b19-p03         Rendering Attrs I              (13 criteria) <- 5 attributes
/b19-p04         Rendering Attrs II             (12 criteria) <- 5 attributes
/b19-reflect     Self-Reflection
/b19-check-a     System Integrity               (22 criteria) <- GATE
/b19-p05         Documentation                  (7 criteria)
/b19-check-c     Final Validation               (34 criteria) <- GATE
```

## Tips

- P02 is the hardest: fenced code blocks hook into the delimiter system, book doctype adds
  part/chapter semantics, toc::[] modifies post-parse TOC insertion logic.
- P03 and P04 are relatively straightforward attribute checks in the HTML renderer.
- Every phase has a mandatory Step 0 with regression tests.
- Minimum 38 new tests expected (12 regression + 26 feature).
- After beta.19, all known Asciidoctor core processor features are covered.
