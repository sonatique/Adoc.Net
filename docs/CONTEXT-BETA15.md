# Beta.15 Context — Incremental Rendering

> Generated during P00 context discovery. Read-only reference for subsequent phases.

## AstNode Base Class

**File**: `src/AdocNet.Ast/AstNode.cs` (36 lines)

```csharp
public abstract class AstNode
{
    private readonly List<AstNode> _children = [];
    public SourceRange Source { get; set; }
    public IReadOnlyList<AstNode> Children => _children;
    public abstract AstNodeKind Kind { get; }
    public void AddChild(AstNode child);
    public void InsertChild(int index, AstNode child);
    public virtual IEnumerable<KeyValuePair<string, string>> GetProperties() => [];
}
```

### Key observations

- **No hash or equality overrides** — no `GetHashCode()`, no `Equals()`.
- **No `StructuralHash`** — must be added by beta.15.
- `Children` backed by `List<AstNode>`, exposed as `IReadOnlyList<AstNode>`.
- `Source` is a `SourceRange` (readonly record struct with Start/End `SourcePosition`).
- `Kind` is an enum (`AstNodeKind`) — 38 values for all concrete types.

### GetProperties() — Hash-Friendly Design

Every concrete node type overrides `GetProperties()` to yield key-value pairs
representing all semantically meaningful data for that node. This is the ideal
input for structural hashing because:

1. It captures all node-specific content without knowing the concrete type.
2. Returns `IEnumerable<KeyValuePair<string, string>>` — deterministic iteration.
3. Properties are already string-serialized (no floating-point, no culture issues).

**Pattern observed across all node types**: properties include ALL content that
distinguishes one node from another of the same kind. For example:
- `SectionNode`: Level, Title
- `ParagraphNode`: Text (only when Inlines is empty)
- `DelimitedBlockNode`: BlockKind, Style, Title, Language, Attribution, CitationSource, Content, Callouts
- `TextInlineNode`: Value
- `ListNode`: ListKind, Start, ListStyle
- `TableNode`: HasHeader, IsAutoWidth, HasFooter, Stripes, Grid, Frame, Columns

**Caveat**: `ParagraphNode.GetProperties()` returns Text only when `Inlines.Count == 0`.
When inlines are populated, the structural hash must also consider children (which it
naturally does via recursive child hashing). Similarly, some nodes have `Inlines` or
`TitleInlines` collections that are children conceptually but stored as separate properties.
The hash computation must account for these — but since they appear as child nodes in the
AST tree, the recursive hash should capture them.

**Important**: `BlockNode` base has `Id`, `Reftext`, `Roles`, `Substitutions` properties
that are NOT yielded by `GetProperties()`. The hash must include these explicitly or
they risk being invisible to structural comparison.

## Concrete Node Types — 38 Total

### Hierarchy

| Base class | Count | Types |
|-----------|-------|-------|
| `AstNode` (direct) | 3 concrete | `DocumentNode`, `TableRowNode`, `TableCellNode` |
| `BlockNode` | 17 concrete | `SectionNode`, `ParagraphNode`, `ListNode`, `ListItemNode`, `DelimitedBlockNode`, `TableNode`, `AdmonitionNode`, `BlockImageNode`, `DescriptionListNode`, `DescriptionItemNode`, `BibliographyEntryNode`, `AudioNode`, `VideoNode`, `PageBreakNode`, `ThematicBreakNode`, `TocNode`, `IndexNode` |
| `InlineNode` | 18 concrete | `TextInlineNode`, `EmphasisInlineNode`, `StrongInlineNode`, `MonospaceInlineNode`, `HighlightInlineNode`, `LinkInlineNode`, `InlineLinkMacroNode`, `InlineImageNode`, `InlineMacroNode`, `CrossReferenceInlineNode`, `FootnoteInlineNode`, `PassthroughInlineNode`, `InlineAnchorNode`, `InterDocumentXrefNode`, `SuperscriptInlineNode`, `SubscriptInlineNode`, `IndexTermNode`, `IndexTermHiddenNode` |
| Abstract bases | 3 | `AstNode`, `BlockNode`, `InlineNode` |

### AstNodeKind Enum

38 values defined in `src/AdocNet.Ast/AstNodeKind.cs`: Document, Section, Paragraph,
List, ListItem, DelimitedBlock, Table, TableRow, TableCell, InlineText, InlineEmphasis,
InlineStrong, InlineMonospace, InlineLink, InlineLinkMacro, InlineImage, BlockImage,
DescriptionList, DescriptionItem, Admonition, InlinePassthrough, InlineCrossReference,
InlineFootnote, BibliographyEntry, InlineMacro, InlineSuperscript, InlineSubscript,
InterDocumentXref, Toc, InlineAnchor, InlineHighlight, Video, Audio, PageBreak,
ThematicBreak, IndexTerm, IndexTermHidden, Index.

## HTML Section Rendering

**File**: `src/AdocNet.Converters.Html/HtmlRenderer.cs`

### Document structure

```
RenderDocument()
  -> RenderDocumentBody()
       -> <h1>Title</h1>  (if document has title)
       -> RenderChildBlocks()  (iterates document.Children)
            -> RenderBlock() per child
                -> SectionNode -> RenderSection()
                -> ParagraphNode -> RenderParagraph()
                -> ... etc
  -> RenderFootnotesSection()
```

### Section rendering (RenderSection, line 376)

```csharp
private void RenderSection(SectionNode section, ...)
{
    // Level 1 -> <h2>, Level 2 -> <h3>, etc.
    var tag = section.Level switch { 1 => "h2", 2 => "h3", ... };
    sb.Append($"<{tag} id=\"{section.Id}\">");
    // ... renders title inlines
    sb.Append($"</{tag}>\n");
    RenderChildBlocks(sb, section.Children, ...);
}
```

### Key observations for incremental rendering

1. **No section wrapper elements**: sections emit a heading tag followed by child blocks
   directly. There are no `<div class="sect1">` or `<section>` wrapper tags.
2. **No comment markers**: no `<!-- sect:N -->` markers exist in current output.
3. **ID attributes**: sections with `Id` get `id="..."` on the heading tag.
4. **Sections are top-level children**: of `DocumentNode.Children`, sections are direct
   children containing their own nested blocks.

**For incremental rendering**: the HtmlRenderer will need to add invisible comment
markers (`<!-- sect:N -->`) around sections so the incremental renderer can identify
and splice changed sections in the output. The beta.15 rules explicitly allow this
modification: "HtmlRenderer may add invisible section comment markers."

### Section identification strategy

Sections are identifiable by their position in `DocumentNode.Children`. Each top-level
child block maps to a contiguous region of HTML output. Sections with `Id` are also
identifiable by their heading `id` attribute. For blocks without explicit IDs, a
positional index (`sect:0`, `sect:1`, ...) is needed.

## Current Incremental Flow (Cache-Only)

**File**: `src/AdocNet.Core/AdocEngine.cs`, line 407

```csharp
public DocumentSnapshot ParseIncremental(DocumentSnapshot snapshot)
{
    if (_enableCaching)
    {
        var inputHash = CacheKeyBuilder.ComputeInputHash(snapshot.Text);
        if (_parseCache.TryGet(inputHash, out var cachedDoc))
            return new DocumentSnapshot(snapshot.Version, snapshot.Text, cachedDoc);
        var doc = Parser(snapshot.Text);
        _parseCache.Set(inputHash, doc);
        return new DocumentSnapshot(snapshot.Version, snapshot.Text, doc);
    }
    var parsed = Parser(snapshot.Text);
    return new DocumentSnapshot(snapshot.Version, snapshot.Text, parsed);
}
```

### How it works today

1. Editor sends `DocumentChange` objects (offset, length, newText).
2. `DocumentSnapshot.ApplyChanges()` produces a new snapshot with updated text.
3. `ParseIncremental()` hashes the full text via SHA-256, checks parse cache.
4. **Cache hit**: return cached `DocumentNode` (no re-parse).
5. **Cache miss**: full re-parse via `Parser(text)`, cache result.

### What's missing (beta.15 adds)

- **No AST diffing**: even with a cache hit, there's no way to know WHAT changed in the AST.
- **No structural hashing on AST nodes**: can't compare old vs new AST efficiently.
- **No incremental render**: every render re-renders the entire document.
- **No section markers in HTML**: can't identify which HTML regions correspond to which sections.

### Flow after beta.15

```
Text changes → ApplyChanges → ParseIncremental (cache-assisted)
  → StructuralHash on old + new AST
  → TreeDiff (compare hashes at section level)
  → IncrementalRender (re-render only changed sections, splice into previous HTML)
```

## Caching Infrastructure (Reusable)

**File**: `src/AdocNet.Core/Caching/CacheKeyBuilder.cs`

- `ComputeInputHash(string)` — SHA-256 hex of UTF-8 bytes. Used for parse cache keys.
- `ComputeRenderKey(inputHash, format, options)` — composite SHA-256 of input+format+options.
- `ComputeOptionsHash(RenderOptions)` — reflects over all public properties, sorts by name.
- **SHA-256 is in `System.Security.Cryptography`** — available on both netstandard2.0 and net10.0.
- `#if NET5_0_OR_GREATER` uses `SHA256.HashData()`, else `SHA256.Create()`.

### For beta.15 structural hashing

Per the rules: structural hashing should use **FNV-1a or DJB2**, NOT SHA-256.
Reason: `AdocNet.Ast` has zero dependencies and must stay that way.
`System.Security.Cryptography` requires NuGet on netstandard2.0.
A non-cryptographic hash is sufficient — collisions acceptable since full tree
comparison verifies equality when hashes match.

SHA-256 remains in `AdocNet.Core` for cache keys (where it's already used).

## DocumentSnapshot

**File**: `src/AdocNet.Core/Editor/DocumentSnapshot.cs`

Immutable snapshot with:
- `Version` (int, monotonically increasing)
- `Text` (string, full document content)
- `Document` (DocumentNode?, parsed AST)
- `Diagnostics` (IReadOnlyList<Diagnostic>)

Factory: `DocumentSnapshot.Initial(text)` creates version 0.
Mutation: `ApplyChanges(changes)` returns new snapshot with incremented version.

## Summary of What Beta.15 Needs to Add

### Layer 1 — AST Structural Hashing (in AdocNet.Ast)
- Add `StructuralHash` property to `AstNode`
- Lazy-computed on first access, cached on node
- FNV-1a or DJB2 hash of: `Kind` + `GetProperties()` + children's hashes
- Must also account for `BlockNode.Id/Reftext/Roles/Substitutions`

### Layer 2 — Tree Diff (new, in AdocNet.Core)
- `AstDiff.Compare(DocumentNode old, DocumentNode new)` -> `IReadOnlyList<AstDiffEntry>`
- Section-level granularity
- Uses StructuralHash for O(1) equality check per subtree
- ChangeType: Unchanged, Modified, Added, Removed

### Layer 3 — Incremental HTML Render (new, in AdocNet.Core or Converters.Html)
- Add `<!-- sect:N -->` comment markers to HtmlRenderer output
- `IncrementalRender(oldDoc, newDoc, previousHtml)` method
- Re-renders only changed sections, splices into previous HTML
- HTML-only (PDF/DocBook/EPUB remain full-render)
