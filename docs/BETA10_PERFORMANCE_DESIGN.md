# Beta.10 Performance Design — Caching System

> Status: Design document for v1.0.0-beta.10
> Prerequisite: `docs/PERF-BASELINE-BETA10.md` (baseline numbers)

## 1. Parse Cache

### Purpose

Avoid re-parsing identical input strings. Parsing consumes 75-89% of end-to-end
time and 64-76% of memory allocations. In live-preview and IDE scenarios, the same
document is parsed on every keystroke — even when the content has not changed.

### Key

SHA-256 hash of the input string, encoded as UTF-8 bytes.

```
key = SHA256(Encoding.UTF8.GetBytes(input))
```

SHA-256 is chosen because:
- Available via `System.Security.Cryptography.SHA256` on all target frameworks
  (netstandard2.0, net10.0) — no external dependency.
- Collision-resistant: 2^128 security level makes false cache hits effectively impossible.
- Deterministic: same input → same hash across platforms and runs.
- Fast enough: ~2 µs for a 1 KB string, ~50 µs for a 500 KB string — negligible
  compared to parse times (17 µs and 22 ms respectively).

The hash is stored as a `string` (hex-encoded, 64 characters) for use as a dictionary key.

### Value

The parsed `DocumentNode` — the full AST tree as returned by `Parser(input)`.

### Location

Instance field on `AdocEngine`:

```csharp
private LruCache<string, DocumentNode>? _parseCache;
```

The cache is lazily created when `EnableCaching` is set to true.
It is per-engine-instance — no global/static state.

### When Used

In `Convert()`, before calling `Parser(input)`:

```
1. If caching disabled → parse normally
2. Compute inputHash = SHA256(input)
3. If _parseCache.TryGet(inputHash, out doc) → use cached doc
4. Else → doc = Parser(input); _parseCache.Add(inputHash, doc)
```

### AST Mutability Concern

AST nodes are mutable (`AddChild`, `InsertChild`, `SetAttribute`). If extensions
modify a cached AST, subsequent cache hits would return the already-modified tree,
which could cause double-processing or incorrect output.

**Decision**: When extensions are registered, the parse cache stores the **pre-extension**
AST. Each `Convert()` call that hits the parse cache must still run extensions on the
cached AST. Since extensions are deterministic (same AST → same mutations), this produces
correct output. The cached AST is re-mutated each time — this is safe because:

1. Extensions are idempotent in practice (they check before modifying).
2. The ProcessingPipeline already handles this (CanProcess checks, NodeReplacements).

If a non-idempotent extension is registered, the user should disable caching.
This is documented as a known limitation.

**Alternative considered**: Deep-clone the cached AST before extension processing.
Rejected for beta.10 because:
- Adds complexity (AST has no Clone method, would need reflection or visitor).
- Allocates as much memory as parsing (defeating the purpose for memory savings).
- Most real-world extensions are idempotent (AutoId, DocumentMetadata, Icon macros).

### Thread Safety

The `LruCache<K, V>` implementation uses a `lock` for all reads and writes.
This is simpler than `ConcurrentDictionary` because LRU eviction requires
maintaining access order, which needs synchronized read-then-write semantics.

Cache operations are fast (hash lookup + linked list move), so lock contention
is negligible even under concurrent access.

## 2. Render Cache

### Purpose

Avoid re-rendering when both the input and render options are unchanged.
Useful in watch mode, live preview, and repeated CLI invocations.

### Key

Composite hash of three components:

```
renderKey = SHA256(inputHash + rendererFormat + optionsHash)
```

Where:
- `inputHash`: the 64-character hex SHA-256 of the input string (same as parse cache key).
- `rendererFormat`: the `IDocumentRenderer.Format` string (e.g., "html", "pdf").
- `optionsHash`: SHA-256 of the serialized render options properties.

### Options Hashing

`RenderOptions` has no `GetHashCode` override, and subclasses (`HtmlRenderOptions`,
`PdfRenderOptions`) have many properties. The hashing strategy:

1. Get the concrete type name: `options.GetType().FullName`.
2. Enumerate all public instance properties via reflection.
3. For each property, append `name=value` (using `ToString()` with InvariantCulture).
4. Compute SHA-256 of the concatenated string.

This is implemented in `CacheKeyBuilder` (internal static class):

```csharp
internal static class CacheKeyBuilder
{
    static string ComputeInputHash(string input) { ... }
    static string ComputeOptionsHash(RenderOptions options) { ... }
    static string ComputeRenderKey(string inputHash, string format, RenderOptions options) { ... }
}
```

Reflection cost is ~1-2 µs — negligible compared to rendering (2 µs for small, 8 ms for large).

### Value

`byte[]` — the rendered output bytes. Captured by rendering into a `MemoryStream`,
then calling `ToArray()`.

### Location

Instance field on `AdocEngine`:

```csharp
private LruCache<string, byte[]>? _renderCache;
```

### When Used

In `Convert()`, after extensions have run (or been skipped):

```
1. If caching disabled → render normally
2. Compute renderKey = CacheKeyBuilder.ComputeRenderKey(inputHash, Renderer.Format, opts)
3. If _renderCache.TryGet(renderKey, out bytes) → output.Write(bytes)
4. Else → render to MemoryStream, copy to output, store bytes in cache
```

### When NOT Used

The render cache is **skipped** (but not disabled) when:
- `EnableCaching` is false (obviously).
- The output stream is not seekable and not a MemoryStream — we always need to
  capture bytes anyway, so this is always feasible.

The render cache **remains valid** even when extensions are registered, because:
- Extensions are frozen after first `Convert()` (same set for all calls).
- Extensions are deterministic: same pre-extension AST → same post-extension AST.
- Same post-extension AST + same options → same render output.
- The render key already includes the input hash (which determines the AST)
  and the options hash (which determines render behavior).

**Note**: If an extension has external side effects (e.g., reading a file that changes
between calls), the render cache could serve stale output. This is an inherent
limitation of caching. The user should call `ClearCache()` or disable caching
for such extensions. This is documented.

## 3. Cache Configuration

### Properties on AdocEngine

```csharp
/// <summary>
/// Enables parse and render caching. When true, repeated Convert() calls
/// with the same input and options return cached results.
/// Default: false (opt-in).
/// </summary>
public bool EnableCaching { get; set; }

/// <summary>
/// Maximum number of entries in each cache (parse cache and render cache
/// are sized independently). Default: 16. Minimum: 1.
/// When the cache is full, the least-recently-used entry is evicted.
/// </summary>
public int MaxCacheEntries { get; set; } = 16;
```

### Behavior

- `EnableCaching = false` (default): no caching. Convert() always parses and renders.
  The `_parseCache` and `_renderCache` fields remain null — zero overhead.
- `EnableCaching = true`: caches are created lazily on first Convert() call.
  Both parse cache and render cache use the same `MaxCacheEntries` limit.
- Setting `EnableCaching` back to false clears both caches.
- `MaxCacheEntries` can be changed at any time. Takes effect on next eviction check.

### LRU Eviction

The `LruCache<TKey, TValue>` is an internal class in `src/AdocNet.Core/Caching/`:

```csharp
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    LruCache(int capacity);
    bool TryGet(TKey key, out TValue value);  // moves to front on hit
    void Add(TKey key, TValue value);         // evicts LRU if full
    void Clear();
    int Count { get; }
}
```

Implementation: `Dictionary<TKey, LinkedListNode<(TKey, TValue)>>` + `LinkedList<(TKey, TValue)>`.
This gives O(1) lookup and O(1) eviction.

The linked list maintains access order: most-recently-used at the front,
least-recently-used at the back. `TryGet` moves the accessed node to the front.
`Add` adds at the front and removes from the back if over capacity.

### Default Size Rationale

16 entries is chosen because:
- Covers the common case: a single document being edited (1 entry) or
  a small project with multiple files (4-8 entries).
- Memory overhead is bounded: 16 × ~500 KB ASTs = ~8 MB for parse cache,
  which is acceptable for a development tool.
- For live preview (the primary use case), typically only 1-2 entries are active.
- Users processing hundreds of documents in batch should not enable caching
  (they process each document once — cache would just waste memory).

## 4. Cache Invalidation

### Automatic Invalidation (via key mismatch)

- **Parse cache**: different input string → different SHA-256 hash → cache miss → fresh parse.
  No explicit invalidation needed. Changed input naturally bypasses the cache.
- **Render cache**: different input OR different options → different composite hash → cache miss.

### Extension Registration

Calling any `Register*()` or `Load*()` method clears both caches:

```csharp
public AdocEngine RegisterBlockProcessor(IBlockProcessor processor)
{
    ThrowIfFrozen();
    _blockProcessors.Add(processor);
    ClearCacheInternal();  // extensions changed → cached ASTs may be processed differently
    return this;
}
```

Rationale: extensions modify the AST between parse and render. Adding a new extension
changes the processing pipeline, potentially invalidating all cached render outputs.
Parse cache entries are also cleared because cached ASTs may have been mutated by
previous extension runs (see AST Mutability Concern in Section 1).

### Manual Invalidation

```csharp
/// <summary>
/// Clears all cached parse results and render outputs.
/// Call this if external state affecting extensions has changed.
/// </summary>
public void ClearCache()
{
    _parseCache?.Clear();
    _renderCache?.Clear();
}
```

### Setting EnableCaching to False

Setting `EnableCaching = false` clears and disposes both caches:

```csharp
public bool EnableCaching
{
    get => _enableCaching;
    set
    {
        _enableCaching = value;
        if (!value)
        {
            _parseCache?.Clear();
            _renderCache?.Clear();
            _parseCache = null;
            _renderCache = null;
        }
    }
}
```

## 5. Correctness Guarantee

### The Rule

**Cached output MUST be byte-identical to non-cached output.**

This is the #1 invariant of the caching system. If there is ANY doubt about
whether caching produces correct output, the cache is bypassed.

### Verification Strategy

Tests in `tests/AdocNet.Core.Tests/Caching/`:

1. **Byte-identity test**: For each document size (Small, Medium, Large) and
   each renderer (HTML, PDF):
   - Render without caching → `expected` bytes
   - Enable caching, render twice → `cached` bytes
   - Assert `expected.SequenceEqual(cached)`

2. **Multi-options test**: Render the same document with different options,
   verify each produces the correct (different) output.

3. **Extension test**: Register extensions, render with and without caching,
   verify byte-identical output.

4. **Eviction test**: Fill cache beyond capacity, verify evicted entries
   produce correct output when re-rendered.

### Safety Net

If a correctness issue is discovered in the caching implementation:
1. Disable the affected cache layer (parse or render).
2. Add a regression test reproducing the issue.
3. Fix and re-enable.

The opt-in design (`EnableCaching = false` by default) means correctness bugs
in caching do not affect existing users.

## 6. Extensions + Caching Interaction

### Extension Lifecycle Recap

- Extensions are registered before the first `Convert()` call.
- Registration is frozen (`_frozen = true`) after first `Convert()`.
- Extensions run in `ProcessingPipeline.Run()` between parse and render.
- Extensions are deterministic: same input AST → same output AST.

### Parse Cache + Extensions

The parse cache stores the **pre-extension** AST (the direct output of `Parser(input)`).

On cache hit:
1. Retrieve cached `DocumentNode`.
2. Run `ProcessingPipeline.Run()` on it (same as uncached path).
3. Render.

This means extensions run on every `Convert()` call, even on cache hit.
The savings are in skipping the parser — which is the most expensive step.

### Render Cache + Extensions

The render cache stores the **final rendered bytes** (after extensions have run).

On render cache hit:
1. Retrieve cached `byte[]`.
2. Write directly to output stream.
3. Skip both extensions and rendering.

This is valid because:
- Extensions are frozen (same set for all calls).
- Extensions are deterministic (same AST → same mutations).
- Rendering is deterministic (same AST + same options → same output).
- Therefore: same input + same options → same rendered bytes.

### Decision Matrix

| Extensions? | Parse Cache | Render Cache | Notes |
|-------------|-------------|--------------|-------|
| None        | Valid       | Valid        | Pure pipeline: input → AST → output |
| Registered  | Valid       | Valid        | Extensions are frozen + deterministic |

Both caches are valid regardless of extension registration state.
This is a simpler design than the initial proposal (which disabled render cache
with extensions). The simplification is possible because extensions are guaranteed
to be frozen and deterministic within a single engine instance.

### Non-Deterministic Extension Warning

If a user registers an extension with external side effects (file I/O, network,
random), the render cache may serve stale output. This is documented:

> **Warning**: If you register extensions that depend on external state
> (e.g., file contents, network responses), you should either disable caching
> or call `ClearCache()` when external state changes.

## 7. Performance Targets

### Parse Cache Hit

| Metric | Target | Rationale |
|--------|--------|-----------|
| Time   | < 5 µs | Hash computation (~2 µs) + dictionary lookup (~0.1 µs) |
| Allocations | 0 (amortized) | Hash computed into reusable buffer, cached AST returned by reference |
| Speedup (Small) | ~4× | 17.4 µs → ~3 µs |
| Speedup (Large) | ~4000× | 22.3 ms → ~5 µs |

### Render Cache Hit

| Metric | Target | Rationale |
|--------|--------|-----------|
| Time   | < 10 µs + O(n) copy | Hash (~3 µs) + lookup (~0.1 µs) + stream write |
| Allocations | O(n) for stream write | Cached bytes copied to output |
| Speedup (Small) | ~2× | 19.6 µs → ~10 µs |
| Speedup (Large) | ~5000× | 49.6 ms → ~10 µs + copy |

### Cold Path (No Cache Hit)

| Metric | Target | Rationale |
|--------|--------|-----------|
| Overhead | < 5% | SHA-256 computation + dictionary miss + cache store |
| Small doc | +1 µs | 19.6 µs → ~20.6 µs |
| Large doc | +50 µs | 49.6 ms → ~49.65 ms |

### Memory Overhead

| Cache | Per Entry | Max (16 entries) |
|-------|-----------|------------------|
| Parse (Small AST) | ~27 KB | ~432 KB |
| Parse (Large AST) | ~16 MB | ~256 MB |
| Render (Small HTML) | ~9 KB | ~144 KB |
| Render (Large HTML) | ~9 MB | ~144 MB |

For typical usage (1-3 entries, small-medium docs), total cache overhead is < 5 MB.
For large documents with `MaxCacheEntries = 16`, users should reduce the limit.

## 8. Benchmark Additions

### New Benchmark Suite: CacheBenchmarks

Located in `benchmarks/AdocNet.Benchmarks/CacheBenchmarks.cs`.

```csharp
[MemoryDiagnoser]
[ShortRunJob]
public class CacheBenchmarks
{
    // Setup: create AdocEngine with EnableCaching = true
    // Pre-warm: call Convert once to populate cache

    [Benchmark] CacheHitSmall()   // Convert same Small input → cache hit
    [Benchmark] CacheHitMedium()  // Convert same Medium input → cache hit
    [Benchmark] CacheHitLarge()   // Convert same Large input → cache hit
    [Benchmark] CacheMissSmall()  // Convert different Small input → cache miss
    [Benchmark] CacheMissLarge()  // Convert different Large input → cache miss
}
```

### Measurement Goals

1. **Cache hit latency**: time from `Convert()` entry to output written (cache path).
2. **Cache miss overhead**: compare uncached Convert() with caching-enabled-but-miss Convert().
3. **Memory**: allocations per cached Convert() vs uncached Convert().
4. **Hash computation**: standalone SHA-256 hashing benchmark for various input sizes.

### Expected Results

| Benchmark | Expected Mean | vs Baseline |
|-----------|--------------|-------------|
| Cache Hit Small | < 10 µs | ~50× faster than E2E baseline (19.6 µs) |
| Cache Hit Large | < 100 µs | ~500× faster than E2E baseline (49.6 ms) |
| Cache Miss Small | ~20 µs | < 5% overhead vs baseline |
| Cache Miss Large | ~50 ms | < 1% overhead vs baseline |

## 9. Explicit Non-Goals

The following are **out of scope** for beta.10:

1. **Distributed/remote caching**: No Redis, no shared cache across processes.
   Caching is per-engine-instance, in-process only.

2. **Persistent cache**: Cache does not survive process restart.
   No file-based cache, no serialization. Cache is memory-only.

3. **Background/async parsing**: Parser is synchronous. No background threads
   for cache warming or pre-parsing.

4. **Parallel parsing**: No concurrent parsing of different documents.
   Each `Convert()` call is synchronous.

5. **Incremental AST diffing**: When input changes, the entire document is re-parsed.
   No attempt to detect which sections changed and reuse partial ASTs.
   This is a future optimization requiring significant AST infrastructure.

6. **Cache statistics/metrics**: No hit rate counters, no eviction counters,
   no monitoring API. Add in a future release if needed.

7. **Cache serialization**: No way to save/load cache to/from disk.
   Cache is transient and memory-only.

8. **Per-renderer cache tuning**: Both parse and render caches use the same
   `MaxCacheEntries` limit. No per-renderer or per-format cache configuration.

9. **Cache warming API**: No `PreloadCache(string input)` method.
   Cache is populated lazily on first Convert() call.

## 10. File Layout

### New Files

```
src/AdocNet.Core/
├── Caching/
│   ├── LruCache.cs           // LRU cache with O(1) lookup and eviction
│   └── CacheKeyBuilder.cs    // SHA-256 hashing utilities
│
tests/AdocNet.Core.Tests/
├── Caching/
│   ├── LruCacheTests.cs      // Unit tests for LRU cache
│   ├── CacheKeyBuilderTests.cs  // Hash computation tests
│   └── EngineCachingTests.cs // Integration: cached vs uncached correctness
│
benchmarks/AdocNet.Benchmarks/
└── CacheBenchmarks.cs        // Cache hit/miss performance
```

### Modified Files

```
src/AdocNet.Core/AdocEngine.cs  // Add EnableCaching, MaxCacheEntries, ClearCache(),
                                // cache integration in Convert()
```

No other existing files are modified.

## 11. Convert() Flow with Caching

Complete pseudocode for the cached Convert() path:

```
Convert(string input, Stream output, RenderOptions? options):
    opts = options ?? RenderOptions.Default

    // ── Uncached path (EnableCaching = false) ────────────────
    if not EnableCaching:
        doc = Parser(input)
        RunExtensions(doc, opts)
        Renderer.Render(doc, output, opts)
        return

    // ── Cached path ──────────────────────────────────────────
    inputHash = CacheKeyBuilder.ComputeInputHash(input)

    // Check render cache first (avoids both parse AND render)
    renderKey = CacheKeyBuilder.ComputeRenderKey(inputHash, Renderer.Format, opts)
    if _renderCache.TryGet(renderKey, out cachedBytes):
        output.Write(cachedBytes)
        return

    // Check parse cache (avoids parse only)
    if _parseCache.TryGet(inputHash, out doc):
        // Cache hit — still need to run extensions and render
    else:
        doc = Parser(input)
        _parseCache.Add(inputHash, doc)

    // Run extensions (always, even on cache hit)
    RunExtensions(doc, opts)

    // Render and cache the output
    using var buffer = new MemoryStream()
    Renderer.Render(doc, buffer, opts)
    var bytes = buffer.ToArray()
    _renderCache.Add(renderKey, bytes)
    output.Write(bytes, 0, bytes.Length)
```

Key ordering: render cache is checked **before** parse cache. This is because
a render cache hit skips everything (parse + extensions + render), while a parse
cache hit only skips parsing. Checking render first maximizes the benefit.

## 12. Thread Safety Design

### Lock Strategy

Both `LruCache` instances use a private `object _lock` field:

```csharp
internal sealed class LruCache<TKey, TValue>
{
    private readonly object _lock = new();

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock) { /* lookup + move to front */ }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock) { /* add + evict if needed */ }
    }
}
```

### Why Not ConcurrentDictionary

`ConcurrentDictionary` provides concurrent reads and writes, but LRU eviction
requires maintaining a linked list of access order. Updating this list on every
read requires synchronization anyway, so `ConcurrentDictionary` provides no
benefit over a simple lock.

### Concurrent Convert() Calls

Two threads calling `Convert()` with the same input:
1. Both compute the same `inputHash`.
2. Both miss the parse cache (if first call).
3. Both parse the document (duplicate work on first call only).
4. Both try to `Add()` to the cache — second write overwrites first (same key, same value).
5. Subsequent calls from either thread hit the cache.

This "thundering herd" on cold start is acceptable because:
- It only happens once per unique input.
- The duplicate parse is correct (pure function).
- Adding lock-based "single-flight" would add complexity for negligible benefit.

## 13. SHA-256 Implementation Notes

### netstandard2.0 Compatibility

`System.Security.Cryptography.SHA256` is available on netstandard2.0 via the
`System.Security.Cryptography.Algorithms` package (part of the .NET Standard BCL).

Usage:

```csharp
using System.Security.Cryptography;

static string ComputeHash(string input)
{
    var bytes = Encoding.UTF8.GetBytes(input);
    var hash = SHA256.HashData(bytes);  // .NET 5+ only
    // For netstandard2.0:
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(bytes);
    return Convert.ToHexString(hash);   // .NET 5+ only
    // For netstandard2.0: BitConverter or manual hex
}
```

### TFM-Conditional Implementation

Use `#if` to pick the optimal API per target:

```csharp
#if NET5_0_OR_GREATER
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
#else
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(bytes);
    return BitConverter.ToString(hash).Replace("-", "");
#endif
```

The `NET5_0_OR_GREATER` path avoids allocating a `SHA256` instance (uses static method)
and uses the faster `Convert.ToHexString`.

### Large Input Optimization

For inputs larger than ~80 KB, avoid allocating a single UTF-8 byte array.
Instead, use `IncrementalHash` to hash in chunks:

```csharp
using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
// Feed chunks of the input string
hasher.AppendData(chunk);
var hash = hasher.GetHashAndReset();
```

`IncrementalHash` is available on netstandard2.0 and avoids the large byte[] allocation.
For inputs < 80 KB, the simple `SHA256.ComputeHash(bytes)` path is fine.
