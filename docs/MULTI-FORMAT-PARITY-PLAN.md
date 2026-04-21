# Multi-Format Asciidoctor Parity Plan

Working document. Captures the methodology used to bring the PDF renderer to
visual parity with `asciidoctor-pdf`, generalises it, and lays out a phased
plan for closing the same kinds of gaps in the other output formats.

This file should be self-contained enough that a fresh session can resume the
work without prior context.

## Background

`v1.0.0-beta.25` shipped a PDF renderer that visually matches `asciidoctor-pdf`
for the `HOWTO.adoc` reference document. The fixes ranged from font-subset
correctness (TrueType `hmtx` packed metrics, FixedPitch flag) to layout
positioning (first-line leading reservation), to PDF catalog properties
(`/ViewerPreferences`, `/PageLabels`, `/OpenAction`, `/Names`, outline TOC
section numbers and flatness), to content rendering (SVG document order,
metadata source priority, header/footer placeholder substitution under
embedded fonts).

The fixes were not blind — they were driven by a tight diffing loop:

1. Render both engines on the same input.
2. Visually compare the output (PyMuPDF screenshots, side-by-side cropping).
3. Structurally compare the output (PDF catalog/info dict/font tables).
4. For each gap: identify root cause, write a regression test, fix, re-verify.
5. Lock the new behaviour with tests against future drift.

Most of those root causes are not PDF-specific. The same playbook should work
for the other output formats, with format-specific tooling at the diff layer.

## What carries over (format-agnostic root causes)

These bugs were identified in the PDF renderer but the underlying mistake
pattern is portable. Each one is worth checking in every other renderer.

### 1. Document-order processing

The SVG renderer grouped shapes by element type (paths, polygons, rects,
circles) and emitted each group in turn, which meant later document-order
shapes were sometimes drawn behind earlier ones. The fix was to walk the
SVG in source order regardless of element type.

**Where to look elsewhere:** any renderer that iterates collections grouped
by `Kind` rather than walking the AST in declaration order. Grep for
`GroupBy(...Kind)`, `OfType<...>`, or successive `foreach` loops over
filtered subsets of the same parent collection.

### 2. Document-title vs `header-title` source priority

The PDF info-dict `/Title` was being populated from the `header-title`
document attribute (intended only as a per-page header text override) rather
than from the actual document title (level-0 heading). The same confusion
likely exists in:

- **EPUB** `dc:title` in `content.opf`
- **HTML** `<title>` element in `<head>`
- **DocBook** `<title>` inside the root `<info>`
- **Reveal.js** `<title>` of the deck HTML

Fix pattern: `DocumentTitle = document.Title`; `HeaderTitle = attribute("header-title")`;
keep them as distinct fields with separate consumers.

### 3. Section number prefix in TOC / outline / nav

When `:sectnums:` is on, the rendered section heading shows `"1. Generate..."`
but the PDF outline entry was emitting just `"Generate..."`. Asciidoctor mirrors
the rendered title in the outline. Same logic should apply to:

- **EPUB** `nav.xhtml` and `toc.ncx`
- **HTML** `#toc` lists
- **DocBook** any rendered ToC structure
- **Reveal.js** any slide ToC

Fix pattern: compute the section number prefix once and pass the *prefixed*
title to both the renderer and the TOC/outline registration.

### 4. TOC hierarchy shape

The PDF outline tree had the document title as the root with sections nested
under it; Asciidoctor flattens these — the document title is a sibling of
top-level sections. Worth checking the EPUB `nav.xhtml` tree shape against
asciidoctor output for the same kind of mismatch.

### 5. Cross-reference targets / IDs

Internal anchors, named destinations, and cross-references must agree across
formats so that links work end-to-end. The PDF added `/Names` with named
destinations matching section IDs. EPUB and HTML need the same IDs, exposed
as `id="..."` on the section/heading elements.

### 6. Page-bound placeholder substitution under different encodings

The PDF `___TOTAL___` placeholder failed to substitute when the footer used
an embedded TrueType font, because the placeholder was encoded as glyph IDs
(hex pairs) and the byte-level ASCII scan couldn't find it. Format-specific
analogue: any post-render placeholder substitution that operates on bytes
must consider all encodings the placeholder might end up in.

EPUB/HTML/DocBook generally don't have this exact bug because they're text
formats, but any binary container (EPUB) with compressed inner streams could
hit a similar gotcha.

### 7. First-line leading reservation

PDF-specific (paginated, fixed-pitch text positioning). Doesn't apply to flow
formats.

### 8. Font / glyph metrics

PDF-specific (we embed and subset TTF fonts). Doesn't apply to formats that
delegate font handling to the consuming renderer (browser, EPUB reader,
PostScript engine).

## Format inventory and triage

| Format | Visual diff feasible? | Structural diff feasible? | Effort | Value |
|---|---|---|---|---|
| **EPUB** | Yes (Calibre / `epub.js` / Apple Books) | High — binary container of XHTML + OPF + NCX | Medium | **High** — most PDF lessons transfer |
| **HTML** | Yes (headless Chrome via Playwright) | Highest — DOM tree comparison | Medium | **High** — most-consumed format |
| **DocBook** | No (pure interchange XML) | Highest — canonical XML diff | Low | Medium — enables downstream chains |
| **Reveal.js** | Yes (browser at slide viewport) | Medium — section tree + reveal-specific attrs | Low | Low–Medium |
| **Man** | Yes (`groff -Tutf8 -man`) | Low — line-based text diff | Very low | Low — simplest format |

## Phased plan

### Phase 1 — EPUB (highest leverage)

EPUB is a zip container of XHTML + metadata + nav. It inherits both the PDF
lessons (metadata source, nav/TOC structure, manifest correctness) and the
HTML lessons that will land later (content rendering, anchors, IDs).

Tooling:
- `tools/epub-diff.py` — extract both `.epub` files into temp dirs, walk
  every part, normalise whitespace + attribute order, emit per-part diffs.
  Highlight: `META-INF/container.xml`, `OEBPS/content.opf`, `OEBPS/nav.xhtml`,
  `OEBPS/toc.ncx`, every chapter `*.xhtml`, every CSS file.
- Optional Phase 1b: render both EPUBs in Calibre's reader or `epub.js`,
  screenshot each chapter, pixel-compare.

Workflow per gap:
1. Run diff tool against `HOWTO.adoc` output.
2. Pick the highest-value gap (start with metadata, then nav, then content).
3. Identify root cause in `AdocNet.Converters.Epub`.
4. Write regression test that locks the corrected output.
5. Fix, re-run.
6. Repeat.

Expected first-pass gaps (working hypotheses):
- `dc:title` likely echoes `header-title` instead of document title (same as PDF)
- `nav.xhtml` entries probably missing section number prefixes
- `nav.xhtml` hierarchy may nest under document title rather than flatten
- Manifest `media-type` accuracy for embedded images / fonts
- Spine ordering and `linear="yes/no"` attributes

### Phase 2 — HTML

Tooling:
- `tools/html-diff.py` — parse both outputs (BeautifulSoup), canonicalize
  (sorted attrs, normalised whitespace, sorted classes), emit unified diff.
- Visual layer: Playwright headless Chrome, screenshot at 1280×* and 375×*,
  pixel-compare with the asciidoctor reference (using their default stylesheet).

Existing `CompatibilityTests` covers a lot of the structural side already.
The visual diff would catch CSS/theme variance and any DOM differences the
existing normaliser hides.

Likely gaps once we look:
- TOC: section number prefix (same fix as PDF outline)
- `<title>` element sourcing (doc title vs header-title)
- Class names exactly matching asciidoctor's (`sect1`, `sect2`, `paragraph`,
  `listingblock`, etc.)
- Anchor IDs identical (already covered by some tests)
- Smart punctuation edge cases

### Phase 3 — DocBook

Tooling:
- `tools/docbook-diff.py` — XML C14N (canonicalisation) of both outputs,
  unified diff. Trivial since both are pure XML.

Existing `DocBookCompatibilityTests` claims 181/181, but that's against a
relaxed normaliser. C14N diff will surface attribute-order, namespace, and
whitespace differences the normaliser might be hiding.

### Phase 4 — Reveal.js + Man

Reveal.js: same `html-diff` tool from Phase 2, plus screenshot at common
slide viewports (1024×768, 1920×1080).

Man: render with `groff -Tutf8 -man`, line-based text diff. The existing
`ManNormalizer` covers a lot already — just need a parallel rendered-output
diff to confirm the normaliser isn't hiding gaps.

## Reference document

`C:/Workspace/Adoc2Pdf/HOWTO.adoc` (3 pages of typical content: code blocks,
sections, inline code, lists, attributes, sectnums) has been the proven
exercise document for PDF. Keep using it as the entry-point reference for
each phase.

For broader coverage once a format is past first-pass parity, layer in:
- `spec/conformance/*.adoc` — feature-focused reference docs
- `spec/fixtures/**/*.adoc` — small targeted examples

## How to resume this work after context loss

If you're picking this up cold:

1. Read `docs/MULTI-FORMAT-PARITY-PLAN.md` (this file).
2. Check `git log v1.0.0-beta.25..` for what's been started since the plan.
3. Look in `tools/` for any `*-diff.py` companions to `pdf-visual-diff.py` —
   those are the format-specific diffing tools.
4. The methodology is identical for every format: render both, diff, pick
   the highest-value gap, regression test, fix, lock.

The successful-PDF playbook commits are in the squashed `v1.0.0-beta.25`
release commit. Walk that commit's diff for examples of the fix shapes
(metadata, outline, font subsetting, layout positioning).
