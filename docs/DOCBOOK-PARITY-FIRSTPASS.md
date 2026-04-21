# DocBook Parity — First-Pass Findings (HOWTO.adoc)

**Date**: 2026-04-20 (post-v1.0.0-beta.25)
**Tool**: `tools/docbook-diff.py`
**Reference engine**: `asciidoctor 2.0.26` (`-b docbook5`)
**Candidate engine**: `AdocNet 1.0.0-beta.25` (`adocnet -b docbook5`)

## Headline numbers

| Side | Tags | Canonical diff lines | After fixes |
|---|---|---|---|
| Reference | 96 | — | — |
| Candidate | 97 → 98 → 95 → 96 | 154 | **0** |

The tool canonicalises both XML trees (sorted attributes, normalised
whitespace, stripped doctype/declaration) then emits a unified diff of
the pretty-printed canonical form.

## What carried over from PDF / EPUB / HTML lessons

Same structural-wrapper pattern showed up for the fourth time: a handful
of mechanical omissions account for most of the diff. Once fixed, the
residual is dominated by the parser list-splitting bug that also bit HTML.

## First-pass gaps and fixes

### Tier 1 (mechanical) — DONE

1. ✅ **`xml:lang` on root `<article>`** — Asciidoctor adds this (defaults
   to `"en"`, honours `:lang:` attribute). AdocNet omitted it entirely.

2. ✅ **`<info>` wrapper for document metadata** — Asciidoctor wraps
   `<title>` (and `<date>` when `:revdate:` is set) inside an `<info>`
   element. AdocNet emitted a bare `<title>` directly under `<article>`.
   Both forms are valid DocBook5, but Asciidoctor consistently uses
   `<info>` and many downstream toolchains expect it.

3. ✅ **`<date>` element inside `<info>`** — emitted from `:revdate:`.

4. ✅ **`<simpara>` instead of `<para>` for inline-only paragraphs** —
   Asciidoctor uses `<simpara>` (DocBook5's paragraph for text without
   nested block content) for body paragraphs and list items; reserves
   `<para>` for when nested block content is present. AdocNet emitted
   `<para>` for both. Fixed in `RenderParagraph` (top-level) and the
   list-item inline path in `RenderList`.

Result: canonical diff dropped from **154 → 56 lines**. 6 new regression
tests added in `DocBookRendererTests` plus 1 updated (`Paragraph_rendered_as_para`
→ `Paragraph_rendered_as_simpara`).

### Tier 2 (parser bug shared with HTML) — DONE

5. ✅ **List splitting on continuation blocks** — closed by the same
   `BlockParser.cs` blank-line preservation fix that closed the HTML gap.
   Reduced canonical diff from 56 → 43 lines.

### Tier 3 (residual simpara/para) — DONE

6. ✅ **`<simpara>` vs `<para>` for list items containing continuation
   blocks** — closed in `DocBookRenderer.RenderListItem`. Asciidoctor
   always wraps the item's inline text in `<simpara>` and renders any
   continuation blocks as siblings inside the `<listitem>`. AdocNet was
   switching to `<para>` whenever the item had children. Fixed: always
   use `<simpara>` for the item text. 1 regression test added in
   `DocBookRendererTests`.

   Reduced canonical diff from 43 → 10 lines.

### Tier 4 (`<date>` mtime fallback) — DONE

7. ✅ **`<date>` falls back to file mtime** — closed via two changes:

   - `DocBookRenderer.RenderDocument` now emits `<date>` from
     `:revdate:` → `:docdate:` (in that precedence order), matching
     Asciidoctor's date resolution.
   - `AdocParser.Parse(text, options)` overrides `:docdate:`/`:docyear:`
     from the file's last-write timestamp when `SourceFilePath` is set,
     unless the source explicitly sets `:docdate:`, `:revdate:`, or
     `:reproducible:`. The CLI's parse path automatically benefits.
   - `AdocEngine.ConvertFile` performs the same injection for
     programmatic file callers via text-prefix attribute injection
     (parser-level fix benefits the CLI; engine-level fix benefits the
     library API).

   Reduced canonical diff from 10 → **0 lines** against asciidoctor
   for HOWTO.adoc. 4 new tests in `DocBookRendererTests` (date
   precedence, reproducible opt-out) plus 5 new tests in a new
   `ConvertFileMtimeTests` file.

### Tier 5 (structural cases not yet reviewed)

8. Other DocBook-specific elements (admonitions, examples, callouts,
   tables) haven't been audited yet — HOWTO.adoc doesn't exercise them
   heavily. A second-pass sweep against `spec/conformance/*.adoc` would
   surface any remaining structural gaps there.

## Resuming this work

```bash
asciidoctor -b docbook5 -o /tmp/docbook-diff/reference.xml HOWTO.adoc
adocnet HOWTO.adoc -b docbook5 -o /tmp/docbook-diff/adocnet.xml
python tools/docbook-diff.py /tmp/docbook-diff/reference.xml /tmp/docbook-diff/adocnet.xml
```

Output: `docbook-diff-out/_summary.md`, `canonical.diff`, and the
`ref.canonical.xml` / `cand.canonical.xml` dumps.
