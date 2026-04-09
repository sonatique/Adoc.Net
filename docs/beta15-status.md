# AdocNet v1.0.0-beta.15 — Status

## Phase Progress

| Phase | Command | Description | Effort | Criteria | Status |
|-------|---------|-------------|--------|----------|--------|
| P00 | `/b15-p00` | Context Discovery | Medium (~10-12) | 8 | **PASS** |
| P01 | `/b15-p01` | Design Document | **HIGH** (~15-20) | 9 | **PASS** |
| P02 | `/b15-p02` | AST Structural Hashing | **HIGH** (~18-22) | 13 | **PASS** |
| P03 | `/b15-p03` | Tree Diff | **HIGH** (~18-22) | 11 | **PASS** |
| P04 | `/b15-p04` | Incremental HTML Render | **HIGH** (~18-22) | 10 | **PASS** |
| Check A | `/b15-check-a` | System Integrity | Low-Med (~8-10) | 13 | **PASS** |
| P05 | `/b15-p05` | Documentation | Medium (~10-15) | 9 | **PASS** |
| Reflect | `/b15-reflect` | Self-Reflection | Medium (~8-10) | 5 checks | **PASS** |
| Check C | `/b15-check-c` | Final Validation | Medium (~10-15) | 25 + feature table | **PASS** |

## Validation Reports

(appended after each phase)

### P00 — Context Discovery (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `docs/CONTEXT-BETA15.md` exists | PASS |
| 2 | AstNode has no hash/equality overrides confirmed | PASS |
| 3 | GetProperties() documented as hash-friendly | PASS |
| 4 | Concrete node type count documented | PASS (38 concrete) |
| 5 | HTML section rendering documented | PASS |
| 6 | Current incremental flow documented | PASS |
| 7 | No source files modified | PASS |
| 8 | Document >= 100 lines | PASS (230 lines) |

**Verdict: PASS** (8/8)

Key findings:
- 38 concrete node types, `GetProperties()` on all returns string KV pairs (ideal hash input)
- `BlockNode.Id/Reftext/Roles/Substitutions` not in GetProperties — must hash separately
- HTML sections render as bare `<hN>` tags, no wrappers or comment markers
- `ParseIncremental()` is cache-only (no AST diffing)
- Structural hash must use FNV-1a/DJB2 (keep AdocNet.Ast dependency-free)

### P01 — Design Document (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `docs/BETA15_DESIGN.md` exists | PASS |
| 2 | All 12+ sections present | PASS (42 H2+ sections) |
| 3 | StructuralHash property design specified | PASS |
| 4 | Hash algorithm specified (Kind + GetProperties + children) | PASS |
| 5 | AstDiff algorithm described | PASS |
| 6 | HTML section markers identified | PASS |
| 7 | IncrementalHtmlRender method specified | PASS |
| 8 | No source files modified | PASS |
| 9 | Document >= 300 lines | PASS (693 lines) |

**Verdict: PASS** (9/9)

Key design decisions:
- FNV-1a 64-bit hash stored as `long` on AstNode (lazy-computed, cached)
- `GetStructuralInlines()` virtual method handles side-channel Inlines collections
- `MixBlockNodeProperties()` captures BlockNode.Id/Reftext/Roles/Substitutions
- Section markers: `<!-- adoc:block:N -->` / `<!-- /adoc:block:N -->` (opt-in)
- Two-pass matching: ID-based for named sections, positional for the rest
- Fallback to full render on metadata change, structural changes, or no markers

### P02 — AST Structural Hashing (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS (0 warnings, 0 errors) |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1921 passed) |
| 3 | AstNode has StructuralHash property | PASS |
| 4 | Hash is dependency-free (no NuGet in Ast) | PASS (0 PackageReferences) |
| 5 | Hash includes Kind | PASS |
| 6 | Hash includes GetProperties | PASS |
| 7 | Hash includes children hashes | PASS |
| 8 | Same structure -> same hash test passes | PASS |
| 9 | Different content -> different hash test passes | PASS |
| 10 | InvalidateHash test passes | PASS |
| 11 | >= 5 new hashing tests | PASS (17 tests) |
| 12 | All existing tests pass | PASS |

**Verdict: PASS** (12/12)

Implementation: FNV-1a 32-bit hash on AstNode, with `GetStructuralInlines()` for
side-channel inline collections (12 node types) and `MixAdditionalState()` for
BlockNode properties and inline Roles.

### P03 — Tree Diff (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1933 passed) |
| 3 | `AstDiffChangeType.cs` exists | PASS |
| 4 | `AstDiffEntry.cs` exists | PASS |
| 5 | `AstDiffer.cs` exists | PASS |
| 6 | Identical docs -> all Unchanged | PASS |
| 7 | Modified section detected | PASS |
| 8 | Added section detected | PASS |
| 9 | Removed section detected | PASS |
| 10 | >= 5 new diff tests | PASS (12 tests) |
| 11 | All existing tests pass | PASS |

**Verdict: PASS** (11/11)

Implementation: Two-pass matching (ID-based then positional), StructuralHash comparison,
handles reorder/add/remove/modify. 12 test cases including edge cases.

### P04 — Incremental HTML Render (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1943 passed) |
| 3 | `IncrementalHtmlRenderer.cs` exists | PASS |
| 4 | AdocEngine has ConvertIncrementalHtml | PASS |
| 5 | Modified section re-rendered correctly | PASS |
| 6 | Incremental output == full re-render output | PASS |
| 7 | Added section handled | PASS (falls back to full render) |
| 8 | Removed section handled | PASS (falls back to full render) |
| 9 | >= 4 new incremental tests | PASS (10 tests) |
| 10 | Existing tests pass | PASS |

**Verdict: PASS** (10/10)

Implementation: HtmlRenderer emits `<!-- sect:N -->` markers (opt-in via EnableIncrementalMarkers),
IncrementalHtmlRenderer diffs + splices, AdocEngine.ConvertIncrementalHtml convenience method.
Falls back to full render on structural changes, metadata changes, or missing markers.

### Check A — System Integrity (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1943 passed) |
| 3 | Parser unmodified | PASS (0 lines diff) |
| 4 | Existing renderers unmodified | PASS (PDF/DocBook/EPUB: 0 diff, HTML Render signature unchanged) |
| 5 | AstNode has StructuralHash | PASS |
| 6 | Hash is deterministic | PASS (test: Same_structure_produces_same_hash) |
| 7 | AdocNet.Ast has zero PackageReferences | PASS (0) |
| 8 | AstDiffer exists | PASS |
| 9 | IncrementalHtmlRenderer exists | PASS |
| 10 | Incremental output == full render output | PASS (test: Incremental_output_matches_full_render) |
| 11 | netstandard2.0 builds | PASS (Ast + Core DLLs present) |
| 12 | net10.0 builds | PASS (Ast + Core DLLs present) |
| 13 | Existing caching unbroken | PASS (35 cache tests pass) |

**Verdict: PASS** (13/13)

### P05 — Documentation (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `docs/INCREMENTAL_RENDERING.md` exists >= 100 lines | PASS (170 lines) |
| 2 | Doc covers all 4 key types | PASS (13 occurrences) |
| 3 | Doc notes HTML only + section-level granularity | PASS |
| 4 | CHANGELOG has beta.15 section >= 6 items | PASS (10 items) |
| 5 | Directory.Build.props version = 1.0.0-beta.15 | PASS |
| 6 | README mentions incremental rendering | PASS |
| 7 | `dotnet build` exits 0 | PASS |
| 8 | `dotnet test` exits 0, 0 failures | PASS (1943 passed) |
| 9 | No source code modified in P05 | PASS (docs/config only) |

**Verdict: PASS** (9/9)

### Reflect — Self-Reflection (2026-04-10)

#### Hash Performance
- **Lazy**: StructuralHash is computed only on first access (`_structuralHashComputed` guard).
  For documents where incremental rendering is not used, zero cost.
- **O(N) for full tree**: hash computation visits every node once (recursive through children
  and structural inlines). For a large document (~500KB, ~1000 nodes), this is sub-millisecond.
- **Cached**: subsequent accesses are O(1) (return cached `int`).
- **Not a bottleneck**: FNV-1a is a few multiplications per character. The parser and renderer
  are orders of magnitude more expensive.

#### Diff Correctness
- **ID-based matching**: sections with `Id` are matched across reordering. Tested.
- **Positional fallback**: sections without IDs match by position. Tested.
- **Edge case: section added in middle with no IDs**: downstream sections match by shifted
  positions, which may show Modified even if content is identical. This is conservative
  (causes more re-rendering) but correct. With IDs, this is handled properly.
- **Edge case: all sections removed/added**: produces all Removed + all Added entries,
  incremental renderer falls back to full render. Correct.

#### Incremental Correctness
- **Byte-identical**: test `Incremental_output_matches_full_render_for_modified_section`
  verifies `Assert.That(result, Is.EqualTo(expectedHtml))` — string-identical.
- **Markers invisible**: HTML comments `<!-- sect:N -->` are invisible in browsers.
  Default `EnableIncrementalMarkers = false` means existing output unchanged.
- **Fallback safe**: any case the incremental renderer can't handle (structural changes,
  metadata changes, missing markers) falls back to full render.

#### File Sizes
- All files under 300 lines. Largest: `IncrementalHtmlRenderer.cs` (215), `AstDiffer.cs` (206),
  `AstNode.cs` (145). No files flagged.

#### Test Count
- **39 new tests** in beta.15: 17 structural hash + 12 tree diff + 10 incremental render.
- All existing 1904 tests continue to pass (total: 1943).

### Check C — Final Validation (2026-04-10)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | `dotnet build` exits 0 | PASS (0 warnings, 0 errors) |
| 2 | `dotnet test` exits 0, 0 failures | PASS (1943 passed, 14 skipped) |
| 3 | Parser unmodified | PASS (0 lines diff) |
| 4 | Renderers' Render() unmodified (HTML: markers only) | PASS (markers are additive, opt-in) |
| 5 | AstNode.StructuralHash exists | PASS |
| 6 | Hash deterministic (same tree = same hash) | PASS (test) |
| 7 | Different content = different hash | PASS (test) |
| 8 | Hash includes Kind + GetProperties + children | PASS (code review) |
| 9a | Hash dependency-free (0 PackageReferences in Ast) | PASS |
| 9b | AstDiffer.DiffSections exists | PASS |
| 10 | Identical docs -> all Unchanged | PASS (test) |
| 11 | Modified section detected | PASS (test) |
| 12 | Added/Removed sections detected | PASS (tests) |
| 13 | IncrementalHtmlRenderer exists | PASS |
| 14 | ConvertIncrementalHtml on AdocEngine | PASS |
| 15 | Incremental output == full render output | PASS (test) |
| 16 | Section add/remove handled in incremental | PASS (falls back to full render, tested) |
| 17 | Non-HTML format -> falls back | PASS (no markers = fallback, tested) |
| 18 | No NEW file > 500 lines | PASS (max 215 lines) |
| 19 | No commit messages mention AI tools | PASS (0 mentions) |
| 20 | Directory.Build.props = 1.0.0-beta.15 | PASS |
| 21 | netstandard2.0 builds | PASS |
| 22 | net10.0 builds | PASS |
| 23 | Existing caching unbroken | PASS (35 cache tests pass) |
| 24 | docs/INCREMENTAL_RENDERING.md exists | PASS |

### Feature Checklist

| Feature | Test exists? | Test passes? |
|---------|-------------|-------------|
| StructuralHash deterministic | Yes: `Same_structure_produces_same_hash` | PASS |
| Different content = different hash | Yes: `Different_text_produces_different_hash` | PASS |
| Identical subtrees = same hash | Yes: `Identical_subtrees_in_different_locations_have_same_hash` | PASS |
| InvalidateHash resets hash | Yes: `InvalidateHash_clears_cached_hash` | PASS |
| AstDiffer: identical docs -> Unchanged | Yes: `Identical_documents_all_unchanged` | PASS |
| AstDiffer: modified section detected | Yes: `Modified_section_detected` | PASS |
| AstDiffer: added section detected | Yes: `Section_added_at_end` | PASS |
| AstDiffer: removed section detected | Yes: `Section_removed_from_middle` | PASS |
| Incremental render: modified section only | Yes: `Incremental_output_matches_full_render_for_modified_section` | PASS |
| Incremental render == full render | Yes: `Multiple_modified_sections_all_updated` | PASS |
| Incremental: section added | Yes: `Section_added_falls_back_to_full_render` | PASS |
| Incremental: section removed | Yes: `Section_removed_falls_back_to_full_render` | PASS |
| Non-HTML: throws/falls back | Yes: `No_markers_in_previous_html_falls_back_to_full_render` | PASS |

**Verdict: PASS** (25/25 criteria + 13/13 features)

## Open Issues

(none)
