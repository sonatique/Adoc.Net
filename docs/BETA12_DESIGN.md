# Beta.12 — Performance II & Extension Maturity Design

> Design document for AdocNet v1.0.0-beta.12.
> No code in this document — implementation follows in P02-P04.

---

## 1. IExtensionCapabilities — Determinism Declaration

### Problem

The render cache in `AdocEngine.Convert()` caches rendered bytes keyed by
`SHA-256(inputHash | format | optionsHash)`. The key does NOT include extension
identity or state. If extensions are non-deterministic (e.g., inject timestamps,
random IDs), the cache serves stale/incorrect results.

Currently there is no guard — the render cache is used regardless of whether
extensions are registered (P00 finding).

### Interface Design

```csharp
// src/AdocNet.Core/Extensions/IExtensionCapabilities.cs
namespace AdocNet.Extensions;

/// <summary>
/// Optional interface for processors to declare their runtime capabilities.
/// Enables cache optimizations when all processors are deterministic.
/// </summary>
public interface IExtensionCapabilities
{
    /// <summary>
    /// Returns true if this processor always produces identical AST mutations
    /// for the same input AST. A deterministic processor enables render caching.
    /// </summary>
    bool IsDeterministic { get; }
}
```

### Evaluation Rules

- Checked once per engine lifetime: when `Convert()` first needs to decide
  whether to use the render cache.
- **ALL** registered document, block, and inline processors must implement
  `IExtensionCapabilities` AND return `IsDeterministic = true` for the render
  cache to be active.
- If ANY processor does not implement the interface → treated as non-deterministic
  (safe default).
- If ANY processor returns `IsDeterministic = false` → render cache disabled.
- Output processors (`IOutputProcessor`) are excluded from this check because they
  run AFTER the render cache read/write point. Their output is not cached.

### Computed Property

```csharp
// AdocEngine — new private field
private bool? _allProcessorsDeterministic;

// Computed on first Convert() when caching is enabled
private bool AreAllProcessorsDeterministic()
{
    if (_allProcessorsDeterministic.HasValue)
        return _allProcessorsDeterministic.Value;

    var allProcessors = _documentProcessors.Cast<object>()
        .Concat(_blockProcessors)
        .Concat(_inlineProcessors);

    bool result = true;
    foreach (var p in allProcessors)
    {
        if (p is not IExtensionCapabilities caps || !caps.IsDeterministic)
        {
            result = false;
            break;
        }
    }

    _allProcessorsDeterministic = result;
    return result;
}
```

Reset `_allProcessorsDeterministic = null` whenever a processor is registered
(inside `RegisterDocumentProcessor`, `RegisterBlockProcessor`, `RegisterInlineProcessor`).

---

## 2. Render Cache with Extensions

### Current Behavior

```
Convert() with caching enabled:
  1. Compute renderKey
  2. Check render cache → if hit, return (REGARDLESS of extensions)
  3. Parse (with parse cache)
  4. Run extensions
  5. Render
  6. Cache render output
```

There is NO condition disabling the render cache when extensions are registered.

### New Behavior

```
Convert() with caching enabled:
  1. Compute renderKey
  2. If NO extensions registered OR all processors are deterministic:
     → Check render cache → if hit, return
  3. Parse (with parse cache)
  4. Run extensions
  5. Render
  6. If NO extensions registered OR all processors are deterministic:
     → Cache render output
  7. Run output processors, write to output
```

### Exact Code Change

In `AdocEngine.Convert()`, wrap the render cache read and write behind
`CanUseRenderCache()`:

```csharp
private bool CanUseRenderCache()
{
    // No extensions → render cache is always safe
    if (_documentProcessors.Count == 0 &&
        _blockProcessors.Count == 0 &&
        _inlineProcessors.Count == 0)
        return true;

    // Extensions present → only safe if all are deterministic
    return AreAllProcessorsDeterministic();
}
```

The parse cache is unaffected — it caches the PRE-extension AST, which is
always safe (parser is a pure function).

### Cache Key

The render cache key remains `SHA-256(inputHash | format | optionsHash)`.
Since render caching is only enabled when ALL processors are deterministic,
same input + same options always produces the same output regardless of
which deterministic processors are registered. The key does NOT need to
include extension identity — deterministic extensions are idempotent by definition.

---

## 3. Persistent Cache

### Purpose

Cross-session render cache. When a document was rendered in a previous session,
the next session can skip parsing, extensions, and rendering entirely.

### What Is Persisted

**Render cache only.** The parse cache stores `DocumentNode` object references
(in-memory graph) which are not serializable without modifying the AST boundary
(forbidden). The render cache stores `byte[]` — trivial to persist.

### Location

Default: `~/.adocnet/cache/`
Configurable via: `AdocEngine.PersistentCachePath` property.

Platform paths:
- Windows: `%USERPROFILE%\.adocnet\cache\`
- Linux/macOS: `~/.adocnet/cache/`

### File Layout — One File Per Entry

```
~/.adocnet/cache/
    v1/                          ← version directory
        A1B2C3D4...F0.bin        ← filename = render cache key (hex)
        E5F6A7B8...12.bin
```

**Why one-file-per-entry:**
- Atomic writes (temp file + rename) — no corruption risk
- No lock file needed — last-write-wins for single-user tool
- Easy eviction: delete file
- No complex binary format to parse/maintain
- Grep-friendly: `ls ~/.adocnet/cache/v1/ | wc -l` for count

**Version directory** (`v1/`): enables format migration. If binary format changes,
bump to `v2/` and ignore old entries.

### File Format

Each `.bin` file is a simple binary blob:

```
[4 bytes]  Magic: "ADC\0" (0x41 0x44 0x43 0x00)
[4 bytes]  Format version: uint32 LE (currently 1)
[N bytes]  AdocNet version string, UTF-8, length-prefixed (uint16 LE)
[M bytes]  Rendered output (remaining bytes to EOF)
```

**Why include AdocNet version?** If the engine version changes, rendered output
may differ (new renderer features, bug fixes). On read, compare the stored
version with the current engine version. Mismatch → discard entry.

### API on AdocEngine

```csharp
/// <summary>
/// Enables persistent (disk-based) render caching. When true, render cache
/// entries are written to disk and survive across sessions.
/// Requires <see cref="EnableCaching"/> to also be true.
/// Default: false.
/// </summary>
public bool EnablePersistentCache { get; set; }

/// <summary>
/// Directory path for persistent cache files.
/// Default: ~/.adocnet/cache/
/// </summary>
public string? PersistentCachePath { get; set; }

/// <summary>
/// Maximum number of persistent cache files on disk.
/// Oldest files (by last access time) are evicted when exceeded.
/// Default: 256.
/// </summary>
public int MaxPersistentCacheEntries { get; set; } = 256;
```

### PersistentCacheStore — Internal Class

```csharp
// src/AdocNet.Core/Caching/PersistentCacheStore.cs
internal sealed class PersistentCacheStore
{
    PersistentCacheStore(string cacheDir, int maxEntries)
    bool TryGet(string key, string currentVersion, out byte[] value)
    void Set(string key, string currentVersion, byte[] value)
    void Clear()
    void EvictExcess()
}
```

**Thread safety**: file I/O is naturally serialized by the OS for single-user use.
For extra safety, use `lock` around read/write to prevent concurrent `Convert()` calls
from colliding on the same cache directory.

### Integration with Convert()

```
Convert() with persistent cache:
  1. Check in-memory render cache → hit? return
  2. Check persistent cache → hit? populate in-memory cache, return
  3. Parse + extensions + render
  4. Write to in-memory render cache
  5. Write to persistent cache (background/fire-and-forget)
```

Persistent cache is checked AFTER in-memory cache (in-memory is faster).
Writes to disk happen after the in-memory cache is populated.

### Invalidation

- **Version mismatch**: stored AdocNet version != current → discard on read
- **Manual clear**: `AdocEngine.ClearCache()` clears both in-memory and persistent
- **Capacity eviction**: oldest files deleted when count > MaxPersistentCacheEntries
- **Extension change**: if extensions change between sessions, the render cache key
  may match but produce wrong output. Mitigated by: persistent cache only works
  when `CanUseRenderCache()` is true (all processors deterministic), so same
  input + same deterministic extensions = same output regardless of session.

---

## 4. MaxEngine Version in Manifest

### Problem

Extensions can declare `minAdocNetVersion` but cannot declare a maximum.
An extension written for beta.8 might break with beta.15 API changes.
There's no way to warn users.

### Manifest Change

Add `"maxAdocNetVersion"` field to `extension.json`:

```json
{
  "name": "my-extension",
  "version": "1.0.0",
  "entry": "MyExtension.dll",
  "minAdocNetVersion": "1.0.0-beta.7",
  "maxAdocNetVersion": "1.0.0-beta.15"
}
```

### ExtensionManifest Model Change

Add `MaxAdocNetVersion` property:

```csharp
/// <summary>Gets the maximum compatible AdocNet version, or null if no maximum.</summary>
public string? MaxAdocNetVersion { get; }
```

Parsed from `"maxAdocNetVersion"` field via `SimpleJsonParser.ParseFlatObject()`.
No JSON parser changes needed — it's just another flat string field.

### Version Check Logic

In `ExtensionDirectoryLoader.LoadInstalledExtensions()`, after the existing
`minAdocNetVersion` check, add:

```csharp
if (manifest.MaxAdocNetVersion is not null)
{
    var currentVersion = GetCurrentAdocNetVersion();
    if (!IsVersionBelow(currentVersion, manifest.MaxAdocNetVersion))
    {
        onWarning?.Invoke(
            $"Extension '{manifest.Name}' requires AdocNet <= {manifest.MaxAdocNetVersion}, " +
            $"current is {currentVersion}, skipping");
        continue;
    }
}
```

Where `IsVersionBelow(current, max)` returns true if `current <= max`.
This reuses the existing `IsVersionCompatible` logic — just inverted:
`IsVersionBelow(current, max)` ≡ `IsVersionCompatible(max, current)`.

### ExtensionState

Extensions that fail the max version check get `ExtensionState.Incompatible`
(same as min version failures).

---

## 5. Extension Load Priority

### Problem

Processors execute in FIFO registration order. When extensions are loaded from
multiple DLLs in a directory, execution order depends on alphabetical filename
sort. Extensions cannot influence their execution order.

### Interface Design

```csharp
// src/AdocNet.Core/Extensions/IExtensionPriority.cs
namespace AdocNet.Extensions;

/// <summary>
/// Optional interface for processors to declare their execution priority.
/// Lower values execute first. Default priority (no interface) is 1000.
/// Within the same priority, registration order (FIFO) is preserved.
/// </summary>
public interface IExtensionPriority
{
    /// <summary>
    /// Execution priority. Lower values execute first.
    /// Typical ranges: 0-100 (early), 500 (normal), 900-1000 (late).
    /// Default for processors not implementing this interface: 1000.
    /// </summary>
    int Priority { get; }
}
```

### Default Priority

Processors that do not implement `IExtensionPriority` get a default of **1000**.
This means all existing processors (pre-beta.12) run at priority 1000 in their
existing FIFO order. No behavioral change for existing code.

### Sorting Strategy

Sort happens once, at the transition from "registration" to "execution" —
specifically, in the `RunExtensions()` method on first call (when `_frozen`
becomes true), or in a dedicated `FreezeProcessors()` method.

```csharp
private void FreezeProcessors()
{
    if (_frozen) return;
    _frozen = true;
    SortByPriority(_documentProcessors);
    SortByPriority(_blockProcessors);
    SortByPriority(_inlineProcessors);
}

private static void SortByPriority<T>(List<T> processors) where T : class
{
    // Stable sort: preserves FIFO for same priority
    var sorted = processors
        .Select((p, i) => (processor: p, index: i))
        .OrderBy(x => x.processor is IExtensionPriority ep ? ep.Priority : 1000)
        .ThenBy(x => x.index)
        .Select(x => x.processor)
        .ToList();

    processors.Clear();
    processors.AddRange(sorted);
}
```

### Impact on ProcessingPipeline

`ProcessingPipeline.Run()` receives already-sorted lists. No changes to the
pipeline itself. The sorting is purely an `AdocEngine` responsibility.

### Output Processors

Output processors are NOT sorted by priority. They run in registration order
(FIFO) because post-render ordering is typically intentional (e.g., minify
then watermark, not watermark then minify).

---

## 6. Testing Strategy

### IExtensionCapabilities + Render Cache

- Engine with no extensions: render cache works (existing behavior, regression test)
- Engine with all-deterministic processors: render cache works, cached output matches
- Engine with one non-deterministic processor: render cache skipped, output still correct
- Engine with mix of deterministic and non-deterministic: render cache skipped
- Processor without `IExtensionCapabilities`: treated as non-deterministic
- Cache key stability: same input produces same key across calls

### Persistent Cache

- Write + read round-trip: cached bytes identical to original render output
- Version mismatch: entry with different version is discarded
- Missing cache directory: auto-created on first write
- Corrupt file: gracefully ignored (warning, re-render)
- Max entries eviction: oldest files deleted when limit exceeded
- `ClearCache()` clears both in-memory and persistent
- Persistent cache disabled when `EnableCaching` is false
- File format validation: magic bytes, format version check

### MaxEngine Version

- Extension with `maxAdocNetVersion` higher than current: loads successfully
- Extension with `maxAdocNetVersion` equal to current: loads successfully
- Extension with `maxAdocNetVersion` lower than current: skipped, Incompatible state
- Extension without `maxAdocNetVersion`: loads (no max check)
- Manifest parsing: `maxAdocNetVersion` field parsed correctly

### Extension Priority

- No processors implement `IExtensionPriority`: FIFO order preserved (regression)
- All processors at default priority (1000): FIFO order preserved
- Mixed priorities: lower priority runs first
- Same priority: FIFO within that priority level
- Priority sorting happens once, not per Convert() call
- Output processors: NOT sorted by priority (always FIFO)

---

## 7. Explicit Non-Goals

The following are explicitly out of scope for beta.12:

1. **Full incremental AST diffing** — beta.11 added cache-assisted "incremental"
   parsing (check cache, full re-parse on miss). True AST diffing is v2.x.

2. **Distributed/shared cache** — persistent cache is local disk only.
   No network, no Redis, no shared filesystem support.

3. **Parallel parsing** — parse cache handles repeated calls efficiently.
   Parallel AST construction is a fundamentally different problem.

4. **AST serialization** — persisting the parse cache would require making
   `DocumentNode` and all AST types serializable. This violates the AST
   immutability boundary and is not worth the complexity.

5. **Extension dependency ordering** — priority is manual (developer declares it).
   Automatic ordering based on dependency graph is not in scope.

6. **Cache compression** — persistent cache files store raw bytes. Compression
   would add complexity and CPU overhead for marginal disk savings.

7. **Cache warming/preloading** — no API to pre-populate the cache.
   Entries are created lazily on `Convert()`.

8. **Output processor capabilities** — output processors are not checked for
   `IExtensionCapabilities` because they run outside the render cache boundary.

---

## Implementation Phases

| Phase | Content | Key Files |
|-------|---------|-----------|
| P02 | IExtensionCapabilities + render cache guard | `IExtensionCapabilities.cs`, `AdocEngine.cs` |
| P03 | Persistent cache | `PersistentCacheStore.cs`, `AdocEngine.cs` |
| P04 | MaxEngine version + extension priority | `ExtensionManifest.cs`, `IExtensionPriority.cs`, `AdocEngine.cs` |
