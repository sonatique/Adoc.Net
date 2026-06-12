# Performance & Caching Guide

AdocNet includes an opt-in caching system that dramatically improves performance
for repeated conversions of the same document. This is particularly useful for
live preview, watch mode, IDE integrations, and any scenario where the same
document is rendered multiple times.

## Quick Start

```csharp
var engine = new AdocEngine(renderer, parser)
{
    EnableCaching = true
};

// First call: parses and renders (populates cache)
engine.Convert(source, output);

// Second call with same input: returns cached result (15-45x faster)
engine.Convert(source, output);
```

## How It Works

AdocNet uses a two-layer caching system:

### Layer 1: Parse Cache

The parse cache stores the AST (`DocumentNode`) produced by the parser,
keyed by the SHA-256 hash of the input string.

- **Key**: SHA-256 hash of the input text
- **Value**: Parsed `DocumentNode` (full AST tree)
- **Hit condition**: Identical input string

Since the parser is a pure function (same input always produces the same AST),
the parse cache is always correct. On cache hit, parsing is completely skipped.

Parsing is the most expensive operation (75-89% of total time), so the parse
cache alone provides significant speedup.

### Layer 2: Render Cache

The render cache stores the final rendered bytes, keyed by a composite hash
of the input, renderer format, and render options.

- **Key**: SHA-256 of (input hash + renderer format + options hash)
- **Value**: Rendered output bytes (`byte[]`)
- **Hit condition**: Same input AND same render options AND same renderer

On render cache hit, both parsing and rendering are completely skipped.
The cached bytes are written directly to the output stream.

## Configuration

### EnableCaching

```csharp
engine.EnableCaching = true;  // opt-in (default: false)
```

When `false` (default), no caching occurs. Convert() always parses and renders
from scratch. There is zero overhead when caching is disabled.

Setting `EnableCaching` back to `false` clears all cached data immediately.

### MaxCacheEntries

```csharp
engine.MaxCacheEntries = 32;  // default: 16
```

Controls the maximum number of entries in each cache (parse and render caches
are sized independently). Uses LRU (Least Recently Used) eviction when full.

Guidelines:
- **Live preview / IDE** (editing 1 file): `MaxCacheEntries = 4` is sufficient
- **Watch mode** (monitoring a directory): `MaxCacheEntries = 16` (default)
- **Batch conversion** (each file converted once): disable caching entirely

### ClearCache

```csharp
engine.ClearCache();  // clears both parse and render caches
```

Call this when external state has changed that could affect output (e.g.,
included files modified, image files replaced).

## Performance Numbers

Measured on .NET 10.0.5, X64 RyuJIT AVX2. HTML rendering.

### Cold (Uncached) vs Cached (Cache Hit)

| Document Size | Cold Mean     | Cached Mean | Speedup | Memory Reduction |
|---------------|---------------|-------------|---------|------------------|
| Small (~1 KB) | 17.8 us      | 1.2 us      | **15x** | 13.6x            |
| Medium (~50 KB) | 411 us      | 15.4 us     | **27x** | 18.8x            |
| Large (~500 KB) | 28,776 us   | 635 us      | **45x** | 13.9x            |

Cache hits are 15-45x faster with 14-19x less memory allocation.

### Cache Hit Allocation Breakdown

On a cache hit, the only allocations are for copying the cached bytes to the
output stream. Parse and render allocations are completely eliminated:

| Document Size | Cold Allocations | Cached Allocations |
|---------------|------------------|--------------------|
| Small (~1 KB) | 33.5 KB          | 2.5 KB             |
| Medium (~50 KB) | 825 KB          | 44 KB              |
| Large (~500 KB) | 22,513 KB       | 1,620 KB           |

## Extensions and Caching (beta.12)

### Render Cache with Deterministic Extensions

The render cache is only enabled when ALL registered processors declare
themselves as deterministic via `IExtensionCapabilities`:

```csharp
public class MyProcessor : IBlockProcessor, IExtensionCapabilities
{
    public bool IsDeterministic => true;  // enables render cache
    // ...
}
```

- If ALL processors implement `IExtensionCapabilities` and return `true`,
  the render cache works normally (same input → same output, cache hit).
- If ANY processor does not implement `IExtensionCapabilities`, or returns
  `false`, the render cache is disabled (parse cache still works).
- With no extensions registered, the render cache always works.

### Parse Cache

The parse cache stores the pre-extension AST. It works independently of
extension determinism when the render cache is active (render cache protects
against double-mutation). When the render cache is disabled (non-deterministic
extensions), the parse cache is also bypassed to prevent AST mutation bugs.

### When to Disable Caching

Disable caching (`EnableCaching = false`) or call `ClearCache()` if:

- You register extensions that depend on **external mutable state** (files,
  network responses, timestamps) and don't implement `IExtensionCapabilities`.
- You modify extension configuration between Convert() calls.

### Cache Invalidation on Registration

Calling any `Register*()` or `Load*()` method automatically clears both caches.
This ensures that newly registered extensions don't see stale cached results.

## Thread Safety

Multiple threads can call `Convert()` concurrently on the same engine instance.
Cache reads and writes are synchronized with locks, and when extensions are
registered the engine serializes extension execution (and never shares a
parse-cached AST with the mutating extension pipeline), so concurrent renders
stay correct. Engines **without** extensions render fully in parallel; engines
**with** extensions serialize the extension phase of each render.

One caveat: `LastExtensionDiagnostics` is a single mutable property reflecting the
most recent `Convert()` call, so it is not reliable to read after concurrent
converts. Read it only when converts are not overlapping (e.g. single-threaded use).

On the first call for a given input (cold start with multiple threads), duplicate
parsing may occur briefly before the cache is populated. This is by design —
it avoids complex single-flight coordination for negligible benefit.

## Correctness Guarantee

**Cached output is byte-identical to non-cached output.** This is verified by
automated tests that compare cached and uncached renders byte-for-byte for
multiple document sizes and renderer configurations.

If you ever observe different output with caching enabled vs disabled, please
report it as a bug.

## Memory Considerations

Cache entries can be large:
- Parse cache: each entry holds a full AST tree (~27 KB for small docs, ~16 MB for large docs)
- Render cache: each entry holds rendered bytes (varies by format and document size)

For large documents, consider reducing `MaxCacheEntries` to limit memory usage:

```csharp
engine.MaxCacheEntries = 4;  // for large documents
```

The default of 16 entries works well for typical documentation projects.

## Persistent Cache (beta.12)

The persistent cache writes render cache entries to disk for cross-session reuse.
When a document was rendered in a previous session, the next session can skip
parsing, extensions, and rendering entirely.

```csharp
var engine = new AdocEngine(renderer, parser)
{
    EnableCaching = true,
    EnablePersistentCache = true,                    // opt-in (default: false)
    PersistentCacheDirectory = "~/.adocnet/cache/",  // default location
    MaxPersistentCacheEntries = 256                   // default: 256
};
```

### What Is Persisted

**Render cache only.** The parse cache stores in-memory object references
(`DocumentNode`) which are not serializable. The render cache stores `byte[]`
which is trivially persisted.

### Invalidation

- **Version mismatch**: cache files include the AdocNet version. If the engine
  version changes, old entries are discarded on read.
- **Manual clear**: `ClearCache()` clears both in-memory and disk caches.
- **Capacity eviction**: oldest files are deleted when count exceeds
  `MaxPersistentCacheEntries`.

### File Layout

Cache files are stored one-per-entry in a versioned subdirectory:

```
~/.adocnet/cache/v1/
    A1B2C3D4...F0.bin
    E5F6A7B8...12.bin
```

Writes use atomic temp-file-plus-rename to prevent corruption.

## Implementation Details

- **Hashing**: SHA-256 via `System.Security.Cryptography` (built-in, no external deps)
- **Eviction**: LRU (Least Recently Used) with O(1) operations
- **Storage**: Per-engine instance (no global/static state)
- **Thread safety**: Lock-based synchronization
- **TFM**: Works on both netstandard2.0 and net10.0
