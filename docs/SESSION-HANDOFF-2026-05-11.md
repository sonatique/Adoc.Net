# Session Handoff — 2026-05-11

## What this session accomplished

35 commits over 4 calendar days closed Asciidoctor parity for three of the
five output formats across the entire 36-document conformance corpus.

### Final corpus state

| Format | Session start | Now | Change | Perfect docs |
|---|---|---|---|---|
| **HTML** | 153 (worst) | **0** | -100% | **36/36** ✓ |
| **DocBook** | 245 | **0** | -100% | **36/36** ✓ |
| **Reveal.js** | 8052 | **0** | -100% | **36/36** ✓ (DOM diff, see caveat) |
| Man | 6968 | 6306 | -10% | 11/36 |
| EPUB-struct | 36 (artifact) | 91 (real) | (path fix unmasked structural) | — |

Test suite: **3044 pass / 0 fail / 21 skipped** (+149 tests this session).

### CAVEAT: Reveal.js "36/36 perfect" means structural DOM, not visual

The reveal.js diff metric (`tools/revealjs-diff.py`) compares the parsed slide
DOM after canonicalization. **The visual side-by-side panels for Reveal.js will
show stark differences** even when the DOM matches:

- Asciidoctor's reveal.js converter emits **local relative paths** for CSS/JS
  (`reveal.js/dist/reveal.css`). Standalone snapshots can't load these →
  reveal.js doesn't boot → browser falls back to flow-mode HTML showing all
  slides scrolled.
- AdocNet emits **CDN URLs** (`https://cdn.jsdelivr.net/npm/reveal.js@4/dist/...`).
  These load successfully → reveal.js boots → slide-deck mode showing only
  the title slide.

Both are valid reveal.js HTML; the slide content is byte-identical. AdocNet's
choice is intentional (better end-user experience: open the HTML, it works).
**Don't be misled by the visual panels for Reveal.js — `revealjs-diff.py` is
the source of truth for that format.**

For HTML / DocBook / Man / EPUB-struct, the panels and the diff metric agree.
For PDF (and `*-asciidoctor-theme` variants), only the panels exist — these
are visual-only formats.

### How to verify state

```bash
dotnet test tests/AdocNet.Tests/AdocNet.Tests.csproj --nologo
python tools/parity-sweep.py --glob "spec/conformance/*.adoc"
cat parity-sweep-out/_summary.md
```

## What was fixed (commit reference)

Fixes are in newest-first order. Run `git show <hash>` for the full message
+ rationale.

### Parser fixes (apply across all renderers)

- `34c7e84` — Source-block role/id preserved in list-continuation contexts
  (`[source,role="primary"]` after `+` in lists/dlists).
- `5e98796` — Paragraph-style admonitions (`[WARNING]\nparagraph`) emit
  `AdmonitionNode` instead of being dropped.
- `60fa2b6` — Constrained `#text#` highlight now uses word-boundary check
  (matches `*`, `_`, `` ` ``).
- `c5907a2` — Parser sets `:docname:`, `:docfile:`, `:docfilesuffix:`
  intrinsic attributes from `ParseOptions.SourceFilePath`.

### DocBook fixes

- `7da30d8` — Five gaps closed: root `xml:id` from doc anchor; link/xref
  labels parsed as inlines (backticks → `<literal>`); block titles parsed;
  conditional `linenumbering="unnumbered"`; conditional `arearefs`.
- `5aa5256` — Title and label parsing for inline formatting.

### Man fixes

- `5aa5256` — Title and label parsing for inline formatting.
- `c5907a2` — Five cross-cutting roff conventions: `'\" t` directive,
  `.TH` name from docname (with hyphen escaping), `.TH` source/manual
  default to `\ \&`, `.PP` → `.sp` for paragraphs, smart-quote escapes
  (U+2018→`\(cq` etc.).

### EPUB fixes

- `5403662` — Standard EPUB 3 paths: `EPUB/package.opf` (was `OEBPS/content.opf`).
  Note: this fix unmasked deeper structural diffs that the canonicalizer
  previously couldn't see — the 91-line "regression" is real feature gap, not a
  fix regression.

### Reveal.js fixes (24 commits — 8052 → 0 over the session)

Block structure:
- `81a7bc1` — Wrap source/listing/literal/quote/example/sidebar in proper
  `<div class="…block">` + `<div class="content">`.
- `97f3c87` — Full table structure (`frame-/grid-/halign-/valign-` classes,
  `<colgroup>`, `<thead>/<tbody>/<tfoot>`, `<p class="tableblock">` cell wrappers).
- `7246284` — Admonition 2-column table structure; example block id;
  xref empty-label fallback.
- `f69d444` — Conditional preamble div (only when sections exist) +
  `:icons: font` admonition icons.
- `c85ca8f` — Admonition title in content cell + image-block wrapper.
- `45aa93a` — Per-slide footnote rendering (`<sup class="footnote">` +
  `<div class="footnotes">`).
- `1bbab7b` — `[horizontal]` description list table structure.
- `b7eea98` — `[qanda]` description list ordered-list structure.
- `c27146b` — Checklist `[x]`/`[ ]` items render `<input type="checkbox">`.

Inline + macros:
- `16b4afe` — Render xref / interdoc-xref / footnote / image inline nodes
  (were silently dropped).
- `e6138e0` — kbd/btn/menu inline macros + passthrough block + quote
  bare-content rendering.
- `9ee1ecc` — Inline-format role classes (`*text*` with `[.term]` →
  `<strong class="term">`) + link `target` from `Window`.
- `7973eea` — `:hide-uri-scheme:` strips scheme from displayed URLs.

Section/list/heading:
- `23e7c48` — Heading level off-by-one fix; preamble grouped in title slide.
- `9f2bae8` — Section numbering (`:sectnums:`); `:source-highlighter: highlight.js`
  classes; bare-link class on `<a>`.
- `9f9e5b7` — Number only slide-level sections (1-2), not deeper headings.
- `6c3079e` — Appendix sections get `Appendix A:` letter prefix.
- `b0aeedd` — Discrete headings render as inline `<h{N+1} class="discrete">`.
- `2677d6b` — Collapsible example block skips `Example N.` prefix.

List structure:
- `150bad1` — Nested list children rendered (were dropped); title subtitle split.
- `67d606d` — Quote block content + ordered-list numbering style.
- `d673760` — Author email in byline + ordered-list `type` attribute.
- `ed42336` — Full description list structure with parsed inlines.
- `4af3302` — Listing-block roles propagated to wrapper class.

Other:
- `a767c29` — Callout markers (`<b>(N)</b>`) + colist after listings.
- `3056f3b` — Strip trailing comment marker before conum.
- `05dd40f` — Paragraph roles propagated.
- `e9b3667` — Paragraph id propagated (the last gap).

## Code surface area

Three renderers gained per-render mutable state via fields:

- `RevealjsRenderer`: `_exampleCounter`, `_tableCounter`, `_figureCounter`,
  `_appendixCounter`, `_orderedListDepth`, `_sectnumsEnabled`,
  `_sectionCounters[]`, `_sectnumLevels`, `_highlightJs`, `_iconsFont`,
  `_hideUriScheme`, `_slideFootnoteTexts`. All reset in `Render()`.

Most rendering methods on `RevealjsRenderer` were converted from `static`
to instance — required by counter access. The partial-class structure
(`RevealjsRenderer.cs` + `RevealjsRendererInlines.cs`) was preserved.

`AdocNet.Parser` `InternalsVisibleTo` was extended to cover Man and
Reveal.js converters so they can call `InlineParser.Parse` directly
(HTML and DocBook already had access).

## Test infrastructure added

Three new test files (no existing test removed):

- `tests/AdocNet.Tests/SourceBlockRoleTests.cs` (6 tests)
- `tests/AdocNet.Tests/ParagraphAdmonitionTests.cs` (12 tests)
- `tests/AdocNet.Tests/HighlightBoundaryTests.cs` (8 tests)
- `tests/AdocNet.Tests/DocBookConverterGapTests.cs` (9 tests)
- `tests/AdocNet.Tests/ConverterTitleParsingTests.cs` (8 tests)
- `tests/AdocNet.Tests/ManCrossCuttingTests.cs` (6 tests)
- `tests/AdocNet.Tests/RevealjsCrossCuttingTests.cs` (84 tests — by far the biggest)

Existing `EpubRendererTests.cs`, `CrossRendererTests.cs`,
`ManRendererTests.cs`, `RevealjsRendererTests.cs` had assertions updated
in lockstep with the path/format changes (intentional behavior changes,
not regressions).

## Working-directory state

Working tree clean for all parity work — only stale untracked files in
`docs/GETTING-STARTED-BETA*.md` and `samples/beta3-*.pdf` (unrelated to
this session).

```bash
git status --short | grep -v "^??" | head
# (empty)
```

## Remaining work

### Man (sum 6306, median 16/doc)

Despite -10% drop and 11 docs perfect, the bulk is structural roff
differences. Worst remaining docs:

- `spring-security-auth` (1177), `mixed-features` (1069),
  `quarkus-getting-started` (995), `user-manual` (573),
  `api-reference` (488).

Likely candidate gaps (from earlier sampling): list rendering, table
structure, source-block formatting, admonition rendering. Each is a
medium-scope refactor similar to what Reveal.js needed for tables.
Sample with `cat parity-sweep-out/<doc>/man/normalized.diff | head -80`
to find next biggest patterns.

### EPUB-struct (sum 91, 2-4 lines/doc)

These small numbers are an artifact of the structural canonicalizer; the
**actual** EPUB output gap is much larger:

- Asciidoctor's EPUB embeds Noto Serif + M+ Mono + FontAwesome TTF fonts
- Three CSS files (`epub3.css`, `epub3-css3-only.css`, `epub3-fonts.css`)
- Author avatar + headshot images
- Calibre-detection JS in chapter pages
- Title-page byline structure

Each is a substantial feature addition. The current EPUB output is a
minimal but valid EPUB 3 document; matching Asciidoctor's full output
would be a multi-day effort.

### PDF (visual, not in `_summary.md`)

Tracked separately in `docs/DEFERRED-PARITY-ITEMS.md`. Out of scope for
the parity-sweep tool (it's a visual format).

## How to pick up next

1. Run `python tools/parity-sweep.py --glob "spec/conformance/*.adoc"` —
   confirm baseline still matches this handoff.
2. Pick a target format (Man recommended — biggest sum diff, no real
   feature gaps, all parity-style).
3. Sort docs by remaining diff size:
   `grep -E "^\| \`" parity-sweep-out/_summary.md | sort -t"|" -k4 -nr | head`
   (column `4` for man, `6` for revealjs, `5` for docbook)
4. Sample the biggest doc's diff, identify a recurring pattern, write
   regression-locking tests, fix, full-corpus sweep, commit.
5. **Critical discipline that paid off this session**: re-run the full
   corpus sweep after every commit, not just the docs you targeted. Two
   regressions this session (preamble div over-wrapping, quote paragraph
   wrapping) were caught only because of this — they would have silently
   broken other docs.

## Key insights from this session

1. **Cheap-fix-wide-impact pattern**: fixes in heavily-used code paths
   (table renderer, list renderer, admonition renderer) had outsized impact
   per commit (-51%, -23%, -16%). Rank gaps by *how many docs reference
   the missing feature*, not just by line count of the worst diff.

2. **Renderer-specific class orderings**: Asciidoctor's HTML and
   reveal.js converters use *different* CSS class orderings
   (`colist arabic` vs `arabic colist`, `olist arabic` vs `arabic olist`)
   and *different* tag wrappers (`<b class="conum">` vs bare `<b>`).
   Always sample the *target* converter's reference output, not the
   sister converter's.

3. **Static-to-instance conversion is mechanical but unavoidable**: when
   a renderer needs cross-block state (counters, footnotes), the entire
   rendering chain has to lose `static`. Bite the bullet early; don't
   try to thread state through arguments.

4. **Style-driven structural variants**: when a node has a style attribute
   that selects an entirely different output structure (`[horizontal]`
   table vs `<dl>`, `[qanda]` `<ol>` vs `<dl>`), branch at the renderer
   boundary. Per-style helpers stay simpler than threading the style
   flag through one big function.

5. **Precedence matters with multiple modifiers**: `[appendix]` sections
   should not get `:sectnums:` prefixes. Discrete headings should not get
   appendix or numbering prefixes. When multiple modifiers can fire on
   the same node, spell out precedence explicitly in the dispatch order.

6. **Parser plumbing is leverage**: setting `:docname:` once in the
   parser benefits Man, Reveal.js, EPUB metadata, HTML docinfo —
   every converter that reads document attributes. One-place changes
   that benefit N consumers compound over time.
