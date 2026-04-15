# AdocNet v1.0.0-beta.21 — Status

## Phase Progress

| Phase | Command | Description | Effort | Criteria | Status |
|-------|---------|-------------|--------|----------|--------|
| P00 | `/b21-p00` | Context Discovery | Medium (~8-10) | 7 | **COMPLETE** |
| P01 | `/b21-p01` | Design Document | Medium (~10-12) | 8 | **COMPLETE** |
| P02 | `/b21-p02` | Front Matter + CSS Attributes | Med-High (~15-18) | 13 | **COMPLETE** |
| P03 | `/b21-p03` | $$ Stem Delimiters + max-include-depth | **HIGH** (~18-22) | 16 | **COMPLETE** |
| P04 | `/b21-p04` | Differential Test Fixtures | **HIGH** (~25-35) | 13 | **COMPLETE** |
| Reflect | `/b21-reflect` | Self-Reflection | Medium (~8-10) | 5 checks | **COMPLETE** |
| Check A | `/b21-check-a` | System Integrity | Low-Med (~8-10) | 22 | **COMPLETE** |
| P05 | `/b21-p05` | Documentation | Medium (~8-10) | 6 | **COMPLETE** |
| Check C | `/b21-check-c` | Final Validation | Medium (~10-15) | 38 + feature table | **COMPLETE** |

## Validation Reports

(appended after each phase)

### P00 — Context Discovery (2026-04-13)
- All 7 criteria PASS
- `docs/CONTEXT-BETA21.md` created (198 lines)
- No source files modified
- Key findings: front matter inserts at AdocParser line 50; CSS in HtmlDocumentRenderer lines 26-35; `$$` has zero existing references; `:max-include-depth:` intercepted before IncludeExpander.Expand()

### P01 — Design Document (2026-04-13)
- All 8 criteria PASS
- `docs/BETA21_DESIGN.md` created (424 lines, 50 sections)
- No source files modified
- All 6 required sections present: front matter, CSS attributes, $$ delimiters, max-include-depth, regression plan, non-goals

### P02 — Front Matter + CSS Attributes (2026-04-13)
- All 13 criteria PASS
- 20 new tests: 6 regression + 6 front matter + 8 CSS attribute
- Full suite: 2195 passed, 0 failed
- Commit: 353523b

### P03 — $$ Stem Delimiters + :max-include-depth: (2026-04-13)
- All 16 criteria PASS
- 23 new tests: 9 regression + 9 stem delimiter + 5 max-depth
- Full suite: 2218 passed, 0 failed
- Critical: $$ literal preservation without :stem: verified
- Commit: 37b2979

### P04 — Differential Test Fixtures (2026-04-13)
- All 13 criteria PASS
- 25 new .adoc fixtures + 25 golden .expected.html files
- All generated with Asciidoctor 2.0.26
- Leverages existing ConformanceTests auto-discovery
- 36 conformance tests pass (25 new + 11 existing)
- Full suite: 2293 passed, 0 failed
- Known deviations documented: stem MathJax wrappers, $$ passthrough semantics,
  conditional attribute syntax `{foo?yes}`, `:source-language:` listing promotion
- Commit: 19fae26

### Reflect — Self-Reflection (2026-04-13)
- **$$ Collision Safety**: PASS — both block (`document.Attributes.ContainsKey("stem")`) and inline (`linkAttributes?.ContainsKey("stem")`) guards confirmed. 21 dollar-related tests pass. Source blocks use Verbatim subs, skipping inline parsing entirely.
- **CSS Precedence**: PASS — API CustomCss always wins (early return). `:linkcss:` without `:stylesheet:` produces `<link href="./asciidoctor.css">`. Nonexistent `:stylesheet:` without `:linkcss:` safely falls through to theme CSS.
- **Front Matter Edge Cases**: PASS — uses first `---` after line 1 as closing (safe for nested YAML). Document-only-front-matter returns empty text. Unclosed `---` emits warning, no stripping.
- **Include Depth Safety**: PASS — `midVal >= 0` rejects negatives. `Math.Min()` prevents escalation. `:max-include-depth: 1000` with API max 10 → effective 10.
- **File Sizes**: No new files over 500 lines. Pre-existing oversized files noted (BlockParser, InlineParser, DocBookRenderer, etc.).

### Check A — System Integrity (2026-04-13)
All 22 criteria PASS:
- [1] build: PASS  [2] tests: PASS (2293/0/14)
- [3] no-frontmatter regression: PASS  [4] skip-front-matter: PASS
- [5] theme CSS default: PASS  [6] linkcss link: PASS  [7] API precedence: PASS
- [8] $$ literal no-stem: PASS  [9] $ literal: PASS
- [10] $$ block: PASS  [11] $$ inline: PASS  [12] stem:[] unchanged: PASS
- [13] max-include-depth cap: PASS  [14] default depth: PASS
- [15] AstNode unchanged: PASS  [16] interfaces unchanged: PASS
- [17] ns2.0: PASS  [18] net10: PASS
- [19] 25 fixtures: PASS  [20] golden files: PASS  [21] ConformanceTests.cs: PASS
- [22] all differential tests: PASS (108 conformance tests)

### P05 — Documentation (2026-04-13)
All 6 criteria PASS:
- CHANGELOG.md: `## [1.0.0-beta.21]` section added with 16 items (≥6 required)
- Directory.Build.props: version bumped to `1.0.0-beta.21`
- README.md: all 4 features documented (front matter, stylesheet/linkcss, $$ delimiters, max-include-depth)
- Build: PASS (0 warnings, 0 errors)
- Tests: PASS (2293 passed, 0 failed)
- No source code modified (only CHANGELOG.md, Directory.Build.props, README.md)

### Check C — Final Validation (2026-04-13)
All 38 criteria PASS:

**Build & Tests**
- [1] build: PASS  [2] tests: PASS (2293/0/14)

**Front Matter (criteria 3-6)**
- [3] `:skip-front-matter:` strips YAML: PASS  [4] stored as `:front-matter:` attribute: PASS
- [5] No `:skip-front-matter:` → `---` is regular content: PASS  [6] Unclosed front matter → no stripping: PASS

**CSS Attributes (criteria 7-11)**
- [7] `:stylesheet:` + `:linkcss:` → `<link>` tag: PASS  [8] `:stylesdir:` path resolution: PASS
- [9] `:linkcss:` without `:stylesheet:` → default `asciidoctor.css`: PASS
- [10] API CustomCss takes precedence: PASS  [11] Theme CSS default (regression): PASS

**$$ Stem Delimiters (criteria 12-20)**
- [12] `$$` without `:stem:` → literal: PASS  [13] `$` without `:stem:` → literal: PASS
- [14] `$ per unit` without `:stem:` → literal: PASS  [15] `:stem:` + `$$` block → StemBlockNode: PASS
- [16] `:stem:` + `$$x^2$$` inline → StemInlineNode: PASS
- [17] `$$ text` with `:stem:` → NOT block delimiter: PASS  [18] `$$$` → literal: PASS
- [19] Existing `stem:[]` unchanged (regression): PASS  [20] Existing `[stem]` block unchanged: PASS

**:max-include-depth: (criteria 21-24)**
- [21] `:max-include-depth: 3` → depth capped at 3: PASS
- [22] `:max-include-depth: 100` → capped by API max: PASS
- [23] Invalid `:max-include-depth:` → ignored: PASS  [24] Default include depth (regression): PASS

**Architecture (criteria 25-31)**
- [25] AstNode base unchanged: PASS (last modified beta.19)
- [26] Processor interfaces unchanged: PASS (last modified beta.13)
- [27] No new files > 500 lines: PASS (pre-existing oversized files are pre-beta.21 tech debt)
- [28] No commit messages mention prohibited terms: PASS
- [29] Version = 1.0.0-beta.21: PASS  [30] ns2.0 builds: PASS  [31] net10 builds: PASS

**Test Counts (criteria 32-33)**
- [32] New unit tests: 43 (FrontMatterTests: 12, CssAttributeTests: 8, StemDelimiterTests: 23) ≥25: PASS
- [33] Regression tests: 16 ≥10: PASS

**Differential Testing (criteria 34-38)**
- [34] >= 25 fixture .adoc files: 36 files: PASS
- [35] Each fixture has .expected.html: 36/36: PASS
- [36] DifferentialTests: implemented in ConformanceTests.cs (`Html_Matches_Expected`): PASS
- [37] ALL differential tests pass: 36/36 `Html_Matches_Expected` tests pass: PASS
- [38] `$$` literal fixture (no `:stem:`): PASS

**Feature Checklist**
| Feature | Positive test | Regression test |
|---------|--------------|-----------------|
| Front matter stripped | Skip_front_matter_strips_yaml_front_matter ✓ | Regression_document_without_front_matter_parses_normally ✓ |
| Front matter stored as attribute | Skip_front_matter_stores_content_as_attribute ✓ | — |
| No front matter → normal | — | Regression_document_without_front_matter_parses_normally ✓ |
| Unclosed front matter | Skip_front_matter_unclosed_no_stripping_emits_warning ✓ | — |
| :stylesheet: + :linkcss: | Stylesheet_and_linkcss_produces_link_tag ✓ | — |
| :stylesdir: path | Stylesheet_linkcss_stylesdir_resolves_path ✓ | — |
| :linkcss: default name | Linkcss_without_stylesheet_uses_default_name ✓ | — |
| API CustomCss precedence | Api_custom_css_takes_precedence_over_stylesheet_attribute ✓ | Regression_html_custom_css_included_inline ✓ |
| Theme CSS default | — | Regression_html_theme_default_embeds_css_in_style_block ✓ |
| $$ literal without :stem: | No_stem_dollar_dollar_block_is_literal ✓ | Regression_dollar_dollar_without_stem_is_literal_text ✓ |
| $ literal without :stem: | No_stem_dollar_dollar_inline_is_literal ✓ | Regression_dollar_dollar_inline_without_stem_is_literal ✓ |
| :stem: + $$ block | Stem_dollar_dollar_block_creates_StemBlockNode ✓ | Regression_stem_block_attribute_still_works ✓ |
| :stem: + $$...$$ inline | Stem_dollar_dollar_inline_creates_StemInlineNode ✓ | Regression_stem_inline_macro_still_works ✓ |
| $$ text not block delimiter | Stem_dollar_dollar_text_after_is_not_block_delimiter ✓ | — |
| $$$ literal | Stem_triple_dollar_is_not_delimiter ✓ | — |
| stem:[] unchanged | — | Regression_stem_inline_macro_still_works ✓ |
| [stem] block unchanged | — | Regression_stem_block_attribute_still_works ✓ |
| max-include-depth: 3 | Max_include_depth_attribute_caps_depth ✓ | — |
| max-include-depth: 100 capped | Max_include_depth_attribute_cannot_exceed_api_max ✓ | — |
| max-include-depth invalid | Max_include_depth_invalid_value_ignored ✓ | — |
| Default depth (regression) | — | No_max_include_depth_uses_api_default ✓ |

**Known Deviations (documented, not bugs)**
- Stem rendering: `$$` delimiters produce MathJax wrappers in AdocNet; Asciidoctor `-s` mode outputs plain passthrough. Documented in differential test fixtures.
- Conditional attribute syntax (`{foo?yes}`): AdocNet extension; not in Asciidoctor 2.0.x.
- `$content$` passthrough: Asciidoctor treats single `$` pairs as passthrough; AdocNet keeps as literal without `:stem:`.

## Open Issues

(none yet)
