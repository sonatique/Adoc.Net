# Reveal.js Parity — First-Pass Findings

**Date**: 2026-04-20 (post-v1.0.0-beta.25)
**Tool**: `tools/revealjs-diff.py`
**Reference engine**: `asciidoctor-revealjs 5.2.0`
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -b revealjs`)
**Reference document**: minimal slides fixture (`/tmp/slides-test.adoc`)

## Headline numbers

| Side | Slide-subtree tags | First-pass diff lines | After fixes |
|---|---|---|---|
| Reference | 33 | — | — |
| Candidate | 21 → 33 | 117 | **0** |

The diff tool extracts the `<div class="slides">` subtree and ignores
the surrounding CDN bootstrap (scripts, themes, plugin init) — those
are template responsibilities, not parser/renderer parity concerns.

The slide hierarchy itself is already correct: section depth
distribution (4 horizontal + 3 vertical) matches the reference.
Differences are about wrapper divs, IDs, and content-class hooks.

## What carried over from earlier formats

Same pattern as HTML: Asciidoctor wraps content in semantic divs
(`.slide-content`, `.paragraph`, `.ulist`) so theme CSS can target
them. AdocNet emits the bare structural skeleton.

## First-pass gaps and fixes

### Tier 1 (mechanical) — DONE

1. ✅ **Section IDs on `<section>` slides** — Asciidoctor emits
   `<section id="_intro">` on each slide using the section's auto-ID
   or explicit `[#anchor]`. AdocNet emitted bare `<section>`. Fixed in
   `RevealjsRenderer.RenderSlide` via a new `AppendSlideOpenTag` helper.

2. ✅ **Vertical slides use `<h2>`, not `<h3>`** — Reveal.js treats
   horizontal and vertical slides as the same hierarchy (one slide each,
   just nested in an outer `<section>`). Asciidoctor reflects this with
   `<h2>` for both. AdocNet was using `<h3>` for verticals, breaking
   the visual hierarchy.

   Reduced slide DOM diff from 117 → 103 lines. 2 regression tests
   added in `RevealjsRendererTests`. 2 existing tests updated.

### Tier 2 (semantic wrappers) — DONE

3. ✅ **`<div class="slide-content">` wrapper** — emitted around the
   non-heading body content of each slide in `RevealjsRenderer.RenderSlide`.
   Heading-only slides skip the wrapper.

4. ✅ **Asciidoctor block wrappers** — paragraphs now emit
   `<div class="paragraph"><p>...</p></div>`, lists emit
   `<div class="ulist"><ul>` / `<div class="olist"><ol>`, list items
   wrap text in `<p>` for parity with Asciidoctor.

5. ✅ **Title slide class hook** — title slide now emits
   `<section class="title" data-state="title">`.

   Slide DOM diff dropped from 117 → **0 lines** against
   `asciidoctor-revealjs 5.2.0`. 6 new regression tests added in
   `RevealjsRendererTests`; 3 existing tests updated.

### Tier 3 (CDN scaffolding) — INTENTIONALLY DIFFERENT

6. **Bootstrap differences** — Asciidoctor pulls reveal.js from a
   relative path (`reveal.js/dist/reveal.js`), expects the user to
   provide the framework. AdocNet pulls from a CDN. Neither is
   "wrong"; they're different deployment models. Out of scope for
   first-pass parity.

## Resuming this work

```bash
asciidoctor-revealjs -o /tmp/slides-ref.html <fixture>.adoc
adocnet <fixture>.adoc -b revealjs -o /tmp/slides-cand.html
python tools/revealjs-diff.py /tmp/slides-ref.html /tmp/slides-cand.html
```

Output: `revealjs-diff-out/_summary.md` (section-depth distribution,
tag counts, class deltas, ID deltas), `dom.diff` (full canonical-DOM
unified diff of the slides subtree), `ref.dump`/`cand.dump` (the
inputs to the diff).

The Tier 2 wrappers are the obvious next target. Adding `slide-content`
alone should close ~30 of the remaining 103 lines.
