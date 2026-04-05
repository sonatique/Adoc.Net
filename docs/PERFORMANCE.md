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

## Extensions and Caching

Both caches work correctly when extensions are registered:

- **Parse cache**: Returns the pre-extension AST. Extensions still run on
  every Convert() call (even on cache hit). This is correct because extensions
  are deterministic — same AST in, same mutations out.

- **Render cache**: Returns the final rendered bytes (after extensions have run).
  Valid because extensions are frozen after the first Convert() call and are
  deterministic.

### When to Disable Caching

Disable caching (`EnableCaching = false`) or call `ClearCache()` if:

- You register extensions that depend on **external mutable state** (files,
  network responses, timestamps). The render cache assumes deterministic output.
- You modify extension configuration between Convert() calls (extensions are
  frozen after the first call, but if you create a new engine with different
  extensions, caching is per-engine and won't cross-contaminate).

### Cache Invalidation on Registration

Calling any `Register*()` or `Load*()` method automatically clears both caches.
This ensures that newly registered extensions don't see stale cached results.

## Thread Safety

The caching system is fully thread-safe. Multiple threads can call Convert()
concurrently on the same engine instance. Cache reads and writes are synchronized
using locks.

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

## Implementation Details

- **Hashing**: SHA-256 via `System.Security.Cryptography` (built-in, no external deps)
- **Eviction**: LRU (Least Recently Used) with O(1) operations
- **Storage**: Per-engine instance (no global/static state)
- **Thread safety**: Lock-based synchronization
- **TFM**: Works on both netstandard2.0 and net10.0
