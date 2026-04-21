# EPUB Parity — First-Pass Findings (HOWTO.adoc)

**Date**: 2026-04-19 (post-v1.0.0-beta.25)
**Tool**: `tools/epub-diff.py`
**Reference engine**: `asciidoctor-epub3 2.3.0`
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -b epub`)
**Reference document**: `C:/Workspace/Adoc2Pdf/HOWTO.adoc`

## Headline numbers

| Side | Parts | Total bytes |
|---|---|---|
| Reference (asciidoctor-epub3) | 25 | 935,786 |
| Candidate (AdocNet) | 6 | 9,081 |

The reference is ~120× larger because asciidoctor-epub3 bundles default
fonts, three stylesheets, and avatar/headshot placeholder images. AdocNet
ships only the bare minimum.

## Structural gaps (parts only on one side)

### Only in reference (23)

```
EPUB/_how_to_generate_pdf_from_adoc.xhtml   # chapter content
EPUB/nav.xhtml                              # EPUB3 navigation document
EPUB/toc.ncx                                # legacy NCX (EPUB2 fallback)
EPUB/package.opf                            # package descriptor
EPUB/styles/epub3.css                       # main stylesheet
EPUB/styles/epub3-css3-only.css             # progressive enhancement CSS
EPUB/styles/epub3-fonts.css                 # font face declarations
EPUB/fonts/*.ttf  (×13)                     # M+ 1p, M+ 1mn, NotoSerif, FontAwesome
EPUB/avatars/default.jpg
EPUB/headshots/default.jpg
META-INF/com.apple.ibooks.display-options.xml
```

### Only in candidate (4)

```
OEBPS/content.opf                            # AdocNet's package descriptor (different name + dir)
OEBPS/content.xhtml                          # all content in single file
OEBPS/style.css                              # single minimal stylesheet
OEBPS/toc.xhtml                              # AdocNet's TOC (different filename)
```

## Directory naming convention

Reference uses `EPUB/` as the OPF root directory. Candidate uses `OEBPS/`.
Both are valid EPUB3 (the spec doesn't mandate a name), but the diff is
non-trivial — it changes the path of every internal reference. Two paths
forward: (a) align AdocNet to `EPUB/` to match asciidoctor's convention and
make diffs cleaner, (b) keep `OEBPS/` and accept the divergence.

Recommendation: align to `EPUB/` for consistency with the reference. The
container.xml already differs only in this path — fixing it would make
that part identical too.

## Per-file gap analysis

### `META-INF/container.xml` — only the rootfile path differs

Diff:
```
-    <ns0:rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" />
+    <ns0:rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
```

Solved automatically once the OPF directory + filename align.

### `package.opf` — multiple gaps in metadata, manifest, spine

**Metadata gaps:**

| Asciidoctor reference | AdocNet candidate | Issue |
|---|---|---|
| `<dc:identifier id="pub-identifier">_how_to_generate_pdf_from_adoc</dc:identifier>` | `<dc:identifier id="uid">urn:adocnet:00000000-0000-0000-0000-000000000000</dc:identifier>` | AdocNet uses zero-UUID; reference derives from doc title slug |
| `<meta property="identifier-type" refines="#pub-identifier">uuid</meta>` | (missing) | Identifier-type metadata not emitted |
| `<dc:title id="pub-title">…</dc:title>` | `<dc:title>…</dc:title>` (no id) | Missing `id` attribute on title |
| `<dc:date>2026-03-09T21:01:22Z</dc:date>` | (missing) | Publication date not emitted |
| `<dc:language id="pub-language">en</dc:language>` | `<dc:language>en</dc:language>` | Missing `id` attribute on language |
| (no `dc:creator` since none set in source) | `<dc:creator>Unknown</dc:creator>` | AdocNet emits placeholder; reference omits |

**Manifest / spine gaps:**

- AdocNet bundles all chapters into one `content.xhtml` and a separate `toc.xhtml`.
  Reference splits per chapter and has both `nav.xhtml` (EPUB3) and `toc.ncx` (EPUB2
  fallback). For drop-in parity, AdocNet should emit both nav formats and split
  per top-level section.

### `nav.xhtml` / `toc.xhtml` — section number prefix missing

This is **the same bug pattern as PDF outline** (fixed in beta.25). The TOC
entries should reflect the rendered section title — including the
`:sectnums:` numeric prefix.

Reference:
```html
<li><a href="_how_to_generate_pdf_from_adoc.xhtml#_generate_pdf_from_adoc">1. Generate PDF from ADOC</a></li>
```

Candidate:
```html
<li><a href="content.xhtml#_generate_pdf_from_adoc">Generate PDF from ADOC</a></li>
```

Other gaps in the nav document:
- AdocNet's TOC is flat (top-level sections only). Reference nests sections
  under the document title.
- AdocNet missing the `<nav epub:type="landmarks">` block (lists "Start of
  Content" entry — required for some readers).
- AdocNet missing `<link rel="stylesheet">` references in `<head>`.

### `toc.ncx` — entirely missing

AdocNet doesn't emit the legacy NCX TOC. Required for EPUB2 backward
compatibility. Many readers (notably older Kindles) need this.

### Default stylesheets, fonts, images — entirely missing

These are asciidoctor-epub3 defaults. AdocNet ships a single 416-byte
`style.css` vs reference's three CSS files + 13 TTF fonts. Whether to
replicate this is a product question — for visual parity at the reader
level, yes; for minimal output, the current AdocNet behavior is fine.

## Prioritised fix list

In order of value × cost:

### Tier 1 (small fixes, high value, mirror PDF beta.25 work) — DONE

All Tier 1 fixes landed post-first-pass. Status:

1. ✅ **TOC section number prefix** — when `:sectnums:` is set, top-level
   section entries in `toc.xhtml` are prefixed `"1. "`, `"2. "`, etc.
   Same root cause and fix shape as the PDF outline bug from beta.25.
2. ✅ **TOC hierarchy** — section entries are now nested under the
   document title in a single top-level `<li>`. Matches reference shape.
3. ✅ **`dc:identifier` derivation** — uses the doc title slug
   (`_how_to_generate_pdf_from_adoc`) when a title exists; falls back to
   the deterministic urn only when no title is present. Also adds the
   `<meta property="identifier-type" refines="#pub-identifier">uuid</meta>`
   sibling that the reference emits.
4. ✅ **`dc:creator` only when set** — removed the "Unknown" placeholder;
   element is now omitted when no author is present.
5. ✅ **`<dc:date>`** — emitted when `revdate` is set on the document
   (e.g. `v1.0, 2025-06-01` author/revision line).
6. ✅ **`id` attributes on `dc:title` and `dc:language`** —
   `id="pub-title"` and `id="pub-language"` now match the reference.
7. ✅ **Bonus: landmarks nav** — `<nav epub:type="landmarks">` block with
   "Start of Content" entry. Required by some readers; the reference
   always emits it.
8. ✅ **Bonus: stylesheet link in TOC `<head>`** —
   `<link rel="stylesheet" href="style.css"/>` so the TOC renders themed.

Regression coverage: 8 new tests in `EpubRendererTests` lock these
behaviours.

### Tier 2 (more involved, broader output changes)

7. ✅ **Chapter naming + book-doctype splitting** —
   - Article doctype: the single chapter file is now named after the
     document title slug (e.g. `_how_to_generate_pdf_from_adoc.xhtml`)
     instead of the fixed `content.xhtml`. Matches asciidoctor-epub3.
   - Book doctype (`:doctype: book`): one xhtml per top-level section,
     each named after the section title slug
     (`_first_chapter.xhtml`, `_second_chapter.xhtml`, etc.).
   - Manifest declares one `<item>` per chapter; spine references them
     in source order so EPUB readers paginate correctly.
   - TOC nav.xhtml and toc.ncx anchors now point at the right per-chapter
     filename instead of the old fixed name.
   - 7 new regression tests covering article naming, book splitting,
     spine ordering, and TOC anchor correctness.
8. ✅ **Emit `toc.ncx`** — legacy EPUB2 navigation.
9. ✅ **Landmarks nav block** — done in Tier 1.

### Tier 3 (product decisions, larger scope)

10. ✅ **Default stylesheet enriched** — bundled `style.css` now covers
    Asciidoctor's structural classes (sect1..sect5, paragraph,
    listingblock, exampleblock, sidebarblock, admonitionblock with
    note/tip/warning/caution/important variants, quoteblock, tableblock,
    hdlist, kbd, mark, sub/sup) using web-safe font stacks (with M+/Noto
    Serif as preferred when readers ship them). EPUB readers without
    their own stylesheet now show themed output. 1 regression test added.
11. **Default fonts** — deferred. Bundling 600 KB of TTF data per EPUB
    is overkill when most readers ship their own font selection; the
    bundled CSS uses generic `serif`/`sans-serif` fallbacks plus
    asciidoctor's preferred fonts when available.
12. **Apple Books display options file** — deferred (single-vendor
    targeting, low generic value).

### Tier 3 (product decisions, larger scope)

10. **Default stylesheets** — match reference's `epub3.css` /
    `epub3-css3-only.css` / `epub3-fonts.css`.
11. **Default fonts** — bundle M+ and NotoSerif, or skip and rely on
    reader defaults.
12. **Apple Books display options file** — small, but pure-iBooks targeting.

## Resuming this work

Re-run the first pass any time:

```bash
ruby C:/Users/sylva/.local/share/gem/ruby/3.4.0/gems/asciidoctor-epub3-2.3.0/bin/asciidoctor-epub3 \
    -o /tmp/epub-diff/reference.epub C:/Workspace/Adoc2Pdf/HOWTO.adoc

adocnet C:/Workspace/Adoc2Pdf/HOWTO.adoc -b epub -o /tmp/epub-diff/adocnet.epub

python tools/epub-diff.py /tmp/epub-diff/reference.epub /tmp/epub-diff/adocnet.epub
```

Output goes to `epub-diff-out/` (see `_summary.md` for the index, individual
`*.diff` files for per-part diffs).

Compare the prioritised fix list above against the new `_summary.md` to see
which gaps remain. Tackle Tier 1 first — those are mechanical changes
mirroring the PDF beta.25 work.
