# AdocNet v1.0.0-beta.16 — Getting Started

## Prerequisites

- Beta.15 must be merged and stable
- Create a branch: `git checkout -b beta16`

## What Beta.16 Adds — Asciidoctor Parity I (6 Features)

1. **Collapsible blocks** — `[%collapsible]` → `<details><summary>` in HTML
2. **Data URI embedding** — `:data-uri:` → base64 inline images
3. **Font Awesome CSS** — `icons=font` → FA stylesheet in HTML `<head>`
4. **Docinfo injection** — docinfo.html files injected in head/body
5. **Safe modes** — UNSAFE/SAFE/SERVER/SECURE controlling includes + attributes
6. **STEM/Math** — stem blocks, latexmath/asciimath inline, MathJax injection

## Phase Sequence

```
/b16-p00         Context Discovery           (7 criteria)
/b16-p01         Design Document             (8 criteria) <- HIGH, 6 features
/b16-p02         Collapsible + Data URI + FA (9 criteria) <- 3 quick wins
/b16-p03         Docinfo + Safe Modes        (10 criteria) <- HIGH
/b16-p04         STEM/Math (MathJax)         (10 criteria) <- HIGH, new AST node
/b16-reflect     Self-Reflection
/b16-check-a     System Integrity            (14 criteria) <- GATE
/b16-p05         Documentation               (6 criteria)
/b16-check-c     Final Validation            (22 criteria) <- GATE
```

## Tips

- P02 bundles 3 small features — commit after each step
- P04 adds a new AST node (StemBlockNode) — ensure StructuralHash works with it
- HtmlRenderer is 1978 lines — reflect step checks if it grew past 2200
- Safe mode SECURE must truly block ALL includes
