# AdocNet v1.0.0-beta.17 — Status

## Phase Progress

| Phase | Command | Description | Effort | Criteria | Status |
|-------|---------|-------------|--------|----------|--------|
| P00 | `/b17-p00` | Context Discovery | Medium (~10-12) | 7 | COMPLETE |
| P01 | `/b17-p01` | Design Document | **HIGH** (~15-20) | 7 | COMPLETE |
| P02 | `/b17-p02` | Man Page Converter | **HIGH** (~20-25) | 13 | COMPLETE |
| P03 | `/b17-p03` | Extraction + Converter Templates | **HIGH** (~25-35) | 21 | COMPLETE |
| P04 | `/b17-p04` | Reveal.js Slides | **HIGH** (~20-25) | 13 | COMPLETE |
| Check A | `/b17-check-a` | System Integrity | Low-Med (~8-10) | 11 | COMPLETE |
| P05 | `/b17-p05` | Documentation | Medium (~10-15) | 6 | COMPLETE |
| Reflect | `/b17-reflect` | Self-Reflection | Medium (~8-10) | 4 checks | COMPLETE |
| Check C | `/b17-check-c` | Final Validation | Medium (~10-15) | 27 + feature table | COMPLETE |

## P00 — Context Discovery (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `docs/CONTEXT-BETA17.md` exists | PASS |
| 2 | Existing converter project structure documented | PASS |
| 3 | CLI dispatch pattern documented | PASS |
| 4 | roff format basics documented | PASS |
| 5 | reveal.js structure documented | PASS |
| 6 | No source files modified | PASS |
| 7 | Document >= 100 lines (389 lines) | PASS |

### Verdict: PASS

### Key Findings

- Converter projects follow a clean pattern: extend `DocumentRendererBase`, override `Format` and `RenderDocument()`
- CLI tools are one-liners calling `Program.Run(args, OutputFormat.X, "adocnet-x")`
- Format dispatch in `ConvertCommand.RenderOutput()` uses a switch on `OutputFormat` enum
- Template hooks go before the `switch` in `HtmlRenderer.RenderBlock()` (line ~397)
- `INodeTemplate` interface belongs in `AdocNet.Core` (zero deps)
- Man pages use roff format: `.TH`, `.SH`, `\fB`/`\fI` escapes, `.nf`/`.fi` for code
- Reveal.js uses nested `<section>` elements with CDN-loaded scripts

## P01 — Design Document (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `docs/BETA17_DESIGN.md` exists | PASS |
| 2 | All 6 sections present (39 `##` headings) | PASS |
| 3 | roff mapping table present | PASS |
| 4 | INodeTemplate interface defined | PASS |
| 5 | Section-to-slide mapping described | PASS |
| 6 | No source files modified | PASS |
| 7 | Document >= 250 lines (568 lines) | PASS |

### Verdict: PASS

## P02 — Man Page Converter (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1996+22 passed) |
| 3 | ManRenderer.cs exists | PASS |
| 4 | ManRenderer.Format = "man" | PASS |
| 5 | .TH header in output | PASS |
| 6 | Section renders as .SH | PASS |
| 7 | Bold -> \fB...\fR | PASS |
| 8 | Code block -> .nf/.fi | PASS |
| 9 | >= 4 new tests (22 tests) | PASS |
| 10 | Project in solution | PASS |
| 11 | src/AdocNet.Cli.Man/ exists | PASS |
| 12 | AdocNet.Cli.Man in solution | PASS |
| 13 | Existing tests pass | PASS |

### Verdict: PASS

## P03 — Converter Templates (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | dotnet build exits 0 | PASS |
| 2 | dotnet test exits 0, 0 failures | PASS (2018 passed) |
| 3 | HtmlRenderer.cs < 500 lines (495) | PASS |
| 4 | HtmlDocumentRenderer.cs exists (175 lines) | PASS |
| 5 | HtmlSectionRenderer.cs exists (110 lines) | PASS |
| 6 | HtmlBlockRenderer.cs exists (479 lines) | PASS |
| 7 | HtmlListRenderer.cs exists (183 lines) | PASS |
| 8 | HtmlTableRenderer.cs exists (270 lines) | PASS |
| 9 | HtmlInlineRenderer.cs exists (385 lines) | PASS |
| 10 | HtmlImageRenderer.cs exists (77 lines) | PASS |
| 11 | HtmlStemRenderer.cs exists (35 lines) | PASS |
| 12 | All helpers under line budgets | PASS |
| 13 | >= 16 extraction regression tests (16) | PASS |
| 14 | INodeTemplate.cs exists | PASS |
| 15 | HtmlRenderOptions has Templates | PASS |
| 16 | Template overrides default rendering | PASS |
| 17 | Non-matching nodes unchanged | PASS |
| 18 | Multiple templates -> first match wins | PASS |
| 19 | No templates -> default rendering | PASS |
| 20 | >= 5 template feature tests (6) | PASS |
| 21 | All pre-existing HTML tests pass | PASS (156/156) |

### Verdict: PASS

## P04 — Reveal.js Slides Converter (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | dotnet build exits 0 | PASS |
| 2 | dotnet test exits 0, 0 failures | PASS (2038 passed) |
| 3 | RevealjsRenderer.cs exists | PASS |
| 4 | Format = "revealjs" | PASS |
| 5 | Level-1 -> horizontal slides | PASS |
| 6 | Level-2 -> vertical slides | PASS |
| 7 | Theme attribute works | PASS |
| 8 | reveal.js scripts in output | PASS |
| 9 | >= 4 new tests (20 tests) | PASS |
| 10 | Project in solution | PASS |
| 11 | src/AdocNet.Cli.Revealjs/ exists | PASS |
| 12 | AdocNet.Cli.Revealjs in solution | PASS |
| 13 | Existing tests pass | PASS |

### Verdict: PASS

## Check A — System Integrity (2026-04-11)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | dotnet build exits 0 | PASS |
| 2 | dotnet test exits 0, 0 failures | PASS (2038 passed) |
| 3 | Existing converters unmodified (Pdf, DocBook, Epub) | PASS |
| 4 | Parser/AST unmodified | PASS |
| 5 | ManRenderer exists with Format "man" | PASS |
| 6 | INodeTemplate exists | PASS |
| 7 | Templates work in HtmlRenderer | PASS (6 tests) |
| 8 | RevealjsRenderer exists with Format "revealjs" | PASS |
| 9 | Both new projects in solution (4 entries) | PASS |
| 10 | netstandard2.0 builds | PASS |
| 11 | net10.0 builds | PASS |

### Verdict: PASS

## P05 — Documentation (2026-04-11)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | CHANGELOG.md contains beta.17 with >= 6 items (12 items) | PASS |
| 2 | Directory.Build.props version = 1.0.0-beta.17 | PASS |
| 3 | README.md mentions man page, templates, reveal.js | PASS |
| 4 | dotnet build exits 0 | PASS |
| 5 | dotnet test exits 0, 0 failures | PASS (2038 passed) |
| 6 | No source code modified | PASS |

### Verdict: PASS

## Reflect — Self-Reflection (2026-04-11)

### New Project Quality
- **ManRenderer**: Covers all block types (sections, paragraphs, lists, description lists, delimited blocks, admonitions, tables, images, STEM) and all inline types (bold, italic, mono, links, xrefs, footnotes, macros). 22 tests. Split into partial classes (475 + 184 lines).
- **RevealjsRenderer**: Proper slide structure with horizontal (level-1) and vertical (level-2) sections. CDN links use jsDelivr with configurable theme/transition. Speaker notes via `[.notes]` role. 20 tests. Split into partial classes (419 + 127 lines).

### Template System
- **INodeTemplate** is minimal: 2 methods (`CanRender`, `Render`), 21 lines. Not over-engineered.
- First-match-wins is tested and works correctly (6 template tests).
- Templates are opt-in (null by default), zero overhead when unused.

### File Sizes
- All files under 500 lines after splitting:
  - ManRenderer.cs: 475, ManRendererInlines.cs: 184
  - RevealjsRenderer.cs: 419, RevealjsRendererInlines.cs: 127
  - HtmlRenderer.cs: 495 (coordinator)
  - All 8 HTML partial files under their budgets

### Test Coverage
- **Man page**: .TH header, sections (.SH/.SS), bold/italic/mono, code blocks, lists (ordered/unordered/description), admonitions, escaping, full round-trip = 22 tests
- **Reveal.js**: horizontal/vertical slides, themes, transitions, controls, slide numbers, paragraphs, bold, code, lists, speaker notes, escaping, round-trip = 20 tests
- **Templates**: override matching, non-matching default, first-match-wins, null templates, inline templates = 6 tests
- **Regression**: 16 golden-output tests for HtmlRenderer extraction

### Verdict: PASS — all checks satisfied

## Check C — Final Validation (2026-04-11)

### Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | dotnet build exits 0 | PASS |
| 2 | dotnet test exits 0, 0 failures | PASS (2038 passed) |
| 3 | Existing converters unmodified (Pdf, DocBook, Epub) | PASS |
| 4 | Parser/AST unmodified | PASS |
| 5 | ManRenderer exists | PASS |
| 6 | Man: .TH header | PASS |
| 7 | Man: section -> .SH | PASS |
| 8 | Man: bold -> \fB..\fR | PASS |
| 9 | INodeTemplate exists | PASS |
| 10 | Template overrides rendering | PASS |
| 11 | Template non-match -> default | PASS |
| 12 | RevealjsRenderer exists | PASS |
| 13 | Reveal: horizontal slides | PASS |
| 14 | Reveal: vertical slides | PASS |
| 15 | Reveal: theme attribute | PASS |
| 16 | All new projects in solution (4) | PASS |
| 17 | No file > 500 lines (14 files checked) | PASS |
| 18 | No sensitive terms in commit messages | PASS (0 matches) |
| 19 | Directory.Build.props version = 1.0.0-beta.17 | PASS |
| 20 | netstandard2.0 builds | PASS |
| 21 | net10.0 builds | PASS |
| 22 | HtmlRenderer.cs coordinator < 500 lines (495) | PASS |
| 23 | All 8 extracted helper files exist | PASS |
| 24 | Extraction regression tests >= 16 (16) | PASS |
| 25 | All pre-existing HTML tests pass (172/172) | PASS |
| 26 | Incremental rendering still works (15/15) | PASS |
| 27 | Total test count >= previous release (2038 > 1974) | PASS |

### Feature Checklist

| Feature | Test exists? | Test passes? |
|---------|-------------|-------------|
| Man: .TH header | Yes | Yes |
| Man: sections (.SH) | Yes | Yes |
| Man: bold/italic | Yes | Yes |
| Man: code blocks | Yes | Yes |
| Man: lists | Yes | Yes |
| Template: override rendering | Yes | Yes |
| Template: first match wins | Yes | Yes |
| Template: non-match default | Yes | Yes |
| Reveal: horizontal slides | Yes | Yes |
| Reveal: vertical slides | Yes | Yes |
| Reveal: theme attribute | Yes | Yes |
| Reveal: scripts in output | Yes | Yes |
| Extraction: HtmlTableRenderer regression | Yes | Yes |
| Extraction: HtmlInlineRenderer regression | Yes | Yes |
| Extraction: HtmlBlockRenderer regression | Yes | Yes |
| Extraction: HtmlListRenderer regression | Yes | Yes |
| Extraction: HtmlSectionRenderer regression | Yes | Yes |
| Extraction: HtmlImageRenderer regression | Yes | Yes |
| Extraction: HtmlStemRenderer regression | Yes | Yes |
| Extraction: HtmlDocumentRenderer regression | Yes | Yes |
| Incremental rendering preservation | Yes | Yes |

### Verdict: PASS — all 27 criteria satisfied, all 21 features have passing tests

### Release Summary
- **64 new tests** added (22 man, 20 revealjs, 6 template, 16 regression)
- **4 new projects**: AdocNet.Converters.Man, AdocNet.Converters.Revealjs, AdocNet.Cli.Man, AdocNet.Cli.Revealjs
- **1 new interface**: INodeTemplate (21 lines)
- **HtmlRenderer extraction**: 2097 lines -> 8 partial files + 495-line coordinator
- **Total tests**: 2038 (up from ~1974 in beta.16)
