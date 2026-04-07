# AdocNet v1.0.0-beta.11 — Design Document

> Two themes: **Editor Integration** (incremental-friendly APIs) and **Developer Experience**
> (packaging, lifecycle, post-render processing, diagnostics).

---

## Theme A — Editor Integration

### 1. DocumentChange Model

An immutable struct representing a single text edit at a character offset.

```csharp
// src/AdocNet.Core/Editor/DocumentChange.cs
namespace AdocNet.Editor;

/// <summary>
/// An immutable text change: replace <see cref="Length"/> characters starting at
/// <see cref="Offset"/> with <see cref="NewText"/>.
/// </summary>
public readonly struct DocumentChange
{
    /// <summary>Zero-based character offset into the document text.</summary>
    public int Offset { get; }

    /// <summary>Number of characters to remove (0 for pure insertion).</summary>
    public int Length { get; }

    /// <summary>Replacement text (empty string for pure deletion).</summary>
    public string NewText { get; }

    public DocumentChange(int offset, int length, string newText)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Offset = offset;
        Length = length;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }
}
```

**Composing changes**: changes are applied sequentially — each change's offset refers
to the text state *after* all preceding changes. Callers must adjust offsets themselves
(standard LSP convention). A helper `DocumentChange.ApplyAll(string text, IReadOnlyList<DocumentChange>)` static
method applies a list of changes in order and returns the resulting string.

**Design rationale**: `readonly struct` avoids allocation. Offsets are character-based
(not line:column) because that's what editors use internally and what makes sequential
application straightforward. Line-based mapping can be derived from the full text.

---

### 2. DocumentSnapshot Model

Tracks a versioned document state for editor scenarios.

```csharp
// src/AdocNet.Core/Editor/DocumentSnapshot.cs
namespace AdocNet.Editor;

/// <summary>
/// An immutable snapshot of a document at a specific version.
/// Holds the full text and a lazily-computed parse result.
/// </summary>
public sealed class DocumentSnapshot
{
    /// <summary>Monotonically increasing version number (starts at 1).</summary>
    public int Version { get; }

    /// <summary>The full document text at this version.</summary>
    public string Text { get; }

    /// <summary>The parse result for this version (computed on first access).</summary>
    public ParseResult ParseResult { get; }

    public DocumentSnapshot(int version, string text, ParseResult parseResult);
}
```

**Creating snapshots**: two factory paths:

1. **Initial**: `DocumentSnapshot.Create(string text, Func<string, ParseResult> parser)` —
   creates version 1 from text, immediately parses.
2. **Incremental**: `DocumentSnapshot.ApplyChanges(IReadOnlyList<DocumentChange> changes, Func<string, ParseResult> parser)` —
   instance method on an existing snapshot. Applies changes to `Text`, bumps version,
   parses the new text.

**Snapshot chaining**: each snapshot is independent — it does not hold a reference to
previous snapshots. This avoids memory leaks in long editing sessions. The text and
parse result are the only retained state.

**Thread safety**: snapshots are immutable once created. Creating a new snapshot from
an existing one is safe from any thread.

---

### 3. ParseIncremental API

New method on `AdocEngine` for cache-aware re-parse.

```csharp
/// <summary>
/// Applies changes to a previous snapshot and returns a new snapshot.
/// If caching is enabled and the new text matches a cached parse, the cache is used.
/// Otherwise performs a full re-parse.
/// </summary>
public DocumentSnapshot ParseIncremental(
    DocumentSnapshot previous,
    IReadOnlyList<DocumentChange> changes)
```

**Algorithm**:
1. Apply all `DocumentChange` entries to `previous.Text` → `newText`.
2. If `EnableCaching` is true:
   a. Compute `CacheKeyBuilder.ComputeInputHash(newText)`.
   b. Check parse cache. If hit → return new `DocumentSnapshot(previous.Version + 1, newText, cachedResult)`.
3. Parse `newText` using `Parser` function.
4. If caching enabled, store in parse cache.
5. Return new `DocumentSnapshot(previous.Version + 1, newText, freshResult)`.

**No true incremental parsing**: this is explicitly "cache-assisted" — if the text hash
matches a previous parse (e.g., an undo operation, or whitespace-only change that produces
the same text as a prior version), we skip re-parsing. If not, full parse. True AST-diffing
incremental parsing is out of scope (v2.x).

**Interaction with existing `Convert()`**: `ParseIncremental` is an alternative entry
point for editor scenarios. It returns a `DocumentSnapshot` with a `ParseResult`, not
rendered output. Editors can then call `Render()` separately if needed.

---

### 4. Integration with AdocEngine Caching

`ParseIncremental` reuses the existing `LruCache<string, DocumentNode>` parse cache
(keyed by SHA-256 of input text).

- When `EnableCaching` is `false`, `ParseIncremental` still works — it just always
  does a full re-parse. No performance penalty; the method remains functional.
- When `EnableCaching` is `true`, `ParseIncremental` checks and populates the same
  parse cache used by `Convert()`. This means an editor doing `ParseIncremental` warms
  the cache for a subsequent `Convert()` call with the same text.
- `ClearCache()` clears the parse cache, so the next `ParseIncremental` will re-parse.
- `MaxCacheEntries` bounds the parse cache size (LRU eviction applies).

**New parser function requirement**: `ParseIncremental` needs a `Func<string, ParseResult>`
rather than `Func<string, DocumentNode>`. Since the existing `Parser` property returns
`DocumentNode`, we add a new optional property:

```csharp
/// <summary>
/// Optional full parser function that returns ParseResult (with diagnostics).
/// Used by ParseIncremental. Falls back to wrapping Parser if not set.
/// </summary>
public Func<string, ParseResult>? FullParser { get; set; }
```

If `FullParser` is null, `ParseIncremental` wraps the `Parser` function:
`new ParseResult(Parser(input), Array.Empty<Diagnostic>())`.

---

## Theme B — Developer Experience

### 5. IOutputProcessor — Post-Render Transformation

```csharp
// src/AdocNet.Core/Extensions/IOutputProcessor.cs
namespace AdocNet.Extensions;

/// <summary>
/// Transforms rendered output after the renderer has completed.
/// Use cases: HTML minification, watermarking, custom post-processing.
/// </summary>
public interface IOutputProcessor
{
    /// <summary>
    /// Transforms the rendered output bytes.
    /// </summary>
    /// <param name="rendered">The rendered output bytes from the renderer.</param>
    /// <param name="format">The renderer format string (e.g., "html", "pdf").</param>
    /// <returns>The transformed output bytes.</returns>
    byte[] Process(byte[] rendered, string format);
}
```

**Registration**: `engine.RegisterOutputProcessor(IOutputProcessor processor)` — fluent,
must be called before first `Convert()`, same frozen-check as other registrations.
Multiple processors run in registration order (FIFO).

**Pipeline position**: Parse → Extensions → Render → **OutputProcessors** → write to caller's stream.

**Caching decision**: render cache stores **pre-processor** output. Output processors
always run on every `Convert()` call, even for cached renders. Rationale:
- Output processors may have side effects (e.g., watermarking with a timestamp).
- Output processors may depend on external state that changes between calls.
- Caching post-processor output would require including processor state in the cache key,
  which is complex and fragile.
- The render cache's primary value is avoiding the expensive parse + extension + render
  pipeline. Output processors are typically lightweight transforms.

**Interaction with `ConvertFile()`**: output processors run identically — `ConvertFile`
delegates to `Convert()` internally.

---

### 6. KrokiDiagramToolRunner

Implements `IDiagramToolRunner` using the Kroki HTTP API (https://kroki.io).

```csharp
// src/AdocNet.Core/Extensions/KrokiDiagramToolRunner.cs
namespace AdocNet.Extensions;

public sealed class KrokiDiagramToolRunner : IDiagramToolRunner
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public KrokiDiagramToolRunner(string baseUrl = "https://kroki.io", HttpClient? httpClient = null);

    public bool IsAvailable { get; } // HEAD request to base URL, cached after first check

    public string? Generate(string language, string source, string outputDirectory);
}
```

**HTTP protocol**: POST to `{baseUrl}/{language}/png` with body = diagram source text,
Content-Type = `text/plain`. Kroki returns the PNG image bytes.

**Supported languages**: plantuml, mermaid, graphviz, dot, ditaa, blockdiag, nwdiag,
seqdiag, actdiag, c4plantuml, erd, excalidraw, nomnoml, pikchr, svgbob, umlet, vega,
vegalite, wavedrom. The `IsDiagramLanguage` check in `DiagramBlockProcessor` already
gates which blocks are processed — Kroki handles the actual language support.

**Error handling**:
- HTTP failure (non-2xx) → return null (DiagramBlockProcessor falls back to code block).
- Network timeout → return null. Default timeout: 30 seconds (configurable via HttpClient).
- Connection refused → `IsAvailable` returns false, no generation attempted.

**IsAvailable caching**: the first call to `IsAvailable` makes a HEAD request to the
base URL. The result is cached for the lifetime of the runner instance. This avoids
repeated network checks. If the initial check fails, all `Generate` calls return null.

**HttpClient lifetime**: caller owns the HttpClient (or we create a default one).
The runner does NOT dispose it — follows .NET HttpClient best practices
(HttpClient is designed to be long-lived and shared).

**Output format**: always PNG. The generated file is saved to `outputDirectory` with
a deterministic filename (SHA-256 hash of source, same approach as `ProcessDiagramToolRunner`).

**No new NuGet deps**: `System.Net.Http.HttpClient` is available on netstandard2.0
as part of the framework.

---

### 7. Zip Install Support

Extend `adocnet ext install` to accept `.zip` files in addition to directories.

**Detection**: if the source path ends with `.zip` (case-insensitive) and is an
existing file, treat it as a zip archive.

**Algorithm**:
1. Extract zip to a temp directory (`Path.GetTempPath()` + GUID subfolder).
2. Validate: the extracted content must contain `extension.json` either at the root
   or in a single top-level subdirectory.
3. Load and validate the manifest (same as directory install).
4. Copy validated directory to `~/.adocnet/extensions/{name}/` (same as directory install).
5. Clean up temp directory.

**Error handling**:
- Invalid zip → error message, exit 1.
- No manifest found → error message, exit 1.
- Temp directory cleanup failure → warning (non-fatal).

**Implementation**: uses `System.IO.Compression.ZipFile.ExtractToDirectory()`.
The `System.IO.Compression` namespace is available on netstandard2.0 via framework reference.
If the Cli project needs it, add `<PackageReference Include="System.IO.Compression" />`
only if not already available (it typically is on net10.0).

---

### 8. Extension Enable/Disable

Add an `Enabled` property to `ExtensionInfo`, persisted in `registry.json`.

**ExtensionInfo changes**:
```csharp
/// <summary>Whether this extension is enabled for loading. Default: true.</summary>
public bool Enabled { get; }
```

Add `Enabled` parameter to constructor (default true). Update `FromManifest` and
`FromDictionary` factory methods. Update `DependenciesToString` → add `EnabledToString`.

**Registry JSON changes**: add `"enabled": "true"` field to each extension entry.
When reading, missing `enabled` field defaults to `true` (backward compatible with
existing registries).

**ExtensionDirectoryLoader changes**: `LoadInstalledExtensions` checks `ExtensionInfo.Enabled`
from the registry before loading. Disabled extensions are skipped with an info-level
log (not a warning — it's intentional).

Implementation approach: `LoadInstalledExtensions` already reads manifests from the
filesystem. Beta.11 adds a registry check: load the registry, for each extension
subdirectory, check if the registry says it's disabled. If disabled, skip.

**CLI commands**:
- `adocnet ext enable <name>` — loads registry, finds extension, sets Enabled=true, saves.
- `adocnet ext disable <name>` — loads registry, finds extension, sets Enabled=false, saves.
- `adocnet ext list` — shows `[disabled]` indicator next to disabled extensions.
- `adocnet ext status` — shows `Disabled` state for disabled extensions.

**ExtensionRegistry changes**: add `SetEnabled(string name, bool enabled)` method.
Creates a new `ExtensionInfo` with the toggled Enabled flag (since ExtensionInfo is
immutable-ish — the property is get-only). Or: make `Enabled` settable. Design
decision: add a `SetEnabled` method to registry that replaces the entry.

---

### 9. IExtensionLifecycle

Optional interface for extensions that hold resources.

```csharp
// src/AdocNet.Core/Extensions/IExtensionLifecycle.cs
namespace AdocNet.Extensions;

/// <summary>
/// Optional lifecycle interface for extensions that hold resources
/// (file handles, HTTP clients, temp directories). Extensions that do not
/// hold resources need not implement this interface.
/// </summary>
public interface IExtensionLifecycle
{
    /// <summary>
    /// Called after the extension is instantiated and registered.
    /// Use for one-time initialization (open connections, create temp dirs, etc.).
    /// </summary>
    void Initialize();

    /// <summary>
    /// Called when the extension is being unloaded or the engine is being disposed.
    /// Use to release resources (close connections, delete temp files, etc.).
    /// </summary>
    void Dispose();
}
```

**NOT `IDisposable`**: this is a custom interface to avoid coupling to `System.IDisposable`
semantics. The method is named `Dispose()` for familiarity but has no `using` pattern
integration.

**When `Initialize()` is called**: after the extension is instantiated via reflection
in `ExtensionLoader`, before it is registered into the engine. Specifically, in
`RegisterExtensions()` — after `_documentProcessors.Add(dp)` etc.

Actually, better: call `Initialize()` at the end of `RegisterExtensions()` after all
processors from a single load operation are registered. This means if an extension
provides multiple processors, all are registered before `Initialize()` runs.

**When `Dispose()` is called**: two triggers:
1. When `AdocEngine` itself is disposed (new: make AdocEngine implement `IDisposable`? NO —
   that changes the public API contract. Instead: add an explicit `Shutdown()` method).
   Better: add `public void Shutdown()` which calls `Dispose()` on all lifecycle extensions.
2. NOT called on disable — disable only affects loading, not running instances.

**Error handling**: if `Initialize()` throws, catch the exception, invoke `OnWarning`,
and skip the extension (remove its processors from the lists). If `Dispose()` throws,
catch and invoke `OnWarning`.

**Extensions that don't implement it**: completely unaffected. The lifecycle check is
`if (instance is IExtensionLifecycle lifecycle) lifecycle.Initialize()`.

---

### 10. Extension Diagnostics

Extensions emit structured `Diagnostic` objects during processing.

**RenderContext changes**:
```csharp
// Added to RenderContext
private readonly List<Diagnostic> _diagnostics = new();

/// <summary>
/// Adds a diagnostic produced during extension processing.
/// </summary>
public void AddDiagnostic(Diagnostic diagnostic)
{
    _diagnostics.Add(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
}

/// <summary>
/// Gets all diagnostics produced during extension processing.
/// </summary>
public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
```

**How extensions use it**:
```csharp
public void Process(BlockNode node, RenderContext context)
{
    if (!_runner.IsAvailable)
    {
        context.AddDiagnostic(new Diagnostic(
            DiagnosticSeverity.Warning,
            "PlantUML tool not available, rendering as code block",
            node.Source));
        return;
    }
}
```

**Surfacing diagnostics after Convert()**:
Add `LastDiagnostics` property to `AdocEngine`:
```csharp
/// <summary>
/// Diagnostics from the most recent Convert() call's extension processing.
/// Empty if no extensions ran or no diagnostics were emitted.
/// </summary>
public IReadOnlyList<Diagnostic> LastDiagnostics { get; private set; }
    = Array.Empty<Diagnostic>();
```

In `Convert()`, after `RunExtensions()`, capture `context.Diagnostics` into
`LastDiagnostics`. This is the simplest surfacing mechanism — no return value changes,
no callbacks.

**Thread safety**: `LastDiagnostics` is set during `Convert()`. If multiple threads
call `Convert()` concurrently, `LastDiagnostics` reflects the most recent call.
This is acceptable — concurrent convert calls already share the engine's mutable state
(failure counters, disabled processors). For thread-safe diagnostic access, callers
should use the `RenderContext.Diagnostics` within a single `Convert()` scope.

**Editor integration**: the LSP server can read `LastDiagnostics` after each convert
and merge them with parse diagnostics from `ParseResult.Diagnostics` for a complete
diagnostic picture. Extension diagnostics use the same `Diagnostic` type and
`SourceRange` positioning as parse diagnostics.

---

## Cross-Cutting Concerns

### 11. Testing Strategy

| Feature | Test Approach |
|---------|--------------|
| DocumentChange | Unit: apply single change, multiple changes, insert/delete/replace, empty text, boundary offsets |
| DocumentSnapshot | Unit: create initial, apply changes, version incrementing, text correctness |
| ParseIncremental | Unit: cache hit (same text), cache miss (new text), caching disabled, version chaining |
| IOutputProcessor | Unit: single processor, chained processors, processor that throws (caught + warned) |
| KrokiDiagramToolRunner | Unit with mock HTTP: successful generation, HTTP error, timeout, availability check. Integration test skipped unless Kroki is reachable. |
| Zip install | Unit: valid zip, nested zip (single subdir), no manifest, invalid zip. Use temp directories. |
| Enable/disable | Unit: disable skips loading, enable restores loading, registry persistence, default enabled. |
| IExtensionLifecycle | Unit: Initialize called after registration, Dispose called on Shutdown, errors caught. |
| Extension diagnostics | Unit: AddDiagnostic collects, LastDiagnostics populated after Convert, empty when no extensions. |

**Test framework**: NUnit (`[Test]`), consistent with existing tests.

**No modification of existing tests**: all new test files in existing test projects
or new test projects as needed.

---

### 12. Explicit Non-Goals

The following are explicitly **out of scope** for beta.11:

1. **Full incremental parser** — true AST-diffing where only changed subtrees are
   re-parsed. This is a fundamental parser architecture change (v2.x).
2. **LSP server changes** — the LSP server exists but beta.11 does not modify it.
   The new APIs (DocumentSnapshot, ParseIncremental, diagnostics) are designed to be
   consumed by the LSP server in a future release.
3. **Editor UI changes** — the Avalonia viewer is not modified. The editor integration
   APIs are library-level, not UI-level.
4. **Extension signing or verification** — no code signing, no hash verification of
   extension DLLs. Trust model remains "user installs explicitly."
5. **Remote extension registry** — `ext search` remains local-only. No downloads,
   no marketplace, no network access for extension management.
6. **Extension dependency resolution** — dependencies remain informational (warn-only).
   No automatic installation of transitive dependencies.
7. **Incremental rendering** — re-rendering only changed sections. Out of scope.
   `ParseIncremental` + render caching is the optimization path for beta.11.
8. **Breaking API changes** — all existing method signatures preserved. New features
   are additive. Zero-extension behavior is byte-identical to beta.10.

---

## Implementation Phases

| Phase | Scope | Key Files |
|-------|-------|-----------|
| P02 | DocumentChange + DocumentSnapshot + ParseIncremental | `src/AdocNet.Core/Editor/`, `AdocEngine.cs` |
| P03 | IOutputProcessor + Kroki + IExtensionLifecycle + Diagnostics | `src/AdocNet.Core/Extensions/`, `RenderContext.cs`, `AdocEngine.cs` |
| P04 | Zip install + Enable/Disable + CLI changes | `src/AdocNet.Cli/`, `ExtensionInfo.cs`, `ExtensionRegistry.cs`, `ExtensionDirectoryLoader.cs` |
| P05 | Documentation + version bump | `docs/`, `Directory.Build.props` |

---

## Summary of New Public API Surface

```csharp
// New types
AdocNet.Editor.DocumentChange          // readonly struct
AdocNet.Editor.DocumentSnapshot        // sealed class
AdocNet.Extensions.IOutputProcessor    // interface
AdocNet.Extensions.IExtensionLifecycle // interface
AdocNet.Extensions.KrokiDiagramToolRunner // sealed class

// New AdocEngine members
AdocEngine.FullParser                  // Func<string, ParseResult>? (optional)
AdocEngine.ParseIncremental(...)       // DocumentSnapshot method
AdocEngine.RegisterOutputProcessor(...) // fluent registration
AdocEngine.LastDiagnostics             // IReadOnlyList<Diagnostic>
AdocEngine.Shutdown()                  // calls IExtensionLifecycle.Dispose()

// New RenderContext members
RenderContext.AddDiagnostic(Diagnostic)
RenderContext.Diagnostics              // IReadOnlyList<Diagnostic>

// Modified types
ExtensionInfo.Enabled                  // bool (default true)
ExtensionRegistry.SetEnabled(...)      // method

// New CLI commands
adocnet ext enable <name>
adocnet ext disable <name>
adocnet ext install <file.zip>         // zip support added to existing command
```
