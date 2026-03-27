# Beta.6 Design — Dynamic Extension Loading

> Design document for AdocNet v1.0.0-beta.6.
> This phase adds runtime loading of extensions from external DLLs.

## 1. Extension Discovery

Extension discovery scans a loaded assembly for concrete types implementing one or more
of the three processor interfaces: `IDocumentProcessor`, `IBlockProcessor`, `IInlineProcessor`.

### Reflection Approach

```csharp
var assembly = Assembly.LoadFrom(dllPath);
var types = assembly.GetExportedTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface)
    .Where(t => IsProcessorType(t))
    .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
    .OrderBy(t => t.FullName);
```

`GetExportedTypes()` returns only `public` types — internal types are intentionally excluded
since they are not part of the extension's public contract.

### Multiple Interface Implementation

A single type may implement more than one processor interface. For example:

```csharp
public class MyExtension : IDocumentProcessor, IBlockProcessor { ... }
```

The loader checks each interface independently and registers the instance once per
interface it implements. One object instance is created; it is registered into multiple
processor lists. Registration order within a type follows: Document → Block → Inline.

### No Parameterless Constructor

Types without a parameterless constructor are skipped. A warning is emitted via
`OnWarning`: `"Skipping type {FullName}: no parameterless constructor."` These types
are intended for manual registration via the existing `Register*` methods where the
caller can provide constructor arguments.

## 2. Loading Model

### Assembly.LoadFrom

`Assembly.LoadFrom(string path)` is the loading mechanism. Chosen because:

- Available on `netstandard2.0` (in `mscorlib` / `System.Runtime`) — no additional NuGet packages
- Works on .NET Framework 4.6.1+, .NET Core 2.0+, and .NET 10
- Simpler than `AssemblyLoadContext` which requires `System.Runtime.Loader` (netcoreapp only)

**Trade-off**: No assembly isolation. Extensions load into the default AppDomain/context.
This is acceptable for a simple extension model without sandboxing requirements.

### Single DLL Loading

```csharp
engine.LoadExtension("path/to/MyExtension.dll");
```

Loads one assembly, discovers extension types, instantiates, and registers them.

### Directory Loading

```csharp
engine.LoadExtensions("extensions/");
```

Enumerates all `*.dll` files in the directory (non-recursive), sorts alphabetically by
filename, and loads each one via `LoadExtension`. Subdirectories are ignored.

### Transitive Dependencies

Loaded assemblies must resolve their own dependencies. The CLR probes:

1. The directory containing the loaded assembly (Assembly.LoadFrom's probing behavior)
2. The application base directory
3. The GAC (on .NET Framework)

Extension authors are responsible for shipping their dependencies alongside their DLL.
If a dependency cannot be resolved, the assembly is skipped with a warning.

## 3. Deterministic Load Order

Determinism is a core project requirement. The loading system guarantees identical
registration order across platforms and runs:

1. **Directory level**: DLLs are sorted by `Path.GetFileName(path)` using
   `StringComparer.Ordinal` (byte-level, culture-independent)
2. **Assembly level**: Types within each assembly are sorted by `Type.FullName` using
   `StringComparer.Ordinal`
3. **Interface level**: For types implementing multiple interfaces, registration order
   is fixed: `IDocumentProcessor` → `IBlockProcessor` → `IInlineProcessor`

This ensures that given the same set of DLLs, the same processor registration order
is produced on Windows, Linux, and macOS.

## 4. IExtension Metadata

### Optional Interface

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Optional metadata interface for extensions loaded dynamically.
/// Extensions that implement this interface provide a name and version for
/// identification in warnings, logging, and diagnostic output.
/// Extensions that do not implement this interface are still loaded —
/// metadata defaults to the type name and "0.0.0".
/// </summary>
public interface IExtension
{
    /// <summary>Gets a human-readable name for the extension.</summary>
    string Name { get; }

    /// <summary>Gets the version string (e.g., "1.0.0").</summary>
    string Version { get; }
}
```

### Behavior

- `IExtension` is **optional** — types are discovered by their processor interface, not by `IExtension`
- If a type also implements `IExtension`, its `Name` and `Version` are used in log/warning messages
- If not, defaults: `Name = type.Name`, `Version = "0.0.0"`
- `IExtension` lives in `src/AdocNet.Core/Extensions/IExtension.cs`
- A type implementing only `IExtension` (without any processor interface) is ignored

### Example

```csharp
public class MyProcessor : IBlockProcessor, IExtension
{
    public string Name => "My Custom Processor";
    public string Version => "1.2.0";

    public bool CanProcess(BlockNode node) => ...;
    public void Process(BlockNode node, RenderContext context) => ...;
}
```

## 5. Error Handling

All errors go through the `OnWarning` callback on `AdocEngine`. The loader never throws
exceptions to the caller — it skips problematic assemblies/types and continues.

| Scenario | Exception Caught | Warning Message |
|----------|-----------------|-----------------|
| File not found | `FileNotFoundException` | `"Extension not found: {path}"` |
| Not a .NET assembly | `BadImageFormatException` | `"Not a valid .NET assembly: {path}"` |
| Missing dependency | `ReflectionTypeLoadException` | `"Failed to load types from {path}: {details}"` |
| Missing dependency (load) | `FileNotFoundException` | `"Failed to load assembly {path}: {message}"` |
| No parameterless ctor | (detected, not exception) | `"Skipping {FullName}: no parameterless constructor"` |
| Constructor throws | Any `Exception` | `"Failed to instantiate {FullName}: {message}"` |
| Empty directory | (no DLLs found) | `"No extension DLLs found in: {path}"` |
| Directory not found | `DirectoryNotFoundException` | `"Extension directory not found: {path}"` |

### ReflectionTypeLoadException Handling

`Assembly.GetExportedTypes()` may throw `ReflectionTypeLoadException` when some types
cannot be loaded (e.g., missing dependency for one type but not others). The loader
catches this and uses the `Types` property to get the successfully loaded types:

```csharp
Type[] types;
try
{
    types = assembly.GetExportedTypes();
}
catch (ReflectionTypeLoadException ex)
{
    onWarning?.Invoke($"Partial load of {path}: {ex.LoaderExceptions.Length} type(s) failed");
    types = ex.Types.Where(t => t is not null).ToArray()!;
}
```

## 6. Engine Integration

### New Public Methods on AdocEngine

```csharp
/// <summary>
/// Loads extensions from a single assembly file. Discovers types implementing
/// IDocumentProcessor, IBlockProcessor, or IInlineProcessor with parameterless
/// constructors, instantiates them, and registers them.
/// Must be called before the first Convert() call.
/// </summary>
public AdocEngine LoadExtension(string assemblyPath);

/// <summary>
/// Loads extensions from all *.dll files in the specified directory.
/// DLLs are loaded in alphabetical order by filename.
/// Must be called before the first Convert() call.
/// </summary>
public AdocEngine LoadExtensions(string directoryPath);
```

Both methods:
- Call `ThrowIfFrozen()` — cannot load after first `Convert()`
- Return `this` for fluent chaining (consistent with `Register*` methods)
- Delegate to `ExtensionLoader` for the actual discovery and instantiation
- Route warnings through `OnWarning`

### ExtensionLoader (internal static class)

```csharp
// src/AdocNet.Core/Extensions/ExtensionLoader.cs
internal static class ExtensionLoader
{
    internal static List<object> LoadFromAssembly(string path, Action<string>? onWarning);
    internal static List<object> LoadFromDirectory(string path, Action<string>? onWarning);
}
```

Returns a flat list of instantiated processor objects. The engine then inspects each
object and calls the appropriate `Register*` method based on which interfaces it implements.

### Registration Flow

```
LoadExtension("ext.dll")
  → ThrowIfFrozen()
  → ExtensionLoader.LoadFromAssembly("ext.dll", OnWarning)
  → For each instance:
      if (instance is IDocumentProcessor dp) RegisterDocumentProcessor(dp)
      if (instance is IBlockProcessor bp)    RegisterBlockProcessor(bp)
      if (instance is IInlineProcessor ip)   RegisterInlineProcessor(ip)
```

## 7. CLI Integration

### New Flags

| Flag | Argument | Description |
|------|----------|-------------|
| `--extensions` | `<path>` | Load extensions from a DLL or directory. Repeatable. |
| `--extension-dir` | `<dir>` | Load all `*.dll` extensions from directory. Repeatable. |

Both flags can be used together and repeated:

```bash
adocnet doc.adoc --extensions my-ext.dll --extensions other.dll --extension-dir ./plugins/
```

### Argument Parsing

Added to `ParseArguments` in the same pattern as `-a`/`--attribute`:

```csharp
if (arg is "--extensions")
{
    if (i + 1 >= args.Length)
        return new CliArgs.Error("Option --extensions requires a path.");
    extensionPaths ??= new List<string>();
    extensionPaths.Add(args[++i]);
    continue;
}

if (arg is "--extension-dir")
{
    if (i + 1 >= args.Length)
        return new CliArgs.Error("Option --extension-dir requires a directory path.");
    extensionDirs ??= new List<string>();
    extensionDirs.Add(args[++i]);
    continue;
}
```

### CliArgs.Run Extension

```csharp
internal sealed record Run(
    // ... existing parameters ...
    IReadOnlyList<string>? ExtensionPaths = null,
    IReadOnlyList<string>? ExtensionDirs = null) : CliArgs;
```

### Execute Integration

In `Execute(CliArgs.Run)`, after creating the engine and before `Convert()`:

```csharp
if (run.ExtensionPaths is { Count: > 0 })
    foreach (var path in run.ExtensionPaths)
        engine.LoadExtension(path);

if (run.ExtensionDirs is { Count: > 0 })
    foreach (var dir in run.ExtensionDirs)
        engine.LoadExtensions(dir);
```

### Help Text Addition

```
  --extensions <path>     Load extensions from a DLL file (repeatable)
  --extension-dir <dir>   Load all extension DLLs from directory (repeatable)
```

## 8. Testing Strategy

### Test Extension DLL Project

Create a separate project `tests/AdocNet.TestExtension/` that produces a DLL containing
simple test processors:

```
tests/AdocNet.TestExtension/
  AdocNet.TestExtension.csproj   — class library targeting net10.0
  UpperCaseProcessor.cs          — IBlockProcessor, parameterless ctor
  TestDocProcessor.cs            — IDocumentProcessor, parameterless ctor
  TestInlineProcessor.cs         — IInlineProcessor, parameterless ctor
  MetadataExtension.cs           — IBlockProcessor + IExtension, parameterless ctor
  NoCtorProcessor.cs             — IBlockProcessor, NO parameterless ctor (for skip test)
```

This project references `AdocNet.Core` to get the processor interfaces.

### Test Cases (in `tests/AdocNet.Tests/`)

| Test | Scenario | Expected |
|------|----------|----------|
| LoadExtension_ValidDll | Load test extension DLL | Processors discovered and registered |
| LoadExtension_MissingFile | Path to non-existent DLL | Warning emitted, no crash |
| LoadExtension_NotDotNet | Path to a non-.NET file | `BadImageFormatException` caught, warning, skip |
| LoadExtension_NoProcessors | DLL with no processor types | Empty result, no crash |
| LoadExtension_NoParamlessCtor | Type without parameterless ctor | Type skipped, warning emitted |
| LoadExtension_MultipleInterfaces | Type implementing 2+ interfaces | Registered for each interface |
| LoadExtension_IExtensionMetadata | Type implementing IExtension | Name/Version used in log |
| LoadExtensions_Directory | Directory with multiple DLLs | All loaded, alphabetical order |
| LoadExtensions_EmptyDirectory | Directory with no DLLs | Warning, no crash |
| LoadExtensions_MissingDirectory | Non-existent directory | Warning, no crash |
| LoadExtension_AfterConvert | Load after Convert() called | `InvalidOperationException` thrown |
| LoadExtension_EndToEnd | Load, convert, verify output | Extension modifies AST, rendered output reflects change |

### Build Integration

The test extension DLL must be built before tests run. Use a project dependency:

```xml
<!-- tests/AdocNet.Tests/AdocNet.Tests.csproj -->
<ProjectReference Include="..\AdocNet.TestExtension\AdocNet.TestExtension.csproj"
                  ReferenceOutputAssembly="false" />
```

`ReferenceOutputAssembly="false"` ensures the DLL is built but not added as a compile-time
reference — the test project loads it dynamically at runtime via `Assembly.LoadFrom`.

## 9. Explicit Non-Goals

The following are explicitly out of scope for beta.6:

| Non-Goal | Reason |
|----------|--------|
| `AssemblyLoadContext` / isolation | Not available on `netstandard2.0`; simplicity over isolation |
| Plugin dependency resolution | Extensions ship their own deps; no NuGet restore at runtime |
| Plugin lifecycle (init/dispose) | Extensions are stateless or use `RenderContext`; no lifecycle hooks |
| Hot-reloading | Registration is frozen after first `Convert()`; restart to reload |
| Extension marketplace / registry | Out of scope; extensions are local DLLs |
| Sandboxing / security | Extensions run with full trust in the host process |
| Configuration per-extension | Extensions are stateless via constructor; use document attributes for config |
| Recursive directory scanning | Only top-level `*.dll` in specified directories |
| NuGet package loading | Extensions must be pre-built DLLs, not package references |

## 10. File Manifest

### New Files

| File | Purpose |
|------|---------|
| `src/AdocNet.Core/Extensions/IExtension.cs` | Optional metadata interface |
| `src/AdocNet.Core/Extensions/ExtensionLoader.cs` | Assembly scanning + instantiation |
| `tests/AdocNet.TestExtension/AdocNet.TestExtension.csproj` | Test extension DLL project |
| `tests/AdocNet.TestExtension/*.cs` | Test processor implementations |
| `tests/AdocNet.Tests/Extensions/ExtensionLoaderTests.cs` | Loader unit tests |
| `tests/AdocNet.Tests/Extensions/EngineExtensionTests.cs` | Engine integration tests |

### Modified Files

| File | Change |
|------|--------|
| `src/AdocNet.Core/AdocEngine.cs` | Add `LoadExtension()`, `LoadExtensions()` methods |
| `src/AdocNet.Cli/Program.cs` | Add `--extensions`, `--extension-dir` flags + wiring |
| `AdocNet.slnx` | Add `AdocNet.TestExtension` project |
| `Directory.Build.props` | Update version to `1.0.0-beta.6` |
| `docs/EXTENSIONS.md` | Add dynamic loading section |
