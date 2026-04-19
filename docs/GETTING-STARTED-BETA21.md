# AdocNet v1.0.0-beta.21 — Getting Started

## Prerequisites

- Beta.20 must be merged and stable
- Create a branch: `git checkout -b beta21`

## What Beta.21 Adds — Drop-in Asciidoctor Compatibility (4 Features)

1. **`:skip-front-matter:`** — strips YAML front matter (`---` delimited) from document start
2. **`:stylesheet:` / `:linkcss:` / `:stylesdir:`** — document-attribute-driven CSS management
3. **`$$...$$` stem delimiters** — alternative math block/inline delimiters (when `:stem:` set)
4. **`:max-include-depth:` attribute** — document-level include depth control (capped by API max)

After this release, AdocNet is a **complete drop-in replacement** for Asciidoctor's core processor.

## Phase Sequence

```
/b21-p00         Context Discovery           (7 criteria)
/b21-p01         Design Document             (8 criteria)
/b21-p02         Front Matter + CSS          (13 criteria) <- preprocessor + renderer
/b21-p03         $$ Delimiters + Depth       (16 criteria) <- HIGH, collision risk
/b21-p04         Differential Test Fixtures  (13 criteria) <- HIGH, Asciidoctor comparison
/b21-reflect     Self-Reflection
/b21-check-a     System Integrity            (22 criteria) <- GATE
/b21-p05         Documentation               (6 criteria)
/b21-check-c     Final Validation            (38 criteria) <- GATE
```

## Critical Safety Notes

### $$ Delimiter Collision Risk
The `$$` stem delimiter is ONLY active when `:stem:` attribute is set. Without it, `$$` is
literal text. P03 Step 0 mandates **4 critical regression tests** verifying that `$$50`,
`$$100 per unit`, etc. remain literal text when `:stem:` is absent. This is the highest-risk
change in the release — the reflect step specifically re-verifies $$ literal safety.

### CSS Precedence
API `HtmlRenderOptions.CustomCss` always takes precedence over `:stylesheet:` document attribute.
Programmatic control wins over document attributes — existing API consumers are not surprised.

### Include Depth Safety
`:max-include-depth:` can only LOWER depth from the API-set maximum, never raise it.
`min(API_max, attribute_value)`. A malicious document cannot escalate recursion.

## Tips

- P02 is straightforward: front matter is pure preprocessing, CSS attributes are renderer-only.
- P03 is the high-risk phase: $$ parsing touches both BlockParser and InlineParser.
- The 9 regression tests in P03 Step 0 are the safety net — do NOT skip them.
- Minimum 25 new tests expected (10 regression + 15 feature).
