# Deferred Parity Items vs Asciidoctor

This document captures parity gaps between AdocNet output and the reference
asciidoctor / asciidoctor-pdf / asciidoctor-revealjs / etc. tools.
Items marked **RESOLVED** were closed in a specific release; the remainder
are tracked for follow-up.

## v1.0.0 release status (2026-05-17)

| Format | Status |
|---|---|
| HTML | byte-identical (36/36 corpus docs) ✓ |
| DocBook | byte-identical (36/36 corpus docs) ✓ |
| Reveal.js | byte-identical slide DOM (36/36 corpus docs) ✓ |
| Man | structurally equivalent; cleaner roff than reference. 11/36 perfect, remaining 5510 lines are stylistic conventions (item 12 below) |
| EPUB | full asciidoctor-epub3 asset/structure parity; chapter XHTML visually indistinguishable. Small residuals tracked in items 10a-10c. |
| PDF | items 1-4 below remain for v1.x.minor |
| HTML asciidoctor-theme | items 5-9 below remain for v1.x.minor |

The verification methodology relies on `tools/parity-sweep.py` to render both
sides and on PyMuPDF span extraction (font, size, color, bbox) to surface
differences invisible at panel resolution. See
`memory/feedback_pdf_color_extraction.md` for the lesson behind this.

A CI parity gate (in `.github/workflows/ci.yml`) enforces the v1.0 baseline:
HTML/DocBook/Reveal.js sums must stay at 0; Man and EPUB-struct have soft
thresholds (6000 and 120 respectively).

---

## PDF (asciidoctor-theme)

### 1. In-source-block conum markers — needs parser change

**What's missing.** Asciidoctor renders `<1>`, `<2>` markers inside a source
block as small circled-number glyphs (① ② …) positioned at the end of the
referenced line, in the codespan accent color (#B12146). AdocNet currently
strips or literal-renders them inside the verbatim text, so the connection
between the line and the callout list below is invisible in PDF.

**Why it's deferred.** Requires a layered change:
1. **Parser** — `BlockParser` already strips `// <1>` end-of-line comments
   when populating `DelimitedBlockNode.Callouts`. It needs to instead retain
   their column position so the renderer can place the glyph correctly.
2. **AST** — add a `(int line, int column, int conumNumber)` list to
   `DelimitedBlockNode` (e.g. `InlineCallouts`).
3. **PDF renderer** — in `RenderVerbatimBlock` / `WriteWrappedVerbatimText`,
   after writing each line, look up any conums for that line and emit the
   circled-number glyph in mono font + codespan color at the correct x.

**Reference values** (already verified against `quarkus-getting-started.adoc`):
- Glyph: `\u2460` + `(num - 1)` for num ≤ 20; fallback `(N)` otherwise
- Font: `_fontMono` (codespan font)
- Color: `_codespanColor` ?? `#B12146` (already wired up for the callout list)
- Size: same as `_codeFontSize`

**Where to hook in.** `PdfRenderer.cs` around line 865 (the verbatim line
loop) — for each line, after `WriteWrappedVerbatimText`, check if the
current source line has callouts and append a colored conum glyph.

### 2. Color 1-bit precision differences

**Symptom.** PyMuPDF reports color values one-off from REF (e.g. CAND
`#1a407d` vs REF `#19407c`, CAND `#bf3300` vs REF `#bf3400`).

**Root cause.** `PdfWriter.SetFillColor(float r, g, b)` writes RGB as
floats with `Fmt(...)`; the resulting PDF uses fractional values that round
differently than asciidoctor-pdf's integer-based color emission. Visually
identical, machine-different.

**Fix sketch.** Add `SetFillColorBytes(byte r, byte g, byte b)` on
`PdfWriter` that emits `r/255 g/255 b/255 rg` with consistent precision
(or use `r 255 div g 255 div b 255 div setrgbcolor`-like notation). Use it
for any color sourced from a hex string.

**Why deferred.** Cosmetic / not user-visible.

### 3. Page-bottom rule for non-list content

**What's missing.** I added `EnsureSpaceForLine(leading)` and called it from
`RenderList` and `RenderParagraph`, but other block kinds (admonition body,
description list items, table rows, sidebar contents) still rely on the
weaker `_cursorY < _marginBottom` check that fires *after* a line has
already started rendering with descender below the bottom margin.

**Plan.** Audit each `RenderXxx` and add `EnsureSpaceForLine(_bodyLeading)`
before drawing the next line of content. Most paths already call
`EnsurePage()` first; just replace with the stricter check.

---

## PDF (default theme)

### 4. Page-margin units in built-in defaults

`PdfRenderOptions` defaults have raw numbers for margins. Once theme YAML
loading proved that `0.5in` style values are common, the built-in defaults
should also accept and document unit suffixes for consistency. Currently
`MarginTop = 36f` (a magic number); cleaner as `MarginTop = "0.5in"` parsed
through `ParseLengthSafe` at load time.

---

## HTML (default theme) — SIGNIFICANT, user-visible

### 5. `:toc: left` not respected

**Symptom.** `user-manual.adoc` has `:toc: left` and asciidoctor renders a
fixed left sidebar TOC. AdocNet renders an inline TOC at the top of the
document.

**Plan.** In `HtmlDocumentRenderer`, read `:toc:` attribute value:
- `left` → add `body class="toc2 toc-left"` and wrap TOC in
  `<div id="toc" class="toc2">`
- `right` → `body class="toc2 toc-right"`, same wrapping
- `macro` → only render TOC at `toc::[]` macro position
- empty / unset / `auto` → inline at top (current behavior)

The CSS for `body.toc2 #toc.toc2` is already in `HtmlThemeCss.cs` (lines
377–415) for the asciidoctor theme; likely needs porting to default theme
too if we want the sidebar to look right outside asciidoctor mode.

### 6. Section heading color

**Symptom.** REF default theme uses asciidoctor's terracotta `#BA3925` for
`<h2>`–`<h4>`; AdocNet default theme uses near-black (browser default).

**Plan.** Add to default theme CSS:
```css
h2, h3, h4 { color: #ba3925; font-family: "Open Sans", sans-serif; font-weight: 300; }
```

### 7. `:icons: font` not honored in default theme

**Symptom.** `:icons: font` injects FA stylesheet only when
`Theme = Asciidoctor`. Default theme renders `<i class="fa icon-note">`
without the FA `<link>`, so admonition icons show as empty space.

**Plan.** In `HtmlDocumentRenderer.AppendDocumentPrologue`, the
`:icons: font` check at line 29 already injects FA. Verify it triggers
for default theme too (it should — there's no `Theme` guard around it).
The issue may be missing CSS rules `.icon-note:before { content: "\f05a" }`
in the default theme block. Port them from the asciidoctor theme block.

### 8. Subtitle line-break

**Symptom.** REF: `DataSync Documentation Team — Version 1.8, 2025-09-30`
on one line with em-dash separator. AdocNet: author and revdate on two
separate `<div class="details">` lines.

**Plan.** In `HtmlRenderer.cs` around line 666 (header rendering), emit
author + revdate on a single line when both exist, separated by
`&#8201;–&#8201;` (thin-space + en-dash + thin-space).

---

## HTML (asciidoctor-theme) — minor

### 9. CSS content differs from verbatim asciidoctor.css

**State.** AdocNet ships ~280 lines of "asciidoctor-mimic" CSS in
`HtmlThemeCss.cs::AsciidoctorTheme`. Reference `asciidoctor.css` is ~430
lines including reset, print styles, callout styles, deeper table styling,
and quoteblock variants we haven't ported.

**Plan (optional).** Embed verbatim `asciidoctor.css` (~28KB) as a resource
when `Theme = Asciidoctor`. License is MIT, attribution kept in comments.
Pros: pixel-perfect parity. Cons: can't selectively override (e.g. our
`#toc.toc2` width tweaks for AdocNet's slightly different content density).

A middle ground: keep our CSS but add a regression test that diffs a
representative document's rendered CSS rules against asciidoctor's output
to catch drift.

---

## EPUB-struct — small

### 10. `META-INF/container.xml` — RESOLVED in v1.0.0

Path moved from `OEBPS/content.opf` → `EPUB/package.opf` (commit `5403662`).
Container.xml now byte-identical.

### 10a. EPUB chapter XHTML residuals (~500–2000 bytes per doc)

After the dedicated `EpubChapterRenderer` (v1.0.0 commit `6e0003c`), per-doc
chapter XHTML diff dropped 95%. Remaining gaps:
- `<dd>` empty `<span class="principal"/>` + nested-list `complex` class when
  a description-list item has only block children (no inline description text)
- `class="last"` placement heuristic: AdocNet walks the AST and picks the
  deepest-trailing paragraph; Asciidoctor's rule appears narrower (only the
  chapter-terminal paragraph, not deep-section terminals)
- Minor table style attributes (`style="width: 100%"`, per-col widths) not
  yet emitted

None are visually significant; sample with `cat parity-sweep-out/user-manual/epub-struct/EPUB___datasync_user_manual.xhtml.diff`.

### 10b. `dcterms:modified` timestamp

Asciidoctor uses sweep-run time (`2026-05-15T22:50:14Z`); AdocNet uses
the source file mtime (deterministic). Single-line diff per doc, unavoidable
without reintroducing non-determinism.

### 10c. `toc.xhtml` (alongside `nav.xhtml`)

Some Asciidoctor reference EPUBs emit both `nav.xhtml` (EPUB 3) and
`toc.xhtml` (legacy reader fallback). AdocNet emits only `nav.xhtml`.
Older e-readers may not find a TOC.

---

## Reveal.js — RESOLVED in v1.0.0

Full Asciidoctor parity achieved: 36/36 docs byte-identical on slide DOM
(diff metric: 0 lines, sum 0 across corpus). 34 commits closed the gap.

**Visual-only caveat**: the side-by-side panels still show different
content because the snapshot tool renders Asciidoctor's local
`reveal.js/dist/reveal.css` paths (missing in snapshot env, fallback to
flow mode) while AdocNet uses CDN URLs (load successfully, slide-deck
mode). Content is identical — only the asset-loading strategy differs.
Documented in `docs/SESSION-HANDOFF-2026-05-11.md`.

---

## Man — PARTIALLY RESOLVED in v1.0.0

Diff reduced 21% over v1.0 arc (6968 → 5510). 11/36 docs now byte-perfect.

### 12. Stylistic roff conventions (remaining 5510 lines)

The remaining diff is **not bugs**. AdocNet emits cleaner, more idiomatic
roff than Asciidoctor (which wraps every list item in 7 lines of conditional
nroff/troff branching + horizontal positioning, etc.). Both produce
equivalent man pages. Sample:

- Asciidoctor list item: `.RS 4` + `.ie n \{\` + `\h'-04'\(bu\h'+03'\c` + `.\}` + `.el \{\` + `.  sp -1` + `.  IP \(bu 2.3` + `.\}` + `\f(CRtext\fP` + `.RE` (10+ lines)
- AdocNet list item: `.IP "\(bu" 2` + `\fBtext\fP` (2 lines)

Closing this would require emitting more verbose roff for byte parity at
no visible benefit. Deferred indefinitely; not v1.x scope.

### 12a. Tab expansion default (RESOLVED)
### 12b. ASCII hyphen escape (RESOLVED)
### 12c. Smart-quote escapes (RESOLVED)
### 12d. Bold-monospace `\f(CB` for backticks (RESOLVED)
### 12e. Numbered example titles (RESOLVED)

---

## Patterns / lessons (apply automatically)

The "icon was wrong color" thread settled into a repeatable pattern:

1. **Don't approximate** — when the reference uses a specific font /
   glyph / color, embed/use that exact resource. Drawn-circle-with-vector-glyph
   was a "looks similar" hack; embedding the actual FontAwesome was the right
   answer always.

2. **Use exact theme constants** — colors like `#19407C` come from
   `AdmonitionIcons` in
   `asciidoctor-pdf/lib/asciidoctor/pdf/converter.rb:41-47`,
   not from "looks blue-ish."

3. **Verify with PyMuPDF span extraction** — color/font/size differences
   that are invisible at panel resolution show up immediately in span
   attributes. Tool: `tools/pdf-visual-diff.py`, plus quick scripts at
   `/c/tmp/spacing-check/*.py` for one-off checks.

4. **Lazy-load embedded resources** — large fonts (FA Solid 200KB, FA
   Regular 34KB) only embed when actually needed. Pattern:
   ```csharp
   _fontAwesome = _useIconFeature
       ? TryLoadEmbeddedFont(writer, "Resources.foo.ttf", "FA")
       : null;
   ```
   Tests assert "no `/CIDFontType2`" for documents that don't use the
   feature, so unconditional embedding will fail them.

5. **Tracker discipline** — when something is "small enough to skip for
   now", write it here. Don't trust memory across sessions.

---

## How to extend this list

When deferring an item, add a section with:
- **Symptom.** What the user / sweep sees.
- **Root cause.** What's actually broken.
- **Plan.** Specific files / line ranges / code sketches.
- **Reference values.** Exact colors / glyphs / metrics from REF when known.
- **Why deferred.** What's blocking; e.g. needs parser change, cosmetic only,
  large scope.

Remove the section once fixed and verified by a sweep.
