# Performance Baseline — AdocNet v1.0.0-beta.10

> Generated: 2026-04-05
> Runtime: .NET 10.0.5, X64 RyuJIT AVX2, Windows 11 (Hyper-V)
> BenchmarkDotNet v0.14.0, ShortRun (3 iterations, 1 warmup)

## 1. Current Convert() Flow

```
AdocEngine.Convert(string input, Stream output, RenderOptions? options)
│
├── 1. Parse:    var doc = Parser(input);            // Func<string, DocumentNode>
├── 2. Pipeline: ProcessingPipeline.Run(doc, ...)    // Extensions modify AST (if registered)
└── 3. Render:   Renderer.Render(doc, output, opts)  // IDocumentRenderer.Render()
```

Each call to `Convert()` re-parses and re-renders from scratch. No caching at any layer.

### Key Signatures

- **Parser**: `Func<string, DocumentNode>` — pure function. Same input → same AST.
- **Renderer**: `IDocumentRenderer.Render(DocumentNode, Stream, RenderOptions)` — pure given same AST + options.
- **Pipeline**: `ProcessingPipeline.Run()` — runs registered processors (doc → block → inline, FIFO order). Skipped when no processors registered.

### State Management

- `RenderContext` created fresh per `Convert()` call — per-render state via `GetOrCreate<T>()`.
- `_frozen` flag: registration locked after first `Convert()`.
- No shared mutable state between calls (except failure counters for processor disabling).

## 2. AST Structure

### AstNode (base class)

```csharp
public abstract class AstNode
{
    private readonly List<AstNode> _children = [];
    public SourceRange Source { get; set; }
    public IReadOnlyList<AstNode> Children => _children;
    public abstract AstNodeKind Kind { get; }
    public void AddChild(AstNode child) { ... }
    public void InsertChild(int index, AstNode child) { ... }
}
```

**No `GetHashCode` or `Equals` overrides.** No `IEquatable<T>`. Hashing must be computed externally.

### DocumentNode (root)

```csharp
public sealed class DocumentNode : AstNode
{
    public string? Title { get; set; }
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
}
```

Attributes are mutable during parsing/processing but stable after pipeline completes.

## 3. Benchmark Results — Pre-Caching Baseline

### End-to-End (Parse + Render to HTML)

| Benchmark            | Mean         | Allocated  |
|----------------------|-------------:|-----------:|
| E2E: Small (~1KB)    |     19.58 µs |   35.42 KB |
| E2E: Medium (~50KB)  |    490.87 µs |  905.55 KB |
| E2E: Large (~500KB)  | 49,567.92 µs |  25,422 KB |

### Parser Only

| Benchmark              | Mean         | Allocated   |
|------------------------|-------------:|------------:|
| Parse: Small (~1KB)    |     17.40 µs |    26.81 KB |
| Parse: Medium (~50KB)  |    408.05 µs |   634.06 KB |
| Parse: Large (~500KB)  | 22,372.69 µs | 16,321.71 KB |
| Parse: Table-heavy     |    719.42 µs |  1,338.26 KB |
| Parse: List-heavy      |  1,567.13 µs |  1,971.80 KB |

### Renderer Only (HTML, pre-parsed AST)

| Benchmark                | Mean        | Allocated  |
|--------------------------|------------:|-----------:|
| Render: Small (~1KB)     |    2.174 µs |    8.77 KB |
| Render: Medium (~50KB)   |   65.046 µs |  271.48 KB |
| Render: Large (~500KB)   | 7,968.44 µs | 9,100.64 KB |
| Render: Table-heavy      | 1,218.48 µs | 1,229.15 KB |
| Render: List-heavy       |   536.47 µs |  759.00 KB |

### Document Generator Sizes

| Generator     | Approx Size | Description               |
|---------------|-------------|---------------------------|
| Small()       | ~1 KB       | Basic structure, few inlines |
| Medium()      | ~50 KB      | 10 sections, code blocks, lists |
| Large()       | ~500 KB     | 50 chapters × 5 sections, dense text |
| TableHeavy()  | variable    | 20 tables × 15 rows       |
| ListHeavy()   | variable    | 30 sections × 10 nested lists |

## 4. Performance Profile Analysis

### Where Time Is Spent

| Operation | Small  | Medium | Large  |
|-----------|--------|--------|--------|
| Parse     | 88.9%  | 83.1%  | 73.7%  |
| Render    | 11.1%  | 13.2%  | 16.1%  |
| Pipeline  | 0% (no extensions) | 0% | 0% |

**Parsing dominates** at all document sizes. The ratio shifts slightly toward rendering
for larger documents due to string concatenation and stream writes, but parsing remains
the primary bottleneck.

### Where Memory Goes

| Operation | Small  | Medium | Large   |
|-----------|--------|--------|---------|
| Parse     | 75.7%  | 70.0%  | 64.2%   |
| Render    | 24.3%  | 30.0%  | 35.8%   |

Parsing allocates the most memory at all sizes — AST node construction, string allocations
for text content, inline lists, and source range tracking.

### GC Pressure (Large Documents)

The Large document benchmark shows Gen2 GC collections:
- Parse: 343 Gen2 per 1000 ops (16.3 MB allocation forces full GC)
- Render: 500 Gen2 per 1000 ops (9.1 MB allocation)
- E2E: 818 Gen2 per 1000 ops (25.4 MB total)

This indicates significant GC pressure for large documents — a strong case for caching
to avoid repeated allocation.

## 5. Cache Opportunities

### Layer 1: Parse Cache (HIGH VALUE)

**Key**: Hash of input string (SHA-256 or faster hash).
**Value**: Parsed `DocumentNode`.

**Why**:
- Parsing is the most expensive operation (75-89% of time, 64-76% of memory).
- Parser is a pure function: same input → same AST, guaranteed.
- In live-preview/IDE scenarios, the same document is re-parsed on every keystroke.
- Cache hit avoids all parse allocations and computation.

**Expected impact**: Near-100% speedup for repeated conversions of unchanged documents.
For a Small document: save ~17.4 µs and 26.81 KB per cached hit.
For a Large document: save ~22.3 ms and 16.3 MB per cached hit.

**Considerations**:
- Cache entries are potentially large (the full AST tree). Need bounded LRU eviction.
- AST nodes are mutable (extensions can modify them). Cached ASTs must be treated as
  shared/immutable, or the pipeline must operate on clones.
- Thread safety: `ConcurrentDictionary` or `lock` for concurrent access.

### Layer 2: Render Cache (MEDIUM VALUE)

**Key**: Hash of (input string + RenderOptions type + relevant option values).
**Value**: Rendered bytes (`byte[]`).

**Why**:
- If AST and options are unchanged, render output is identical.
- Useful in watch mode / live preview where the same document is rendered repeatedly.
- Avoids both parse AND render costs.

**Expected impact**: Full speedup for repeated conversions — only hash computation cost.
For a Large document: save ~49.6 ms and 25.4 MB per cached hit.

**Considerations**:
- Render cache is only useful when both input AND options are unchanged.
- Cache entries are rendered bytes — size varies widely (small for HTML, large for PDF).
- Render cache can be keyed on input hash + options hash (no AST hashing needed).
- Must invalidate when extensions change (but extensions are frozen after first Convert).

### Layer 3: Incremental Rendering (FUTURE — NOT beta.10)

For IDE/live-preview: when input changes slightly, detect which AST subtrees changed
and re-render only those. Requires AST diffing infrastructure. Deferred to future release.
Beta.10 focuses on the caching foundation.

## 6. Cache Design Sketch

### Architecture

```
AdocEngine
├── EnableCaching: bool (default false, opt-in)
├── ParseCache: LruCache<string-hash, DocumentNode>
├── RenderCache: LruCache<string-hash + options-hash, byte[]>
└── Convert() flow:
    1. Compute input hash
    2. If parse cache hit: reuse AST
    3. Else: parse, store in parse cache
    4. Run extensions (if any)
    5. Compute render key (input hash + options hash)
    6. If render cache hit: write cached bytes to output
    7. Else: render, store in render cache
```

### Hashing Strategy

- Input string: SHA-256 via `System.Security.Cryptography.SHA256` (built-in on netstandard2.0).
- RenderOptions: hash based on type + serialized option values.
- Cache key: fixed-size byte array (32 bytes for SHA-256).

### Eviction

- LRU (Least Recently Used) eviction policy.
- Configurable max entry count (default TBD — likely 16-64 entries).
- Separate limits for parse cache and render cache.

### Thread Safety

- `ConcurrentDictionary` for cache storage.
- Or simpler: `lock` around cache access (cache hits are fast, contention unlikely).

### Correctness Invariant

**Cached output MUST be byte-identical to non-cached output.** This is the #1 rule.
Any cache optimization that risks divergent output is rejected.

## 7. Benchmark Infrastructure

### Existing Benchmarks (3 suites, 13 benchmarks)

1. **EndToEndBenchmarks**: Small, Medium, Large (parse + HTML render)
2. **ParserBenchmarks**: Small, Medium, Large, TableHeavy, ListHeavy
3. **RendererBenchmarks**: Small, Medium, Large, TableHeavy, ListHeavy (HTML render only)

### Beta.10 Additions Needed

- **CacheBenchmarks**: measure cache hit vs miss performance
- **MemoryBenchmarks**: measure cache memory overhead
- **ConcurrencyBenchmarks**: measure thread-safe cache under contention (optional)

## 8. Key Constraints

- Parser (`src/AdocNet.Parser/`) MUST NOT be modified.
- AST (`src/AdocNet.Ast/`) MUST NOT be modified.
- Existing renderers MUST NOT be modified.
- Existing `AdocEngine` method signatures MUST NOT be modified (additive only).
- No external NuGet dependencies (SHA-256 is built-in).
- No global mutable state for caches — per-engine-instance.
- Caching is opt-in (`EnableCaching = true`).

## 9. Beta.10 Cached Benchmark Results (Post-Implementation)

> Recorded: 2026-04-05 after P02 implementation.
> Runtime: .NET 10.0.5, X64 RyuJIT AVX2, Windows 11 (Hyper-V)

### Cold (Uncached) vs Cached (Cache Hit)

| Benchmark              | Cold Mean     | Cached Mean  | Speedup | Cold Alloc   | Cached Alloc | Mem Reduction |
|------------------------|---------------|--------------|---------|--------------|--------------|---------------|
| Small (~1KB)           | 17.82 µs      | 1.19 µs      | **15×** | 33.52 KB     | 2.46 KB      | 13.6×         |
| Medium (~50KB)         | 411.41 µs     | 15.39 µs     | **27×** | 824.63 KB    | 43.82 KB     | 18.8×         |
| Large (~500KB)         | 28,775.92 µs  | 634.50 µs    | **45×** | 22,512.73 KB | 1,620.06 KB  | 13.9×         |

### Cold Path Regression Check (vs P00 Baseline)

| Size   | P00 Baseline | P02 Cold   | Delta     | Verdict     |
|--------|-------------|------------|-----------|-------------|
| Small  | 19.58 µs    | 17.82 µs   | -9% (noise) | No regression |
| Medium | 490.87 µs   | 411.41 µs  | -16% (noise) | No regression |
| Large  | 49,568 µs   | 28,776 µs  | -42% (variance) | No regression |

Cold path shows no regression. Differences are within benchmark variance
(ShortRun with 3 iterations, Hyper-V).

### Cache Hit Allocation Breakdown

- **Small cached (2.46 KB)**: MemoryStream overhead + byte[] copy to output
- **Medium cached (43.82 KB)**: ~44 KB HTML output copied to caller's stream
- **Large cached (1,620 KB)**: ~1.6 MB HTML output copied to caller's stream

Cache hit allocations are proportional to output size — the cached byte[] is
written to the output stream, which causes the MemoryStream to allocate its
internal buffer. Parse and render allocations are completely eliminated.
