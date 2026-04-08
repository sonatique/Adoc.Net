# Extension Safety Guide

AdocNet v1.0.0-beta.9 adds production hardening to the extension system:
per-extension state tracking, failure-based disabling, API version compatibility,
structured load reporting, and CLI diagnostics.

## Extension States

Every extension in the system is in one of four states:

| State | Meaning |
|-------|---------|
| `Loaded` | Extension loaded successfully; all processors are active. |
| `Failed` | Extension failed to load (bad assembly, missing constructor, etc.). |
| `Disabled` | Extension was disabled at runtime due to repeated processor failures. |
| `Incompatible` | Extension skipped because its API version is incompatible with the host. |

`Failed` and `Incompatible` are determined at load time and are terminal.
`Disabled` is reached only at runtime when a processor exceeds the failure threshold.

## Failure-Based Disabling

When a processor throws an exception during rendering, the pipeline catches it,
emits a warning, and continues. In beta.9, the engine also counts consecutive
failures per processor. When the count reaches the threshold, the processor is
permanently disabled for the lifetime of that engine instance.

### Configuration

```csharp
var engine = new AdocEngine(renderer, parser);

// Default: disable after 3 consecutive failures
engine.MaxProcessorFailures = 3;

// Disable after the very first failure
engine.MaxProcessorFailures = 1;

// Never disable (beta.8 behavior — warn and continue forever)
engine.MaxProcessorFailures = 0;
```

### How It Works

1. A processor throws during `Process()` — the failure counter increments.
2. The same processor succeeds on the next invocation — the counter resets to 0.
3. The counter reaches `MaxProcessorFailures` — the processor is added to the
   disabled set, and one warning is emitted: `"Processor X disabled after N consecutive failure(s)"`.
4. On subsequent renders, the disabled processor is silently skipped.
5. Other processors are unaffected — each has its own independent counter.

### Key Properties

- **Per-engine-instance**: Failure counters are stored on the `AdocEngine` instance,
  not as static state. Each engine tracks its own processors independently.
- **Persists across Convert() calls**: The counters survive multiple `Convert()` calls
  on the same engine instance. A processor disabled in the 3rd render stays disabled
  in the 4th, 5th, etc.
- **Resets on success**: A single successful invocation resets the counter to 0.
  Only *consecutive* failures trigger disabling.
- **Permanent once disabled**: A disabled processor cannot be re-enabled. Create a
  new engine instance if you need to retry.

## API Version Compatibility

Extensions can declare which API version they require in their `extension.json` manifest:

```json
{
  "name": "my-extension",
  "version": "1.0.0",
  "entry": "MyExtension.dll",
  "apiVersion": "1.0"
}
```

The `apiVersion` field uses `major.minor` format. Compatibility rules:
- Extension major **must equal** host major.
- Extension minor **must be <=** host minor.
- If `apiVersion` is omitted, the extension is assumed compatible (backward compatible
  with pre-beta.9 extensions).

The current host API version is available as `AdocEngine.ExtensionApiVersion`.

## Structured Load Results (Safe Loading)

The new `LoadExtensionSafe()` and `LoadExtensionsSafe()` methods return structured
results instead of silently swallowing errors:

```csharp
var engine = new AdocEngine(renderer, parser);

// Load with structured feedback
IReadOnlyList<ExtensionLoadResult> results = engine.LoadExtensionSafe("path/to/ext.dll");

foreach (var result in results)
{
    Console.WriteLine($"{result.Name}: {result.State}");
    if (result.FailureReason is not null)
        Console.WriteLine($"  Reason: {result.FailureReason}");
    Console.WriteLine($"  Processors: {result.Processors.Count}");
}
```

### ExtensionLoadResult Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Extension name (from `IExtension.Name` or assembly filename) |
| `State` | `ExtensionState` | Load result: `Loaded`, `Failed`, or `Incompatible` |
| `FailureReason` | `string?` | Human-readable failure reason, null on success |
| `Processors` | `IReadOnlyList<object>` | Loaded processor instances, empty on failure |

### Existing Methods Unchanged

The original `LoadExtension()` and `LoadExtensions()` methods still work exactly
as before — fluent API returning `AdocEngine`. Use them when you don't need
structured feedback.

## CLI: `ext status`

The `ext status` command shows the load state of all installed extensions:

```bash
adocnet ext status
```

Output:

```
Extension status (~/.adocnet/extensions/):

  Name           Version  State         Reason
  -------------  -------  ------------  ----------------------
  diagram        1.2.0    Loaded        3 processor(s)
  my-icons       0.5.0    Failed        No parameterless constructor
  future-ext     2.0.0    Incompatible  Requires API version 2.0

3 extension(s): 1 loaded, 1 failed, 1 incompatible.
```

For each extension, `ext status`:
1. Reads the `extension.json` manifest.
2. Checks API version and runtime version compatibility.
3. Attempts to load the entry DLL and discover processors.
4. Reports the result.

Note: `ext status` cannot show the `Disabled` state because disabling is a runtime
phenomenon that occurs during rendering. Use `ext status` for pre-flight checks.

## AssemblyLoadContext Isolation (beta.13, net6.0+)

On .NET 6.0 and later, each extension DLL is loaded in its own `AssemblyLoadContext`.
This prevents version conflicts between extensions that depend on different versions
of the same third-party library.

- Each extension gets an `ExtensionLoadContext` (collectible, enabling unloading)
- Dependencies are resolved from the extension's directory first, then from the host
- Host assemblies (AdocNet.Core, runtime libraries) are never duplicated
- On netstandard2.0: `Assembly.LoadFrom()` is used (no isolation)

## Hot-Reload (beta.13, net6.0+)

The engine can watch extension directories for DLL changes and automatically reload:

```csharp
var engine = new AdocEngine(renderer, parser);
engine.EnableHotReload = true;
engine.LoadExtensions("./my-extensions/");
```

When a DLL is modified, created, or deleted:
1. A 500ms debounce waits for writes to complete
2. Old extension contexts are unloaded
3. New DLLs are loaded in fresh isolated contexts
4. Processor lists are rebuilt and caches cleared
5. `OnWarning` fires with a reload notification

**Limitations:**
- Hot-reload requires .NET 6.0+ (setting `EnableHotReload = true` on ns2.0 throws `NotSupportedException`)
- All caches are cleared on reload (parse + render + persistent)
- `Shutdown()` stops all file watchers and unloads extension contexts

## Best Practices

1. **Set `MaxProcessorFailures` explicitly** in production applications. The default
   of 3 is reasonable, but your use case may warrant a different threshold.

2. **Use `LoadExtensionSafe()` for user-facing tools** that need to report extension
   load failures clearly. Use the original `LoadExtension()` for internal pipelines
   where `OnWarning` is sufficient.

3. **Declare `apiVersion`** in your `extension.json` if your extension depends on
   specific processor interfaces or `RenderContext` features. This prevents confusing
   runtime errors when the host is too old.

4. **Wire `OnWarning`** to your logging system. Even with structured load results and
   failure disabling, runtime warnings from processors provide valuable diagnostic
   information.

5. **Run `adocnet ext status`** after installing extensions to verify they load
   correctly before using them in production.

## See Also

- [Extension Developer Guide](EXTENSIONS.md) — writing custom renderers and processors
- [Dynamic Extensions Guide](DYNAMIC_EXTENSIONS.md) — loading extensions from external DLLs
- [Extension Packaging Guide](EXTENSION_PACKAGING.md) — manifest-based packaging
- [Extension Registry Guide](EXTENSION_REGISTRY.md) — registry, search, and dependencies
