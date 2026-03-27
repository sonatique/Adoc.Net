# Beta.6 Context — Dynamic Extension Loading

> Generated during P00 — Context Discovery. Read-only analysis of the beta.5 codebase.

## AdocEngine Public API (`src/AdocNet.Core/AdocEngine.cs`)

### Constructor

```csharp
public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
```

### Properties

| Member | Type | Notes |
|--------|------|-------|
| `Renderer` | `IDocumentRenderer` (init) | The renderer used to produce output |
| `Parser` | `Func<string, DocumentNode>` (init) | Parser function: AsciiDoc source → AST |
| `OnWarning` | `Action<string>?` (get/set) | Warning callback; null = silent discard |

### Registration Methods (fluent, return `AdocEngine`)

| Method | Parameter | Notes |
|--------|-----------|-------|
| `RegisterDocumentProcessor` | `IDocumentProcessor processor` | FIFO order, throws if frozen |
| `RegisterBlockProcessor` | `IBlockProcessor processor` | FIFO order, throws if frozen |
| `RegisterInlineProcessor` | `IInlineProcessor processor` | FIFO order, throws if frozen |

All three call `ThrowIfFrozen()` before adding to internal `List<T>`.

### Convert Methods

| Method | Parameters | Notes |
|--------|-----------|-------|
| `Convert` | `string input, Stream output, RenderOptions? options = null` | Parse → process → render |
| `ConvertFile` | `string inputPath, Stream output, RenderOptions? options = null` | File.ReadAllText → Convert |

### Frozen Flag (`_frozen`)

- Set to `true` on first `Convert()` call (only when processors are registered)
- `ThrowIfFrozen()` throws `InvalidOperationException("Cannot register processors after the first Convert() call.")`
- Purpose: registration list is immutable after first render

### Pipeline Integration

```csharp
// In Convert():
if (_documentProcessors.Count > 0 || _blockProcessors.Count > 0 || _inlineProcessors.Count > 0)
{
    _frozen = true;
    var context = new RenderContext(doc, opts);
    ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors, _inlineProcessors, OnWarning);
}
```

## Extension Interfaces (`src/AdocNet.Core/Extensions/`)

### Files in `src/AdocNet.Core/Extensions/`

| File | Type | Category |
|------|------|----------|
| `IDocumentProcessor.cs` | Interface | Core interface |
| `IBlockProcessor.cs` | Interface | Core interface |
| `IInlineProcessor.cs` | Interface | Core interface |
| `NodeReplacements.cs` | Class (public sealed) | AST mutation helper |
| `ProcessingPipeline.cs` | Class (internal static) | Pipeline execution |
| `AutoIdBlockProcessor.cs` | Class (public sealed) | Built-in block processor |
| `IconMacroProcessor.cs` | Class (public sealed) | Built-in inline processor |
| `DocumentMetadataProcessor.cs` | Class (public sealed) | Built-in document processor |
| `DiagramBlockProcessor.cs` | Class (public sealed) | Built-in block processor |
| `IDiagramToolRunner.cs` | Interface | Diagram tool abstraction |
| `ProcessDiagramToolRunner.cs` | Class (public sealed) | Diagram tool impl |

**Total: 11 files**

### Interface Signatures

```csharp
// IDocumentProcessor
void Process(DocumentNode document);

// IBlockProcessor
bool CanProcess(BlockNode node);
void Process(BlockNode node, RenderContext context);

// IInlineProcessor
bool CanProcess(InlineNode node);
void Process(InlineNode node, RenderContext context);
```

### Built-in Extension Classes (6 total)

| Class | Interface | Constructor | Notes |
|-------|-----------|-------------|-------|
| `AutoIdBlockProcessor` | `IBlockProcessor` | `(string prefix = "_")` | Has optional param — NOT parameterless |
| `IconMacroProcessor` | `IInlineProcessor` | `()` | Parameterless ✓ |
| `DocumentMetadataProcessor` | `IDocumentProcessor` | `(string text)` | Required param — NOT parameterless |
| `DiagramBlockProcessor` | `IBlockProcessor` | `(IDiagramToolRunner, string)` | Required params — NOT parameterless |
| `ProcessDiagramToolRunner` | `IDiagramToolRunner` | `(string, string)` | Required params — NOT parameterless |
| `NodeReplacements` | (none) | `()` | Parameterless ✓, but not an extension |

**Important for dynamic loading**: Only `IconMacroProcessor` has a true parameterless constructor.
`AutoIdBlockProcessor` has a default value but uses `(string prefix = "_")`.
The loader should scan for types implementing `IDocumentProcessor`, `IBlockProcessor`, or
`IInlineProcessor` and instantiate only those with parameterless constructors.
Types without parameterless constructors should be skipped with a warning.

### ProcessingPipeline (internal static)

- `Run()` method: document processors (FIFO) → block walk (depth-first, FIFO) → inline walk (depth-first, FIFO)
- Exception handling: catch per-processor, invoke `onWarning`, continue
- Block replacements: applied via `NodeReplacements` after each block node
- Inline replacements: applied via `NodeReplacements` after each inline node

### RenderContext (`src/AdocNet.Core/RenderContext.cs`)

- `Document` (DocumentNode), `Options` (RenderOptions), `Attributes` (IReadOnlyDictionary)
- `GetOrCreate<T>(Func<T> factory)` — per-render state keyed by `typeof(T)`
- Created per `Convert()` call — naturally thread-safe across concurrent renders

## CLI Argument Model (`src/AdocNet.Cli/Program.cs`)

### Entry Point

```csharp
public static int Run(string[] args, OutputFormat defaultFormat, string toolName)
```

Dispatches to: `ShowHelp`, `Error`, `Preview`, or `Run` (via `Execute(run)`).

### ParseArguments

```csharp
internal static CliArgs ParseArguments(string[] args, OutputFormat defaultFormat = OutputFormat.Html)
```

Manual `for` loop over `args[]`. Pattern: check for flag, consume next arg if needed.
Returns discriminated union: `CliArgs.Run | CliArgs.ShowHelp | CliArgs.Preview | CliArgs.Error`.

### CliArgs Types

```csharp
internal abstract record CliArgs
{
    internal sealed record Run(
        string InputPath,
        string? OutputPath,
        string? OutDir,
        bool DumpAst,
        OutputFormat Format = OutputFormat.Html,
        bool Styled = false,
        HtmlTheme Theme = HtmlTheme.Default,
        bool Watch = false,
        bool Verbose = false,
        bool Quiet = false,
        bool Recursive = false,
        string? ConfigPath = null,
        IReadOnlyDictionary<string, string>? Attributes = null) : CliArgs;
    internal sealed record ShowHelp() : CliArgs;
    internal sealed record Preview(...) : CliArgs;
    internal sealed record Error(string Message) : CliArgs;
}
```

### Where `--extensions` Fits

The `--extensions` flag should be added in `ParseArguments` alongside existing flags.
It follows the same pattern as `-a`/`--attribute`: accept one or more paths.

Proposed: `--extensions <path>` — can be a DLL path or directory path.
Multiple paths via repeated flags: `--extensions foo.dll --extensions ./plugins/`.

The `CliArgs.Run` record needs a new property (e.g., `IReadOnlyList<string>? ExtensionPaths`).

In `Execute(CliArgs.Run)`, before calling `engine.Convert()`, load extensions from the specified paths.

### Current Flags (for reference)

`-b`, `-o`, `-D`, `-a`, `-n`, `-e`, `--theme`, `--dump-ast`, `-w`, `-v`, `-q`, `-r`, `--config`, `-h`

## Assembly.LoadFrom Availability

`System.Reflection.Assembly.LoadFrom(string assemblyFile)` is part of `netstandard2.0`.
It is defined in `System.Runtime` / `mscorlib` and available on:

- .NET Framework 4.6.1+ (via netstandard2.0)
- .NET Core 2.0+
- .NET 5/6/7/8/9/10

No additional NuGet package is required. This is the correct loading mechanism for beta.6
(as opposed to `AssemblyLoadContext` which requires `System.Runtime.Loader` and is .NET Core only).

### Loading Pattern

```csharp
var assembly = Assembly.LoadFrom(dllPath);
var extensionTypes = assembly.GetExportedTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface)
    .Where(t => typeof(IDocumentProcessor).IsAssignableFrom(t)
             || typeof(IBlockProcessor).IsAssignableFrom(t)
             || typeof(IInlineProcessor).IsAssignableFrom(t))
    .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
    .OrderBy(t => t.FullName);
```

## Project Configuration (`src/AdocNet.Core/AdocNet.Core.csproj`)

- TFMs: `netstandard2.0;net10.0` ✓
- LangVersion: `latest`
- RootNamespace: `AdocNet`
- No external NuGet dependencies ✓
- Project reference: `AdocNet.Ast`
- InternalsVisibleTo: `AdocNet.Parser`, `AdocNet.Tests`

## Key Design Decisions for Beta.6

1. **ExtensionLoader** goes in `src/AdocNet.Core/Extensions/ExtensionLoader.cs` — keeps extension code together
2. **`IExtension` marker interface**: NOT needed per beta.6 rules — scan for the three processor interfaces directly
3. **Parameterless constructors only**: skip types without them, warn via `OnWarning`
4. **DLL sort order**: alphabetical by filename for deterministic loading
5. **Type sort order within DLL**: alphabetical by `FullName`
6. **Error handling**: invalid/missing assemblies skipped with warning, never crash
7. **Integration point**: new method(s) on `AdocEngine` (e.g., `LoadExtensionsFrom(string path)`)
8. **CLI integration**: `--extensions` flag on `CliArgs.Run`, processed in `Execute()`
