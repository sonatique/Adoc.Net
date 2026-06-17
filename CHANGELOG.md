# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.0.14] - 2026-06-17

A PDF table-layout fix.

### Fixed

- **PDF: wide tables no longer render overlapping columns (#48).** A table whose
  natural width exceeded the page divided the width across columns by weight and
  wrapped each cell to that share, with no handling for content wider than its
  shrunken column — so words spilled into the next column and columns overlapped.
  Column widths are now floored at the width each column's widest unbreakable word
  needs: a starved column borrows the shortfall from columns that have slack
  (ordinary tables that already fit are unchanged). As a final safety net, when a
  table's minimum width still exceeds the page the whole table's font is scaled
  down just enough that no word is wider than its column. Columns never visually
  overlap.

## [1.0.13] - 2026-06-17

A source-provenance release for editor integration: authoritative mappings from
the expanded AST back to the source the author edits. Metadata only — rendered
output (HTML, PDF, DocBook, EPUB, man, reveal.js) is unchanged.

### Added

- **Post-include source provenance (#46).** `ParseResult` now exposes a
  `LineOrigins` table and `TryGetLineOrigin(expandedLine, out LineOrigin)`,
  mapping each line of the fully-expanded document (the coordinate space AST
  `SourcePosition`s count in) back to the file and line the author edits. The
  new `LineOrigin(string? SourceFile, int SourceLine, bool IsSynthetic)` record
  flags content pulled in from an `include::` as synthetic while still locating
  it in its origin file. The mapping is produced authoritatively by the parser
  and stays correct through `tags=`/`lines=`/`leveloffset=` filtering, nested
  includes, conditional (`ifdef`/`ifeval`) stripping, and front-matter removal —
  letting a consuming editor delete its hand-rolled include line-map.

### Fixed

- **Per-cell table source ranges (#45).** `TableCellNode.Source` /
  `TableCellLayout.Source` previously carried the whole *row*'s range — every
  cell in a row reported an identical span — making per-cell source mapping
  (in-place cell editing, per-cell diagnostics) impossible. Each cell now
  carries its own content span. Inline ranges inside cells are promoted to
  absolute document coordinates (consistent with #38) instead of being relative
  to the cell's content, and multi-line `a|` AsciiDoc cells — which previously
  reported `Source = (none)` — now carry an absolute range, with their nested
  block children numbered in absolute document lines.

A broad hardening release: secure-by-default document processing, parser
robustness against hostile input, Asciidoctor parity fixes, thread safety, and
wider framework reach.

### Security

- **`ParseOptions.SafeMode` now defaults to `SafeMode.Safe`** (was `Unsafe`).
  Processing an untrusted document with a base directory set (e.g. via
  `Adoc.ConvertFile` or the language server) previously allowed `include::` to
  read arbitrary local files through `..`, absolute, and UNC paths. The default
  now confines includes to the document's base directory while leaving
  legitimate in-tree includes working. The CLI continues to opt into `Unsafe`
  explicitly (trusted local invocation). Set `SafeMode = SafeMode.Unsafe` to
  restore the previous behaviour for trusted sources.
- **Safe-mode include confinement bypass fixed.** The base-directory check used a
  bare `StartsWith`, so a sibling sharing the base as a string prefix (base
  `.../docs` vs. `.../docs-private/secret.adoc`) slipped through. It now compares
  against the base with a trailing separator. Same fix applied to the CLI preview
  server's traversal guard.
- **`:data-uri:` image embedding is gated by safe mode.** The HTML converter read
  any file the document named — including absolute paths and `..` escapes — and
  base64-embedded it. `HtmlRenderOptions.SafeMode` (default `Safe`) now confines
  data-uri reads to the base directory.
- **Avalonia default link-click restricted to safe schemes.** The `LinkClicked`
  fallback shell-executed any document-controlled href; it now auto-opens only
  `http`/`https`/`mailto`, so a single click can't launch a local executable or
  leak credentials over SMB.
- **`ParseOptions.LockedAttributes` is honoured through `AdocParser.Parse`.** It
  was silently a no-op via the primary public API.

### Added

- **`net8.0` target** on every library and on `AdocNet.Avalonia` (previously
  net10-only). .NET 8/9 consumers now bind to a build that uses the BCL
  `IReadOnlySet<T>` — resolving a `System.Collections.Generic.IReadOnlySet<T>`
  ambiguity (CS0433) — and gain the NET6+ feature set. A `ReadOnlySetAdapter<T>`
  is shipped for netstandard2.0 consumers.
- **`AdocEngine` implements `IDisposable`** (releases file watchers and
  collectible assembly load contexts; `Shutdown()` remains as an alias).
- **Table column-spec style letters** — `cols="1,2a,1l"` now parses per-column
  widths and applies the trailing style letter (e.g. `a` = AsciiDoc, `l` =
  literal) as the column's default cell style.
- **CLI `--theme github`** is now wired up (the `HtmlTheme.Github` theme).
- **Project files**: `global.json` (pins the .NET 10 SDK band), `CONTRIBUTING.md`,
  `.github/SECURITY.md` (private vulnerability reporting), and `.gitattributes`
  (LF normalization for cross-OS-stable fixtures).

### Fixed

- **No more stack overflow or OOM on adversarial documents.** Recursive block
  parsing now has a nesting-depth guard (deeply nested blocks degrade to a
  diagnostic instead of an uncatchable `StackOverflowException`), and the
  `[cols="N*"]` repeat multiplier is clamped so a huge value can't exhaust memory.
- **Callout-number parsing no longer throws** on Unicode digits or oversized
  numbers (`<99999999999>`, `<٣>`); such tokens degrade to plain text.
- **Inline `{counter:}` no longer double-increments** (a speculative block-macro
  probe was advancing counters before the real render).
- **`include::[indent=N]`** now applies Asciidoctor's common-indent semantics
  (removes the block's minimum indentation, then re-indents), preserving nested
  code structure instead of flattening it.
- **`[stem]`/`++++` stem blocks are recognized**, stem defaults to **asciimath**
  (matching Asciidoctor) instead of latexmath, and inline `stem:[...]` honours the
  `:stem:` interpreter attribute.
- **Consecutive styled description lists** no longer merge — a `[qanda]` list
  after a `[horizontal]` list now renders as a separate, correctly-styled list.
- **`HtmlRenderer` is thread-safe** (per-render state no longer leaks across
  concurrent renders on a shared instance). `DocBookRenderer`/`RevealjsRenderer`/
  `ManRenderer` serialize concurrent renders on a shared instance, and
  **`AdocEngine.Convert` is concurrency-safe when extensions are registered**.
- **Render cache key** now distinguishes collection-valued options (custom
  templates, syntax colours), which previously collided and served stale output.
- **URL `include::` fetching** no longer risks a sync-over-async deadlock on
  UI / classic-ASP.NET callers.
- **LanguageServer** resolves includes against the document's filesystem path
  instead of the raw `file://` URI, fixing spurious "include not found"
  diagnostics on every keystroke.
- **CLI**: text stdout is written as UTF-8 (no more mojibake when redirected on
  Windows); `*.adoc` directory globbing is case-insensitive on Linux; diagram-tool
  arguments are passed individually so paths with spaces work.
- Non-compiling README/USAGE "Full API" samples (missing `using AdocNet;`); the
  `examples/CustomIncludeReader` project (wrong namespace) — rewritten to use the
  real `ParseOptions.IncludeReader` API, with all examples now in the solution.

### Changed

- **LangVersion** is `latest` instead of `preview` (no preview language features
  in shipped packages); CI/release `dotnet-quality` is `ga`.
- The repo-wide `NuGetAuditSuppress` of GHSA-xrw6-gwf8-vvr9 is removed; the
  affected transitive `Tmds.DBus.Protocol` is pinned to the patched 0.21.3 in the
  three (non-shipped) projects that used it. The advisory is now actually fixed,
  not masked.
- **`NOTICES.md`** third-party font attributions corrected (Noto Serif is Apache
  2.0; M+ fonts are the M+ FONTS License; the PDF converter's Font Awesome fonts
  are now listed) and the file is packed into the font-bundling packages.
- Documentation: accurate "structurally identical to Asciidoctor" parity wording,
  a package-selection guide, net8.0 TFM tables, and corrected renderer/theme/
  backend inventories.

## [1.0.11] - 2026-06-04

A table-parser correctness fix.

### Fixed

- **Rowspan cells now reserve their column in the rows they span (#41).**
  When a cell had a rowspan (`.N+|`) in a column other than the
  last-filled one, the row-grouping algorithm advanced past *leading*
  columns held by an active rowspan but never skipped *trailing* ones
  before deciding a row was complete. A rowspan in a non-last column
  therefore left the row looking unfilled, so the next source row's
  cells were packed into it — collapsing several source rows into fewer
  AST rows and, with overlapping rowspans across multiple columns,
  dropping trailing cells entirely. The loop now skips trailing columns
  held by active rowspans before closing a row, so each source row
  consumes exactly `numCols − occupiedByActiveSpans − colspansInRow`
  cells, matching Asciidoctor's table-fill behaviour. A rowspan in the
  left column already worked and is unaffected. Because the bug was at
  the AST level (`TableNode.Children`), every backend (HTML, Avalonia,
  PDF) inherits the corrected row structure.

## [1.0.10] - 2026-06-02

A focused follow-up to the 1.0.9 editor-foundation work: inline source
ranges are now usable for click-to-source.

### Fixed

- **Inline source ranges are now absolute document positions (#38).**
  1.0.9 added `InlineLayout.Source` (and the Avalonia
  `SourceRangeProperty` that mirrors it) so an editor could map a
  rendered inline run back to its source span. But the ranges were
  block-RELATIVE — `InlineParser` produces positions relative to each
  block's inline-text buffer (line 1, col 1 at the buffer start), and
  `LayoutBuilder` stamped them onto the layout verbatim. Every inline
  therefore reported line 1, so a live-preview editor hit-testing the
  inline under the pointer resolved every click to the document's first
  line — defeating the feature. `LayoutBuilder` now promotes each
  inline's range to absolute coordinates using the owning block's
  content origin:
  - Lines are absolute for every inline-bearing block kind (heading,
    paragraph, list item, table cell, admonition, description item).
  - Columns are exact for paragraphs and headings (heading origins skip
    the `== ` marker, so a title on line 3 reports its true `3:4`
    start). For list items, table cells, and description items — whose
    content column the AST does not expose — columns are exact when the
    content begins at the block's start column; lines are always exact.

  The Avalonia `SourceRangeProperty` inherits the fix automatically (the
  renderer stamps it from the now-absolute layout inline `Source`).
  `InlineParser`'s ranges remain deliberately slice-relative (its
  contract and unit tests are unchanged); promotion happens in the
  `LayoutBuilder` consumer.

## [1.0.9] - 2026-06-01

A correctness, parity, performance, and editor-foundation release from a
full code review. Every parser and converter fix was verified
byte-for-byte against Asciidoctor 2.0.26; the full test suite stays green
(no regressions). All new public API is additive.

### Added

- **Inline-level source ranges.** `AdocNet.Layout.InlineLayout` now carries
  a `Source` range (populated by `LayoutBuilder` from each inline AST node),
  and `AdocNet.Avalonia.AvaloniaRenderer` stamps every rendered inline with a
  new `SourceRangeProperty` attached property (`GetSourceRange`/
  `SetSourceRange`). An editor can hit-test the inline under the pointer to
  map a click — or a selection — back to a source offset at inline (not just
  block) granularity.
- **Themeable Avalonia renderer.** New `AdocNet.Avalonia.AvaloniaRenderTheme`
  (brushes, monospace font, document-title and per-level heading sizes)
  exposed via `AvaloniaRenderer.Theme`; each renderer starts with its own
  defaults, so re-theming the preview (dark mode, host palette) no longer
  requires forking the renderer.
- **`AvaloniaRenderer.LinkClicked` event** (`LinkClickedEventArgs` with a
  `Handled` flag). The host can intercept link navigation — route `xref:`
  internally, sandbox external URLs — with the OS-shell open as the fallback.
- **Overridable render dispatch.** `AvaloniaRenderer.RenderBlock` and
  `RenderInlineCore` are now `protected virtual`, so a subclass can render
  custom block/inline kinds and defer the rest to `base`.

### Fixed

- **Parser — inline formatting.** Emphasis nested inside monospace
  (`` `_x_` `` → `<code><em>x</em></code>`) is no longer suppressed; the
  single-`+` passthrough now honours the constrained word-boundary rule, so
  ordinary prose/maths (`a + b + c`, `C++`, `1+1`) stays literal instead of
  having its `+` markers swallowed.
- **Parser — delimited blocks.** The closing delimiter must now match the
  opener's length, so a longer rule stays content inside a verbatim block and
  same-type blocks nest correctly. Applied to listing/example/quote/etc.,
  comment blocks, and list-continuation blocks.
- **Parser — tables.** Backslash-escaped pipes (`\|`) are treated as literal
  cell content; the `cols` repeat multiplier (`N*`, including `N*spec` and
  alignment) is honoured inside comma-separated specs.
- **Parser — includes & header.** `include::[lines=...]` accepts
  comma-separated ranges (`"1..2,5..6"`), and a revision line without a `v`
  prefix (`1.0, 2024-01-15: ...`) now extracts the revision number.
- **HTML converter.** Double quotes in image `alt` text are escaped
  (`&quot;`), preventing malformed markup; the `menu:` macro emits
  Asciidoctor's `menu`/`submenu`/`menuitem`/`caret` structure; verbatim-block
  callout numbers use the icon-font form under `:icons: font`; and table
  column widths carry the remainder to the last column and strip trailing
  zeros (e.g. `14.2858`, `9.091`).
- **DocBook converter.** Inline-only content (e.g. a lone `<link>` in a
  `<simpara>`) is emitted on a single line rather than indented, and the
  XLink namespace uses Asciidoctor's `xl:` prefix.
- **Avalonia incremental renderer.** Fixed incorrect element mapping on any
  document containing a section: `LayoutBuilder` flattens a section into a
  heading plus its body blocks, so the previous 1:1 panel-child ↔ AST-child
  assumption mis-targeted incremental edits and dropped section bodies from
  the preview. Each top-level AST child is now rendered as one tagged
  container holding all its blocks, located by tag (robust to a leading
  document-title block). The editor sample's block controller was updated to
  match.
- **Editor in-place edit.** Committing an in-place block edit now verifies the
  captured source slice still matches before splicing, aborting (and
  re-rendering) instead of corrupting unrelated text when the document shifted
  underneath the edit.

### Changed

- **Performance.** Inline source-range computation is now `O(n + k·log n)`
  instead of `O(n·k)` (newline-indexed position lookup); the syntax-highlight
  tokenizer anchors its rules with `\G` to avoid forward rescans; the
  conditional preprocessor skips its three directive regexes for lines that
  can't be directives; and the verbatim-block attribute-reference regex is
  compiled once. All output is unchanged.

## [1.0.8] - 2026-05-22

Lands the rest of the WYSIWYG roadmap (Phases 3–6) onto `main`.

PRs #11–#14 each report as MERGED on GitHub but their base branch
was the previous stacked PR's branch rather than `main` — a
stacked-PR gotcha — so the merge commits actually sat on the stacked
feature branches (`hybrid-editor`, `incremental-render`,
`block-wysiwyg`, `full-wysiwyg`) and never propagated. v1.0.3 through
v1.0.7 silently shipped without
the hybrid editor sample, the incremental Avalonia renderer, the
Block-WYSIWYG controller, or the AST-mutation commands. PR #35
cherry-picked the four phase commits onto current main; this
release packages the result.

### Added

- **`AdocNet.Avalonia.IncrementalAvaloniaRenderer`** (new public
  class in the existing `AdocNet.Avalonia` NuGet package). Mirrors
  `AdocNet.Editor.IncrementalHtmlRenderer`: after an initial full
  render, subsequent renders use `AstDiffer.DiffSections` to find
  the top-level blocks that changed and splice fresh Avalonia
  control subtrees into the existing visual tree in place,
  preserving scroll position and avoiding a full re-render. Added/
  Removed sections and document-metadata changes fall back to a
  full re-render automatically. Each top-level child carries a
  `SectionTag(Index, StructuralHash)` via `Control.Tag` so the diff
  can identify it positionally.

- **`AdocNet.Avalonia.AvaloniaRenderer.Render(BlockLayout)` overload.**
  Renders a single layout block to a control without the wrapping
  `ScrollViewer`. Used by `IncrementalAvaloniaRenderer` to rebuild
  individual sections, also useful to any consumer composing their
  own preview surface from per-block controls.

- **`samples/AdocNet.Avalonia.Editor`** (runnable Avalonia sample).
  Full demonstration of the hybrid + Block-WYSIWYG + Full-WYSIWYG
  flow on top of the AdocNet.Parser / AdocNet.Layout /
  AdocNet.Avalonia / AdocNet.Emitter stack:

  - Source pane (AvaloniaEdit) on the left, live Avalonia preview
    on the right, toolbar of common formatting actions (Bold /
    Italic / Monospace / Heading / Bullet list / Numbered list /
    Link / Image / Table / Quote / Admonition / Code / HR),
    status bar showing version + char count + parse timing +
    diagnostic count + AST node at the caret.
  - Debounced parse-render loop (`EditorViewModel`, 120 ms)
    feeding the incremental renderer above. Cancels in-flight
    parses when newer changes arrive; marshals back to the UI
    thread for the splice.
  - Block-WYSIWYG `BlockEditController`: double-click any rendered
    block in the preview to swap it for an in-place AvaloniaEdit
    prefilled with that block's source slice (resolved via the new
    `SourceRangeOffsets` helper that maps an AST `SourceRange` to
    a `(start, length)` pair of source-string offsets). Commit
    splices back into the source editor; the incremental renderer
    refreshes just that block.
  - Right-click context menu with `Edit block`, `Duplicate block`,
    `Delete block`, and `Toggle role: [.warning] / [.important]
    / [.lead]`. On paragraphs, also `Promote to heading H1 / H2
    / H3`.
  - `WYSIWYG mode` toolbar toggle: collapses the source pane and
    splitter so the preview takes the full editor area.
  - `AstMutationCommands`: AST mutations (toggle role, duplicate
    block, promote to heading) round-trip through
    `AsciidocEmitter` — the typed AST node is mutated, emitted
    fresh, and the resulting slice is spliced back into the
    source. The splice range extends backward over any preceding
    `[…]` attribute / `.Title` lines so role changes overwrite
    existing attribute lines instead of leaving them stale.

- **`tests/AdocNet.Avalonia.Editor.Tests`** (new test project,
  46 cases). Uses `Avalonia.Headless.NUnit` to instantiate
  `TextEditor` instances without a real window. Covers the
  toolbar command primitives (17), the caret-context resolver
  built on the inline source ranges added in v1.0.3 (7), the
  source-range-to-offset helper (6), the incremental renderer's
  diff dispatch (6), and the AST-mutation commands (9).

- **`AdocNet.slnx`** entries for `samples/AdocNet.Avalonia.Editor`
  and `tests/AdocNet.Avalonia.Editor.Tests` — both will now be
  built and the test project's 46 tests run in CI on every push.

### Fixed

- **Doc-comment cref ambiguity in `AvaloniaRenderer.cs`.** Once
  `Render(BlockLayout)` joined `Render(DocumentLayout)`, the
  `<see cref="Render"/>` reference on `WrapInScrollViewer`'s
  XML doc became ambiguous and broke the build. Pinned to
  `Render(DocumentLayout)`, which is the overload the property
  actually affects.

## [1.0.7] - 2026-05-22

First release that actually ships the `AdocNet.Emitter` NuGet package.

The emitter library was merged in v1.0.3 (PR #10, the round-trip
AsciiDoc emitter) but the project was never added to `AdocNet.slnx`.
The release workflow uses `dotnet pack` against the solution, so
v1.0.3..v1.0.6 silently omitted `AdocNet.Emitter` from the artifact
set even though the source was on `main`. This release fixes that
oversight and also lights up CI test coverage for the emitter and
its test project.

### Added

- **`AdocNet.Emitter` NuGet package.** The round-trip emitter is now
  in the build solution and ships as `AdocNet.Emitter.1.0.7.nupkg`.
  Two emit modes:
  - From-AST synthesis: walks the typed AST and produces AsciiDoc
    source from each node's properties. Foundation for AST-mutation
    features (e.g. toggling a `[.role]` and round-tripping the
    edit back to source).
  - Source-anchored fast path
    (`EmitOptions.PreserveOriginalWhenAvailable = true` +
    `OriginalSource`): for any node carrying a populated
    `SourceRange`, the emitter copies the original source slice
    verbatim. Unchanged subtrees round-trip byte-identical;
    only freshly synthesised nodes (those with `SourceRange.None`)
    pay the synthesis cost. The format-preservation mechanism
    for WYSIWYG and live-preview consumers.
- **`tests/AdocNet.Emitter.Tests` is now part of the solution**, so
  CI runs the emitter's 82 tests on every build — including the
  round-trip property test across the full conformance corpus.

### Fixed

- **netstandard2.0 build error in `DelimitedBlockEmitter` (latent
  since v1.0.3).** The verbatim-content emitter used
  `ReadOnlySpan<char>` without a `#if NET10_0_OR_GREATER` gate or a
  `System.Memory` package reference, so the netstandard2.0 TFM
  failed to compile. This had been undetected because the project
  was never in the build solution. Refactored the helper to operate
  on `(string, int start, int end)` indices instead, which compiles
  cleanly on both TFMs without a new package dependency.

### Notes for downstream consumers

If you were referencing the emitter source via a local checkout, no
code changes are required — the public API is unchanged from v1.0.3.
You can now switch to the published NuGet package:

```xml
<PackageReference Include="AdocNet.Emitter" Version="1.0.7" />
```

## [1.0.6] - 2026-05-22

Per-row and per-cell source positions on tables (#31). Completes the
sync-scroll story started in #19 (block-level source positions in
v1.0.3) down to table-cell granularity.

### Fixed

- **`TableRowNode.Source` and `TableCellNode.Source` are now populated
  by the parser (#31).** Previously both reported `Source.IsNone == true`
  on every row and cell, so consumers building a live-preview editor
  could map "source line N falls inside table T" but not "source line N
  falls in row R of T" — forcing dead-zones in editor sync-scroll while
  the cursor traversed long tables. The parser now tracks an effective-
  line buffer where each entry knows its source-line range, tags every
  cell with the range, and unions the cell sources to set the row's
  source on finalisation. Multi-line cells (an `a|` AsciiDoc cell whose
  content spans several physical lines) get a range whose `End` line is
  after `Start`. Works for the column-aware grouping path, the line-as-
  row fallback, and the CSV / DSV / TSV path.

### Added

- **`TableRowLayout.Source` and `TableCellLayout.Source`.** Init-only
  `SourceRange` properties, populated by `LayoutBuilder` from the AST
  nodes. Defaults to `SourceRange.None` for layouts constructed
  directly. Mirrors the `BlockLayout.Source` API added in v1.0.3.

## [1.0.5] - 2026-05-19

Root-cause fix for Avalonia table-column collapse with row-spans (#26),
which v1.0.4's median cap alone did not resolve.

### Fixed

- **`TableColumnWeights.Compute` now tracks row-span occupancy when
  placing cells into columns (#26).** Previous versions walked
  `row.Cells` and incremented `col += span` for each cell without
  checking columns held by row-spans from prior rows, so continuation
  cells were attributed to the wrong column index. In tables where a
  long-prose cell sat in a continuation row, the weight landed on the
  wrong column entirely. The algorithm now mirrors the renderer's
  `occupied[]` placement logic; weights line up with what's actually
  on screen. For the Ellisys-style 8-column repro the long-prose
  column's share went from ~3% (~34 px on a 1200-px viewport) to ~22%
  (~266 px).

### Added

- **`TableColumnWeights.ComputeMinWidthsPixels`** returns a per-column
  pixel floor based on the longest single word in the column. Even
  with correct content-weighted star shares, a header-only column
  flanked by prose columns can compute to a tiny star share. The
  Avalonia renderer now wires this into `ColumnDefinition.MinWidth`
  so narrow columns render their longest word on one line. Tunable
  via the `pixelsPerChar` and `horizontalPaddingPixels` parameters
  for downstream consumers using different font metrics.

## [1.0.4] - 2026-05-19

Follow-up to the v1.0.3 Avalonia table-column work.

### Fixed

- **Avalonia table star columns: cap outliers at 3× the median (#26).**
  v1.0.3 weighted star columns by per-column natural content length so
  wide tables fit their host viewport (#16). In tables with one cell of
  long prose, that cell's raw weight (~150 chars) was an order of
  magnitude greater than the other columns' (~4–15 chars), so the prose
  column took ~half the viewport and every other column collapsed to
  one letter per line — even short headers like "Trace writer parser"
  rendered as a vertical stack of letters. Each column's star weight is
  now capped at `max(1, 3 × median(weights))`. The cap only fires when
  one column is an outlier; uniformly-sized tables keep their raw
  weights unchanged. For the issue's repro the prose column's share
  drops from ~57% to ~26%, leaving the other columns enough room to
  render naturally.

### Changed

- The Avalonia column-weight algorithm moved into
  `AdocNet.Layout.TableColumnWeights` (public static helper). The
  algorithm is layout-shape-aware, not Avalonia-specific, so this also
  lets downstream consumers — custom renderers, alternative preview
  panes — reuse the same heuristic.

## [1.0.3] - 2026-05-19

Five fixes against 1.0.2: four touching the Avalonia / Layout side of
the library (driven by live-preview editor consumers) and one in the
PDF table layout.

### Added

- **`BlockLayout.Source` carries originating AST source range (#19).**
  Every emitted block — `HeadingLayout`, `ParagraphLayout`,
  `ListLayout`, `TableLayout`, `CodeBlockLayout`, `AdmonitionLayout`,
  `DescriptionListLayout`, `ThematicBreakLayout` — now exposes the
  `SourceRange` of the AST node it came from, populated by
  `LayoutBuilder`. Consumers building editor sync-scroll can map a
  layout block back to its source line directly instead of walking the
  AST in parallel and pairing by index. Blocks constructed without an
  originating AST node default to `SourceRange.None`.
- **`AvaloniaRenderer.WrapInScrollViewer` opt-out (#18).** Set to
  `false` to receive the bare content `StackPanel` from `Render(...)`
  instead of a wrapping `ScrollViewer`. The natural choice when the
  consumer already hosts the result inside its own scrolling container
  (editor preview pane, uniform chrome, sync-scroll). Default `true`
  preserves the previous behaviour.

### Fixed

- **AvaloniaRenderer emits the document title (#15).** `Render`
  iterated over `DocumentLayout.Children` but silently dropped
  `DocumentLayout.Title`. The title now renders as a bold 28pt
  `TextBlock` at the top of the panel, matching `HtmlRenderer`'s
  `<h1>` for the document title.
- **Avalonia table columns weighted by content (#16).** Tables built
  every column as `GridLength.Star` with equal weight, so a wide table
  with one prose column and several short-identifier columns gave the
  prose column the same narrow slice as the identifiers. The prose's
  longest unbreakable words then forced their column to natural width,
  pushing the whole Grid past its parent `ScrollViewer`'s viewport.
  Star columns are now weighted by the longest plain-text cell length
  in each column. `TextWrapping = Wrap` was also added to the inner
  `TextBlock` used by `LinkRun`, the one inline cell `TextBlock` that
  was missing it.
- **PDF auto-sized table column widths (#17).** The renderer pinned
  each auto-sized column to its longest-word minimum, then distributed
  the remainder by total character count. In wide tables that mixed
  short identifiers with one prose column, the minimum-word
  allocations soaked up the budget and prose collapsed to
  one-word-per-line. Columns are now weighted by max unwrapped cell
  width with the longest-word floor honoured whenever possible.
  Explicit user-set `cols=` widths still go through the explicit-weight
  path unchanged.

## [1.0.2] - 2026-05-18

Two asciidoctor-parity table-parser fixes reported against 1.0.1.

### Fixed

- **Pipe in list items inside `a|` cells (#6).** A `*`-marked list item
  whose text contained a literal `|` inside an `a|` AsciiDoc-content
  cell used to drop the entire `<li>` silently. The root cause was
  over-eager row detection: any line containing `|` was treated as a
  new row, so `* item beta | extra after pipe` became its own row and
  the pre-pipe `* item beta` ended up as plain text in the wrong cell.
  `ParseTableContent` now uses a new `IsCellLineStart` helper to
  distinguish row-opening lines (the separator itself, or a valid
  span/style prefix immediately before it) from continuation lines.
  Lines that are not row-openers fold into the previous cell — even
  their mid-line `|`s, which still act as cell separators *inside*
  the row. The pre-pipe portion now stays in the AsciiDoc cell's list,
  matching asciidoctor.js's "post-pipe consumed as new cell" semantics.
- **Leading blank line inside `|===` block (#7).** A blank line
  between the opening `|===` and the first row of cells no longer
  promotes that first row to a header. Asciidoctor's rule is that the
  implicit header exists only when row N is *immediately* the first
  content of the table body; a leading blank means no header. The
  header-by-blank-line scan now tracks a `sawLeadingBlank` flag and
  bails out when it encounters content after a leading blank.

## [1.0.1] - 2026-05-18

Three asciidoctor-parity parser fixes reported against 1.0.0.

### Fixed

- **Unordered list `-` marker (#1).** `TryParseListItem` now accepts a single
  `-` followed by a space as a depth-1 unordered marker, mirroring
  Asciidoctor's alternative bullet marker. `--` remains the open-block
  delimiter (the spec does not allow `-` to stack for nesting).
- **Empty entries in `cols=` (#2).** `ParseColumnSpec` no longer drops empty
  comma-separated entries, so `cols="<1,1,1,,1,,>"` yields 7 columns with the
  blank slots defaulting to left/top/width=1. Body cells map to the correct
  row/column slots instead of overflowing into extra rows.
- **Multi-line content in `a|` AsciiDoc cells (#3).** `ParseTableContent`
  joins continuation lines (lines that do not contain the cell separator)
  into the preceding cell's text. Previously, the closing `]` of a
  `footnote:[…]` macro on the next physical line inside an `a|` cell was
  silently dropped — the cell text never reached `InlineParser` with a
  balanced `]`, so the macro disappeared.

## [1.0.0] - 2026-05-17

The 1.0.0 release. Headline achievement: **byte-identical output to
Asciidoctor across the 36-document conformance corpus** for three of the
five output formats — HTML, DocBook, and Reveal.js. EPUB ships the full
asciidoctor-epub3 asset payload with a dedicated chapter renderer that
produces the same semantic HTML5. Man output is cleaner roff than the
reference while remaining structurally equivalent.

### Parity sweep results (`tools/parity-sweep.py` over 36 conformance docs)

| Format | Perfect docs | Sum diff lines | Notes |
|---|---|---|---|
| HTML | 36/36 ✓ | 0 | Byte-identical |
| DocBook | 36/36 ✓ | 0 | Byte-identical |
| Reveal.js | 36/36 ✓ | 0 | Byte-identical (slide DOM) |
| Man | 11/36 | 5510 | Remaining diffs are stylistic roff conventions |
| EPUB-struct | 0/36 | 87 | All 25 EPUB parts present, 23/25 byte-identical per doc; chapter visually indistinguishable |

### Added

#### Reveal.js converter — from 8052 → 0 diff over the corpus
- Full Asciidoctor table structure (`frame-`/`grid-`/`halign-`/`valign-` classes, `<colgroup>`, `<thead>/<tbody>/<tfoot>`, `<p class="tableblock">` cell wrappers)
- Asciidoctor delimited-block wrappers for source/listing/literal/quote/example/sidebar
- Per-slide footnote rendering (`<sup class="footnote">` markers + `<div class="footnotes">` end-of-slide block)
- Description-list variants: standard, `[horizontal]` (table-based), `[qanda]` (numbered ordered list)
- Callout markers (`<b>(N)</b>` inline after marked lines) + colist (`<div class="arabic colist">`)
- Admonition table structure with `:icons: font` support (Font Awesome class+title attribute pairs)
- Checklist items (`<input type="checkbox">` with `checked`/`data-item-complete` attributes)
- Section numbering with appendix prefix (`Appendix A:`)
- Discrete headings (`<hN class="discrete">`)
- Document-title subtitle splitting on `": "` (`<h1>` + `<h2>`)
- Conditional preamble div (only when sections follow; bare siblings otherwise)
- Author email in title-slide byline (`<a href="mailto:…">`)
- Ordered-list `type` attribute (`a`/`A`/`i`/`I`)
- highlight.js source-highlighter classes (`hljs`, `language-X`, `data-noescape`)
- Inline xref / interdoc-xref / footnote / image / kbd / btn / menu rendering
- `:hide-uri-scheme:` strips scheme from displayed URLs

#### DocBook converter
- Root `xml:id` from document `[[anchor]]`
- Link / xref labels parsed as inlines (backticks → `<literal>`)
- Block titles parsed for inline formatting
- Conditional `linenumbering="unnumbered"` on `<screen>`/`<programlisting>`
- Conditional `arearefs` on callouts (empty when no `<co>` markers emitted)

#### EPUB converter
- Standard EPUB 3 paths (`EPUB/package.opf` not `OEBPS/content.opf`)
- Full asciidoctor-epub3 asset bundle embedded as resources:
  - 13 TTF fonts (Noto Serif, M+ 1p, M+ 1mn, FontAwesome 5 Solid, assorted-icons)
  - 3 CSS files (epub3.css, epub3-css3-only.css, epub3-fonts.css)
  - Default avatar + headshot JPEGs
  - `META-INF/com.apple.ibooks.display-options.xml`
- `<dc:date>` and `dcterms:modified` from file mtime
- `<dc:description>` from `:description:` doc attribute
- Always-emitted byline (with bundled avatar default when no `:author:`)
- Calibre/reader-detection JavaScript in chapter `<head>`
- `<small class="subtitle">` wrapping for chapter titles
- **New `EpubChapterRenderer`**: dedicated renderer emitting asciidoctor-epub3's semantic HTML5 chapter structure (`<section class="sect{N}">`, `<aside class="admonition">`, `<figure class="listing">`, etc.) instead of HtmlRenderer's div-wrapped output. Visually indistinguishable from reference.

#### Man converter
- `'\" t` preprocessor directive for tbl
- `.TH` name from `:docname:` (uppercased + escaped hyphens)
- `.TH` source/manual default to `"\ \&"` (nbsp+zwsp idiom)
- `.sp` for paragraphs instead of `.PP`
- Smart-quote escapes (U+2018→`\(cq`, etc.)
- Bold-monospace (`\f(CB`) for backtick text
- Tab expansion to 8 spaces in verbatim blocks
- `\-` escape for ASCII hyphens in body text
- Numbered `Example N.` block titles
- Inline formatting in titles and labels

#### Parser
- Source-block role/id preserved in list-continuation contexts (`[source,role="primary"]` inside `tabs`/`dlist`)
- Paragraph-style admonitions (`[WARNING]\nparagraph`) emit `AdmonitionNode`
- Constrained `#text#` highlight gets word-boundary check (matches `*`/`_`/`` ` ``)
- `:docname:` / `:docfile:` / `:docfilesuffix:` intrinsic attributes populated from `ParseOptions.SourceFilePath`

### Changed
- `Directory.Build.props` version 1.0.0
- 90 new tests added across the parity-sweep arc (3048 pass / 0 fail)

### Documentation
- `docs/DEFERRED-PARITY-ITEMS.md` — known parity gaps tracked for follow-up
- `docs/MIGRATION-FROM-ASCIIDOCTOR.md` — CLI/attribute migration guide
- `NOTICES.md` — third-party attribution for bundled EPUB assets

## [1.0.0-beta.21] - 2026-04-13

### Added
- `:skip-front-matter:` attribute: strips YAML front matter (`---` fences) before parsing and stores extracted content as `:front-matter:` document attribute
- `:stylesheet:` attribute: specifies a custom CSS file to use instead of the built-in theme
- `:linkcss:` attribute: delivers CSS via `<link rel="stylesheet">` instead of inline `<style>` embedding
- `:stylesdir:` attribute: base directory for resolving relative `:stylesheet:` paths
- `$$...$$` stem delimiters: block (`$$` alone on a line) and inline (`$$formula$$`) forms, active only when `:stem:` attribute is set; `$$` remains literal text without `:stem:`
- `:max-include-depth:` document attribute: allows documents to lower the include recursion depth (capped at the API maximum; documents cannot escalate beyond the caller's limit)
- 25 Asciidoctor differential test fixtures covering beta.16–21 features with golden reference files generated by Asciidoctor 2.0.26
- 108 conformance tests via auto-discovered fixture/golden-file pairs
- 48 new unit tests (regression, feature, and conformance coverage)

### Fixed
- CSS attribute precedence: `HtmlRenderOptions.CustomCss` now always takes priority over document `:stylesheet:` attribute

## [1.0.0-beta.20] - 2026-04-13

### Added
- Inline conditional attribute substitution: `{foo?yes}` (if-set) and `{foo!no}` (if-unset)
- Attribute value line continuation with trailing ` \` for multi-line attribute values
- Book doctype part rendering: level-0 sections render as `<h1>` with "Part I", "Part II" Roman numeral prefix
- Level-0 sections now map to `<h1>` tag (previously fell through to `<h6>`)
- 41 new tests covering conditional substitution, line continuation, and part rendering

### Fixed
- Level-0 section heading tag: was `<h6>` (wildcard fallback), now correctly `<h1>`

## [1.0.0-beta.19] - 2026-04-13

### Added — Parser Features
- Markdown fenced code blocks: triple-backtick (```) delimiters with optional language identifier
- Book doctype section styles: `[appendix]`, `[glossary]`, `[colophon]`, `[dedication]`, `[preface]` on sections
- `Style` property on `SectionNode` for section style tracking
- `toc::[]` block macro for manual TOC placement when `:toc: macro` is set
- `RemoveChildAt()` method on `AstNode` for child replacement

### Added — Rendering Attributes
- `:showtitle:` / `:notitle:` attributes for controlling document title display
- `:nofooter:` attribute to suppress the footer div in full-document HTML output
- `:nofootnotes:` attribute to suppress the footnote definitions section
- `:source-language:` attribute for default source block language fallback
- `:linkattrs:` attribute enabling named attribute parsing on `link:` macros (`window=`, `role=`)
- `:sectanchors:` attribute adding anchor links before section headings
- `:sectlinks:` attribute making section titles self-linking
- `:hide-uri-scheme:` attribute stripping `http://`/`https://` from displayed bare URLs
- `:webfonts:` attribute for Google Fonts CSS injection in HTML head
- `:last-update-label:` attribute customizing the footer label text
- `Window` and `Role` properties on `InlineLinkMacroNode` for link attributes
- Footer div (`<div id="footer">`) in full-document HTML output
- Appendix prefix rendering ("Appendix A:", "Appendix B:", etc.) for `[appendix]` sections

## [1.0.0-beta.18] - 2026-04-12

### Added — Asciidoctor Parity
- Markdown-compatible headings: `#` through `######` as alternative to `=` headings
- Markdown-compatible blockquotes: `> ` prefix lines with multi-line support and `-- Author` attribution
- Q&A description list style: `[qanda]` renders as numbered `<ol class="qanda">` list
- Horizontal description list style: `[horizontal]` renders as `<table>` layout within `<div class="hdlist">`
- Include `indent=` attribute: `indent=N` prepends N spaces, `indent=0` strips leading whitespace
- `Style` property on `DescriptionListNode` for qanda/horizontal style tracking
- Trailing `#` stripping on Markdown headings (`## Title ##` → "Title")

### Changed
- `[horizontal]` block attribute now propagated to `DescriptionListNode.Style` (was parsed but lost)

## [1.0.0-beta.17] - 2026-04-11

### Added — Converters and Templates
- Man page converter: new `AdocNet.Converters.Man` project producing roff-format man pages
- Man page rendering: `.TH` header, `.SH`/`.SS` sections, bold/italic font escapes, `.nf`/`.fi` code blocks, lists, tables, admonitions
- Man page CLI: `adocnet -b man` and standalone `adocnet-man` tool
- Reveal.js slides converter: new `AdocNet.Converters.Revealjs` project producing reveal.js HTML presentations
- Reveal.js slide mapping: level-1 sections → horizontal slides, level-2 → vertical (nested) slides
- Reveal.js attributes: `:revealjs_theme:`, `:revealjs_transition:`, `:revealjs_controls:`, `:revealjs_progress:`, `:revealjs_slideNumber:`
- Reveal.js speaker notes: `[.notes]` role on blocks renders `<aside class="notes">`
- Reveal.js CLI: `adocnet -b revealjs` and standalone `adocnet-revealjs` tool
- Converter templates: `INodeTemplate` interface for custom per-node HTML rendering
- Template registration: `HtmlRenderOptions.Templates` property (first match wins)
- Template hooks in both block and inline rendering paths

### Changed
- HtmlRenderer extracted into 8 partial class files for maintainability (< 500 lines per file)

## [1.0.0-beta.16] - 2026-04-10

### Added — Asciidoctor Parity
- Collapsible blocks: `[%collapsible]` option renders `<details>/<summary>` in HTML
- Data URI embedding: `:data-uri:` attribute converts images to inline base64 `data:` URIs
- Font Awesome CSS injection: automatic CDN link when `:icons: font` is set (FA 4.7.0)
- Custom icon font CDN: `:iconfont-cdn:` attribute overrides the default FA URL
- Docinfo injection: `:docinfo:` attribute injects `docinfo.html` / `docinfo-footer.html` content
- Private docinfo: `{docname}-docinfo.html` per-document header/footer injection
- Safe modes: `SafeMode` enum (Unsafe, Safe, Server, Secure) on `ParseOptions`
- Safe mode enforcement: include path restrictions, attribute locking, file I/O controls
- CLI `--safe-mode` / `-S` flag for safe mode selection
- STEM/Math: `StemBlockNode` and `StemInlineNode` AST types for mathematical notation
- Block-level math: `[stem]`, `[latexmath]`, `[asciimath]` on open blocks
- Inline math: `stem:[]`, `latexmath:[]`, `asciimath:[]` macros
- MathJax v3 script injection: automatic `<script>` tag when `:stem:` attribute is set
- AsciiMath support: `:stem: asciimath` configures MathJax for AsciiMath input
- `HtmlRenderOptions.BaseDirectory` property for image and docinfo file resolution

## [1.0.0-beta.15] - 2026-04-10

### Added — Incremental Rendering
- `AstNode.StructuralHash` property: deterministic FNV-1a hash of node structure, properties, and children
- `AstNode.InvalidateStructuralHash()` method for clearing cached hash after AST mutation
- `GetStructuralInlines()` virtual method on AstNode for hashing side-channel inline collections
- `MixAdditionalState()` virtual method for hashing BlockNode.Id/Reftext/Roles and inline Roles
- `AstDiffer.DiffSections()`: two-pass (ID-based + positional) section-level tree diff algorithm
- `AstDiffEntry` struct and `AstDiffChangeType` enum for structured diff results
- `IncrementalHtmlRenderer`: re-renders only changed sections, splices into previous HTML
- `AdocEngine.ConvertIncrementalHtml()` convenience method for incremental HTML rendering
- `HtmlRenderOptions.EnableIncrementalMarkers`: opt-in section comment markers in HTML output
- HTML section markers (`<!-- sect:N -->` / `<!-- /sect:N -->`) for incremental splice points

## [1.0.0-beta.14] - 2026-04-09

### Added — Dependency-Ordered Loading
- `DependencyResolver` topological sort using Kahn's algorithm for extension load ordering
- Extensions now load in dependency order: if A depends on B, B loads before A
- Cycle detection with descriptive error messages; falls back to alphabetical on cycle
- Two-pass loading: all manifests read first, then DLLs loaded in resolved order

### Added — Extension Signing Verification
- `publicKeyToken` field in `extension.json` for strong-name token verification
- Pre-load token check via `AssemblyName.GetAssemblyName()` (DLL not loaded if mismatch)
- `SigningHelper` internal utility for hex conversion and token format validation
- Unsigned DLLs with token expectation are skipped with warning

### Added — Extension Validation Tool
- `adocnet ext validate <path>` CLI command for pre-publish extension checking
- `ExtensionValidator` class with 10 checks: manifest, fields, DLL, processors, API version, min/max version, dependencies, signing
- Supports both directory and zip inputs
- Structured output with `[PASS]`/`[FAIL]`/`[WARN]`/`[SKIP]` per check
- `ValidationResult` and `ValidationStatus` public types for programmatic use

### Compatibility
- Parser and AST unmodified
- Processor interfaces unmodified (stable from beta.13)
- Core maintains zero external NuGet dependencies
- Both netstandard2.0 and net10.0 compile

## [1.0.0-beta.13] - 2026-04-08

### Changed — API Improvement
- `IDocumentProcessor.Process` now returns `bool` and receives `RenderContext` (was `void Process(DocumentNode)`)
- `IBlockProcessor.Process` now returns `bool` (was `void`)
- `IInlineProcessor.Process` now returns `bool` (was `void`)
- `ProcessingPipeline` short-circuits when `Process()` returns `true` — remaining processors of the same type are skipped for that node
- All built-in processors (`DocumentMetadataProcessor`, `AutoIdBlockProcessor`, `DiagramBlockProcessor`, `IconMacroProcessor`) return `false` to preserve existing behavior

### Added — Extension Isolation
- `ExtensionLoadContext` (net6.0+): each extension DLL loads in its own collectible `AssemblyLoadContext`
- Host assemblies (AdocNet.Core, runtime) are never duplicated across contexts
- Extension dependencies resolve from the extension's directory first, then fall back to the host
- On netstandard2.0: `Assembly.LoadFrom()` fallback (no isolation)

### Added — Hot-Reload
- `AdocEngine.EnableHotReload` property watches extension directories for DLL changes
- `ExtensionHotReloader` with 500ms debounce coalesces rapid file system events
- Reload cycle: unfreeze → dispose lifecycle → clear processors → unload contexts → reload → re-freeze
- `ClearCache()` called automatically after reload
- `Shutdown()` stops all watchers and unloads extension contexts
- Hot-reload requires net6.0+ (`NotSupportedException` on netstandard2.0)

### Compatibility
- Parser and AST unmodified
- Core maintains zero external NuGet dependencies
- Both netstandard2.0 and net10.0 compile
- No backward compatibility constraint (no users/extensions/forks exist)

## [1.0.0-beta.12] - 2026-04-07

### Added — Performance II
- `IExtensionCapabilities` interface with `IsDeterministic` property for declaring processor determinism
- Render cache now works with extensions when all processors declare `IsDeterministic = true`
- Persistent (disk-based) render cache via `EnablePersistentCache` for cross-session reuse
- `PersistentCacheDirectory` and `MaxPersistentCacheEntries` configuration properties
- Atomic writes (temp file + rename) for crash-safe persistent cache

### Added — Extension Maturity
- `IExtensionPriority` interface with `int Priority` for controlling processor execution order
- `maxAdocNetVersion` field in `extension.json` for forward-compatibility boundaries
- Priority-based processor sorting: lower priority values execute first, FIFO within same priority

### Changed
- Render cache disabled when any registered processor is non-deterministic (safety fix)
- Parse cache bypassed when non-deterministic extensions are present to prevent AST double-mutation
- `ClearCache()` now also clears persistent cache files on disk
- Processors sorted by priority on first Convert() call (stable sort preserving FIFO)

### Compatibility
- All new API is additive; no existing public API changed
- Zero-extension behavior is byte-identical to beta.11
- Parser and AST unmodified
- Core maintains zero external NuGet dependencies
- Both netstandard2.0 and net10.0 supported

## [1.0.0-beta.11] - 2026-04-06

### Added — Editor Integration
- `DocumentChange` immutable struct for representing text edits (offset, length, newText)
- `DocumentSnapshot` versioned document state with text and optional parsed AST
- `AdocEngine.ParseIncremental()` cache-aware re-parse method for editor scenarios
- Parse cache integration: identical text returns cached AST without re-parsing

### Added — Developer Experience
- `IOutputProcessor` interface for post-render transformations (HTML minification, watermarking)
- `AdocEngine.RegisterOutputProcessor()` with FIFO chaining after renderer output
- `KrokiDiagramToolRunner` HTTP-based diagram generation via Kroki API (opt-in)
- `IExtensionLifecycle` optional interface with Initialize/Dispose for resource-holding extensions
- `AdocEngine.Shutdown()` calls Dispose on all lifecycle extensions
- Extension diagnostics: `RenderContext.AddDiagnostic()` + `AdocEngine.LastExtensionDiagnostics`
- Zip-based extension install: `adocnet ext install myext.zip`
- Extension enable/disable: `adocnet ext enable/disable <name>` with registry persistence
- `ExtensionInfo.Enabled` property with `ExtensionRegistry.SetEnabled()` method

### Changed
- `ext list` now shows `[disabled]` indicator for disabled extensions
- `ext status` shows `Disabled` state for disabled extensions
- `ExtensionDirectoryLoader` skips disabled extensions during loading

### Compatibility
- All new API is additive; no existing public API changed
- Zero-extension behavior is byte-identical to beta.10
- Parser and AST unmodified
- Core maintains zero external NuGet dependencies
- Both netstandard2.0 and net10.0 supported

## [1.0.0-beta.10] - 2026-04-05

### Added
- Parse caching: SHA-256-keyed LRU cache avoids re-parsing identical input strings
- Render caching: composite-keyed LRU cache avoids re-rendering when input and options are unchanged
- `AdocEngine.EnableCaching` property (bool, default false) for opt-in caching
- `AdocEngine.MaxCacheEntries` property (int, default 16) for configurable cache size with LRU eviction
- `AdocEngine.ClearCache()` method for manual cache invalidation
- `CachedRenderBenchmarks` benchmark suite measuring cold vs cached performance
- Thread-safe `LruCache<TKey, TValue>` with O(1) lookup and eviction
- Documentation: PERFORMANCE.md guide covering caching configuration and performance numbers

### Performance
- Cache hit: 15x faster (small docs), 27x faster (medium), 45x faster (large ~500KB)
- Cache hit memory: 14-19x less allocation vs uncached path
- No regression on cold (uncached) path
- Cached output is byte-identical to non-cached output (verified by automated tests)

### Compatibility
- Caching is opt-in (`EnableCaching = false` by default) — zero behavior change for existing users
- All new API is additive; no existing public API changed
- Both caches work correctly with registered extensions
- Core maintains zero external NuGet dependencies
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.9] - 2026-03-31

### Added
- `ExtensionState` enum: `Loaded`, `Failed`, `Disabled`, `Incompatible` for per-extension state tracking
- `ExtensionLoadResult` structured load result with Name, State, FailureReason, and Processors
- Failure-based disabling: processors automatically disabled after consecutive failures (configurable via `MaxProcessorFailures`, default 3)
- `AdocEngine.ExtensionApiVersion` constant ("1.0") for extension API version compatibility
- `apiVersion` field in `extension.json` manifest for declaring required API version
- `AdocEngine.LoadExtensionSafe()` and `LoadExtensionsSafe()` returning `IReadOnlyList<ExtensionLoadResult>`
- CLI `adocnet ext status` command showing per-extension load state, version, and failure reasons
- `IsApiVersionCompatible()` check: extension major must match host, extension minor must be <= host
- Documentation: EXTENSION_SAFETY.md guide

### Compatibility
- Zero extensions loaded = output identical to beta.8 (when `MaxProcessorFailures = 0`)
- Default `MaxProcessorFailures = 3` introduces automatic disabling (new behavior vs. beta.8)
- All new API is additive; no existing public API changed
- Existing `LoadExtension()` / `LoadExtensions()` unchanged
- Core maintains zero external NuGet dependencies
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.8] - 2026-03-30

### Added
- Extension registry system: local `registry.json` index of installed extensions for fast querying
- `ExtensionInfo` model representing installed extension metadata (name, version, description, path, dependencies)
- `ExtensionRegistry` class with Load, Save, Add, Remove, Find, Search, and Rebuild operations
- Atomic registry writes (temp file + rename) to prevent corruption
- Automatic registry rebuild when `registry.json` is missing, corrupt, or out of sync with filesystem
- `DependencySpec` parser for dependency strings (`"name >= version"`)
- `DependencyValidator` for checking extension dependencies against registry (warn-only, never blocks)
- `dependencies` field in `extension.json` manifest (JSON array or comma-separated string)
- `AdocEngine.GetInstalledExtensions()` — static read-only query for installed extension metadata
- `AdocEngine.FindExtension(name)` — static read-only lookup by extension name
- CLI `adocnet ext info <name>` — show detailed info for an installed extension
- CLI `adocnet ext search <keyword>` — search installed extensions by name or description
- CLI `ext list` now reads from registry for faster listing
- CLI `ext install` and `ext remove` now update the registry automatically
- Extended `SimpleJsonParser` with `ParseObjectWithArray` and `ParseStringArray` methods
- `SimpleJsonWriter` for deterministic registry JSON serialization
- Documentation: EXTENSION_REGISTRY.md guide

### Compatibility
- Zero installed extensions = output identical to beta.7
- All new API is additive; no existing public API changed
- Dependency validation is advisory only — warns but never blocks loading
- Core maintains zero external NuGet dependencies
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.7] - 2026-03-30

### Added
- Extension packaging with `extension.json` manifest format (name, version, description, entry, minAdocNetVersion)
- Standard extension directory at `~/.adocnet/extensions/` with automatic loading
- `ExtensionManifest` model for parsing and validating manifest files
- `ExtensionDirectoryLoader` for scanning extension directories and loading entry DLLs
- `AdocEngine.LoadInstalledExtensions()` for manifest-based extension loading (default and custom directories)
- Version compatibility checking: `minAdocNetVersion` validated against running AdocNet version
- CLI `adocnet ext list` — list installed extensions with name, version, and description
- CLI `adocnet ext install <path>` — install extension from directory (`--force` to overwrite)
- CLI `adocnet ext remove <name>` — remove an installed extension
- CLI `--no-auto-extensions` flag to skip automatic loading of installed extensions
- Minimal JSON parser (`SimpleJsonParser`) for manifest files — zero external NuGet dependencies
- Documentation: EXTENSION_PACKAGING.md guide

### Compatibility
- Zero installed extensions = output identical to beta.6
- All new API is additive; no existing public API changed
- Core maintains zero external NuGet dependencies (hand-written JSON parser avoids System.Text.Json transitive conflicts)
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.6] - 2026-03-27

### Added
- Dynamic extension loading from external DLLs via `AdocEngine.LoadExtension(path)` and `AdocEngine.LoadExtensions(directory)`
- `IExtension` optional metadata interface for extension identification (Name, Version)
- `ExtensionLoader` public utility class for scanning assemblies for processor types
- CLI `--extensions <path>` flag for loading extension DLLs (repeatable)
- CLI `--extension-dir <dir>` flag for loading all DLLs from a directory (repeatable)
- Deterministic load ordering: alphabetical by DLL filename, sorted by type name within each assembly
- Error handling for invalid assemblies (`BadImageFormatException`), missing dependencies (`ReflectionTypeLoadException`), and types without parameterless constructors
- Documentation: DYNAMIC_EXTENSIONS.md guide

### Compatibility
- Zero extensions loaded = output identical to beta.5
- All new API is additive; no existing public API changed
- Uses `Assembly.LoadFrom` for netstandard2.0 compatibility (no AssemblyLoadContext)
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.5] - 2026-03-26

### Added
- Processing extension system: `IDocumentProcessor`, `IBlockProcessor`, `IInlineProcessor`
- `AdocEngine.RegisterDocumentProcessor()`, `RegisterBlockProcessor()`, `RegisterInlineProcessor()` with fluent API
- `AdocEngine.OnWarning` callback for non-fatal processor errors
- `NodeReplacements` for AST node replacement and removal during processing
- `ProcessingPipeline` with guaranteed FIFO execution order (document -> block -> inline)
- `IDiagramToolRunner` abstraction for external diagram tool invocation
- `ProcessDiagramToolRunner` implementation using `Process.Start` with deterministic output filenames
- `DiagramBlockProcessor` supporting PlantUML, Mermaid, Ditaa, Graphviz, and DOT languages
- Built-in example extensions: `IconMacroProcessor`, `DocumentMetadataProcessor`, `AutoIdBlockProcessor`
- 46 new extension tests (pipeline invocation, ordering, error handling, diagram, integration)
- Documentation: DIAGRAMS.md, updated EXTENSIONS.md with processor guide

### Compatibility
- Zero extensions registered = output identical to beta.4
- All new API is additive; no existing public API changed
- Registration freezes after first `Convert()` call (throws `InvalidOperationException`)
- Parser and AST unmodified
- All existing tests pass without modification

## [1.0.0-beta.4] - 2026-03-24

### Added
- Syntax highlighting tokenizer in AdocNet.Core with 7 language support (C#, Java, JavaScript, Python, JSON, XML/HTML, SQL)
- 9 token categories: keyword, string, comment, number, type, punctuation, attribute, preprocessor, plain
- Server-side syntax highlighting for HTML source blocks (`EnableSyntaxHighlighting` option)
- Syntax highlighting CSS rules in all 4 built-in HTML themes
- PDF syntax highlighting via `SyntaxColorScheme` with configurable per-token colors
- Github theme (`HtmlTheme.Github`) — GitHub-flavored HTML styling
- Liang/Knuth hyphenation engine with TeX US English patterns (`EnableHyphenation` option)
- Hyphenation-aware line breaking in PDF text layout
- Configurable paragraph spacing: `ParagraphSpacingBefore` and `ParagraphSpacingAfter`
- Configurable section spacing: `SectionSpacing`
- Heading color: `HeadingColor` property for PDF
- Body text color: `BodyColor` property for PDF
- Table header background: `TableHeaderBackground` property for PDF
- Block indent: `BlockIndent` property for PDF
- PDF style presets: `PdfRenderOptions.Compact` and `PdfRenderOptions.Presentation`
- 84 new tests across 8 test files
- Documentation: THEMING.md, SYNTAX_HIGHLIGHTING.md, TYPOGRAPHY.md

### Changed
- Justification max spacing tightened to 1.5x when hyphenation enabled (was 2x)
- PdfWriter word-wrapping code extracted into PdfWriter.WordWrap.cs partial
- Highlighted verbatim rendering moved to PdfRenderer.Blocks.cs partial

### Compatibility
- All new options default to values matching beta.3 output
- Syntax highlighting defaults to disabled (opt-in) in both renderers
- Existing tests pass without modification

## [1.0.0-beta.3] - 2026-03-24

### Added
- TrueType font embedding with Unicode support (cmap format 4 and 12)
- Font subsetting — only used glyphs are embedded, reducing PDF size
- JPEG image embedding via DCTDecode filter
- PNG image embedding with RGBA alpha channel support via SMask
- Clickable hyperlinks via PDF link annotations
- Table header repetition on continuation pages
- Total page count placeholder `{pages}` in headers/footers
- Configurable typography: FontSize, CodeFontSize, TitleFontSize, HeadingScale, LineSpacing
- Visual styling options: LinkColor, CodeBackground, AdmonitionBorderWidth
- RepeatTableHeader option for table page breaks
- PdfColor type for RGB color configuration
- Line-start punctuation prevention (no line starts with `)`, `.`, etc.)
- Justification spacing cap at 2x normal word space
- PDF renderer documentation (`docs/PDF_RENDERER.md`)
- 27 new tests covering fonts, images, links, tables, headers, configuration, determinism

### Changed
- PdfRenderOptions: 10 new properties (backward compatible — all have defaults matching beta.2)
- Renderer constants (font sizes, leading) now configurable via PdfRenderOptions
- PdfWriter and PdfRenderer split into partial classes for maintainability

## [1.0.0-beta.2] - 2026-03-20

### Added
- Zero-config API: `Adoc.ToHtml()`, `Adoc.ToPdf()`, `Adoc.ToStyledHtml()` one-liners
- Stream overloads: `Adoc.ToHtml(input, stream)`, `Adoc.ToPdf(input, stream)`, `Adoc.ConvertFile(path, stream)`
- Specialized CLI tools: `adocnet-pdf`, `adocnet-epub`, `adocnet-docbook`
- Asciidoctor-compatible CLI flags: `-b`, `-D`, `-a`, `-n`, `-e`
- Default output to file (matching Asciidoctor behavior), `-o -` for stdout
- Built-in document attributes: `{docyear}`, `{docdate}`, `{nbsp}`, `{cpp}`, etc.
- PDF text justification (paragraphs and table cells)
- Real per-character Helvetica AFM font metrics for accurate PDF text measurement
- Allocation guard tests for CI regression detection
- Asciidoctor compatibility documentation
- Comprehensive test fixtures (special characters, long paragraphs, anchors)
- **AdocNet.Layout** — UI-agnostic layout model (netstandard2.0 + net10.0) with LayoutBuilder for AST-to-layout conversion; supports paragraphs, headings, lists, code blocks, admonitions, tables, description lists, TOC, and all inline formatting
- **AdocNet.Avalonia** — Avalonia renderer (net10.0) that converts layout trees to stock Avalonia controls with rich inline formatting, clickable links, table grid rendering, and colored admonitions
- **Sample Avalonia Viewer** — minimal desktop app (`samples/AdocNet.Avalonia.Viewer`) for opening and rendering `.adoc` files with Fluent theme
- Parser: non-indented description list continuation (`Term::\nDescription`), `[horizontal]` attribute consumption, `[#id,reftext]` block anchors before description list items

### Fixed
- PDF paragraphs and code blocks running off right margin (text wrapping)
- Non-ASCII characters (copyright, accented) rendering as `?` in PDF
- `{docyear}` and other built-in attributes not substituted
- `[[id,reftext]]` anchor syntax putting comma+reftext in the ID
- Block anchors `[[id,reftext]]` dropped when preceding definition lists
- Cross-reference link text showing `[id]` instead of reftext
- PDF table column auto-sizing (equal-weight columns treated as auto-sized)

### Changed
- CLI default output changed from stdout to file
- Removed legacy CLI flags (`--styled`, `-f`, `--out-dir`)

## [1.0.0-beta.1] - 2026-03-19

Initial public beta.

### Features
- Full AsciiDoc parser with 94% Asciidoctor conformance (202/215 test cases)
- Four output renderers: HTML5, PDF 1.4, DocBook 5.0, EPUB 3.0
- CLI tools: `adocnet`, `adocnet-pdf`, `adocnet-epub`, `adocnet-docbook` with Asciidoctor-compatible flags
- Watch mode, live preview server, and project configuration
- LSP server with diagnostics, symbols, hover, go-to-definition, completion
- Extension architecture: custom renderers and include readers
- Multi-targeting: netstandard2.0 (.NET Framework 4.6.1+, .NET Core 2.0+) and net10.0 (optimized)
- Symbol packages (.snupkg) for all NuGet packages
- CI/CD with GitHub Actions (3-platform matrix, automated NuGet publishing)
