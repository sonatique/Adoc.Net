# Man Page Parity — First-Pass Findings

**Date**: 2026-04-20 (post-v1.0.0-beta.25)
**Tool**: `tools/man-diff.py`
**Reference engine**: `asciidoctor 2.0.26` (`-b manpage`)
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -b man`)
**Reference document**: minimal manpage fixture (`/tmp/man-test.adoc`)

## Headline numbers

| Side | Macro count | First-pass diff lines | After fixes |
|---|---|---|---|
| Reference | 106 | — | — |
| Candidate | 25 → 43 | 163 | **142** |

The macro-count gap is striking: Asciidoctor emits ~4× more roff
directives. Most of them are boilerplate (preamble, URL macros, font
setup) that AdocNet skips entirely. The two outputs render to similar
visible man pages — but the source diverges substantially because
Asciidoctor produces "modern roff with hyperlink + font support"
while AdocNet produces classic minimal roff.

## What carried over from earlier formats

Same playbook: render both, compare structurally, identify mechanical
gaps. The first-pass macro-count summary table makes the structural
deltas immediately legible.

## First-pass gaps and fixes

### Tier 1 (mechanical) — DONE

1. ✅ **Quoted `.SH` and `.SS` headings** — Asciidoctor emits
   `.SH "NAME"` and `.SS "Sub"` (quoted). AdocNet emitted bare `.SH NAME`.
   Both forms are valid roff, but quoted form is the modern convention
   and matches Asciidoctor exactly. Fixed in `ManRenderer.RenderSection`.
   3 existing tests in `ManRendererTests` updated.

   Reduced normalised diff from 163 → 157 lines.

### Tier 2 (preamble + font reset) — DONE

2. ✅ **Standard preamble (`.ie`/`.el`/`.ss`/`.nh`/`.ad`/URL macros)** —
   `ManRenderer.AppendStandardPreamble` now emits the 18-line preamble
   immediately after `.TH`. Matches asciidoctor's output line-for-line
   (apostrophe glyph guard, sentence spacing, no hyphenation,
   left-align, URL/MTO macros, groff feature detection block).

4. ✅ **Font reset macro: `\fP` vs `\fR`** — all 17 sites in
   `ManRenderer.cs` and `ManRendererInlines.cs` now emit `\fP`
   (previous font) instead of `\fR` (regular). 7 existing tests updated.

   Normalised diff dropped from 163 → **142 lines**. 1 new regression
   test added (`Standard_preamble_emitted_after_TH`).

### Tier 3 (block-separation reformat) — INTENTIONALLY NOT PURSUED

3. **`.sp` / `.RS` / `.RE` instead of `.PP` / `.IP` / `.TP`** —
   Asciidoctor uses `.sp` (vertical space) between blocks and
   `.RS 4` / `.RE` indentation runs with `.ie n \{ ... \}` groff
   conditionals (~6 lines per bullet item) for lists. AdocNet uses
   the classical `.PP` (paragraph), `.IP "\(bu" 2` (bullet list),
   `.TP` (option list) macros (~1 line per bullet item).

   The 142-line diff measures **source-format style**, not output
   quality. Comparing the actual rendered output (via `pandoc -f man
   -t plain` as a stand-in for what `man(1)` shows the user):

   | metric | asciidoctor reference | AdocNet candidate |
   |---|---|---|
   | rendered text size | 64 lines | 48 lines |
   | bullet rendering | `·` and item on separate lines + blank between | clean single-line `- item` |
   | source-file size | 1767 bytes | 1014 bytes |

   AdocNet's classical-roff dialect:
   - Renders cleanly through groff/man (the actual user-facing case)
   - Renders **better** through pandoc and other cross-format tools
     (asciidoctor's `.RS 4` / `.IP \(bu` pattern confuses pandoc's
     roff parser into producing two-line bullets with blank lines)
   - Is more portable to mandoc on BSD
   - Is dramatically more readable as roff source (1014 vs 1767 bytes
     for the same content)

   **Forcing parity here would require ~150 lines added to ManRenderer
   to emit asciidoctor's `.ie n \{ ... \}` 6-line groff conditional
   per bullet, and would make AdocNet's roff source LESS portable and
   pandoc-converted output WORSE.** No benefit to anyone who reads the
   man page via `man(1)`. Decision: keep the classical dialect; do not
   pursue source-format parity for the man backend.

### Tier 3 (intentionally accepted)

5. **Generator metadata comments** — Asciidoctor's preamble includes
   `.\" Title:`, `.\" Author:`, `.\" Generator: Asciidoctor X.X.X`,
   `.\" Date:` etc. These are documentation comments only; man(1)
   ignores them. AdocNet's man-diff already strips comment lines, so
   they don't show up in the diff. Keeping AdocNet's output free of
   identifying comments is harmless and cleaner.

## Resuming this work

```bash
asciidoctor -b manpage -o /tmp/man-ref.man <fixture>.adoc
adocnet <fixture>.adoc -b man -o /tmp/man-cand.man
python tools/man-diff.py /tmp/man-ref.man /tmp/man-cand.man
```

Output: `man-diff-out/_summary.md` (macro counts), `normalized.diff`
(line-by-line diff after stripping comments and normalising the `.TH`
date), `ref.normalised.man` and `cand.normalised.man` (the inputs to
the diff).

The biggest single lever for Tier 2 is the standard preamble + `\fP`
swap (~40 lines added/changed). The `.sp`/`.RS`/`.RE` reformat is the
larger project; defer until Tier 1+2 from other formats are complete.
