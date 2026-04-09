# Incremental Rendering

AdocNet supports incremental HTML rendering: when a document changes slightly,
only the modified sections are re-rendered and spliced into the previous output.
This is designed for live preview, IDE scenarios, and the Avalonia viewer.

## Overview

The incremental rendering pipeline has three layers:

1. **AST Structural Hashing** — each AST node computes a deterministic hash of its
   structure (kind, properties, children). Two identical subtrees have identical hashes.
2. **Tree Diff** — compares two document ASTs at the section level, identifying which
   top-level blocks changed, were added, or were removed.
3. **Incremental HTML Render** — re-renders only the changed sections and splices them
   into the previous HTML output using section markers.

## StructuralHash

Every `AstNode` has a `StructuralHash` property (type `int`) that is lazy-computed
on first access and cached on the node instance.

```csharp
var doc = parser.Parse(input).Document;
int hash = doc.StructuralHash; // Computed once, cached
```

### What the hash includes

- `Kind` — the node's `AstNodeKind` enum value
- `GetProperties()` — all node-specific key-value properties
- `BlockNode` properties — `Id`, `Reftext`, `Roles`, `Substitutions`
- Inline content — side-channel `Inlines`, `TitleInlines`, `TermInlines`, etc.
- Children — recursive structural hashes of all child nodes

### Hash algorithm

FNV-1a (32-bit), implemented directly in `AstNode` with zero external dependencies.
This keeps `AdocNet.Ast` dependency-free (no `System.Security.Cryptography` needed).
The hash is not cryptographic — it's for structural comparison, not security.

### Hash invalidation

Call `InvalidateStructuralHash()` after mutating the AST (e.g., after extensions run).
This clears the cached hash; the next access recomputes it.

```csharp
node.InvalidateStructuralHash();
// Next access to node.StructuralHash will recompute
```

## AstDiffer

`AstDiffer.DiffSections()` compares two `DocumentNode` trees and returns a list of
`AstDiffEntry` structs describing what changed at the top-level block level.

```csharp
using AdocNet.Editor;

var diff = AstDiffer.DiffSections(oldDoc, newDoc);
foreach (var entry in diff)
{
    Console.WriteLine($"Index {entry.Index}: {entry.ChangeType}");
}
```

### Change types

| Type | Meaning |
|------|---------|
| `Unchanged` | Block is structurally identical (hash match) |
| `Modified` | Block content changed (hash mismatch) |
| `Added` | New block with no corresponding old block |
| `Removed` | Old block with no corresponding new block |

### Matching strategy

Sections are matched using a two-pass algorithm:
1. **ID-based** — sections with `Id` attributes are matched across both documents
   regardless of position. This handles section reordering correctly.
2. **Positional** — remaining unmatched blocks are matched by their index position.

### Granularity

The diff operates at **section level** — each top-level child of `DocumentNode` is
one diff unit. A change to any node within a section marks the entire section as
Modified. This is pragmatic: renderers render sections as units, and finer granularity
would require complex intra-section HTML splicing.

## IncrementalHtmlRenderer

Performs the actual incremental rendering by combining tree diffing with HTML splicing.

```csharp
using AdocNet.Editor;
using AdocNet.Converters.Html;

var renderer = new HtmlRenderer();
var options = new HtmlRenderOptions { EnableIncrementalMarkers = true };

// First render (full)
var html = engine.Convert(input, options);

// After edit: incremental render
var incremental = new IncrementalHtmlRenderer(renderer, parser);
var updatedHtml = incremental.Render(oldDoc, newDoc, previousHtml, options);
```

### Section markers

When `EnableIncrementalMarkers = true`, the HTML renderer wraps each top-level block
in invisible comment markers:

```html
<!-- sect:0 -->
<h2 id="section-one">Section One</h2>
<div class="paragraph"><p>Content.</p></div>
<!-- /sect:0 -->
<!-- sect:1 -->
<h2 id="section-two">Section Two</h2>
<div class="paragraph"><p>More content.</p></div>
<!-- /sect:1 -->
```

These markers are used by the incremental renderer to identify and splice sections.
When markers are disabled (default), HTML output is identical to previous versions.

### Fallback to full render

The incremental renderer falls back to a full render when:
- Previous HTML has no section markers (first render, or markers disabled)
- Document metadata changed (title, attributes)
- Sections were added or removed (structural change)
- All sections are modified (no savings from incremental)

Fallback is always safe — it produces identical output to a fresh full render.

## ConvertIncrementalHtml (AdocEngine)

Convenience method on `AdocEngine` that orchestrates the entire incremental flow:

```csharp
var engine = new AdocEngine(new HtmlRenderer(), AdocParser.Parse);
var options = new HtmlRenderOptions { EnableIncrementalMarkers = true };

// Parse and render initially
var oldSnapshot = engine.ParseIncremental(DocumentSnapshot.Initial(oldText));
var previousHtml = RenderToString(engine, oldSnapshot.Document, options);

// After text changes
var newSnapshot = oldSnapshot.ApplyChanges(changes);
var parsedSnapshot = engine.ParseIncremental(newSnapshot);

// Incremental render
var updatedHtml = engine.ConvertIncrementalHtml(
    oldSnapshot, parsedSnapshot, previousHtml, options);
```

## Scope and limitations

- **HTML only** — incremental rendering is only supported for HTML output. PDF, DocBook,
  and EPUB produce monolithic output that cannot be spliced at the section level.
- **Section-level granularity** — changes are detected at the top-level block level.
  Sub-section changes (e.g., a single paragraph within a section) still re-render the
  entire containing section.
- **No true incremental parsing** — the parser still does a full re-parse on every change.
  `ParseIncremental` is cache-assisted (returns cached AST on exact text match) but does
  not do partial parsing.
- **Metadata changes cause full re-render** — changes to the document title or attributes
  affect global rendering state, so the entire document is re-rendered.
