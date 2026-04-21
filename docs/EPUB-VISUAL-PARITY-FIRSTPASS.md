# EPUB Visual Parity — First-Pass Findings (HOWTO.adoc)

**Date**: 2026-04-20 (post-v1.0.0-beta.25)
**Tool**: `tools/epub-visual-diff.py`
**Reference engine**: `asciidoctor-epub3 2.3.0`
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -b epub`)
**Reference document**: `C:/Workspace/Adoc2Pdf/HOWTO.adoc`

This is the visual companion to `EPUB-PARITY-FIRSTPASS.md` (which covered
the structural EPUB skeleton — package.opf, nav, toc.ncx, manifest, dc:*
metadata). This doc compares the actual chapter rendering pixel-by-pixel.

## Headline numbers

| Chapter | Total px | Changed px | First pass | After Tier 1 |
|---|---|---|---|---|
| `_how_to_generate_pdf_from_adoc` | 979,200 | 327,972 | 36.72% | **33.49%** |

Chapter coverage: 1/1 chapters render successfully on both sides
(asciidoctor-epub3 collapses HOWTO.adoc to a single chapter; AdocNet
matches now that beta.25 named chapter files after the doc-title slug).

## How the tool works

1. Extracts both EPUB zip files to temp directories
2. Locates the OEBPS root by searching for `*.opf` (asciidoctor-epub3
   uses `EPUB/`, AdocNet uses `OEBPS/`; both are valid)
3. Pairs chapter XHTML files by basename
4. Renders each via Chrome headless (`--screenshot=...`) at 816×1200
5. Pixel-diffs each pair; produces `ref.png`, `cand.png`, `diff.png`,
   and `side-by-side.png` per chapter, plus an aggregate `_summary.md`

The 816×1200 window matches a typical e-reader viewport. Differences
in scrollbars, margins, and fixed positioning are eliminated by
`--hide-scrollbars` and a fixed window size.

## What carried over from earlier formats

Same playbook: render both, compare, identify mechanical structural
gaps first, defer typographic / theme-CSS work to later tiers. The
first-pass visual pixel-diff is more sensitive than the structural
diffs from earlier formats — typography, font fallback, and bullet
glyph differences all show up as changed pixels even when the DOM is
semantically equivalent.

## First-pass gaps and fixes

### Tier 1 (mechanical structural) — DONE

1. ✅ **Chapter wrapper missing** — Asciidoctor-epub3 wraps each
   chapter body in `<section class="chapter">` with a
   `<header class="chapter-header">` containing
   `<h1 class="chapter-title">`. AdocNet emitted bare body content
   straight into `<body>`. Reader CSS hooks (and external stylesheets)
   target `.chapter-title`/`.chapter-header` for styling.

   Fixed in `EpubRenderer.WriteChapterXhtml`. Also added
   `xmlns:epub`, `xml:lang`, `lang` to the `<html>` element to match
   asciidoctor-epub3's namespace declarations.

   1 regression test added in `EpubRendererTests`.

   Pixel diff dropped from 36.72% → 34.62%. The chapter title now
   renders as expected. Most remaining pixel diff is theme/CSS, not
   structural.

### Tier 2 (theme CSS) — DONE

2. ✅ **Chapter-title styling** — embedded `style.css` now declares
   uppercase + letterspacing + thin border-bottom on `.chapter-title`
   / `.chapter-header`, matching asciidoctor-epub3 exactly.

3. ✅ **Bullet glyph** — `ul > li::before { content: "▪" }`
   pseudo-element matches asciidoctor's bullet rendering. Nested-level
   bullets cycle through ◦ • ▫ with the same colour palette.

4. ✅ **Heading sizes, weights, and spacing** — h1/h2 = 1.5em (was 1.8/1.5),
   h4 = 1.2em with weight 200 (was 1.1em weight 400), h5 = 0.9em uppercase
   weight 700. Letter-spacing -0.01em on all headings. Margins match
   asciidoctor's values exactly.

5. ✅ **Code block styling** — bg #E0E0E0 (was #f4f4f4), top+right borders
   (was left border), 8px/12px padding, 0.85em font-size, line-height 1.4.
   All values copied from asciidoctor's `epub3.css`.

6. ✅ **Body paragraphs** — `text-align: justify` (was left), `widows: 2`,
   `orphans: 2`, margin-top-only spacing.

   Per-chapter pixel diff: 36.72% → **33.49%**. Visible alignment is
   essentially complete; the remaining ~33% is body-text font choice.

### Tier 3 (intentionally accepted) — REMAINING

7. **Body typography (font choice)** — Asciidoctor-epub3 ships embedded
   TTF fonts (~640 KB total: `mplus1mn-*`, `notoserif-*`, `mplus1p-*`);
   AdocNet declares web-safe font stacks in CSS and lets the reader
   fall back. Pixel diff plateaus at ~33% because most pixels are body
   text where Noto Serif vs system serif differ glyph-for-glyph.
   Closing this requires bundling fonts (EPUB size inflation,
   licensing considerations, opt-in flag design) — a project-level
   decision. Visible layout is essentially identical without bundling.

8. **Default avatar / byline** — Asciidoctor-epub3 always emits
   `<p class="byline"><img src="avatars/default.jpg"/></p>` even with
   no author. AdocNet omits it entirely. Arguably AdocNet's behaviour
   is cleaner; intentionally not closing this gap.

### Tier 3 (intentionally different) — ACCEPTED

5. **OEBPS folder name** — Asciidoctor uses `EPUB/`; AdocNet uses
   `OEBPS/`. Both are valid per the EPUB spec. The diff tool handles
   this correctly via `*.opf` discovery. No fix needed.

## Resuming this work

```bash
asciidoctor-epub3 -o /tmp/howto-ref.epub <fixture>.adoc
adocnet <fixture>.adoc -b epub -o /tmp/howto-cand.epub
python tools/epub-visual-diff.py /tmp/howto-ref.epub /tmp/howto-cand.epub
```

Output: `epub-visual-diff-out/_summary.md` (pixel-diff table per
chapter), `<chapter>/{ref,cand,diff,side-by-side}.png` per chapter.

The next high-impact lever is bundling a small set of web-safe-stack
CSS rules to bring fonts and bullet glyphs closer to asciidoctor's
default. That alone should drop the per-chapter pixel diff under 20%.
