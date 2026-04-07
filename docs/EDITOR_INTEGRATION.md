# Editor Integration

AdocNet provides editor-friendly APIs for live preview, IDE integration, and incremental
document processing. These APIs are designed for scenarios where a document is edited
repeatedly and re-parsed frequently.

## Core Types

### DocumentChange

An immutable struct representing a single text edit at a character offset.

```csharp
using AdocNet.Editor;

// Insert text at offset 5
var insert = new DocumentChange(offset: 5, length: 0, newText: "hello");

// Delete 3 characters starting at offset 10
var delete = new DocumentChange(offset: 10, length: 3, newText: "");

// Replace 4 characters starting at offset 0
var replace = new DocumentChange(offset: 0, length: 4, newText: "new text");
```

Changes are applied sequentially. Each change's offset refers to the text state
**after** all preceding changes (standard LSP convention).

```csharp
// Apply multiple changes
string result = DocumentChange.ApplyAll(originalText, changes);
```

### DocumentSnapshot

An immutable snapshot of a document at a specific version. Tracks version number,
full text, and optionally a parsed document.

```csharp
using AdocNet.Editor;

// Create initial snapshot (version 0)
var snapshot = DocumentSnapshot.Initial("= My Document\n\nFirst paragraph.");

// Apply edits to produce a new version
var changes = new[] { new DocumentChange(15, 0, "\n\nNew paragraph.") };
var updated = snapshot.ApplyChanges(changes);
// updated.Version == 1
// updated.Text contains the new content
// updated.Document is null until parsed
```

Snapshots are independent — they do not hold references to previous versions,
avoiding memory leaks in long editing sessions.

### ParseIncremental

Cache-aware re-parsing via `AdocEngine`. When caching is enabled, identical text
content returns the cached AST without re-parsing.

```csharp
var engine = new AdocEngine(renderer, AdocParser.Parse);
engine.EnableCaching = true;

var s0 = DocumentSnapshot.Initial("= Title\n\nContent");
var parsed = engine.ParseIncremental(s0);
// parsed.Document is now populated

// After an edit
var s1 = parsed.ApplyChanges(new[] { new DocumentChange(18, 0, " more") });
var reparsed = engine.ParseIncremental(s1);
// If the text matches a cached parse, no re-parsing occurs
```

## Typical Editor Flow

```
1. User opens file → DocumentSnapshot.Initial(text)
2. User edits → snapshot.ApplyChanges(changes)
3. Debounce → engine.ParseIncremental(snapshot)
4. Display parsed.Document in preview
5. Read engine.LastExtensionDiagnostics for extension warnings
6. Repeat from step 2
```

## Cache Integration

`ParseIncremental` reuses the same LRU parse cache used by `Convert()`:

- **Cache enabled**: computes SHA-256 of snapshot text, checks parse cache, returns
  cached AST on hit, parses and caches on miss.
- **Cache disabled**: always performs a full re-parse. No overhead.
- `ClearCache()` invalidates the parse cache, forcing re-parse on next call.
- `MaxCacheEntries` controls cache size (default 16, LRU eviction).

An editor doing `ParseIncremental` warms the cache for subsequent `Convert()` calls
with the same text, and vice versa.

## Extension Diagnostics

Extensions can emit structured diagnostics during processing:

```csharp
// Inside an extension's Process method:
context.AddDiagnostic(new Diagnostic(
    DiagnosticSeverity.Warning,
    "PlantUML tool not available",
    node.Source));
```

After `Convert()`, diagnostics are available via:

```csharp
engine.Convert(input, output);
foreach (var diag in engine.LastExtensionDiagnostics)
    Console.WriteLine($"{diag.Severity}: {diag.Message} at {diag.Range}");
```

These use the same `Diagnostic` type as parse diagnostics, making it straightforward
for IDEs to merge both sets into a unified diagnostic view.

## Limitations

- **No true incremental parser**: `ParseIncremental` is cache-assisted, not AST-diffing.
  If the text changes, a full re-parse occurs. The cache helps with undo/redo and
  scenarios where the same text appears multiple times.
- **No incremental rendering**: when the AST changes, a full re-render is needed.
  The render cache handles repeated renders of unchanged content.
- **DocumentSnapshot does not auto-parse**: call `ParseIncremental` explicitly to
  populate the `Document` property.
