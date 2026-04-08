# Beta.13 Context Discovery

> Generated during P00. Read-only audit of the codebase before implementation.

---

## 1. Processor Interface Definitions (to be changed: void → bool)

| Interface | File | Signature (current) |
|-----------|------|---------------------|
| `IDocumentProcessor` | `src/AdocNet.Core/Extensions/IDocumentProcessor.cs:16` | `void Process(DocumentNode document)` |
| `IBlockProcessor` | `src/AdocNet.Core/Extensions/IBlockProcessor.cs:25` | `void Process(BlockNode node, RenderContext context)` |
| `IInlineProcessor` | `src/AdocNet.Core/Extensions/IInlineProcessor.cs:25` | `void Process(InlineNode node, RenderContext context)` |
| `IOutputProcessor` | `src/AdocNet.Core/Extensions/IOutputProcessor.cs:16` | `byte[] Process(byte[], string)` — **NO CHANGE** (already returns a value) |

---

## 2. Built-in Processor Implementations (src/)

### IDocumentProcessor implementations

| Class | File | Notes |
|-------|------|-------|
| `DocumentMetadataProcessor` | `src/AdocNet.Core/Extensions/DocumentMetadataProcessor.cs:9` | Has constructor param (string text). Inserts metadata paragraph. |

### IBlockProcessor implementations

| Class | File | Notes |
|-------|------|-------|
| `AutoIdBlockProcessor` | `src/AdocNet.Core/Extensions/AutoIdBlockProcessor.cs:9` | Has constructor param (string prefix). Auto-generates section IDs. |
| `DiagramBlockProcessor` | `src/AdocNet.Core/Extensions/DiagramBlockProcessor.cs:11` | Has constructor params (IDiagramToolRunner, string outputDir). Replaces diagram blocks with images. |

### IInlineProcessor implementations

| Class | File | Notes |
|-------|------|-------|
| `IconMacroProcessor` | `src/AdocNet.Core/Extensions/IconMacroProcessor.cs:9` | Parameterless. Replaces icon macros with Unicode symbols. |

**Total built-in processors to update: 4** (1 document + 2 block + 1 inline)

---

## 3. Test Extension Processors (tests/AdocNet.TestExtension/)

| Class | File | Interface(s) |
|-------|------|---------------|
| `TestDocumentProcessor` | `tests/AdocNet.TestExtension/TestDocumentProcessor.cs:9` | `IDocumentProcessor` |
| `TestPrefixBlockProcessor` | `tests/AdocNet.TestExtension/TestPrefixBlockProcessor.cs:11` | `IBlockProcessor, IExtension` |
| `TestInlineProcessor` | `tests/AdocNet.TestExtension/TestInlineProcessor.cs:10` | `IInlineProcessor` |
| `NoCtorProcessor` | `tests/AdocNet.TestExtension/NoCtorProcessor.cs:9` | `IBlockProcessor` (no parameterless ctor — loading test) |

**Total test extension processors to update: 4**

---

## 4. Test Mock/Stub Processors (tests/AdocNet.Tests/)

### ExtensionRegistrationTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `StubBlockProcessor` | 85 | `IBlockProcessor` |
| `StubDocumentProcessor` | 98 | `IDocumentProcessor` |
| `StubInlineProcessor` | 103 | `IInlineProcessor` |

### PipelineExecutionTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `FlagDocumentProcessor` | 288 | `IDocumentProcessor` |
| `DelegateDocumentProcessor` | 294 | `IDocumentProcessor` |
| `DelegateBlockProcessor` | 301 | `IBlockProcessor` |
| `DelegateInlineProcessor` | 309 | `IInlineProcessor` |
| `TrackingBlockProcessor<T>` | 315 | `IBlockProcessor` |
| `TrackingInlineProcessor<T>` | 331 | `IInlineProcessor` |

### ExtensionPriorityTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `TaggingProcessor` | 20 | `IDocumentProcessor, IExtensionPriority, IExtensionCapabilities` |
| `TrackingDocProcessor` | 38 | `IDocumentProcessor, IExtensionPriority` |
| `DefaultPriorityProcessor` | 57 | `IDocumentProcessor` |

### ExtensionCapabilitiesTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `DeterministicDocProcessor` | 16 | `IDocumentProcessor, IExtensionCapabilities` |
| `NonDeterministicDocProcessor` | 27 | `IDocumentProcessor, IExtensionCapabilities` |
| `UndeclaredDocProcessor` | 38 | `IDocumentProcessor` |
| `DeterministicBlockProcessor` | 47 | `IBlockProcessor, IExtensionCapabilities` |

### ExtensionLifecycleTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `LifecycleBlockProcessor` | 67 | `IBlockProcessor, IExtensionLifecycle` |

### ExtensionIntegrationTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `OrderTrackingBlockProcessor` | 161 | `IBlockProcessor` |

### ExtensionDiagnosticsTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `DiagnosticEmittingProcessor` | 83 | `IBlockProcessor` |

### Extensions/HardeningTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `ThrowingBlockProcessor` | 172 | `IBlockProcessor` |
| `CountingBlockProcessor` | 183 | `IBlockProcessor` |
| `TrackingBlockProcessor` | 196 | `IBlockProcessor` |

### Extensions/ExtensionCommandTests.cs

| Class | Line | Interface |
|-------|------|-----------|
| `DummyDocProcessor` | 271 | `IDocumentProcessor` |

**Total test mock processors to update: 22** across 8 test files.

---

## 5. ProcessingPipeline — processor.Process() Call Sites

File: `src/AdocNet.Core/Extensions/ProcessingPipeline.cs`

| Line | Call | Context |
|------|------|---------|
| 34 | `processor.Process(document)` | Phase 1: document processors, inside FIFO foreach loop |
| 102 | `processor.Process(block, context)` | Phase 2: block processors, inside `WalkBlocks`, guarded by `CanProcess()` |
| 218 | `processor.Process(inline, context)` | Phase 3: inline processors, inside `WalkInlineList`, guarded by `CanProcess()` |

**Total call sites: 3.** All inside try/catch blocks. Each must be updated to check `bool` return
value. When `true` is returned, `break` out of the processor loop for that node (short-circuit).

The pipeline also resets failure counts on success (lines 35, 103, 219) — this logic stays.

---

## 6. ExtensionLoader — Assembly Loading (to be changed for AssemblyLoadContext)

File: `src/AdocNet.Core/Extensions/ExtensionLoader.cs`

### Current loading mechanism

- Line 37: `Assembly.LoadFrom(fullPath)` — single call site for assembly loading
- Line 69-70: `IsProcessorType()` — checks `typeof(IDocumentProcessor).IsAssignableFrom(type)` etc.
- Line 84: `Activator.CreateInstance(type)` — parameterless constructor instantiation
- Line 67-70: Types sorted by `FullName` for deterministic order

### What changes for AssemblyLoadContext

- `Assembly.LoadFrom()` replaced with `AssemblyLoadContext.LoadFromAssemblyPath()` on net6.0+
- New class needed: `ExtensionLoadContext : AssemblyLoadContext` (collectible = true)
- Conditional compilation: `#if NET6_0_OR_GREATER` for isolated loading
- `#else` fallback: keep `Assembly.LoadFrom()` for netstandard2.0

### ExtensionDirectoryLoader (also uses ExtensionLoader)

File: `src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs`

- Line 85: `ExtensionLoader.LoadAssembly(entryPath, onWarning)` — delegates to ExtensionLoader
- No direct `Assembly.LoadFrom()` here — changes propagate via ExtensionLoader

---

## 7. AdocEngine — Registration & Pipeline Integration

File: `src/AdocNet.Core/AdocEngine.cs`

### Processor lists (lines 20-23)

```csharp
private readonly List<IDocumentProcessor> _documentProcessors = new();
private readonly List<IBlockProcessor> _blockProcessors = new();
private readonly List<IInlineProcessor> _inlineProcessors = new();
private readonly List<IOutputProcessor> _outputProcessors = new();
```

### Pipeline invocation (line 414)

```csharp
ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors, _inlineProcessors,
    OnWarning, _failureCounts, _disabledProcessors, MaxProcessorFailures);
```

### Properties needed for hot-reload

- `EnableHotReload` (new, bool, default false)
- FileSystemWatcher integration in `Shutdown()` (line 428-441) — stop watchers
- `_frozen` flag (line 27) — may need unfreezing support for hot-reload re-registration

---

## 8. Platform Availability Confirmation

### AssemblyLoadContext

- **Namespace**: `System.Runtime.Loader`
- **Available on**: .NET Core 1.0+, .NET 5+, .NET 6+, .NET 10
- **NOT available on**: netstandard2.0, .NET Framework
- **Conditional compilation guard**: `#if NET6_0_OR_GREATER`
- **Collectible contexts** (for unloading): available since .NET Core 3.0 (`isCollectible: true`)
- Core project targets `netstandard2.0;net10.0` — conditional compilation required

### FileSystemWatcher

- **Namespace**: `System.IO`
- **Available on**: netstandard2.0, .NET Framework 4.5+, .NET Core 1.0+, .NET 10
- **Already used in the codebase**:
  - `src/AdocNet.Cli/WatchCommand.cs:59` — watches `*.adoc` files
  - `src/AdocNet.Cli/PreviewCommand.cs:37` — watches `*.adoc` files
- **No conditional compilation needed** — works on all target frameworks
- **Important**: DLL writes aren't atomic — debounce required (500ms recommended)

---

## 9. Complete File Change Inventory

### Interface files (3 files)

| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/IDocumentProcessor.cs` | `void Process()` → `bool Process()` |
| `src/AdocNet.Core/Extensions/IBlockProcessor.cs` | `void Process()` → `bool Process()` |
| `src/AdocNet.Core/Extensions/IInlineProcessor.cs` | `void Process()` → `bool Process()` |

### Pipeline (1 file)

| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/ProcessingPipeline.cs` | Check bool return at 3 call sites, break on true |

### Built-in processors (4 files)

| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/DocumentMetadataProcessor.cs` | `void` → `bool`, `return false;` |
| `src/AdocNet.Core/Extensions/AutoIdBlockProcessor.cs` | `void` → `bool`, `return false;` |
| `src/AdocNet.Core/Extensions/DiagramBlockProcessor.cs` | `void` → `bool`, `return false;` (or `true` after replace) |
| `src/AdocNet.Core/Extensions/IconMacroProcessor.cs` | `void` → `bool`, `return false;` (or `true` after replace) |

### ExtensionLoader (1 file, new class)

| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/ExtensionLoader.cs` | `#if NET6_0_OR_GREATER` use AssemblyLoadContext |
| `src/AdocNet.Core/Extensions/ExtensionLoadContext.cs` | **NEW** — `ExtensionLoadContext : AssemblyLoadContext` |

### Hot-reload (new files)

| File | Change |
|------|--------|
| `src/AdocNet.Core/AdocEngine.cs` | `EnableHotReload` property, FileSystemWatcher integration |
| `src/AdocNet.Core/Extensions/ExtensionHotReloader.cs` | **NEW** — FileSystemWatcher + debounce + context reload |

### Test extension processors (4 files)

| File | Change |
|------|--------|
| `tests/AdocNet.TestExtension/TestDocumentProcessor.cs` | `void` → `bool`, `return false;` |
| `tests/AdocNet.TestExtension/TestPrefixBlockProcessor.cs` | `void` → `bool`, `return false;` |
| `tests/AdocNet.TestExtension/TestInlineProcessor.cs` | `void` → `bool`, `return false;` |
| `tests/AdocNet.TestExtension/NoCtorProcessor.cs` | `void` → `bool`, `return false;` |

### Test mock processors (8 files, 22 classes)

| File | # of classes to update |
|------|----------------------|
| `tests/AdocNet.Tests/ExtensionRegistrationTests.cs` | 3 |
| `tests/AdocNet.Tests/PipelineExecutionTests.cs` | 6 |
| `tests/AdocNet.Tests/ExtensionPriorityTests.cs` | 3 |
| `tests/AdocNet.Tests/ExtensionCapabilitiesTests.cs` | 4 |
| `tests/AdocNet.Tests/ExtensionLifecycleTests.cs` | 1 |
| `tests/AdocNet.Tests/ExtensionIntegrationTests.cs` | 1 |
| `tests/AdocNet.Tests/ExtensionDiagnosticsTests.cs` | 1 |
| `tests/AdocNet.Tests/Extensions/HardeningTests.cs` | 3 |
| `tests/AdocNet.Tests/Extensions/ExtensionCommandTests.cs` | 1 |

---

## 10. Summary Counts

| Category | Count |
|----------|-------|
| Interface files to change | 3 |
| ProcessingPipeline call sites | 3 |
| Built-in processor files | 4 |
| Test extension processor files | 4 |
| Test mock processor classes | 23 (across 9 files) |
| New source files | 2 (ExtensionLoadContext, ExtensionHotReloader) |
| Modified source files | ~8 |
| Modified test files | ~9 |
| **Total files touched** | **~23** |

---

## 11. Existing Test Count

- Current test count: **1142 tests** (NUnit `[Test]`)
- All must continue passing after the migration
- New tests needed for: bool return short-circuiting, AssemblyLoadContext isolation, hot-reload

---

## 12. Documentation Files to Update

| File | Update needed |
|------|---------------|
| `docs/EXTENSIONS.md` | Interface signatures, bool return semantics |
| `docs/DYNAMIC_EXTENSIONS.md` | AssemblyLoadContext isolation |
| `docs/EXTENSION_SAFETY.md` | Hot-reload, isolation |
| `CHANGELOG.md` | Beta.13 entry |
