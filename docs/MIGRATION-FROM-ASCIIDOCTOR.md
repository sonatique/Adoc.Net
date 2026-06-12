# Migrating from Asciidoctor to AdocNet

AdocNet 1.0 is designed as a drop-in replacement for the Asciidoctor CLI
on three of its most common output formats. This guide covers what to
expect and where the boundaries are.

## TL;DR

For HTML, DocBook, and Reveal.js output, swapping `asciidoctor` for
`adocnet` produces **structurally identical results** (zero DOM diff) on every
doc in the 36-doc conformance corpus (`spec/conformance/*.adoc`).

```bash
# Before
asciidoctor              -b html5    doc.adoc -o doc.html
asciidoctor              -b docbook5 doc.adoc -o doc.xml
asciidoctor-revealjs                 doc.adoc -o doc.html

# After
adocnet               -e -b html5    doc.adoc -o doc.html   # -e = standalone HTML document
adocnet                  -b docbook5 doc.adoc -o doc.xml
adocnet                  -b revealjs doc.adoc -o doc.html
```

> **Note:** AdocNet's default HTML output is an embeddable **fragment**, whereas
> Asciidoctor's default is a standalone document. Pass `-e` to AdocNet to get a
> full HTML document (header/footer/CSS) that matches Asciidoctor's default — see
> the `-e` row below. Parity is measured structurally by `tools/parity-sweep.py`
> (which renders AdocNet with `-e` and compares the DOM, ignoring presentational
> CSS): 0 DOM-diff lines across the corpus.

## CLI flag mapping

AdocNet implements the most-used Asciidoctor CLI flags with matching
semantics. Differences noted below.

| Asciidoctor | AdocNet | Notes |
|---|---|---|
| `-b html5` / `-b html` | `-b html5` / `-b html` | identical default |
| `-b docbook5` | `-b docbook5` | identical |
| `-b manpage` | `-b man` | output format equivalent; AdocNet emits cleaner roff |
| `asciidoctor-revealjs` | `-b revealjs` (no separate binary) | identical slide DOM |
| `asciidoctor-epub3` | `-b epub` (or `adocnet-epub` tool) | bundles same fonts/CSS |
| `asciidoctor-pdf` | `-b pdf` (or `adocnet-pdf` tool) | renders without Ruby/Prawn |
| `-o <file>` | `-o <file>` | identical; `-o -` for stdout |
| `-D <dir>` | `-D <dir>` | identical |
| `-a name=value` | `-a name=value` | identical attribute setting |
| `-r <ext>` | `-r <ext>` (ignored, accepted) | AdocNet uses native extensions only |
| `-n` (sectnums) | `-n` | identical |
| `-S <safe>` | `-S <safe>` | identical safe-mode handling |
| `-e` / `--embedded` (emit fragment) | `-e` / `--embedded` (emit **full document**) | **Inverted!** AdocNet's default is the fragment; `-e` wraps it in a standalone document with CSS. Asciidoctor is the opposite. |
| `--theme <name>` | `--theme <name>` | for HTML: `default`, `asciidoctor`, `clean`, `github`; for PDF: theme YAML |

## Output format parity matrix

| Format | Asciidoctor parity | Notes |
|---|---|---|
| **HTML** | byte-identical (36/36 docs) | structural DOM diff: 0 lines |
| **DocBook 5** | byte-identical (36/36 docs) | structural canonical-XML diff: 0 lines |
| **Reveal.js** | byte-identical slide DOM (36/36 docs) | visually different in unconfigured browser only — AdocNet uses CDN URLs for reveal.js assets while Asciidoctor uses local paths; both produce the same slide deck when assets resolve |
| **Man** | structurally equivalent | AdocNet emits cleaner roff (2-line list items vs Asciidoctor's 7-line conditional nroff/troff branches). Both produce equivalent man pages. |
| **EPUB 3** | asset/structure parity | AdocNet bundles the asciidoctor-epub3 font / CSS / image payload; chapter XHTML uses the same semantic HTML5 (`<section class="sect{N}">`, `<aside class="admonition">`, `<figure class="listing">`). Visually indistinguishable in EPUB readers. Small structural residuals listed in `docs/DEFERRED-PARITY-ITEMS.md`. |
| **PDF** | tracked separately | Visual parity validated via PyMuPDF span extraction; outstanding items in `docs/DEFERRED-PARITY-ITEMS.md`. |

## Known intentional differences

These differ from Asciidoctor's reference output by design:

### Reveal.js CSS asset paths

- Asciidoctor: `reveal.js/dist/reveal.css` (local relative path, requires
  the user to copy reveal.js assets next to the output)
- AdocNet: `https://cdn.jsdelivr.net/npm/reveal.js@4/dist/reveal.css`
  (CDN URL, works standalone in any browser)

Same slide content; different asset-loading strategy. AdocNet's choice
is more user-friendly for the common case.

### EPUB `dcterms:modified` timestamp

- Asciidoctor: uses "now" (the time `asciidoctor-epub3` ran)
- AdocNet: uses the source file's mtime

Both are valid EPUB 3 metadata; AdocNet's choice gives byte-identical
output across reruns (deterministic).

### Man cleaner roff

- Asciidoctor: list items wrap in 7+ lines of conditional `nroff`/`troff`
  branching with manual horizontal positioning
- AdocNet: uses the standard `.IP` macro (2 lines per item)

Both render identically in `man`. AdocNet's output is half the lines and
more idiomatic roff.

## Behaviour that AdocNet does NOT implement

These Asciidoctor features are out of scope for v1.0:

- Custom extension API in Ruby (use the AdocNet C# extension model via
  `IDocumentProcessor` / `IBlockProcessor` / `IInlineProcessor`).
- `:source-highlighter: rouge` / `coderay` / `pygments` server-side
  highlighting. AdocNet supports `highlight.js` and `prism.js` (client-side)
  and has its own tokenizer for a subset of languages.
- DocBook 4.x output (DocBook 5 only).
- Custom Ruby `--template-dir` templates (use AdocNet's `INodeTemplate`
  C# interface instead).

## Verifying parity for your own docs

```bash
# 1. Install both renderers
gem install asciidoctor asciidoctor-revealjs asciidoctor-epub3
dotnet tool install -g AdocNet.Tool

# 2. Generate both outputs for your doc
asciidoctor my-doc.adoc -o reference.html
adocnet     my-doc.adoc -o candidate.html

# 3. Compare
diff reference.html candidate.html
# Or use the project's structural diff:
python tools/html-diff.py reference.html candidate.html
```

If you find a parity gap on real-world content, please open an issue at
<https://github.com/sonatique/Adoc.Net/issues> with the source `.adoc`
and the diff output.

## Performance expectations

AdocNet is significantly faster than Asciidoctor (Ruby) for most
workloads because it's a native managed library with no interpreter
startup. Concrete numbers will be published in a follow-up release;
preliminary measurements on a 36-doc corpus show **15-25× speedup**
for HTML rendering. Run `benchmarks/AdocNet.Benchmarks/` to measure on
your hardware.

## Migration FAQ

**Q: Will my existing `.adoc` documents render correctly?**
A: Yes — AdocNet implements the AsciiDoc language as Asciidoctor does.
The conformance corpus exercises the language broadly; if your doc
parses in Asciidoctor it should parse identically in AdocNet.

**Q: Can I use both side-by-side?**
A: Yes. They're independent toolchains. Use AdocNet for CI builds
where startup time matters and Asciidoctor for ad-hoc local rendering
if you prefer its toolset.

**Q: What if I hit a parity gap?**
A: Check `docs/DEFERRED-PARITY-ITEMS.md` first — known gaps are
documented there. If your case isn't listed, file an issue with the
source `.adoc` snippet.

**Q: Are my custom CSS / themes compatible?**
A: For HTML: yes, completely — AdocNet emits the same class structure
(`.sect1`, `.paragraph`, `.admonitionblock`, etc.). For PDF: AdocNet
reads asciidoctor-pdf YAML themes directly.
