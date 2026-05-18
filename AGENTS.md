# Agent Handoff — AdocNet

Working notes for any AI agent picking up work on this repo. Consolidates
the lessons accumulated across the beta → v1.0.0 arc. Read this once at
session start; refer back when in doubt.

## 1. Context

**AdocNet** is a pure-managed C# AsciiDoc library. Parses AsciiDoc to a
typed AST and renders to HTML5, PDF, DocBook 5, EPUB 3, man pages, and
reveal.js slides. Zero external runtime dependencies. Dual-targets
`netstandard2.0` and `net10.0`.

**Status**: v1.0.0 shipped 2026-05-17. Byte-identical output to
Asciidoctor for HTML, DocBook, and Reveal.js across the 36-document
conformance corpus. Man is structurally equivalent (cleaner roff). EPUB
ships the full asciidoctor-epub3 asset payload + a dedicated chapter
renderer; visually indistinguishable from reference.

**User**: Sylvain Fasel, project owner. Senior C#/.NET background.
Prefers terse, autonomous execution and concrete evidence over
generalities. Will catch claims that aren't backed by what you actually
ran. Email: sylvain@sonatique.net.

## 2. Hard rules (non-negotiable)

### 2.1 Forbidden words / mentions

NEVER mention any of these in commit messages, code comments, source
files, test assets, or anywhere user-visible:

- AI vendor names: Claude, Anthropic, OpenAI, ChatGPT, Copilot, Cursor,
  Codex, GPT, LLM, AI
- Internal tooling names: Letta, Mempalace
- Co-authored-by trailers referencing any AI assistant
- Ellisys business-domain terms (the user's day-job employer): sniffer,
  Bluetooth, protocol analyzer, FPGA, ASIC, Wireshark, packet capture,
  firmware, BLE, HCI, USB analyzer

**Why**: keeps the repo clean of unrelated branding and AI-generation
disclaimers that would weaken the codebase's professional appearance.

**How to verify after work**:
```bash
git ls-files | xargs -I {} grep -l -i -E '\b(Claude|Anthropic|OpenAI|ChatGPT|Copilot|Codex|Letta|Mempalace|sniffer|Bluetooth|FPGA|ASIC|Wireshark|firmware|BLE|HCI)\b' "{}" 2>/dev/null
```
The only legitimate hits should be `.gitignore` (excluding `.claude/`
and `.letta/` IDE directories).

### 2.2 Commit discipline

- **Commit after every phase/logical change**, without being asked.
- Conventional-commits style: `feat(scope):`, `fix(scope):`, `docs:`,
  `refactor:`, `ci:`, `test:`. Body explains the why, references
  affected files. No AI-attribution trailer.
- Don't amend pushed commits unless explicitly asked. New commits
  preferred.

### 2.3 Autonomy

- Run all phases / steps in sequence without stopping to ask, unless
  genuinely blocked or the phase explicitly requires user input.
- Stopping to "check before continuing" wastes time; just continue.
- Exception: destructive operations (force-push, tag deletion on
  remote, package publication) need explicit confirmation.

## 3. Repo layout (quick orientation)

```
src/
  AdocNet/                       facade + main package
  AdocNet.Ast/                   typed AST node types (zero deps)
  AdocNet.Core/                  base classes, RenderContext, extensions
  AdocNet.Parser/                AsciiDoc → AST
  AdocNet.Converters.Html/       HTML5 renderer
  AdocNet.Converters.Pdf/        pure-managed PDF 1.4 writer
  AdocNet.Converters.DocBook/    DocBook 5 XML renderer
  AdocNet.Converters.Epub/       EPUB 3 (with full asciidoctor-epub3 assets bundled)
  AdocNet.Converters.Man/        roff/man-page renderer
  AdocNet.Converters.Revealjs/   reveal.js HTML renderer
  AdocNet.Cli/                   `adocnet` CLI tool (HTML default)
  AdocNet.Cli.{Pdf,Epub,…}/      format-specialized CLI tools
  AdocNet.Layout/                UI-agnostic layout model
  AdocNet.Avalonia/              Avalonia renderer for live preview
  AdocNet.LanguageServer/        LSP server (`adocnet-lsp`)
tests/
  AdocNet.Tests/                 NUnit, 3000+ tests
  AdocNet.Layout.Tests/          layout/Avalonia tests
spec/conformance/                36 .adoc fixtures driving parity sweep
tools/
  parity-sweep.py                full-corpus diff orchestrator
  html-diff.py / docbook-diff.py / man-diff.py / revealjs-diff.py / epub-diff.py
  _parity_render.py              side-by-side panel renderer (needs Pillow)
docs/
  DEFERRED-PARITY-ITEMS.md       canonical list of known parity gaps
  MIGRATION-FROM-ASCIIDOCTOR.md  v1.0 migration guide
  V1.0.0-READINESS.md            historical pre-release assessment
.github/workflows/
  ci.yml                         build/test on linux/mac/windows + parity-sweep gate
  release.yml                    NuGet publish on tag push
```

## 4. Build / test / sweep commands

### Build
```bash
dotnet build                                  # Debug
dotnet build -c Release                       # CI matches this
```
Watch out: Release build with `TreatWarningsAsErrors=true` is stricter
than Debug on nullability. Run Release locally before pushing CI-bound
changes.

### Test
```bash
dotnet test tests/AdocNet.Tests/AdocNet.Tests.csproj --nologo
# Filter:
dotnet test --filter "FullyQualifiedName~RevealjsCrossCuttingTests"
```
~3 minutes for the full 3000+ test suite.

### Parity sweep
```bash
# Text-only (fast, ~5 min)
python tools/parity-sweep.py --glob 'spec/conformance/*.adoc'

# With visual panels (slower, ~20 min, includes PDF rendering)
python tools/parity-sweep.py --glob 'spec/conformance/*.adoc' --visual --include-pdf --include-html-asciidoctor-theme

# Single doc (for iteration)
python tools/parity-sweep.py --glob 'spec/conformance/user-manual.adoc' --visual
```

Output: `parity-sweep-out/_summary.md` aggregate + per-doc per-format
subdirectories.

Python deps: `Pillow`, `beautifulsoup4`. CI installs these via pip.

External: Ruby + `asciidoctor` + `asciidoctor-revealjs` +
`asciidoctor-epub3` gems for reference rendering.

### CI parity gate

`.github/workflows/ci.yml` has a `parity-sweep` job that runs after
`build-and-test`. Hard gates (any regression fails CI):
- HTML sum == 0
- DocBook sum == 0
- Reveal.js sum == 0

Soft gates (with cushion above v1.0 baseline):
- Man sum ≤ 6000 (v1.0 baseline 5510)
- EPUB-struct sum ≤ 120 (v1.0 baseline 87)

## 5. Visual verification protocols

### 5.1 Visual output is verified by READING, not by counting files

After regenerating PNGs / PDFs / EPUBs / screenshots, **open at least
2-3 representative samples with the Read tool**. Look for:
- Visible error banners
- Layout breakage (huge whitespace, off-page elements, missing content)
- Glaring differences from the reference side
- Obvious feature gaps

"288 panels regenerated" without inspecting any of them is not
verification. The user has caught this repeatedly.

### 5.2 PDF parity needs span-level comparison

Side-by-side PNG comparison at panel resolution misses per-character
font/size/color differences (e.g. `#000` vs `#333` looks identical to
the eye but is objectively different).

Use PyMuPDF span extraction:
```python
import fitz
def fonts(p):
    doc = fitz.open(p)
    out = []
    for block in doc[0].get_text('dict')['blocks']:
        for line in block.get('lines', []):
            for span in line.get('spans', []):
                t = span.get('text', '').strip()
                if t and len(t) < 50:
                    out.append((t[:35], span['size'], f'#{span["color"]:06x}', span['font']))
    return out
```
Print both sides, diff row-by-row.

### 5.3 PDF visual diff workflow

For PDF changes touching `src/AdocNet.Converters.Pdf/**`:
1. Regenerate `C:\Workspace\Adoc2pdf\HOWTO.pdf` via `adoc2pdf.bat`
2. Run `python tools/pdf-visual-diff.py <ref> <cand> pdf-diff-out`
3. Read `pdf-diff-out/p1_*_sxs.png` files via the Read tool
4. See `docs/PDF-VISUAL-VERIFICATION.md` for full procedure

Coordinate-only inspection (regex on PDF `Td` positions) misses real
visual bugs (footer padding, top-margin overlap, etc.).

## 6. Test writing discipline

### 6.1 Semantic correctness over structural keyword checks

Don't rely only on "output contains keyword X" tests for complex
formats. Add:
- **Round-trip tests**: encode → decode → verify original recovered.
- **Consistency tests**: related data structures must agree (e.g. glyph
  IDs in PDF content stream match CIDToGIDMap entries).

Bug that motivated this rule: beta.3 font subsetter renumbered glyph
IDs but `CIDToGIDMap` was `/Identity` → all embedded-font text garbled.
Every existing test passed because they only checked for keyword
presence.

### 6.2 Regression tests BEFORE modifying existing code

Before touching code that may change observable behavior:
1. Identify the tests currently covering it.
2. If none exist, **write tests first**, commit, then modify.
3. For refactors/extractions: add golden-output tests locking exact
   output for representative inputs.
4. After modifying: every pre-existing test must still pass.

The session that brought Reveal.js to 0 followed this discipline; the
test count grew by +149 alongside the fixes.

## 7. Parity work — established patterns

### 7.1 Deferred items live in `docs/DEFERRED-PARITY-ITEMS.md`

Canonical list. Check before starting parity work. Update when deferring
a fix or surfacing a new gap. Resolved items get `RESOLVED in vX.Y`
annotation rather than deletion (preserves history).

### 7.2 Asciidoctor's converters disagree among themselves

Asciidoctor's HTML and reveal.js converters use *different* CSS class
orderings (`colist arabic` vs `arabic colist`) and *different* tag
wrappers (`<b class="conum">` vs bare `<b>`). Don't assume sister
converters share conventions — sample the *target* converter's
reference output directly.

### 7.3 Style-driven structural variants → branch at renderer entry

When a node has a style attribute that selects an entirely different
output structure (e.g. `[horizontal]` dlist → table, `[qanda]` dlist →
ordered list), branch at the renderer-method entry rather than threading
the flag deep into one big function.

### 7.4 Parser plumbing is leverage

Setting `:docname:` once in the parser benefits Man, Reveal.js, EPUB
metadata, HTML docinfo — every renderer that reads document attributes.
One-place changes that benefit N consumers compound over time.

### 7.5 Cheap-fix-wide-impact ranking

Rank parity gaps by *how many docs reference the missing feature*, not
just by line count of the worst diff. A cheap fix in a popular code
path (table renderer, list renderer) beats a thorough fix in a rarely-
used one (a -51% Reveal.js drop came from one table-structure commit).

## 8. Scope-claim discipline

Never claim "drop-in replacement" or generalize a per-format result to
"all formats". Enumerate every output backend (HTML, PDF, DocBook,
EPUB, man, revealjs) and assess each independently. The HTML
DifferentialTester only measures HTML; extrapolating to PDF is wrong.

Concrete v1.0 phrasing template:
> "Byte-identical to Asciidoctor for HTML, DocBook, and Reveal.js
> across the 36-doc conformance corpus; visually indistinguishable for
> EPUB; structurally equivalent for Man (cleaner roff)."

## 9. Working notes about the conversation environment

- **Build verification before push**: CI uses Release with strict
  nullability. Run `dotnet build -c Release` locally if the change
  touches code marked `string?` / `T?` to avoid CI nullability
  surprises.
- **Long-running tasks**: Use `run_in_background: true` for sweeps and
  schedule a wakeup. Don't poll-sleep.
- **Visual sweep is ~20 min** with PDF + html-asciidoctor-theme on a
  36-doc corpus. Plan accordingly.
- **CI parity gate runs ~7-10 min** including gem installs + sweep.
- **Repository remote**: `git@github.com:sonatique/Adoc.Net.git`.
  `gh` CLI is configured.

## 10. Reference state at consolidation

- Current branch: `main`
- v1.0.0 tag: pushed and shipped (CI green, Release green)
- Test suite: 3081 pass / 0 fail / 21 skipped
- Parity sweep last result:
  - HTML: 0 lines, 36/36 perfect
  - DocBook: 0 lines, 36/36 perfect
  - Reveal.js: 0 lines, 36/36 perfect
  - Man: 5510 lines, 11/36 perfect
  - EPUB-struct: 87 lines, 0/36 perfect (visually indistinguishable)
