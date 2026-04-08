# AdocNet v1.0.0-beta.13 — Design Document

> Two themes: **API Improvement** (bool Process() return) and **Extension Isolation**
> (AssemblyLoadContext on net6.0+, hot-reloading for dev workflow).
> No backward compatibility constraint — no users, no extensions, no forks.

---

## Theme A — bool Process() Return

### 1. New Interface Signatures

All three processor interfaces change from `void Process()` to `bool Process()`.
The return value indicates whether the processor "handled" the node:

- `true` — "I handled this node, skip remaining processors for this node."
- `false` — "Continue to next processor" (current behavior preserved).

#### IDocumentProcessor (also gains RenderContext)

```csharp
public interface IDocumentProcessor
{
    /// <summary>
    /// Processes the document. Returns true if this processor handled the document
    /// and remaining document processors should be skipped.
    /// </summary>
    bool Process(DocumentNode document, RenderContext context);
}
```

**Breaking change**: adds `RenderContext` parameter. Currently `IDocumentProcessor.Process`
receives only `DocumentNode` — unlike `IBlockProcessor` and `IInlineProcessor` which both
receive `RenderContext`. This inconsistency is fixed: all three interfaces now receive context.
This allows document processors to emit diagnostics, access render options, and use
per-render state via `GetOrCreate<T>()`.

#### IBlockProcessor

```csharp
public interface IBlockProcessor
{
    bool CanProcess(BlockNode node);

    /// <summary>
    /// Processes the block node. Returns true if this processor handled the node
    /// and remaining block processors should be skipped for THIS node.
    /// </summary>
    bool Process(BlockNode node, RenderContext context);
}
```

#### IInlineProcessor

```csharp
public interface IInlineProcessor
{
    bool CanProcess(InlineNode node);

    /// <summary>
    /// Processes the inline node. Returns true if this processor handled the node
    /// and remaining inline processors should be skipped for THIS node.
    /// </summary>
    bool Process(InlineNode node, RenderContext context);
}
```

#### IOutputProcessor — NO CHANGE

```csharp
// Already returns byte[] — no change needed
byte[] Process(byte[] renderedOutput, string format);
```

---

### 2. Pipeline Short-Circuit Behavior

When `Process()` returns `true`, the pipeline breaks out of the processor loop for that
specific node. Processing continues normally for subsequent nodes.

#### Document processors (Phase 1)

```
foreach (var processor in documentProcessors)
{
    if (processor.Process(document, context))
    {
        failureCounts?.Remove(processor);
        break;  // <-- short-circuit: skip remaining document processors
    }
    failureCounts?.Remove(processor);
}
```

When a document processor returns `true`:
- Remaining document processors are skipped entirely
- Block and inline processors still run (they operate on nodes, not the document)

#### Block processors (Phase 2)

```
foreach (var processor in processors)
{
    if (processor.CanProcess(block))
    {
        if (processor.Process(block, context))
        {
            failureCounts?.Remove(processor);
            break;  // <-- short-circuit: skip remaining block processors for THIS block
        }
        failureCounts?.Remove(processor);
    }
}
// Next block node processed normally with all processors
```

When a block processor returns `true`:
- Remaining block processors are skipped for THIS block node only
- The next block node in the tree walk runs all processors again
- Replacement handling still occurs after the loop

#### Inline processors (Phase 3)

Same pattern as block processors — short-circuit is per-node, not global.

#### Failure tracking interaction

The short-circuit only applies on success. If a processor throws an exception:
- The catch block still handles it (warning + failure tracking)
- The `break` never executes (exception prevented `true` return)
- This is the correct behavior: a crashing processor should not prevent others from running

---

### 3. IDocumentProcessor Gains RenderContext

#### Motivation

`IBlockProcessor.Process` and `IInlineProcessor.Process` both receive `RenderContext`,
but `IDocumentProcessor.Process` does not. This prevents document processors from:

- Emitting diagnostics via `context.AddDiagnostic()`
- Accessing render options via `context.Options`
- Using per-render shared state via `context.GetOrCreate<T>()`

#### Change

```csharp
// Before (beta.12)
void Process(DocumentNode document);

// After (beta.13)
bool Process(DocumentNode document, RenderContext context);
```

#### Impact on ProcessingPipeline

The pipeline already creates `RenderContext` before calling processors (line 413 in AdocEngine).
The context is passed to `ProcessingPipeline.Run()` already. The document processor loop
at line 34 just needs to pass `context` through:

```csharp
// Before
processor.Process(document);

// After
if (processor.Process(document, context))
{
    failureCounts?.Remove(processor);
    break;
}
```

#### Impact on built-in DocumentMetadataProcessor

```csharp
// Before
public void Process(DocumentNode document) { ... }

// After
public bool Process(DocumentNode document, RenderContext context)
{
    // existing logic unchanged
    return false;
}
```

---

### 4. Migration Plan — Complete File List

#### Phase order (important for compilability)

1. Change interface files (3 files) — compilation breaks everywhere
2. Update built-in processors (4 files) — add `return false;` and RenderContext param
3. Update test extension processors (4 files) — add `return false;` and RenderContext param
4. Update test mock processors (9 files, 23 classes) — add `return false;` and RenderContext param
5. Update ProcessingPipeline (1 file) — check bool return, pass context to doc processors
6. Build and test — all 1142 tests must pass with identical behavior

#### Interface files (3)

| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/IDocumentProcessor.cs` | `void Process(DocumentNode)` → `bool Process(DocumentNode, RenderContext)` |
| `src/AdocNet.Core/Extensions/IBlockProcessor.cs` | `void Process(BlockNode, RenderContext)` → `bool Process(BlockNode, RenderContext)` |
| `src/AdocNet.Core/Extensions/IInlineProcessor.cs` | `void Process(InlineNode, RenderContext)` → `bool Process(InlineNode, RenderContext)` |

#### Built-in processors (4)

| File | Change |
|------|--------|
| `DocumentMetadataProcessor.cs` | Add RenderContext param, `return false;` |
| `AutoIdBlockProcessor.cs` | Change `void` to `bool`, `return false;` |
| `DiagramBlockProcessor.cs` | Change `void` to `bool`, `return false;` |
| `IconMacroProcessor.cs` | Change `void` to `bool`, `return false;` |

#### Test extension processors (4)

| File | Change |
|------|--------|
| `TestDocumentProcessor.cs` | Add RenderContext param, `return false;` |
| `TestPrefixBlockProcessor.cs` | Change `void` to `bool`, `return false;` |
| `TestInlineProcessor.cs` | Change `void` to `bool`, `return false;` |
| `NoCtorProcessor.cs` | Change `void` to `bool`, `return false;` |

#### Test mock processors (9 files, 23 classes)

| File | Classes | Change |
|------|---------|--------|
| `ExtensionRegistrationTests.cs` | 3 | `void` → `bool`, add RenderContext where needed, `return false;` |
| `PipelineExecutionTests.cs` | 6 | Same pattern. DelegateDocumentProcessor needs RenderContext in its Action. |
| `ExtensionPriorityTests.cs` | 3 | Same pattern |
| `ExtensionCapabilitiesTests.cs` | 4 | Same pattern |
| `ExtensionLifecycleTests.cs` | 1 | Same pattern |
| `ExtensionIntegrationTests.cs` | 1 | Same pattern |
| `ExtensionDiagnosticsTests.cs` | 1 | Same pattern |
| `Extensions/HardeningTests.cs` | 3 | Same pattern |
| `Extensions/ExtensionCommandTests.cs` | 1 | Same pattern |

#### Pipeline (1)

| File | Change |
|------|--------|
| `ProcessingPipeline.cs` | 3 call sites: check bool return, break on true. Pass context to doc processors. |

#### New tests needed

- Short-circuit test: register 2 block processors, first returns `true` → second never called
- Short-circuit test: document processor returns `true` → remaining doc processors skipped
- Short-circuit test: per-node scope — processor returns `true` for node A, still runs for node B
- Document processor receives RenderContext and can emit diagnostics

---

## Theme B — AssemblyLoadContext Isolation

### 5. ExtensionLoadContext

New class: `src/AdocNet.Core/Extensions/ExtensionLoadContext.cs`

```csharp
#if NET6_0_OR_GREATER
using System.Reflection;
using System.Runtime.Loader;

namespace AdocNet.Extensions;

/// <summary>
/// Isolated assembly load context for extension DLLs.
/// Each extension loads in its own context, preventing version conflicts.
/// Collectible contexts support unloading for hot-reload scenarios.
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _extensionDirectory;

    public ExtensionLoadContext(string name, string extensionDirectory)
        : base(name, isCollectible: true)
    {
        _extensionDirectory = extensionDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve from the extension's directory first
        var path = Path.Combine(_extensionDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(path);

        // Fall back to default context (shared framework assemblies)
        return null;
    }
}
#endif
```

Key design decisions:

- **Collectible = true**: enables unloading for hot-reload (Theme C)
- **Directory-scoped resolution**: dependencies resolve from the extension's own directory
  before falling back to the default context (shared runtime assemblies)
- **Name parameter**: uses extension name for diagnostics (e.g., "ext:diagram")
- **Null return**: `Load()` returning null delegates to the default AssemblyLoadContext,
  which resolves .NET runtime and shared framework assemblies

### 6. Conditional Compilation in ExtensionLoader

The `ExtensionLoader.LoadAssembly()` method changes to use `#if NET6_0_OR_GREATER`:

```csharp
public static List<object> LoadAssembly(string assemblyPath, Action<string>? onWarning)
{
    // ... validation unchanged ...

    Assembly assembly;
    try
    {
#if NET6_0_OR_GREATER
        var dir = Path.GetDirectoryName(fullPath) ?? ".";
        var contextName = $"ext:{Path.GetFileNameWithoutExtension(fullPath)}";
        var context = new ExtensionLoadContext(contextName, dir);
        assembly = context.LoadFromAssemblyPath(fullPath);
#else
        assembly = Assembly.LoadFrom(fullPath);
#endif
    }
    // ... error handling unchanged ...
}
```

The `ExtensionLoadContext` instance is stored alongside the loaded extensions so it can
be unloaded later (for hot-reload). The overloaded `LoadAssembly` returns the context:

```csharp
// New overload for hot-reload scenarios (net6.0+ only)
#if NET6_0_OR_GREATER
internal static (List<object> extensions, ExtensionLoadContext context) LoadAssemblyIsolated(
    string assemblyPath, Action<string>? onWarning)
#endif
```

### 7. Unload Support

AssemblyLoadContext with `isCollectible: true` supports unloading via `Unload()`.
After calling `Unload()`, the GC eventually collects the context and all loaded assemblies.

**Unload sequence** (used by hot-reload):

1. Call `context.Unload()` — marks context for unloading
2. Drop all references to types/instances from that context
3. GC collects the context (may require `GC.Collect()` + `GC.WaitForPendingFinalizers()`)

**Important constraints**:

- Types loaded from a collectible context cannot be used after unload
- The engine must clear its processor lists before unloading
- Any cached output referencing those types must be invalidated → `ClearCache()`

---

## Theme C — Hot-Reloading

### 8. FileSystemWatcher Integration

New class: `src/AdocNet.Core/Extensions/ExtensionHotReloader.cs`

Watches extension directories for `*.dll` changes and triggers reload.

```csharp
internal sealed class ExtensionHotReloader : IDisposable
{
    private readonly AdocEngine _engine;
    private readonly string _extensionDirectory;
    private readonly FileSystemWatcher _watcher;
    private readonly Action<string>? _onWarning;
    private Timer? _debounceTimer;
    private readonly object _reloadLock = new();

    // 500ms debounce — DLL writes aren't atomic
    private const int DebounceMs = 500;
}
```

**Debounce strategy**: DLL files are typically written in multiple stages (create file,
write content, close handle). A single logical update may fire multiple `Changed`/`Created`
events. The debounce timer resets on each event; the reload fires only after 500ms of
silence.

**Watch events**: `Changed`, `Created`, `Deleted` for `*.dll` files in the extension directory.

**Reload flow**:

```
FileSystemWatcher event
  → Reset debounce timer (500ms)
  → Timer fires
  → Acquire _reloadLock
  → engine.ReloadExtensions()  [internal method]
  → Release _reloadLock
```

### 9. Hot-Reload vs _frozen Flag

The `_frozen` flag prevents registration after the first `Convert()` call.
Hot-reload must bypass this, but safely.

**Design: internal ReloadExtensions() method on AdocEngine**

```csharp
#if NET6_0_OR_GREATER
/// <summary>
/// Internal method called by ExtensionHotReloader.
/// Safely unfreezes, clears processors, reloads from directory, re-freezes.
/// </summary>
internal void ReloadExtensions(string extensionDirectory)
{
    lock (_reloadLock)
    {
        // 1. Unfreeze
        _frozen = false;

        // 2. Shutdown lifecycle extensions
        foreach (var lifecycle in _lifecycleExtensions)
        {
            try { lifecycle.Dispose(); } catch { }
        }

        // 3. Clear all processor lists
        _documentProcessors.Clear();
        _blockProcessors.Clear();
        _inlineProcessors.Clear();
        _outputProcessors.Clear();
        _lifecycleExtensions.Clear();
        _failureCounts.Clear();
        _disabledProcessors.Clear();

        // 4. Unload old contexts
        UnloadAllExtensionContexts();

        // 5. Reload from directory
        var extensions = ExtensionLoader.LoadDirectory(extensionDirectory, OnWarning);
        RegisterExtensions(extensions);

        // 6. Re-freeze and invalidate cache
        _frozen = true;
        SortByPriority(_documentProcessors);
        SortByPriority(_blockProcessors);
        SortByPriority(_inlineProcessors);
        ClearCache();
    }
}
#endif
```

**Thread safety**: the `_reloadLock` prevents concurrent `Convert()` and reload operations.
This is acceptable because:
- Hot-reload is a dev-time feature, not production
- The lock is held briefly (milliseconds)
- `Convert()` must also acquire this lock when hot-reload is enabled

**The `_reloadLock` field**: only used when `EnableHotReload == true`. When hot-reload is
disabled, `Convert()` does not acquire any lock (zero overhead for production use).

### 10. Limitations and Platform Behavior

| Feature | net6.0+ (NET6_0_OR_GREATER) | netstandard2.0 |
|---------|----------------------------|----------------|
| `bool Process()` return | Yes | Yes |
| IDocumentProcessor RenderContext | Yes | Yes |
| AssemblyLoadContext isolation | Yes | No — uses Assembly.LoadFrom |
| Extension unloading | Yes (collectible contexts) | No |
| Hot-reload (`EnableHotReload`) | Yes | Throws `NotSupportedException` |
| FileSystemWatcher | Available but unused | Available but unused |

Setting `EnableHotReload = true` on netstandard2.0:

```csharp
public bool EnableHotReload
{
    get => _enableHotReload;
    set
    {
#if NET6_0_OR_GREATER
        _enableHotReload = value;
        if (value) StartWatching();
        else StopWatching();
#else
        if (value)
            throw new NotSupportedException(
                "Hot-reload requires .NET 6.0 or later for assembly unloading.");
        _enableHotReload = false;
#endif
    }
}
```

Hot-reload triggers `ClearCache()` because:
- Parse cache stores `DocumentNode` references that may have been produced by old processors
- Render cache stores `byte[]` that was generated with old extension behavior
- Both are invalid after an extension DLL change

---

## Cross-Cutting Concerns

### 11. Testing Strategy

#### Theme A — bool Process() tests

| Test | Description |
|------|-------------|
| `ShortCircuit_DocumentProcessor_SkipsRemaining` | Two doc processors, first returns true → second never called |
| `ShortCircuit_BlockProcessor_PerNode` | Returns true for node A → still runs for node B |
| `ShortCircuit_InlineProcessor_PerNode` | Same pattern for inline processors |
| `NoShortCircuit_WhenFalse` | All processors returning false → all execute (existing behavior) |
| `DocumentProcessor_ReceivesRenderContext` | Doc processor can call context.AddDiagnostic() |
| `DocumentProcessor_CanAccessOptions` | Doc processor reads context.Options |
| `ShortCircuit_WithFailureTracking` | Short-circuit and failure tracking don't interfere |
| `ExistingTests_AllPass` | All 1142 existing tests pass unchanged |

#### Theme B — AssemblyLoadContext tests

| Test | Description |
|------|-------------|
| `LoadAssembly_UsesIsolatedContext` | On net6.0+, loaded extension is in its own context |
| `IsolatedExtension_ResolvesDependencies` | Extension resolves its own deps from its directory |
| `IsolatedExtension_FallbackToDefault` | Framework assemblies resolve from default context |
| `LoadAssembly_NetStandard_FallsBack` | On ns2.0, Assembly.LoadFrom still works |

These tests need `#if NET6_0_OR_GREATER` guards or runtime TFM checks.

#### Theme C — Hot-reload tests

| Test | Description |
|------|-------------|
| `EnableHotReload_NetStandard_Throws` | Setting true on ns2.0 throws NotSupportedException |
| `HotReload_PicksUpNewDll` | Copy new DLL → debounce → engine uses new processor |
| `HotReload_ClearsCache` | After reload, cached output is cleared |
| `HotReload_DisposesOldLifecycle` | Old IExtensionLifecycle.Dispose() called on reload |
| `HotReload_ThreadSafe` | Concurrent Convert() + reload don't corrupt state |
| `Debounce_CoalescesEvents` | Multiple rapid events → single reload |

Hot-reload tests are integration-heavy and require file I/O. They should be marked
with `[Category("Integration")]` or similar for selective test runs.

### 12. Explicit Non-Goals

The following are explicitly out of scope for beta.13:

| Non-goal | Reason |
|----------|--------|
| Remote registry / marketplace | No users yet; adds network complexity |
| Extension sandboxing | AssemblyLoadContext provides isolation, not sandboxing |
| Permission model | No need until extensions have side effects to restrict |
| True incremental parsing | AST diffing is v2.x scope |
| Extension dependency resolution | Extensions are independent; dep conflicts are the user's problem |
| Cross-extension communication | Extensions interact only via AST mutations |
| GUI for extension management | CLI-only for now |
| NuGet-based extension distribution | Custom packaging is sufficient for beta |
| Hot-reload on netstandard2.0 | Assembly.LoadFrom doesn't support unloading |
| Processor priority changes at runtime | Priority is set at registration time only |

---

## Implementation Order

| Phase | Description | Depends on |
|-------|-------------|------------|
| P02 | `bool Process()` interface change + migration of all implementors | Design (this doc) |
| P03 | `ExtensionLoadContext` + conditional compilation in `ExtensionLoader` | P02 (interfaces must be stable) |
| P04 | `ExtensionHotReloader` + `EnableHotReload` + debounce + tests | P03 (requires AssemblyLoadContext) |
| Check A | System integrity validation | P04 |
| P05 | Documentation updates (EXTENSIONS.md, CHANGELOG, etc.) | P04 |

P02 is the largest phase by file count (~23 files) but mechanically simple.
P03 and P04 are architecturally complex but touch fewer files.

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| Missing a processor implementor during migration | P00 context doc lists all 31 classes |
| Bool return breaks existing test behavior | All existing processors return `false` (no behavior change) |
| AssemblyLoadContext type conflicts | ExtensionLoadContext resolves extension-local first, then falls back to default |
| Hot-reload race condition | `_reloadLock` serializes Convert + reload; debounce prevents rapid reloads |
| DLL file locking on Windows | FileSystemWatcher detects changes; reload retries on IOException |
| GC not collecting unloaded context | Force GC after unload (dev-time feature, acceptable overhead) |
