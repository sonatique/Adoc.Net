# AdocNet v1.0.0-beta.17 — Getting Started

## Prerequisites

- Beta.16 must be merged and stable
- Create a branch: `git checkout -b beta17`

## What Beta.17 Adds — Asciidoctor Parity II (3 Features)

1. **Man page converter** — new `AdocNet.Converters.Man` project, roff output
2. **Converter templates** — `INodeTemplate` for per-node HTML customization
3. **Reveal.js slides** — new `AdocNet.Converters.Revealjs` project

## Phase Sequence

```
/b17-p00         Context Discovery           (7 criteria)
/b17-p01         Design Document             (7 criteria)
/b17-p02         Man Page Converter          (13 criteria) <- HIGH, new project
/b17-p03         HtmlRenderer extraction + Converter Templates  (21 criteria) <- HIGH
/b17-p04         Reveal.js Slides            (13 criteria) <- HIGH, new project
/b17-reflect     Self-Reflection
/b17-check-a     System Integrity            (11 criteria) <- GATE
/b17-p05         Documentation               (6 criteria)
/b17-check-c     Final Validation            (27 criteria) <- GATE
```

## Tips

- **P03 is the big one this release**: HtmlRenderer is 2097 lines at the start. P03 Step 0
  is a mandatory blocking gate that extracts it into 8 helper classes, targeting < 500 lines
  for the coordinator. Only after extraction completes do templates get added. This is
  non-negotiable — template hooks touch every node-rendering method, so clean separation
  must come first.
- P02 and P04 each create a new converter project + a CLI tool project (Cli.Man, Cli.Revealjs)
- Reveal.js: sections map to slides (level-1 = horizontal, level-2 = vertical)
- **Regression tests are mandatory** when touching existing code — see engineering principles
  and the project rules file. For the HtmlRenderer extraction, add 2+ golden-output regression
  tests per extracted helper BEFORE starting that extraction.
