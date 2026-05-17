# AdocNet Layout + Avalonia — Migration Status

## Phase Progress

| Phase | Command | Description | Effort | Status |
|-------|---------|-------------|--------|--------|
| P00 | `/ui-p00` | Context Discovery | Medium (~10-15 turns) | **Complete** |
| P01 | `/ui-p01` | Project Scaffolding | Low-Med (~8-12 turns) | **Complete** |
| P02 | `/ui-p02` | Layout Model Design | Medium (~8-10 turns) | **Complete** |
| P03 | `/ui-p03` | Layout Model Implementation | Low-Med (~10-15 turns) | **Complete** |
| P04 | `/ui-p04` | Layout Builder (AST → Layout) | **High** (~20-30 turns) | **Complete** |
| Check A | `/ui-check-a` | Layout Layer Integrity | Low (~5-8 turns) | **Pass** |
| P05 | `/ui-p05` | Minimal Avalonia Renderer | Med-High (~15-20 turns) | **Complete** |
| P06 | `/ui-p06` | Inline Rendering | Medium (~10-15 turns) | **Complete** |
| P07 | `/ui-p07` | Code Blocks + Admonitions | Low (~5-8 turns) | **Complete** |
| Check B | `/ui-check-b` | Renderer Integrity | Low (~5-8 turns) | **Pass** |
| P08 | `/ui-p08` | Interaction (Viewer) | Low-Med (~8-10 turns) | **Complete** |
| P09 | `/ui-p09` | Light Performance Pass | Low (~5-8 turns) | **Complete** |
| Reflect | `/ui-reflect` | Self-Reflection | Medium (~8-10 turns) | **Complete** |
| P10 | `/ui-p10` | Sample Viewer App | **High** (~15-25 turns) | **Complete** |
| Check C | `/ui-check-c` | Final Validation | Medium (~10-15 turns) | **Pass** |

## Open Issues

(none yet)

## Session Log

| Date | Phase | Notes |
|------|-------|-------|
| 2026-03-21 | P00 | Context Discovery complete. Created `docs/CONTEXT-UI.md` with full AST inventory (17 block types, 18 inline types, 2 non-block/non-inline types), renderer pattern analysis, project map, and test conventions. |
| 2026-03-21 | P01 | Project Scaffolding complete. Created AdocNet.Layout (ns2.0+net10.0), AdocNet.Avalonia (net10.0, Avalonia 11.3.12), AdocNet.Layout.Tests (NUnit 4.x). All added to .slnx. Build: 0 errors, 0 warnings. Tests: all pass. |
| 2026-03-21 | P02 | Layout Model Design complete. Created `docs/AdocNet.LayoutModel.md` — 1 root, 6 block types, 6 inline types, 1 enum. Section flattening, admonition normalization, constructor-based immutability. |
| 2026-03-21 | P03 | Layout Model Implementation complete. 16 files created in AdocNet.Layout. Builds for both netstandard2.0 and net10.0 with 0 warnings. |
| 2026-03-21 | P04 | Layout Builder complete. LayoutBuilder.Build() maps DocumentNode → DocumentLayout with section flattening, admonition normalization, inline conversion. 15 tests all pass. |
| 2026-03-21 | Check A | All 7 checks pass: deps correct, no UI types, model pure, builder pure, ns2.0 compat, 15 tests, 0 warnings. |
| 2026-03-21 | P05 | Minimal Avalonia Renderer complete. AvaloniaRenderer maps 6 block types to stock Avalonia controls. Plain text extraction for inlines (rich rendering in P06). Fixed namespace collision with `global::Avalonia`. |
| 2026-03-21 | P06 | Inline Rendering complete. Paragraphs/headings now use TextBlock.Inlines with Run, Bold, Italic, Span (mono), Underline (links), LineBreak. 0 warnings. |
| 2026-03-21 | P07 | Code blocks: language label, non-wrapping text. Admonitions: left accent border, kind-specific colors (blue/green/orange/red). |
| 2026-03-21 | Check B | All 5 checks pass: deps correct, no AST leakage, 319 lines, layout clean, build+tests pass. |
| 2026-03-21 | P08 | Clickable links via InlineUIContainer + PointerPressed → Process.Start. Hand cursor on hover. |
| 2026-03-21 | P09 | Perf pass: cached static brushes, skip StackPanel when no language label, Array.Empty for empty collections in builder, const bullet prefix. |
| 2026-03-21 | Reflect | No issues found. Architecture clean, no AST leakage, ns2.0 healthy, no TODOs, 15 meaningful tests. Renderer at 346 lines (watch item). |
| 2026-03-21 | P10 | Sample Viewer app complete. 6 files: csproj, Program, App, MainWindow (AXAML+codebehind), sample.adoc. Open file → parse → layout → render pipeline. Fluent theme. |
| 2026-03-21 | Check C | **ALL 10 CHECKS PASS.** Deps correct, no violations, existing code untouched, dual-target works, 15 tests pass, 0 warnings, slnx correct, docs complete, no placeholders. Migration complete. |
