# Dynamic Extension Loading

AdocNet supports loading processing extensions from external DLL files at runtime.
This allows you to distribute custom document, block, and inline processors as
standalone assemblies without modifying the AdocNet source code.

## Building an Extension DLL

Create a class library project that references `AdocNet.Core`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AdocNet.Core" Version="1.0.0-beta.6" />
  </ItemGroup>
</Project>
```

Implement one or more processor interfaces with a **parameterless constructor**:

```csharp
using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

public sealed class ConfidentialStampProcessor : IBlockProcessor, IExtension
{
    // IExtension metadata (optional — improves diagnostic messages)
    public string Name => "ConfidentialStamp";
    public string Version => "1.0.0";

    public bool CanProcess(BlockNode node) => node is ParagraphNode { Id: null };

    public void Process(BlockNode node, RenderContext context)
    {
        node.Id = "confidential";
    }
}

public sealed class UpperCaseInlineProcessor : IInlineProcessor
{
    public bool CanProcess(InlineNode node)
        => node is TextInlineNode t && t.Value != t.Value.ToUpperInvariant();

    public void Process(InlineNode node, RenderContext context)
    {
        var text = (TextInlineNode)node;
        var upper = new TextInlineNode { Value = text.Value.ToUpperInvariant() };
        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, upper);
    }
}
```

Build the project to produce your extension DLL:

```bash
dotnet build -c Release
# Output: bin/Release/net10.0/MyExtension.dll
```

## Loading Extensions via API

### LoadExtension — Single DLL

```csharp
using AdocNet;
using AdocNet.Converters.Html;
using AdocNet.Parser;

var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

// Load extensions from a single DLL
engine.LoadExtension("path/to/MyExtension.dll");

// Warnings callback (optional — for diagnostics)
engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");

using var output = File.Create("output.html");
engine.Convert(source, output);
```

### LoadExtensions — Directory

```csharp
// Load all *.dll files from a directory (alphabetical order)
engine.LoadExtensions("./extensions/");
```

All `*.dll` files in the directory are loaded in alphabetical order by filename.
Subdirectories are not scanned.

### Fluent Chaining

Both methods return the engine instance for fluent chaining:

```csharp
engine
    .LoadExtension("core-ext.dll")
    .LoadExtensions("./plugins/")
    .RegisterBlockProcessor(new MyLocalProcessor());
```

### Registration Timing

Extensions must be loaded **before** the first `Convert()` call. Loading after
`Convert()` throws `InvalidOperationException`, consistent with the `Register*` methods.

## Loading Extensions via CLI

### --extensions flag

Load a specific extension DLL (repeatable):

```bash
adocnet input.adoc --extensions my-extension.dll
adocnet input.adoc --extensions ext1.dll --extensions ext2.dll
```

### --extension-dir flag

Load all DLLs from a directory (repeatable):

```bash
adocnet input.adoc --extension-dir ./plugins/
```

Both flags can be combined:

```bash
adocnet input.adoc --extensions custom.dll --extension-dir ./shared-plugins/
```

## How Discovery Works

When a DLL is loaded, AdocNet scans it for public types that:

1. Implement `IDocumentProcessor`, `IBlockProcessor`, or `IInlineProcessor`
2. Are not abstract or interface types
3. Have a parameterless constructor

Types are instantiated via `Activator.CreateInstance()` and registered into the engine.

### Deterministic Ordering

Loading order is deterministic across platforms:

- **Directory loading**: DLLs sorted alphabetically by filename (`StringComparer.Ordinal`)
- **Within each DLL**: types sorted by `Type.FullName` (`StringComparer.Ordinal`)
- **Multiple interfaces**: registration order is Document → Block → Inline

### IExtension Metadata (Optional)

Extensions can optionally implement `IExtension` for identification:

```csharp
public interface IExtension
{
    string Name { get; }
    string Version { get; }
}
```

This metadata appears in warning messages. Extensions that don't implement `IExtension`
use the type name as their display name.

## Error Handling

The loader never crashes. All errors produce warnings via `OnWarning` and are skipped:

| Scenario | Behavior |
|----------|----------|
| File not found | Warning: "Extension not found: {path}" |
| Not a .NET DLL | Warning: "Not a valid .NET assembly: {path}" |
| Missing dependency | Warning with details, partially loaded types used |
| No parameterless constructor | Warning: "Skipping {type}: no parameterless constructor" |
| Constructor throws | Warning: "Failed to instantiate {type}: {message}" |
| Empty directory | Warning: "No extension DLLs found in: {path}" |
| Directory not found | Warning: "Extension directory not found: {path}" |

## Dependency Resolution

Extension DLLs must ship their dependencies alongside them. The CLR resolves
dependencies from:

1. The directory containing the loaded assembly
2. The application's base directory
3. The GAC (on .NET Framework)

## Limitations

- No assembly isolation (`AssemblyLoadContext` is not used for netstandard2.0 compatibility)
- No hot-reloading (registration freezes after first `Convert()`)
- No plugin lifecycle management (no init/dispose hooks)
- No dependency resolution between extensions
- Only parameterless constructors are supported for dynamic loading

Extensions needing constructor arguments should be registered manually via the
`Register*` methods instead of dynamic loading.

## See Also

- [Extension Developer Guide](EXTENSIONS.md) — writing custom renderers and processors
- [Diagrams Guide](DIAGRAMS.md) — diagram block processing
- [CLI Reference](CLI.md) — command-line tool
