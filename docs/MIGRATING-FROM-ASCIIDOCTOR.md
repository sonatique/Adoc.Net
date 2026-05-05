# Migrating from Asciidoctor to AdocNet

**Version**: AdocNet v1.0.0
**Audience**: existing Asciidoctor users (Ruby) considering AdocNet (.NET)

AdocNet aims to be a drop-in replacement for Asciidoctor for the most
common output formats. This guide maps Asciidoctor's CLI surface, attribute
support, and behavioral choices to AdocNet so a migration produces matching
output without surprises.

## TL;DR

| Format | Migration risk |
|---|---|
| HTML  | Low — 35 of 36 spec/conformance docs produce structurally identical output. |
| DocBook | Low — 31 of 36 docs at zero diff against `asciidoctor -b docbook5`. |
| EPUB  | Low — structurally compatible; intentional path differences (`EPUB/` vs `OEBPS/`). |
| PDF   | Medium — visual parity is close; pass `--pdf-theme path/to/asciidoctor-default-theme.yml` for the iconic asciidoctor look. |
| Reveal.js | Medium — slide structure works, but admonition layout uses table markup in asciidoctor; AdocNet uses div markup. |
| Man   | Medium — produces functionally equivalent man pages via groff, but uses `.PP/.IP/.TP` style instead of asciidoctor's `.sp/.RS/.RE`. |

## CLI flag mapping (`adocnet convert`)

| Asciidoctor flag | AdocNet equivalent | Notes |
|---|---|---|
| `-b html5`, `-b docbook5`, `-b manpage`, `-b epub3` | `-b html5` / `-b docbook5` / `-b man` / `-b epub` | Backend selection identical. AdocNet adds `-b pdf` and `-b revealjs`. |
| `-o outfile.html` | `-o outfile.html` | Same. |
| `-D dir` / `--destination-dir` | `-D dir` / `--destination-dir` | Same. |
| `-a name=value` / `--attribute name=value` | `-a name=value` / `--attribute name=value` | Multiple `-a` flags allowed; same semantics. |
| `-n` / `--section-numbers` | `-n` / `--section-numbers` | Same. |
| `-e` / `--embedded` | `-e` / `--embedded` | Note: AdocNet's `-e` produces a styled standalone fragment with theme CSS embedded — matches asciidoctor's typical behaviour. |
| `-r asciidoctor-pdf` (load gem) | not applicable | PDF support is built in to AdocNet via `-b pdf`. |
| `-S unsafe`/`safe`/`server`/`secure` | `-S unsafe`/`safe`/`server`/`secure` | Same four-level safe-mode model. |
| `-T template-dir` (use template engine) | `INodeTemplate` interface | AdocNet does not embed a template engine; users implement `INodeTemplate` in C# instead — see "Extensions" below. |
| `-w` / `--watch` | `--watch` / `-w` | Same. |
| `-v` / `--verbose` | `--verbose` / `-v` | Same. |
| `-q` / `--quiet` | `--quiet` / `-q` | Same. |
| `--require gem-name` | `--extensions path/to/dll` or `--extension-dir path/` | AdocNet extensions are .NET DLLs, not Ruby gems. |
| not present | `--theme default|asciidoctor|clean` | AdocNet ships with three CSS themes; `--theme asciidoctor` mimics the iconic asciidoctor look. |
| not present | `--pdf-theme path.yml` | Reuses asciidoctor-pdf YAML themes verbatim (loads asciidoctor-pdf bundled `default-theme.yml` and produces matching PDFs). |
| not present | `--pdf-fontsdir path/` | Override font search dir for PDF themes. |
| not present | `--dump-ast` | Print the parsed AST to stdout (developer tool). |
| not present | `--no-auto-extensions` | Skip auto-loading extensions from `~/.adocnet/extensions/`. |

## Document attribute support matrix

### Layout/structure

| Attribute | Status |
|---|---|
| `:doctype:` (article / book / manpage) | ✅ Full |
| `:toc:` (auto / left / right / preamble / macro) | ✅ Full |
| `:toclevels:` | ✅ Full |
| `:sectnums:` / `:sectnumlevels:` / `:sectanchors:` / `:sectlinks:` | ✅ Full |
| `:showtitle:` (in embedded mode) | ✅ Full |
| `:nofooter:` / `:nofootnotes:` | ✅ Full |
| `:last-update-label:` | ✅ Full |
| `:webfonts:` | ✅ Full (CDN font link injection) |
| `:[!]toc:` (negative-set form) | ✅ Full |

### Source code blocks

| Attribute | Status |
|---|---|
| `:source-highlighter:` (highlight.js) | ✅ Full |
| `:source-highlighter:` (rouge / pygments / coderay) | ❌ Not supported — use highlight.js or no highlighter |
| `:source-language:` | ✅ Full (default lang for source blocks) |
| `:icons:` (`font` only) | ✅ Full — Font Awesome CDN injected |
| `:icons:` (image) | ❌ Not supported |

### Images and media

| Attribute | Status |
|---|---|
| `:imagesdir:` | ✅ Full |
| `:data-uri:` | ✅ Full (base64-embed local images in HTML) |
| `:figure-caption:` | ✅ Full |
| `:table-caption:` | ✅ Full |

### Math

| Attribute | Status |
|---|---|
| `:stem:` (latexmath / asciimath) | ✅ Full — MathJax injected |
| `$$...$$` block delimiters | ✅ Full (only when `:stem:` is set) |

### Document metadata

| Attribute | Status |
|---|---|
| `:author:` / `:email:` / `:authorinitials:` | ✅ Full (`<author>`/`<email>`/`<authorinitials>` in DocBook; details block in HTML header). Initials auto-derived from author name. |
| `:revnumber:` / `:revdate:` / `:revremark:` | ✅ Full (HTML footer + DocBook `<revhistory>` when revdate or revremark is set). |
| `:docdate:` / `:docdatetime:` / `:localdate:` / `:localdatetime:` | ✅ Full — populated from file mtime when input is a file, current time for stdin. |
| `:lang:` | ✅ Full (xml:lang on root) |
| `:reproducible:` | ✅ Full — suppresses all timestamps for byte-identical output across runs. |

### Behavior toggles

| Attribute | Status |
|---|---|
| `:experimental:` | ✅ Full (enables `kbd:[Ctrl+S]`, `btn:[OK]`, `menu:File>Save`) |
| `:hide-uri-scheme:` | ✅ Full (HTML and DocBook) |
| `:linkattrs:` | ✅ Full (`link:url[text,window=_blank]` parses attributes) |
| `:skip-front-matter:` | ✅ Full (strips YAML front matter before parsing) |
| `:max-include-depth:` | ✅ Full — capped by `ParseOptions.IncludeMaxDepth` for safety |

## Output format compatibility notes

### HTML
- 35 of 36 conformance documents produce DOM-equivalent output (whitespace and attribute order normalized).
- Only known structural gap: the `[tabs]` block style on the inner Java/XML/Kotlin listing blocks doesn't yet propagate the `primary` / `secondary` CSS class for tab switching — the wrapper is correct, the inner classes need a manual workaround.
- AdocNet's default theme differs visually from asciidoctor's iconic red-on-cream. Use `--theme asciidoctor` to match.

### DocBook
- 31 of 36 documents produce byte-identical output to `asciidoctor -b docbook5`.
- Outputs DocBook 5.0 (`<article>` or `<book>` root depending on doctype).
- Known minor differences:
  - Smart-quoted text in source (`"text"`) renders as curly Unicode quotes; asciidoctor wraps it in `<quote>text</quote>`. Affects ~11 lines per impacted doc.
  - Source blocks with callout markers stripped (e.g. fixture cleanup) emit different `<callout arearefs>` than asciidoctor's empty-arearefs quirk.
- Smart-quote substitution can be disabled per-block via `[subs="-replacements"]`.

### EPUB
- Produces valid EPUB 3.0 archives.
- Path convention: `EPUB/package.opf` (AdocNet) vs `OEBPS/content.opf` (asciidoctor-epub3). Both are valid per the EPUB spec; readers handle both. This is the only structural difference.
- Default theme uses web-safe font stack; asciidoctor-epub3 bundles its own fonts (~640 KB). AdocNet does not bundle fonts by default.
- Timestamps are deterministic when `:reproducible:` is set; otherwise file mtime drives the modified date.

### PDF
- Default rendering is AdocNet's own clean theme — different look from asciidoctor-pdf out of the box.
- For asciidoctor-pdf visual parity: pass `--pdf-theme <path-to-asciidoctor-pdf-default-theme.yml>`. AdocNet's `PdfThemeLoader` reads asciidoctor-pdf's YAML format directly.
- Output is byte-identical across runs and platforms (deterministic since beta.25).
- Known limitations vs asciidoctor-pdf:
  - No floating image layout (text wraps below, not around)
  - No automatic table-of-contents pagination
  - Limited typography (no kerning pair tables, no ligature substitution)

### Reveal.js
- Top-level sections become horizontal slides; level-2 sections become vertical.
- Speaker notes via `[.notes]` block style.
- Known parity gap: asciidoctor-revealjs uses table markup for admonitions and puts the document preamble inside the title slide. AdocNet uses div markup and a separate preamble container. Visual result is similar; structural diff is large (~700 lines per doc).

### Man pages
- Produces standard groff `.man` output.
- Style differs from asciidoctor's manpage backend: AdocNet uses `.PP / .IP / .TP` paragraph macros; asciidoctor uses `.sp / .RS / .RE` indentation macros. Both render functionally equivalent man pages through `groff -man`.
- AdocNet's dialect is more portable to `mandoc` on BSD systems.

## Extension migration

Asciidoctor extensions are Ruby modules; AdocNet extensions are .NET assemblies (DLLs). **They are not interoperable.**

To port a Ruby extension:

| Ruby concept | .NET equivalent |
|---|---|
| `Asciidoctor::Extensions::BlockProcessor` | `IBlockProcessor` |
| `Asciidoctor::Extensions::InlineMacroProcessor` | `IInlineProcessor` |
| `Asciidoctor::Extensions::Treeprocessor` | `IDocumentProcessor` |
| `Asciidoctor::Extensions::Postprocessor` | `IOutputProcessor` |
| `register_for "name"` | `bool CanProcess(node)` returning true for matching nodes |
| `process(parent, target, attrs)` | `bool Process(node, RenderContext)` returning `true` to short-circuit |

Loading:
- Ruby: `require 'my-extension'` or `-r my-gem`
- AdocNet: drop the DLL into `~/.adocnet/extensions/<name>/` with an `extension.json` manifest, or pass `--extensions path/to/MyExtension.dll`.

See the AdocNet extension authoring guide (forthcoming) for a complete walkthrough.

## Known intentional differences

These are choices AdocNet has made that diverge from asciidoctor on purpose; they will not change in v1.x.

| Area | AdocNet behavior | Asciidoctor behavior | Reason |
|---|---|---|---|
| Default HTML theme | Modern clean look | Iconic red-on-cream | Use `--theme asciidoctor` to match. AdocNet's default targets contemporary docs. |
| EPUB content path | `EPUB/package.opf` | `OEBPS/content.opf` | Both valid per EPUB 3 spec. |
| Man page macros | `.PP / .IP / .TP` | `.sp / .RS / .RE` | More portable to mandoc/BSD. |
| Footer date source | `:docdatetime:` (file mtime) | Same | Use `:reproducible:` to suppress. |
| Smart quotes in DocBook | Curly Unicode characters | `<quote>` element | AdocNet may add backend-aware substitution in v1.x.minor. |

## Reporting incompatibilities

If you encounter behaviour that differs from asciidoctor and isn't listed
here, please open an issue at https://github.com/sylvainfasel/adocnet/issues
with:
- The minimal `.adoc` source that reproduces it
- Asciidoctor's output (`asciidoctor -o asciidoctor.html input.adoc`)
- AdocNet's output (`adocnet convert -e input.adoc`)
- The output format (HTML, PDF, DocBook, EPUB, Reveal.js, Man)

Tools to help:
- `tools/parity-sweep.py` — runs structural diffs across the spec/conformance corpus
- `tools/parity-sweep.py --visual --include-pdf` — produces side-by-side PNG comparisons
