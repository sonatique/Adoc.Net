# Beta.9 Hardening Design Document

Version: 1.0.0-beta.9
Date: 2026-03-31
Prerequisite: beta.8 merged and stable.
Reference: `docs/SAFETY-AUDIT-BETA9.md`

---

## 1. Existing Error Handling Inventory

The safety audit (P00) catalogued 28 distinct warning-producing paths across the
extension system. All use `Action<string>?` — no structured types.

### Load-Time (ExtensionLoader.cs)

4 catch blocks handle assembly loading failures:

| Exception | Recovery |
|-----------|----------|
| `BadImageFormatException` | Warning, return empty |
| `FileNotFoundException` | Warning, return empty |
| `ReflectionTypeLoadException` | Warning, continue with loadable types |
| `Exception` (Activator.CreateInstance) | Unwrap TargetInvocationException, warning, skip |

Plus 4 conditional paths: file-not-exists, no-parameterless-ctor, dir-not-exists, no-DLLs.

### Runtime (ProcessingPipeline.cs)

3 try/catch blocks, one per processor phase (document, block, inline).
Each catches `Exception`, produces a string warning, and continues.
No failure counting. No disabling. No per-processor state.

### Directory Loading (ExtensionDirectoryLoader.cs)

3 conditional warning paths: manifest null, version incompatible, entry DLL missing.
Version checking uses `IsVersionCompatible()` with semver prerelease support.

### Dependency Validation (DependencyValidator.cs)

Warn-only. Missing dependency or version mismatch produce warnings but never block.
Unchanged in beta.9.

### Registry (ExtensionRegistry.cs)

5 error paths. Missing/corrupt registry triggers rebuild from filesystem.
Atomic save with temp-file+rename. Registry is a cache — filesystem is truth.

### Key Observation

Every failure path produces a string warning. The caller receives no structured data
about what loaded, what failed, or why. Beta.9 adds that structure.

---

## 2. ExtensionState Enum

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Describes the state of an extension after a load attempt.
/// </summary>
public enum ExtensionState
{
    /// <summary>Extension loaded successfully; all processors instantiated and registered.</summary>
    Loaded,

    /// <summary>Extension failed to load (bad assembly, missing constructor, instantiation error).</summary>
    Failed,

    /// <summary>Extension was disabled due to repeated runtime failures (exceeded MaxProcessorFailures).</summary>
    Disabled,

    /// <summary>Extension skipped because its required API version is incompatible with the host.</summary>
    Incompatible
}
```

### State Transitions

```
                  ┌──────────┐
   Load success → │  Loaded  │ ──(runtime failures exceed threshold)──→ Disabled
                  └──────────┘
   Load failure → │  Failed  │   (terminal — no retry)
                  └──────────┘
   API mismatch → │Incompatible│  (terminal — requires upgrade)
                  └────────────┘
```

- `Loaded` is the only state where processors are active.
- `Disabled` is reached only via failure-based disabling at runtime (see Section 4).
- `Failed` and `Incompatible` are determined at load time and are terminal.
- There is no `Enabled`/`Disabled` toggle — beta.9 does not add user-controlled enable/disable.

### File Location

`src/AdocNet.Core/Extensions/ExtensionState.cs` — new file, ~20 lines.

---

## 3. ExtensionLoadResult

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Structured result of an extension load attempt. Returned by safe loading methods.
/// </summary>
public sealed class ExtensionLoadResult
{
    /// <summary>Gets the extension name (from IExtension.Name, type name, or assembly name).</summary>
    public string Name { get; }

    /// <summary>Gets the state after the load attempt.</summary>
    public ExtensionState State { get; }

    /// <summary>Gets the failure reason, or null if the extension loaded successfully.</summary>
    public string? FailureReason { get; }

    /// <summary>Gets the list of processor instances loaded from this extension.</summary>
    public IReadOnlyList<object> Processors { get; }

    public ExtensionLoadResult(string name, ExtensionState state, string? failureReason,
        IReadOnlyList<object>? processors)
    {
        Name = name;
        State = state;
        FailureReason = failureReason;
        Processors = processors ?? Array.Empty<object>();
    }
}
```

### Name Resolution

The `Name` property is resolved as follows (in priority order):
1. If any loaded processor implements `IExtension`, use `IExtension.Name`.
2. Otherwise, use the assembly file name without extension (e.g., "MyExtension").
3. For incompatible extensions (not loaded), use the manifest name if available,
   or the assembly file name.

### Relationship to Existing `List<object>` Return

The existing `ExtensionLoader.LoadAssembly()` continues to return `List<object>`.
A new internal method `ExtensionLoader.LoadAssemblySafe()` wraps it and returns
`ExtensionLoadResult`. This keeps the existing public API unchanged.

### File Location

`src/AdocNet.Core/Extensions/ExtensionLoadResult.cs` — new file, ~40 lines.

---

## 4. Failure-Based Disabling

### Problem

A buggy processor that throws on every node produces O(n) warnings per render.
There is no circuit-breaker. The pipeline dutifully catches and warns for every node.

### Design

The failure counter lives on `AdocEngine`, not on the pipeline or the processor.
This ensures per-engine-instance scoping with no static mutable state.

```csharp
// On AdocEngine:
private readonly Dictionary<object, int> _failureCounts = new();
private readonly HashSet<object> _disabledProcessors = new();

/// <summary>
/// Maximum consecutive failures before a processor is disabled for this engine's lifetime.
/// Default: 3. Set to 0 to never disable (beta.8 behavior).
/// </summary>
public int MaxProcessorFailures { get; set; } = 3;
```

### Counter Semantics

- **Key**: the processor instance (object reference). Identity comparison via default
  `Dictionary<object, int>` which uses `ReferenceEquals` for object keys.
- **Increment**: on each `catch (Exception)` in ProcessingPipeline.
- **Reset**: on each successful invocation (no exception) of the same processor.
  "Success" means the call to `Process()` completed without throwing.
- **Threshold check**: before invoking a processor, check `_disabledProcessors`.
  If present, skip silently (no warning per invocation — one warning at disable time).
- **Disable**: when `_failureCounts[processor] >= MaxProcessorFailures` (and MaxProcessorFailures > 0),
  add to `_disabledProcessors`, emit one warning:
  `"Processor {Name} disabled after {N} consecutive failures"`.
- **Permanent**: once in `_disabledProcessors`, stays there for the engine's lifetime.
  No re-enable mechanism in beta.9.

### Pipeline Changes

`ProcessingPipeline.Run()` gains two additional parameters:

```csharp
internal static void Run(
    DocumentNode document,
    RenderContext context,
    IReadOnlyList<IDocumentProcessor> documentProcessors,
    IReadOnlyList<IBlockProcessor> blockProcessors,
    IReadOnlyList<IInlineProcessor> inlineProcessors,
    Action<string>? onWarning,
    // NEW beta.9 parameters:
    Dictionary<object, int> failureCounts,
    HashSet<object> disabledProcessors,
    int maxFailures)
```

The pipeline is `internal static` — adding parameters is not a public API break.

Each processor invocation follows this pattern:

```
if (disabledProcessors.Contains(processor)) → skip
try { processor.Process(...) } → on success, reset failureCounts[processor] to 0
catch → increment failureCounts[processor], check threshold, disable if exceeded
```

### MaxProcessorFailures = 0

When `MaxProcessorFailures` is 0, the pipeline never disables processors.
The failure counter dictionary is still updated (no conditional logic at the increment
site), but the threshold check `count >= MaxProcessorFailures` is skipped when
`MaxProcessorFailures == 0`. This preserves exact beta.8 behavior (warn + continue).

### Thread Safety

`_failureCounts` and `_disabledProcessors` are per-engine-instance.
`AdocEngine.Convert()` is not thread-safe today (no concurrent calls documented),
so no additional synchronization is needed. If concurrent rendering is added later,
these collections would need `ConcurrentDictionary` — but that's out of scope.

---

## 5. API Version Compatibility

### Concept

The extension API (the interfaces `IDocumentProcessor`, `IBlockProcessor`, `IInlineProcessor`,
and `RenderContext`) has an implicit version. Beta.9 makes it explicit.

### Host-Side Constant

```csharp
// On AdocEngine:
/// <summary>
/// The extension API version supported by this build.
/// Extensions declare their required API version in the manifest.
/// </summary>
public const string ExtensionApiVersion = "1.0";
```

The API version follows `major.minor` format:
- **Major**: breaking changes to processor interfaces (new required methods, changed signatures).
- **Minor**: additive changes (new optional interfaces, new RenderContext features).

An extension is compatible if its declared `apiVersion` major matches the host's major
and its minor is <= the host's minor.

### Manifest Field

```json
{
  "name": "my-extension",
  "version": "1.0.0",
  "entry": "MyExtension.dll",
  "apiVersion": "1.0"
}
```

`apiVersion` is **optional**. If omitted, the extension is assumed compatible
(backwards-compatible with pre-beta.9 extensions that have no apiVersion field).

### Compatibility Check

```csharp
internal static bool IsApiVersionCompatible(string hostApiVersion, string? extensionApiVersion)
{
    if (extensionApiVersion is null)
        return true; // pre-beta.9 extension, no declaration

    // Parse "major.minor"
    // Compatible if: ext.major == host.major && ext.minor <= host.minor
}
```

Location: `ExtensionDirectoryLoader.cs` alongside existing `IsVersionCompatible()`.

### Integration with ExtensionLoadResult

When API version check fails:
- `ExtensionLoadResult.State = ExtensionState.Incompatible`
- `ExtensionLoadResult.FailureReason = "Extension requires API version X.Y, host supports X.Y"`
- Processors are NOT loaded.
- Warning is emitted via OnWarning.

### When the Check Runs

The API version check runs in the safe loading methods (`LoadExtensionSafe`,
`LoadExtensionsSafe`) during manifest-based loading. For raw assembly loading
(no manifest), there is no API version check — the extension has no way to declare it.

---

## 6. Safe Loading Methods

### New Methods on AdocEngine

```csharp
/// <summary>
/// Loads extensions from a single assembly, returning structured results.
/// Still registers successfully loaded processors into the engine.
/// </summary>
public IReadOnlyList<ExtensionLoadResult> LoadExtensionSafe(string assemblyPath)

/// <summary>
/// Loads extensions from all DLLs in a directory, returning structured results.
/// Still registers successfully loaded processors into the engine.
/// </summary>
public IReadOnlyList<ExtensionLoadResult> LoadExtensionsSafe(string directoryPath)
```

### Behavior

1. Call existing `ExtensionLoader.LoadAssembly()` / `LoadDirectory()`, capturing warnings.
2. Build `ExtensionLoadResult` per assembly:
   - If processors loaded: `State = Loaded`, `Processors = [instances]`.
   - If load failed (warning emitted, no processors): `State = Failed`, `FailureReason = warning text`.
3. Register loaded processors into the engine (same as existing Load methods).
4. Return the result list.

The safe methods still call `ThrowIfFrozen()` — they cannot be used after `Convert()`.

### Implementation Strategy

`ExtensionLoader` gains an internal `LoadAssemblySafe()` that returns
`ExtensionLoadResult` instead of `List<object>`. This avoids duplicating the
loading logic. The existing `LoadAssembly()` calls `LoadAssemblySafe()` internally
and extracts the processor list (preserving the existing public API).

Actually, simpler: create a wrapper in `AdocEngine` that:
1. Collects warnings from a dedicated list.
2. Calls existing `ExtensionLoader.LoadAssembly()`.
3. Builds `ExtensionLoadResult` from the returned processors and any warnings.
4. Registers processors.
5. Returns results.

This avoids modifying `ExtensionLoader` at all — the wrapper is purely additive.

**Decision: Wrapper approach.** ExtensionLoader stays unchanged.

### Existing Methods Unchanged

`LoadExtension(string)` and `LoadExtensions(string)` continue to return `AdocEngine`
(fluent). Their behavior is identical to beta.8.

---

## 7. CLI `ext status` Command

### Invocation

```
adocnet ext status [--extensions-dir <path>]
```

### Behavior

1. Determine the extensions directory (default `~/.adocnet/extensions/` or `--extensions-dir`).
2. Scan each subdirectory for `extension.json` manifests.
3. For each manifest:
   a. Check API version compatibility → may produce `Incompatible`.
   b. Check runtime version compatibility → may produce `Incompatible`.
   c. Attempt to load the entry DLL → may produce `Failed`.
   d. If load succeeds → `Loaded`.
4. Display a table:

```
Name           Version   State          Reason
─────────────  ────────  ─────────────  ────────────────────────
diagram        1.2.0     Loaded         3 processors
my-icons       0.5.0     Failed         No parameterless constructor
future-ext     2.0.0     Incompatible   Requires API version 2.0
```

### Implementation

`ext status` is a new case in `ExtensionCommands.cs`.
It uses `ExtensionDirectoryLoader.LoadInstalledExtensionsSafe()` (a new internal method
that mirrors `LoadInstalledExtensions()` but returns `List<ExtensionLoadResult>`).

Note: `ext status` does NOT show `Disabled` state because it doesn't run a render.
`Disabled` is a runtime state — it only appears after `Convert()` calls trigger
the failure counter. The CLI loads extensions but doesn't render, so it can only
show `Loaded`, `Failed`, or `Incompatible`.

### No Engine Instance

`ext status` does not create a full `AdocEngine`. It calls the loader directly
and builds results. This is a diagnostic command, not a rendering command.

---

## 8. Testing Strategy

### New Test File

`tests/AdocNet.Tests/Extensions/HardeningTests.cs`

### Test Categories

#### ExtensionState + ExtensionLoadResult (P02)

| Test | Description |
|------|-------------|
| ExtensionLoadResult_Loaded_HasProcessors | State=Loaded, Processors non-empty |
| ExtensionLoadResult_Failed_HasReason | State=Failed, FailureReason set |
| ExtensionLoadResult_Incompatible_NoProcessors | State=Incompatible, Processors empty |

#### Failure-Based Disabling (P03)

| Test | Description |
|------|-------------|
| Pipeline_ProcessorThrows_CountIncremented | After 1 throw, count=1, processor still active |
| Pipeline_ProcessorThrows3Times_Disabled | After 3 consecutive throws, processor disabled |
| Pipeline_ProcessorSucceeds_CountResets | Throw, succeed, throw → count=1, not disabled |
| Pipeline_DisabledProcessorSkipped | Once disabled, processor.Process() never called again |
| Pipeline_MaxFailures0_NeverDisables | MaxProcessorFailures=0, processor throws forever, never disabled |
| Pipeline_DisabledWarningEmittedOnce | Disable warning emitted exactly once per processor |

#### API Version (P02)

| Test | Description |
|------|-------------|
| IsApiVersionCompatible_SameVersion_True | "1.0" vs "1.0" → compatible |
| IsApiVersionCompatible_HigherMinor_True | Host "1.1" vs ext "1.0" → compatible |
| IsApiVersionCompatible_LowerMinor_False | Host "1.0" vs ext "1.1" → incompatible |
| IsApiVersionCompatible_DifferentMajor_False | Host "1.0" vs ext "2.0" → incompatible |
| IsApiVersionCompatible_NullExtVersion_True | No apiVersion → compatible (pre-beta.9) |

#### Safe Loading Methods (P03)

| Test | Description |
|------|-------------|
| LoadExtensionSafe_ValidDll_ReturnsLoadedResult | Result has State=Loaded, processors |
| LoadExtensionSafe_MissingDll_ReturnsFailedResult | Result has State=Failed, FailureReason |
| LoadExtensionSafe_InvalidDll_ReturnsFailedResult | Result has State=Failed |
| LoadExtensionsSafe_Directory_MultipleResults | One result per assembly |
| LoadExtensionSafe_StillRegistersProcessors | Processors registered in engine |
| LoadExtensionSafe_AfterConvert_Throws | ThrowIfFrozen still enforced |

#### CLI ext status (P04)

CLI tests are tricky (process invocation). Minimal:

| Test | Description |
|------|-------------|
| ExtStatus_NoExtensions_ShowsEmptyTable | No crash, empty output |
| ExtStatus_WithExtension_ShowsLoaded | Shows name, version, Loaded state |

### Test Extension Augmentation

The existing `AdocNet.TestExtension` project may need a processor that always throws
(for failure-disabling tests). Add `ThrowingBlockProcessor` to the test extension.

Alternatively, use inline test doubles in the test file (mock processors that throw).
**Decision: Use inline test doubles.** This avoids modifying the test extension DLL
and keeps failure tests self-contained.

---

## 9. Explicit Non-Goals

The following are explicitly **NOT** in scope for beta.9:

### Sandboxing

Extensions run in the same AppDomain/process. No isolation, no permission model,
no `AssemblyLoadContext` boundaries. This is a conscious decision — sandboxing adds
significant complexity and is not needed for a local CLI tool with user-installed extensions.

### Code Signing / Trust

No verification of extension authorship. No signature checking on DLLs.
Users are responsible for the extensions they install.

### Remote Registry

No network access, no downloads, no marketplace. `ext search` searches locally.
A remote registry may be added in a future release.

### Lifecycle Hooks

No `Initialize()` / `Dispose()` on extensions. Processors are stateless or use
`RenderContext.GetOrCreate<T>()`. Adding lifecycle hooks would require interface
changes (breaking the immutable boundary).

### User-Controlled Enable/Disable

No `ext disable <name>` / `ext enable <name>` commands. The only disable mechanism
is automatic failure-based disabling at runtime. User-controlled state is deferred
to a future release.

### Retry / Recovery

Once a processor is disabled, it stays disabled for the engine's lifetime.
No automatic retry, no recovery, no "try again after N successful renders."

### Logging Framework

No `ILogger` dependency. Warnings continue to use `Action<string>?`.
Structured logging integration is a future concern.

---

## Appendix A: File Inventory

| File | Action | Lines (est.) |
|------|--------|-------------|
| `src/AdocNet.Core/Extensions/ExtensionState.cs` | NEW | ~20 |
| `src/AdocNet.Core/Extensions/ExtensionLoadResult.cs` | NEW | ~40 |
| `src/AdocNet.Core/AdocEngine.cs` | MODIFY | +40 (new fields, Safe methods, MaxProcessorFailures) |
| `src/AdocNet.Core/Extensions/ProcessingPipeline.cs` | MODIFY | +30 (failure counting, disabled check) |
| `src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs` | MODIFY | +30 (API version check, Safe method) |
| `src/AdocNet.Core/Extensions/ExtensionManifest.cs` | MODIFY | +5 (apiVersion field) |
| `src/AdocNet.Cli/ExtensionCommands.cs` | MODIFY | +40 (ext status command) |
| `tests/AdocNet.Tests/Extensions/HardeningTests.cs` | NEW | ~250 |
| `docs/BETA9_HARDENING_DESIGN.md` | NEW (this file) | ~300 |

## Appendix B: Phase Mapping

| Phase | Features |
|-------|----------|
| P02 | ExtensionState enum, ExtensionLoadResult class, API version const + check |
| P03 | Failure-based disabling in pipeline, Safe loading methods on AdocEngine |
| P04 | CLI `ext status` command, additional tests |

## Appendix C: Backward Compatibility

| Existing API | Change in beta.9 |
|-------------|------------------|
| `AdocEngine.LoadExtension(string)` | Unchanged — returns `AdocEngine` |
| `AdocEngine.LoadExtensions(string)` | Unchanged — returns `AdocEngine` |
| `AdocEngine.LoadInstalledExtensions()` | Unchanged — returns `AdocEngine` |
| `AdocEngine.OnWarning` | Unchanged |
| `AdocEngine.Convert()` | Now tracks failure counts internally |
| `ProcessingPipeline.Run()` | Gains 3 new parameters (internal, not public) |
| `ExtensionLoader.LoadAssembly()` | Unchanged |
| `ExtensionManifest` properties | Additive: new `ApiVersion` property |
| Default behavior (MaxProcessorFailures=3) | **New default behavior** — processors can be disabled |

Note: `MaxProcessorFailures` defaults to 3, which means beta.9 has a **behavioral change**
compared to beta.8 for engines that don't explicitly set this property. This is intentional —
it's a safety improvement. Users who want exact beta.8 behavior can set `MaxProcessorFailures = 0`.
