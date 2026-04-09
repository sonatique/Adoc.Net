# Beta.15 Design — Incremental Rendering

> Reference: `docs/CONTEXT-BETA15.md`

## Overview

Three layers: **AST Structural Hashing** (detect what changed), **Tree Diff** (identify
changed sections), **Incremental HTML Render** (re-render only changed sections).

Goal: when a user edits a document and only one section changes, avoid re-rendering
the entire HTML. Instead, detect the change at the AST level and splice only the
affected section into the previous HTML output.

---

## Layer 1 — AST Structural Hashing

### 1.1 StructuralHash Property

**Location**: `AstNode` base class in `src/AdocNet.Ast/AstNode.cs`.

```csharp
public abstract class AstNode
{
    private long _structuralHash;
    private bool _structuralHashComputed;

    /// <summary>
    /// A structural hash of this node's content, properties, and children.
    /// Lazy-computed on first access and cached. Two subtrees with identical
    /// structure, properties, and children will have identical hashes.
    /// Uses FNV-1a (non-cryptographic, no external dependencies).
    /// </summary>
    public long StructuralHash
    {
        get
        {
            if (!_structuralHashComputed)
            {
                _structuralHash = ComputeStructuralHash();
                _structuralHashComputed = true;
            }
            return _structuralHash;
        }
    }

    /// <summary>
    /// Clears the cached structural hash, forcing recomputation on next access.
    /// Call after AST mutation (e.g., after extensions modify the tree).
    /// Propagation to ancestors is NOT automatic — callers must invalidate
    /// from the mutated node up to the root if parent hashes are needed.
    /// </summary>
    public void InvalidateStructuralHash()
    {
        _structuralHash = 0;
        _structuralHashComputed = false;
    }
}
```

**Type**: `long` (64-bit). FNV-1a produces 64 bits natively. Stored as a primitive
to avoid allocations. Not a hex string — structural hashes are for in-memory comparison,
not for cache keys or persistence.

**Lazy computation**: computed on first access, cached on the node instance. This avoids
paying the cost for nodes that are never compared. The cache is valid for the lifetime
of the node (AST nodes are effectively immutable after parsing + extension processing).

**Invalidation**: `InvalidateStructuralHash()` clears the cache. Called by
`ProcessingPipeline` after extensions run (extensions may mutate the AST). After
invalidation, the next access recomputes. No automatic ancestor propagation — the
pipeline invalidates the root, which forces lazy recomputation top-down.

### 1.2 Hash Algorithm — FNV-1a (64-bit)

**Why FNV-1a**: `AdocNet.Ast` has zero NuGet dependencies and must stay that way.
SHA-256 requires `System.Security.Cryptography` (NuGet on netstandard2.0). FNV-1a
is a fast, well-distributed non-cryptographic hash implementable in ~10 lines of C#.
Collision resistance is not critical — when hashes match, the diff algorithm can
optionally do a full structural comparison to confirm equality.

```csharp
// FNV-1a constants (64-bit)
const long FnvOffsetBasis = unchecked((long)0xcbf29ce484222325);
const long FnvPrime = unchecked((long)0x100000001b3);

private long ComputeStructuralHash()
{
    long hash = FnvOffsetBasis;

    // 1. Node kind (enum value)
    hash = FnvMix(hash, (int)Kind);

    // 2. Node-specific properties (from GetProperties)
    foreach (var kvp in GetProperties())
    {
        hash = FnvMixString(hash, kvp.Key);
        hash = FnvMixString(hash, kvp.Value);
    }

    // 3. BlockNode base properties (Id, Reftext, Roles, Substitutions)
    //    These are NOT in GetProperties() but affect rendering output.
    hash = MixBlockNodeProperties(hash);

    // 4. Side-channel inline collections (Inlines, TitleInlines, etc.)
    //    These are NOT in AstNode.Children but hold content nodes.
    foreach (var inline in GetStructuralInlines())
        hash = FnvMix(hash, inline.StructuralHash);

    // 5. Children (recursive)
    for (int i = 0; i < Children.Count; i++)
        hash = FnvMix(hash, Children[i].StructuralHash);

    return hash;
}
```

**Helper methods** (private, on AstNode):

```csharp
private static long FnvMix(long hash, long value)
{
    hash ^= value;
    hash *= FnvPrime;
    return hash;
}

private static long FnvMixString(long hash, string s)
{
    for (int i = 0; i < s.Length; i++)
    {
        hash ^= s[i];
        hash *= FnvPrime;
    }
    return hash;
}

// Default: no block properties. Overridden by BlockNode.
protected virtual long MixBlockNodeProperties(long hash) => hash;

// Default: no side-channel inlines. Overridden by nodes with Inlines.
protected virtual IEnumerable<AstNode> GetStructuralInlines() => [];
```

### 1.3 BlockNode Property Hashing

`BlockNode` has `Id`, `Reftext`, `Roles`, `Substitutions` that are NOT in
`GetProperties()` but affect rendered output. These must be included in the hash.

```csharp
// In BlockNode:
protected override long MixBlockNodeProperties(long hash)
{
    if (Id is not null) hash = FnvMixString(hash, Id);
    if (Reftext is not null) hash = FnvMixString(hash, Reftext);
    if (Roles.Count > 0)
    {
        for (int i = 0; i < Roles.Count; i++)
            hash = FnvMixString(hash, Roles[i]);
    }
    if (Substitutions is not null)
        hash = FnvMix(hash, (int)Substitutions.Value);
    return hash;
}
```

### 1.4 Side-Channel Inline Collections

Many node types store inline content in properties separate from `AstNode.Children`:

| Node type | Property | Content |
|-----------|----------|---------|
| `ParagraphNode` | `Inlines` | Parsed inline content of the paragraph |
| `SectionNode` | `TitleInlines` | Parsed inline content of the section title |
| `ListItemNode` | `Inlines` | Parsed inline content of the list item |
| `TableCellNode` | `Inlines` | Parsed inline content of the cell |
| `AdmonitionNode` | `Inlines` | Inline content for inline admonitions |
| `DescriptionItemNode` | `TermInlines` + `DescriptionInlines` | Term and description inlines |
| `FootnoteInlineNode` | `Inlines` | Footnote content inlines |
| `BibliographyEntryNode` | `Inlines` | Bibliography entry inlines |
| `StrongInlineNode` | `Children` (new) | Hides AstNode.Children with typed inline list |
| `EmphasisInlineNode` | `Children` (new) | Same pattern |
| `MonospaceInlineNode` | `Children` (new) | Same pattern |
| `HighlightInlineNode` | `Children` (new) | Same pattern (to verify) |

Each of these node types overrides `GetStructuralInlines()` to return its inline
collections. This mirrors the `AstPrettyPrinter.GetNodeInlines()` pattern.

```csharp
// Example: ParagraphNode
protected override IEnumerable<AstNode> GetStructuralInlines() => Inlines;

// Example: SectionNode
protected override IEnumerable<AstNode> GetStructuralInlines() => TitleInlines;

// Example: DescriptionItemNode
protected override IEnumerable<AstNode> GetStructuralInlines()
{
    foreach (var i in TermInlines) yield return i;
    foreach (var i in DescriptionInlines) yield return i;
}
```

**For `StrongInlineNode` etc.** (which use `new` to shadow `AstNode.Children`):
their `new Children` property is NOT the same as `AstNode.Children` (the base `_children`
list). The hash function accesses `AstNode.Children` (which is empty for these types),
so the typed `Children` must be returned via `GetStructuralInlines()`.

```csharp
// StrongInlineNode:
protected override IEnumerable<AstNode> GetStructuralInlines() => Children;
```

### 1.5 Hash Stability Guarantee

The hash MUST be **stable**: same tree structure = same hash value, always.
This requires:
- `GetProperties()` returns properties in deterministic order (already true — each
  node type yields in fixed order).
- `GetStructuralInlines()` returns inlines in deterministic order (already true —
  properties are fixed-order collections).
- FNV-1a constants are fixed (compile-time constants, not configurable).
- No floating-point, no culture-dependent formatting (already true — GetProperties
  returns strings).

### 1.6 Hash Invalidation Strategy

When to invalidate:
- **After extension processing**: `ProcessingPipeline.Run()` calls
  `doc.InvalidateStructuralHash()` on the root after all processors complete.
  This forces recomputation when hashes are next accessed.
- **Not needed after parsing**: the parser creates fresh nodes, so hashes are
  naturally uncomputed.

When NOT to invalidate:
- During diffing or rendering — AST is read-only at that point.
- Per-node during extension processing — too expensive. Invalidate root once at end.

---

## Layer 2 — Tree Diff

### 2.1 AstDiff Algorithm

**Location**: new class `AstDiff` in `src/AdocNet.Core/Incremental/AstDiff.cs`.

```csharp
public static class AstDiff
{
    public static IReadOnlyList<AstDiffEntry> Compare(
        DocumentNode oldDoc, DocumentNode newDoc);
}
```

**Algorithm**: compare top-level children of both documents.

1. Get old children = `oldDoc.Children` and new children = `newDoc.Children`.
2. Walk both lists, matching by **section ID** (for SectionNodes with Id) or by
   **positional index** (for non-section blocks and sections without Id).
3. For each matched pair: compare `StructuralHash`. If equal → Unchanged. If different → Modified.
4. Unmatched old children → Removed.
5. Unmatched new children → Added.

**Matching strategy** (two-pass):

- **Pass 1 — ID-based matching**: for SectionNodes with non-null `Id`, match old[i] to
  new[j] by Id. This handles sections that were reordered.
- **Pass 2 — Positional matching**: remaining unmatched children are matched by their
  index position in the respective lists. Old[k] matches new[k] if both exist.
- Leftover old children → Removed.
- Leftover new children → Added.

**Granularity**: section-level. Each top-level child of DocumentNode is one diff unit.
A change to any node within a section marks the entire section as Modified. This is
pragmatic — the HTML renderer renders sections as units, and finer granularity would
require intra-section HTML splicing which is fragile.

### 2.2 AstDiffEntry

```csharp
/// <summary>
/// Describes a change to a top-level document section.
/// </summary>
public readonly struct AstDiffEntry
{
    /// <summary>
    /// Index of this entry in the new document's children list.
    /// For Removed entries, this is -1 (no corresponding new child).
    /// </summary>
    public int NewIndex { get; init; }

    /// <summary>
    /// Index in the old document's children list.
    /// For Added entries, this is -1 (no corresponding old child).
    /// </summary>
    public int OldIndex { get; init; }

    /// <summary>The type of change.</summary>
    public AstDiffChangeType ChangeType { get; init; }
}
```

### 2.3 AstDiffChangeType Enum

```csharp
public enum AstDiffChangeType
{
    /// <summary>Section unchanged (hash match).</summary>
    Unchanged,

    /// <summary>Section content modified (hash mismatch).</summary>
    Modified,

    /// <summary>New section added (no old counterpart).</summary>
    Added,

    /// <summary>Old section removed (no new counterpart).</summary>
    Removed,
}
```

### 2.4 Edge Cases

| Scenario | Behavior |
|----------|----------|
| Empty old document | All new children are Added |
| Empty new document | All old children are Removed |
| Identical documents | All entries are Unchanged |
| Section reordered | Matched by ID → both show as Unchanged if content identical |
| Section added in middle | Positional mismatch → downstream sections may show as Modified |
| All sections changed | All entries are Modified — falls back to full render |
| Document title changed | Title is rendered separately; diff covers children only. Title change detected by comparing `oldDoc.Title` vs `newDoc.Title` and `oldDoc.Attributes` vs `newDoc.Attributes`. |
| Non-section top-level blocks | Treated as anonymous sections — matched by position only |

### 2.5 Performance

- Hash comparison is O(1) per node (cached `long` comparison).
- Tree diff is O(N) where N = number of top-level children.
- ID-based matching uses a dictionary lookup — O(N) total.
- No deep tree traversal during diff itself — only hash comparisons.
- Hash computation is O(T) where T = total nodes in tree (done once, cached).

---

## Layer 3 — Incremental HTML Render

### 3.1 HTML Section Markers

The HtmlRenderer must add invisible comment markers around each top-level section
so the incremental renderer can identify and splice sections in the output.

**Marker format**:
```html
<!-- adoc:block:0 -->
<h2 id="section-one">Section One</h2>
<p>Content of section one.</p>
<!-- /adoc:block:0 -->
<!-- adoc:block:1 -->
<h2 id="section-two">Section Two</h2>
<p>Content of section two.</p>
<!-- /adoc:block:1 -->
```

**Rules**:
- Markers wrap each top-level child of the document (not just SectionNodes).
- Index is the position in `DocumentNode.Children` (0-based).
- Open marker: `<!-- adoc:block:N -->` before the block's HTML.
- Close marker: `<!-- /adoc:block:N -->` after the block's HTML.
- Markers are only emitted when incremental rendering is enabled (opt-in).
- The document title (`<h1>`) and footnotes section are NOT wrapped — they are
  always re-rendered (outside the section-level diff).

**Implementation**: modify `RenderChildBlocks()` in HtmlRenderer to emit markers
around each child block when the render option `EnableIncrementalMarkers` is true.

```csharp
// New option on HtmlRenderOptions:
public bool EnableIncrementalMarkers { get; init; }
```

**Visible output impact**: when `EnableIncrementalMarkers` is false (default),
HTML output is identical to beta.14. When true, only invisible HTML comments are
added. The rendered visual appearance is unchanged.

### 3.2 IncrementalHtmlRenderer

**Location**: new class in `src/AdocNet.Core/Incremental/IncrementalHtmlRenderer.cs`.

```csharp
/// <summary>
/// Performs incremental HTML rendering by diffing two ASTs and splicing
/// only changed sections into the previous HTML output.
/// </summary>
public sealed class IncrementalHtmlRenderer
{
    private readonly HtmlRenderer _renderer;

    public IncrementalHtmlRenderer(HtmlRenderer renderer);

    /// <summary>
    /// Renders only the changed sections and splices them into the previous HTML.
    /// </summary>
    /// <param name="oldDoc">The previous document AST.</param>
    /// <param name="newDoc">The new document AST.</param>
    /// <param name="previousHtml">The full HTML from the previous render (with markers).</param>
    /// <param name="options">Render options (must have EnableIncrementalMarkers = true).</param>
    /// <returns>Updated HTML with changed sections re-rendered.</returns>
    public string Render(
        DocumentNode oldDoc,
        DocumentNode newDoc,
        string previousHtml,
        HtmlRenderOptions? options = null);
}
```

### 3.3 Incremental Render Algorithm

```
1. Compute diff = AstDiff.Compare(oldDoc, newDoc)
2. If ALL entries are Modified or diff has Added/Removed: fall back to full render
3. Parse previousHtml into sections using marker comments
4. Build new HTML:
   a. Copy document prologue (title, pre-section content)
   b. For each entry in diff (ordered by new index):
      - Unchanged: copy section HTML from previousHtml
      - Modified: re-render this section via HtmlRenderer
      - Added: render this new section via HtmlRenderer
      - Removed: skip (don't copy from previous)
   c. Copy footnotes section (always re-rendered — footnote numbering may change)
5. Return assembled HTML
```

### 3.4 HTML Section Parsing

To extract sections from `previousHtml`, scan for marker comments:

```csharp
/// <summary>
/// Extracts section HTML fragments from a marker-annotated HTML string.
/// Returns a dictionary mapping section index to its HTML content
/// (including the markers themselves).
/// </summary>
internal static Dictionary<int, string> ParseSections(string html);
```

**Parsing approach**: simple string scanning for `<!-- adoc:block:N -->` and
`<!-- /adoc:block:N -->` patterns. Extract the content between matching open/close
markers. This is robust because:
- Markers use a unique prefix (`adoc:block:`) unlikely to collide with user content.
- Markers are well-formed (the renderer controls their format exactly).
- No HTML parsing library needed — just `IndexOf` operations.

### 3.5 Document Metadata Change Detection

The diff algorithm covers section-level changes, but the document may also change in:
- **Title**: `oldDoc.Title` vs `newDoc.Title`
- **Attributes**: `oldDoc.Attributes` vs `newDoc.Attributes` (affects icon rendering, etc.)

If title or attributes changed: fall back to full render. These affect global rendering
state (e.g., `icons=font` attribute changes how admonitions render everywhere).

```csharp
private static bool HasMetadataChanged(DocumentNode oldDoc, DocumentNode newDoc)
{
    if (oldDoc.Title != newDoc.Title) return true;
    if (oldDoc.Attributes.Count != newDoc.Attributes.Count) return true;
    foreach (var kvp in oldDoc.Attributes)
    {
        if (!newDoc.Attributes.TryGetValue(kvp.Key, out var newValue)
            || kvp.Value != newValue)
            return true;
    }
    return true; // should be false on equality
}
```

If metadata changed → full render. This is conservative but correct.

### 3.6 Fallback to Full Render

The incremental renderer falls back to full render when:
1. `previousHtml` has no section markers (first render, or markers disabled).
2. Document metadata (title, attributes) changed.
3. Sections were added or removed (structural change to document layout).
4. All sections are Modified (no savings from incremental).
5. Footnotes are affected (footnote numbering is global).

Fallback is safe: it produces identical output to a fresh full render. The
incremental path is purely an optimization.

### 3.7 Scope — HTML Only

**HTML**: incremental rendering supported. Sections are identifiable via comment markers.
Splicing is simple string operations.

**PDF**: NOT supported. PDF is a binary format with cross-reference tables. Splicing
individual pages or sections is not practical without a full PDF rewrite.

**DocBook**: NOT supported. DocBook XML has cross-references and entity resolution
that makes partial replacement fragile.

**EPUB**: NOT supported. EPUB is a zip container with multiple HTML files and metadata.
Section changes may affect the table of contents, spine, and other metadata files.

---

## Integration with Existing Infrastructure

### 4.1 Integration with ParseIncremental

The new flow for editor/live-preview scenarios:

```
// Editor sends changes
var newSnapshot = oldSnapshot.ApplyChanges(changes);
var parsedSnapshot = engine.ParseIncremental(newSnapshot);

// Incremental render (new in beta.15)
var incrementalRenderer = new IncrementalHtmlRenderer(htmlRenderer);
var newHtml = incrementalRenderer.Render(
    oldSnapshot.Document,
    parsedSnapshot.Document,
    previousHtml,
    new HtmlRenderOptions { EnableIncrementalMarkers = true });
```

**Important**: `ParseIncremental` is cache-assisted (cache hit = O(1), miss = full re-parse).
The incremental render adds value even on cache miss — the AST still needs to be compared
to identify which sections changed for rendering purposes.

### 4.2 Integration with AdocEngine

New convenience method on `AdocEngine`:

```csharp
/// <summary>
/// Performs an incremental HTML render. Compares old and new documents,
/// re-renders only changed sections, and splices into previous HTML.
/// Falls back to full render when incremental is not possible.
/// </summary>
public string RenderIncremental(
    DocumentNode oldDoc,
    DocumentNode newDoc,
    string previousHtml,
    HtmlRenderOptions? options = null);
```

This is a convenience wrapper — it creates `IncrementalHtmlRenderer` internally
and delegates. Users who want more control can use `IncrementalHtmlRenderer` directly.

### 4.3 Integration with Caching

The render cache (keyed by input hash + options + format) stores full render output.
Incremental rendering does NOT interact with the render cache directly:
- The render cache stores full HTML (no markers).
- Incremental rendering stores marker-annotated HTML in the editor's state.
- These are orthogonal: the render cache is for repeated identical renders,
  incremental rendering is for similar-but-different renders.

### 4.4 Integration with Extensions

Extensions run between parse and render. After extensions modify the AST:
1. `ProcessingPipeline.Run()` calls `doc.InvalidateStructuralHash()` on the root.
2. The next access to `StructuralHash` recomputes from scratch.
3. The diff algorithm sees the post-extension AST (correct behavior).

Extensions that are non-deterministic disable the render cache (existing behavior).
They do NOT disable incremental rendering — the diff compares actual AST structures,
not cached output. Even non-deterministic extensions produce a concrete AST that can
be diffed.

---

## Testing Strategy

### 5.1 Layer 1 — Structural Hash Tests

- **Determinism**: same tree → same hash (multiple computations).
- **Sensitivity**: change one property → hash changes.
- **Children sensitivity**: add/remove/reorder a child → hash changes.
- **Inline sensitivity**: change inline content → hash changes.
- **BlockNode properties**: change Id/Roles → hash changes.
- **Type sensitivity**: different node kinds with same properties → different hashes.
- **Stability across access**: hash computed once, cached, same on re-access.
- **Invalidation**: after `InvalidateStructuralHash()`, next access recomputes.

### 5.2 Layer 2 — Tree Diff Tests

- **Identical documents**: all Unchanged.
- **Single section modified**: one Modified, rest Unchanged.
- **Section added at end**: one Added, rest Unchanged.
- **Section removed**: one Removed, rest Unchanged.
- **Section added in middle**: Added + downstream positional changes.
- **Sections reordered (with IDs)**: matched by ID, may show as Unchanged.
- **Empty documents**: empty diff.
- **Non-section top-level blocks**: positional matching.

### 5.3 Layer 3 — Incremental Render Tests

- **Correctness**: incremental render output == full render output (always).
- **Marker format**: markers present when `EnableIncrementalMarkers = true`.
- **No markers by default**: markers absent when option is false.
- **Splice correctness**: modified section HTML is fresh, unchanged sections preserved.
- **Fallback**: metadata change triggers full render.
- **Round-trip**: render → edit → incremental render → compare to full render.

---

## Explicit Non-Goals

### 6.1 Character-Level Diffing

No character-level or line-level diffing of rendered output. The diff operates at
the AST level (section granularity). HTML output differences are handled by
re-rendering entire sections.

### 6.2 Parallel Rendering

No parallel rendering of sections. Sections are rendered sequentially because
the HtmlRenderer maintains state (section numbering counters, footnote collectors)
that is accumulated across sections.

### 6.3 Non-HTML Incremental Rendering

PDF, DocBook, and EPUB renderers produce monolithic output. Incremental rendering
is not applicable to these formats in beta.15.

### 6.4 True Incremental Parsing

The parser still does full re-parse on every change. `ParseIncremental` is
cache-assisted only. True incremental parsing (reusing partial parse results) is
a v2.x feature requiring parser architecture changes.

### 6.5 Sub-Section Granularity

The diff operates at the top-level children of DocumentNode. Changes within a
section always re-render the entire section. Finer granularity (paragraph-level
or inline-level) would require more complex HTML splicing and is deferred.

### 6.6 AST Node Equality

`StructuralHash` is for efficient comparison, not for `Equals`/`GetHashCode` contracts.
No `IEquatable<AstNode>`, no `operator ==`, no changes to object equality semantics.

---

## File Plan

### New files

| File | Layer | Description |
|------|-------|-------------|
| `src/AdocNet.Core/Incremental/AstDiff.cs` | 2 | Tree diff algorithm |
| `src/AdocNet.Core/Incremental/AstDiffEntry.cs` | 2 | Diff entry struct |
| `src/AdocNet.Core/Incremental/AstDiffChangeType.cs` | 2 | Change type enum |
| `src/AdocNet.Core/Incremental/IncrementalHtmlRenderer.cs` | 3 | Incremental render logic |
| `src/AdocNet.Core/Incremental/HtmlSectionParser.cs` | 3 | Marker-based section extraction |
| `tests/AdocNet.Ast.Tests/StructuralHashTests.cs` | 1 | Hash tests |
| `tests/AdocNet.Core.Tests/Incremental/AstDiffTests.cs` | 2 | Diff tests |
| `tests/AdocNet.Core.Tests/Incremental/IncrementalHtmlRenderTests.cs` | 3 | Render tests |

### Modified files

| File | Layer | Change |
|------|-------|--------|
| `src/AdocNet.Ast/AstNode.cs` | 1 | Add `StructuralHash`, `InvalidateStructuralHash()`, `GetStructuralInlines()`, `MixBlockNodeProperties()`, FNV-1a helpers |
| `src/AdocNet.Ast/BlockNode.cs` | 1 | Override `MixBlockNodeProperties()` |
| `src/AdocNet.Ast/ParagraphNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/SectionNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/ListItemNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/TableCellNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/AdmonitionNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/DescriptionItemNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/FootnoteInlineNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/BibliographyEntryNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/StrongInlineNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/EmphasisInlineNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/MonospaceInlineNode.cs` | 1 | Override `GetStructuralInlines()` |
| `src/AdocNet.Ast/HighlightInlineNode.cs` | 1 | Override `GetStructuralInlines()` (if it has Children) |
| `src/AdocNet.Converters.Html/HtmlRenderer.cs` | 3 | Add section markers in `RenderChildBlocks()` |
| `src/AdocNet.Converters.Html/HtmlRenderOptions.cs` | 3 | Add `EnableIncrementalMarkers` |
| `src/AdocNet.Core/AdocEngine.cs` | 3 | Add `RenderIncremental()` convenience method |
| `src/AdocNet.Core/Extensions/ProcessingPipeline.cs` | 1 | Call `InvalidateStructuralHash()` after run |

---

## Implementation Phases

| Phase | Scope | Dependencies |
|-------|-------|-------------|
| P02 | AST Structural Hashing — StructuralHash property, FNV-1a, GetStructuralInlines overrides, BlockNode property mixing, invalidation, tests | None (AST layer only) |
| P03 | Tree Diff — AstDiff, AstDiffEntry, AstDiffChangeType, matching algorithm, tests | P02 (needs StructuralHash) |
| P04 | Incremental HTML Render — section markers, HtmlSectionParser, IncrementalHtmlRenderer, AdocEngine integration, tests | P02 + P03 |
