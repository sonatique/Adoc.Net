# HTML Parity — First-Pass Findings (HOWTO.adoc)

**Date**: 2026-04-20 (post-v1.0.0-beta.25)
**Tool**: `tools/html-diff.py`
**Reference engine**: `asciidoctor 2.0.26`
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -e`)
**Reference document**: `C:/Workspace/Adoc2Pdf/HOWTO.adoc`

## Headline numbers

| Side | Body tags | First-pass DOM diff lines | After fixes |
|---|---|---|---|
| Reference (asciidoctor) | 156 | — | — |
| Candidate (AdocNet) | 160 → 162 → 151 | 804 | **0** |

The diff tool ignores inline CSS, attribute order, and a small set of
generator-specific attributes (`style`, `data-lang`, `rel`, `tabindex`).
Class lists are sorted before comparison. The "before" measurement is
embedded mode (default `adocnet`) vs the asciidoctor reference.

## What carried over from PDF / EPUB lessons

This is the third format we've put through the playbook. The pattern
repeated again: a small number of structural wrappers explained the bulk
of the diff. Same shape as the PDF outline + EPUB nav fixes.

## First-pass gaps and fixes

### Tier 1 (mechanical) — DONE

1. ✅ **`<body class="article">`** — asciidoctor wraps the body so theme
   CSS can target the doctype. AdocNet emitted bare `<body>`. Fixed in
   `HtmlDocumentRenderer.AppendDocumentPrologue`. Body class reflects the
   `:doctype:` attribute (`article`/`book`/`manpage`/`inline`); defaults
   to `article`.

2. ✅ **`<div id="header">`** — asciidoctor wraps the document title `<h1>`
   in a header div so theme CSS can position it independently.
   Fixed in `HtmlRenderer.RenderDocumentBody` (full-document mode only).

3. ✅ **`<div id="content">`** — asciidoctor wraps everything between the
   header and footer in a content div. Fixed in `HtmlRenderer.RenderDocumentBody`
   — open the wrapper after the header, close before the footnotes section.

4. ✅ **Footer revdate** — asciidoctor's footer text reads
   `"Last updated <revdate>"` when `:revdate:` is set. AdocNet was emitting
   only the label. Fixed in `HtmlDocumentRenderer.AppendDocumentEpilogue`.

Result: DOM diff dropped from **804 → 42 lines**. 7 new regression tests
in `HtmlRendererTests` lock the new behaviour, plus 3 existing tests in
`HtmlThemeTests` updated to expect `<body class="article">`.

### Tier 2 (parser bug, larger scope) — DONE

5. ✅ **List splitting on continuation blocks** — accounted for the
   remaining 42 diff lines. Closed by tightening the blank-line list
   preservation rule in `BlockParser.cs`.

   The previous rule preserved the list across a blank line only when
   the previous item had children (continuation content). In a list
   where Item 1 has continuation, Item 2 is plain, Item 3 has
   continuation, the rule closed the list after Item 2 because Item 2
   had no children. New rule: preserve list context whenever the next
   non-blank line is any list item. This matches Asciidoctor and also
   fixes simple `* A\n\n* B` cases that previously split into two lists.

   4 regression tests added in `ListParserTests`. All 2888 existing
   tests still pass.

### Tier 3 (test-fixture differences) — ACCEPTED

6. **Default footer date when `:revdate:` is unset** — asciidoctor falls
   back to the document file's mtime or current time. AdocNet emits just
   the label. Both are valid; asciidoctor's behaviour is non-deterministic
   without `:reproducible:`. Keeping AdocNet's deterministic-by-default
   behaviour is preferable.

## Resuming this work

Re-run the first pass any time:

```bash
asciidoctor -o /tmp/html-diff/reference.html C:/Workspace/Adoc2Pdf/HOWTO.adoc
adocnet C:/Workspace/Adoc2Pdf/HOWTO.adoc -e -o /tmp/html-diff/adocnet.html
python tools/html-diff.py /tmp/html-diff/reference.html /tmp/html-diff/adocnet.html
```

Output goes to `html-diff-out/` (`_summary.md` for the index, `dom.diff`
for the full unified DOM diff, `ref.dump` and `cand.dump` for the
canonical per-renderer dumps).

HTML parity against `HOWTO.adoc` is currently at zero diff lines. Further
sweeps should broaden the input set with files from `spec/conformance/*.adoc`
to expose any remaining structural gaps that HOWTO doesn't exercise.
