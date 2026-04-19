# Beta.7 Context Discovery — Packaging Baseline

> Generated: 2026-03-29. Read-only discovery of the beta.6 codebase for beta.7 planning.

---

## 1. AdocEngine — Extension Loading API

**File**: `src/AdocNet.Core/AdocEngine.cs` (161 lines)

### Constructor
```csharp
public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
```

### Static Registration (beta.5)
```csharp
public AdocEngine RegisterDocumentProcessor(IDocumentProcessor processor)  // fluent, FIFO
public AdocEngine RegisterBlockProcessor(IBlockProcessor processor)        // fluent, FIFO
public AdocEngine RegisterInlineProcessor(IInlineProcessor processor)      // fluent, FIFO
```

### Dynamic Loading (beta.6)
```csharp
public AdocEngine LoadExtension(string assemblyPath)     // single DLL
public AdocEngine LoadExtensions(string directoryPath)   // all *.dll in directory
```

Both delegate to `ExtensionLoader` then call `RegisterExtensions()`.

### Freeze Behavior
- Private `_frozen` bool, starts `false`.
- `ThrowIfFrozen()` called by all Register* and Load* methods.
- Set to `true` on first `Convert()` call (only if extensions exist).
- After freeze: `InvalidOperationException("Cannot register processors after the first Convert() call.")`.

### Private Dispatcher
```csharp
private void RegisterExtensions(List<object> extensions)
```
Iterates instances, checks `is IDocumentProcessor`, `is IBlockProcessor`, `is IInlineProcessor` — a single object can implement multiple interfaces.

### Warning Callback
```csharp
public Action<string>? OnWarning { get; set; }
```
Passed to `ExtensionLoader` and `ProcessingPipeline`. Null = silently discard.

---

## 2. ExtensionLoader — Assembly Discovery

**File**: `src/AdocNet.Core/Extensions/ExtensionLoader.cs` (151 lines)

### Public API
```csharp
public static List<object> LoadAssembly(string assemblyPath, Action<string>? onWarning)
public static List<object> LoadDirectory(string directoryPath, Action<string>? onWarning)
```

### LoadAssembly Behavior
1. Resolve to absolute path via `Path.GetFullPath()`.
2. Check `File.Exists()` — if not, warn and return empty.
3. `Assembly.LoadFrom(fullPath)` — catch `BadImageFormatException` (not a .NET assembly), `FileNotFoundException`.
4. `assembly.GetExportedTypes()` — catch `ReflectionTypeLoadException` (partial load, use available types).
5. Filter: non-abstract, non-interface, implements a processor interface.
6. **Sort by `FullName` (StringComparer.Ordinal)** for deterministic order.
7. Check for parameterless constructor — skip with warning if missing.
8. `Activator.CreateInstance()` — catch `TargetInvocationException`, warn on failure.

### LoadDirectory Behavior
1. Resolve to absolute path.
2. Check `Directory.Exists()` — if not, warn and return empty.
3. `Directory.GetFiles(fullPath, "*.dll")`.
4. **Sort by filename (StringComparer.Ordinal)** for deterministic order.
5. Call `LoadAssembly()` for each DLL, aggregate results.

### Error Handling Pattern
All errors are non-fatal: warn via callback, skip the problematic item, continue.
Never throws (except `ArgumentNullException` on null inputs).

### Private Helpers
- `IsProcessorType(Type)` — checks assignability to `IDocumentProcessor`, `IBlockProcessor`, or `IInlineProcessor`.
- `GetExtensionDisplayName(Type)` — returns `type.Name` (does not instantiate to read IExtension).

---

## 3. IExtension — Optional Metadata Interface

**File**: `src/AdocNet.Core/Extensions/IExtension.cs` (17 lines)

```csharp
public interface IExtension
{
    string Name { get; }
    string Version { get; }
}
```

- **Optional**: extensions that don't implement it still load fine.
- Purpose: human-readable identification in warnings and diagnostics.
- Default for non-implementors: type name / "0.0.0".
- Currently NOT read by `ExtensionLoader.GetExtensionDisplayName()` (just uses `type.Name`).

---

## 4. CLI Argument Parser

**File**: `src/AdocNet.Cli/Program.cs` (437 lines)

### Entry Point
```csharp
public static int Run(string[] args, OutputFormat defaultFormat, string toolName)
```
Dispatches to `ParseArguments()` → `CliArgs` discriminated union.

### CliArgs Variants
```csharp
internal abstract record CliArgs
{
    internal sealed record Run(..., IReadOnlyList<string>? ExtensionPaths, IReadOnlyList<string>? ExtensionDirs) : CliArgs;
    internal sealed record ShowHelp() : CliArgs;
    internal sealed record Preview(...) : CliArgs;
    internal sealed record Error(string Message) : CliArgs;
}
```

### Extension Flags (beta.6)
- `--extensions <path>` — adds to `extensionPaths` list. Repeatable.
- `--extension-dir <dir>` — adds to `extensionDirs` list. Repeatable.

### How Extensions Are Used (ConvertCommand.cs:158-186)
```csharp
if (run.ExtensionPaths is { Count: > 0 } || run.ExtensionDirs is { Count: > 0 })
{
    var engine = new AdocEngine(renderer, _ => document);
    engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");
    LoadExtensions(engine, run);
    engine.Convert("", ms, options);
}
```

### Subcommand Structure
The CLI already handles subcommands: `preview` is parsed via `ParsePreviewArguments()`.
Beta.7 will add `ext list|install|remove` subcommands using the same pattern.

### Help Text
Extension flags are already documented in `PrintHelp()`:
```
  --extensions <path>   Load extensions from a DLL file (repeatable)
  --extension-dir <dir> Load all extension DLLs from directory (repeatable)
```

---

## 5. Core Project — Dependencies

**File**: `src/AdocNet.Core/AdocNet.Core.csproj`

### Target Frameworks
```xml
<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
```

### External NuGet Dependencies
**Zero.** Only dependency is a project reference to `AdocNet.Ast`.

### Implications for Beta.7
Adding `System.Text.Json` for manifest parsing would be the **first external NuGet dependency** on Core. This requires careful consideration (see JSON Parsing section below).

---

## 6. JSON Parsing Options for extension.json

The manifest format (`extension.json`) requires JSON parsing. Options for netstandard2.0 + net10.0 dual-targeting:

### Option 1: System.Text.Json (conditional NuGet package)
```xml
<PackageReference Include="System.Text.Json" Version="9.0.0"
    Condition="'$(TargetFramework)' == 'netstandard2.0'" />
```
- **Pro**: Standard .NET JSON library, well-tested, familiar API.
- **Pro**: Built-in on net10.0 — conditional reference means zero added deps on modern TFMs.
- **Pro**: `JsonSerializer.Deserialize<T>()` is clean and handles edge cases.
- **Con**: First external NuGet dependency on AdocNet.Core.
- **Con**: Adds ~300KB to netstandard2.0 output.

### Option 2: Minimal Hand-Written Parser
- **Pro**: Zero dependencies. Keeps Core dependency-free.
- **Pro**: Manifest has only 5 fields — very simple JSON structure.
- **Con**: Custom JSON parser is error-prone (escaping, encoding, whitespace edge cases).
- **Con**: Maintenance burden for a solved problem.
- **Con**: Users may use editors that add comments/trailing commas — a strict hand-written parser would reject these.

### Option 3: DataContractJsonSerializer (built-in on ns2.0)
```csharp
var serializer = new DataContractJsonSerializer(typeof(ExtensionManifest));
var manifest = (ExtensionManifest)serializer.ReadObject(stream);
```
- **Pro**: Built-in, no NuGet dependency.
- **Con**: Requires `System.Runtime.Serialization` — awkward API.
- **Con**: Requires `[DataContract]` / `[DataMember]` attributes.
- **Con**: No support for camelCase property names without explicit mapping.
- **Con**: Generally deprecated in favor of System.Text.Json.

### Recommendation: Option 1 (System.Text.Json conditional)

System.Text.Json is the standard .NET approach. The conditional reference means:
- On `net10.0`: zero added dependencies (built-in).
- On `netstandard2.0`: one well-known, Microsoft-maintained package.

The manifest schema is stable (5-6 string fields), so the API surface used is minimal.
A hand-written parser for such a standard format would be unnecessary risk.

---

## 7. Home Directory Path Strategy

### API
```csharp
Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
```

### Availability
- Available on netstandard2.0: **Yes** (part of `System.Environment`).
- Available on net10.0: **Yes**.

### Platform Behavior
| Platform | Result |
|----------|--------|
| Windows  | `C:\Users\<username>` |
| Linux    | `/home/<username>` |
| macOS    | `/Users/<username>` |

### Extension Directory Convention
```
{UserProfile}/.adocnet/extensions/
```

Platform-specific paths:
- Windows: `C:\Users\sylva\.adocnet\extensions\`
- Linux: `/home/user/.adocnet/extensions/`
- macOS: `/Users/user/.adocnet/extensions/`

### Implementation Pattern
```csharp
public static string GetDefaultExtensionDirectory()
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".adocnet", "extensions");
}
```

### Edge Cases
- `GetFolderPath` returns empty string if the folder doesn't exist on some platforms.
  Should fall back or handle gracefully (directory just won't exist → no auto-loading).
- Path separator is handled by `Path.Combine()` — works cross-platform.

---

## 8. Existing Extension Types (for reference)

### Test Extensions (tests/)
- `tests/AdocNet.TestExtension/` — contains processor types for integration testing.
- `tests/AdocNet.TestEmptyExtension/` — DLL with no processor types (tests empty-DLL handling).

### Built-in Extensions (src/AdocNet.Core/Extensions/)
- `IconMacroProcessor` (IInlineProcessor)
- `DocumentMetadataProcessor` (IDocumentProcessor)
- `AutoIdBlockProcessor` (IBlockProcessor)
- `DiagramBlockProcessor` (IBlockProcessor) + `IDiagramToolRunner`, `ProcessDiagramToolRunner`

---

## 9. Version Info

- Current version: `1.0.0-beta.6` (in `Directory.Build.props`).
- Beta.7 will update to `1.0.0-beta.7`.
- `minAdocNetVersion` in manifests will check against this version string.

---

## 10. Key Integration Points for Beta.7

### What beta.7 adds:
1. **ExtensionManifest model** — parse `extension.json` (name, version, description, entry, minAdocNetVersion).
2. **ExtensionDirectoryLoader** — scan `~/.adocnet/extensions/*/extension.json`, validate, load entry DLL.
3. **Engine integration** — `AdocEngine.LoadExtensionDirectory(string path)` or similar.
4. **CLI `ext` subcommands** — `ext list`, `ext install`, `ext remove`.
5. **Auto-loading** — on `Convert()`, automatically load from default extension directory.

### What must NOT change:
- `LoadExtension(string)` and `LoadExtensions(string)` signatures.
- `ExtensionLoader.LoadAssembly()` and `LoadDirectory()` signatures.
- Existing Register* methods.
- `_frozen` behavior.
- Extension interface definitions (IDocumentProcessor, IBlockProcessor, IInlineProcessor, IExtension).
