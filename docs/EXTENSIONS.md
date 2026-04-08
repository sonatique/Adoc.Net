# Extension Developer Guide

This guide covers how to extend AdocNet with custom renderers, processing extensions, and include readers.

## Architecture Overview

AdocNet follows a **parse → process → render** pipeline:

1. **Parser** (`AdocParser.Parse`) reads AsciiDoc source text and produces an AST rooted at `DocumentNode`.
2. **Processors** (optional) transform the AST before rendering — document, block, and inline processors run in FIFO registration order.
3. **Renderers** consume the AST and write output to a `Stream`.

The AST consists of **block nodes** (`SectionNode`, `ParagraphNode`, `ListNode`, `DelimitedBlockNode`, etc.)
and **inline nodes** (`TextInlineNode`, `StrongInlineNode`, `EmphasisInlineNode`, `LinkInlineNode`, etc.).
Block nodes live in `AstNode.Children`; inline nodes are on typed properties like `ParagraphNode.Inlines`
or `StrongInlineNode.Children`.

## Writing a Custom Renderer

### 1. Extend `DocumentRendererBase`

Create a class that inherits from `DocumentRendererBase` (in `AdocNet` namespace):

```csharp
using AdocNet;
using AdocNet.Ast;

public class MarkdownRenderer : DocumentRendererBase
{
    public override string Format => "markdown";

    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var writer = new StreamWriter(output, leaveOpen: true);
        context.GetOrCreate(() => writer);

        foreach (var child in context.Document.Children.OfType<BlockNode>())
            RenderBlock(child, context);

        writer.Flush();
    }
}
```

### 2. Set the `Format` property

`Format` is a string identifier for your output format (e.g. `"markdown"`, `"latex"`, `"plaintext"`).
It appears in error messages when a node type is not supported.

### 3. Override `RenderDocument`

This is the entry point. You receive a `RenderContext` and the output `Stream`.

- Use `context.Document` to access the root `DocumentNode` and its `Children`.
- Use `context.GetOrCreate<T>(Func<T>)` to store per-render state (e.g. a `StreamWriter`).
- Call `RenderBlocks(nodes, context)` or `RenderBlock(node, context)` to dispatch child blocks.

### 4. Override virtual methods for each node type

`DocumentRendererBase` has virtual methods for every AST node type. Each throws
`NotSupportedException` by default, so you only override the ones you need:

```csharp
protected override void RenderSection(SectionNode node, RenderContext context)
{
    var writer = context.GetOrCreate<StreamWriter>(() => throw new InvalidOperationException());
    // node.Level: 1 = "##", 2 = "###", etc. (Level 0 is the document title)
    writer.Write(new string('#', node.Level + 1));
    writer.Write(' ');
    writer.WriteLine(node.Title);
    writer.WriteLine();

    RenderBlocks(node.Children.OfType<BlockNode>(), context);
}

protected override void RenderParagraph(ParagraphNode node, RenderContext context)
{
    var writer = context.GetOrCreate<StreamWriter>(() => throw new InvalidOperationException());
    RenderInlines(node.Inlines, context);
    writer.WriteLine();
    writer.WriteLine();
}
```

### 5. Use `RenderBlocks` / `RenderInlines` helpers

These iterate a collection and dispatch each node through the appropriate `Render*` method:

- `RenderBlocks(IEnumerable<BlockNode>, RenderContext)` -- calls `RenderBlock` for each.
- `RenderInlines(IEnumerable<InlineNode>, RenderContext)` -- calls `RenderInline` for each.

### 6. Render and retrieve output

```csharp
var result = AdocParser.Parse(source);
var markdown = new MarkdownRenderer().RenderToString(result.Document);
```

`RenderToString` is an extension method on `IDocumentRenderer` that handles the `MemoryStream` boilerplate.

### Key AST Node Properties

| Node | Key Properties |
|------|---------------|
| `SectionNode` | `Level`, `Title`, `TitleInlines`, `Children` |
| `ParagraphNode` | `Text`, `Inlines` |
| `ListNode` | `ListKind` (Ordered/Unordered), `Children` (ListItemNodes) |
| `ListItemNode` | `Text`, `Inlines`, `Children` (nested lists) |
| `DelimitedBlockNode` | `BlockKind`, `Content`, `Language`, `Title`, `Children` |
| `TextInlineNode` | `Value` |
| `StrongInlineNode` | `Children` (typed as `IReadOnlyList<InlineNode>`) |
| `EmphasisInlineNode` | `Children` (typed as `IReadOnlyList<InlineNode>`) |
| `MonospaceInlineNode` | `Children` (typed as `IReadOnlyList<InlineNode>`) |
| `LinkInlineNode` | `Url` |
| `InlineLinkMacroNode` | `Url`, `Label` |

See the full AST types in `src/AdocNet.Ast/`.

## Writing Processing Extensions (beta.5)

Processing extensions modify the AST between parsing and rendering. There are three types,
executed in this guaranteed order:

1. **Document processors** (`IDocumentProcessor`) — run first, receive the entire document
2. **Block processors** (`IBlockProcessor`) — run second, target individual block nodes
3. **Inline processors** (`IInlineProcessor`) — run last, target individual inline nodes

### Registering Processors

```csharp
using AdocNet;
using AdocNet.Extensions;
using AdocNet.Parser;
using AdocNet.Converters.Html;

var engine = new AdocEngine(new HtmlRenderer(), s => BlockParser.Parse(s).Document);

engine
    .RegisterDocumentProcessor(new DocumentMetadataProcessor("Generated by AdocNet"))
    .RegisterBlockProcessor(new DiagramBlockProcessor(toolRunner, "./images"))
    .RegisterInlineProcessor(new IconMacroProcessor());

engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");

using var output = File.Create("output.html");
engine.Convert(adocSource, output);
```

Processors must be registered **before** the first `Convert()` call. Registration after
`Convert()` throws `InvalidOperationException`.

### IDocumentProcessor

Processes the entire document tree. Use for global transformations.
Returns `bool`: `true` to short-circuit (skip remaining document processors), `false` to continue.
Receives `RenderContext` for diagnostics, options, and per-render state (added in beta.13).

```csharp
using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

public class DocumentMetadataProcessor : IDocumentProcessor
{
    private readonly string _text;

    public DocumentMetadataProcessor(string text) => _text = text;

    public bool Process(DocumentNode document, RenderContext context)
    {
        var para = new ParagraphNode
        {
            Text = _text,
            Inlines = [new TextInlineNode { Value = _text }],
        };
        document.InsertChild(0, para);
        return false; // Continue to next document processor
    }
}
```

### IBlockProcessor

Targets specific block nodes. `CanProcess` filters which blocks to handle.
Returns `bool`: `true` to short-circuit (skip remaining block processors for THIS node), `false` to continue.

```csharp
using AdocNet.Ast;
using AdocNet.Extensions;

public class CustomBlockProcessor : IBlockProcessor
{
    public bool CanProcess(BlockNode node)
        => node is DelimitedBlockNode { BlockKind: DelimitedBlockKind.Example };

    public bool Process(BlockNode node, RenderContext context)
    {
        var block = (DelimitedBlockNode)node;
        // Modify block properties, add children, or register replacements
        return false; // Continue to next block processor for this node
    }
}
```

### IInlineProcessor

Targets specific inline nodes. Commonly used for custom inline macros.
Returns `bool`: `true` to short-circuit (skip remaining inline processors for THIS node), `false` to continue.

```csharp
using AdocNet.Ast;
using AdocNet.Extensions;

public class IconMacroProcessor : IInlineProcessor
{
    public bool CanProcess(InlineNode node)
        => node is InlineMacroNode { Name: "icon" };

    public bool Process(InlineNode node, RenderContext context)
    {
        var macro = (InlineMacroNode)node;
        var symbol = macro.Target switch
        {
            "heart" => "\u2764",
            "star"  => "\u2605",
            "check" => "\u2713",
            _       => $"[{macro.Target}]",
        };

        var text = new TextInlineNode { Value = symbol };
        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, text);
        return false; // Continue to next inline processor for this node
    }
}
```

### Short-Circuiting (bool Process Return)

When `Process()` returns `true`, remaining processors of the same type are skipped for that node:

- **Document processors**: `true` skips remaining document processors entirely.
- **Block processors**: `true` skips remaining block processors for THIS block node only.
  The next block node in the tree walk still runs all processors.
- **Inline processors**: same per-node behavior as block processors.

This enables the "first handler wins" pattern and explicit "I handled this" intent.

### Node Replacement

Processors can replace or remove AST nodes using `NodeReplacements`:

```csharp
var replacements = context.GetOrCreate(() => new NodeReplacements());
replacements.Replace(originalNode, newNode);  // swap nodes
replacements.Remove(originalNode);            // remove node
```

The pipeline applies replacements after all processors run for a given node.

### Per-Render State

Use `RenderContext.GetOrCreate<T>()` for state that persists across a single render:

```csharp
public void Process(BlockNode node, RenderContext context)
{
    var state = context.GetOrCreate(() => new MyProcessorState());
    state.Counter++;
}
```

Each `Convert()` call creates a fresh `RenderContext` — state is naturally isolated.

### Error Handling

If a processor throws, the pipeline catches the exception, invokes `AdocEngine.OnWarning`
with the error message, and continues to the next processor. The failing processor's
partial changes are not rolled back.

## Writing a Custom Include Reader

### The `IIncludeReader` interface

```csharp
public interface IIncludeReader
{
    bool Exists(string path);
    string Read(string path);
}
```

`Exists` checks whether the target path is readable; `Read` returns its full text content.

### Example: In-Memory Reader

```csharp
using AdocNet.Parser;

public class InMemoryIncludeReader : IIncludeReader
{
    private readonly Dictionary<string, string> _files;

    public InMemoryIncludeReader(Dictionary<string, string> files)
        => _files = files;

    public bool Exists(string path)
        => _files.ContainsKey(Path.GetFileName(path));

    public string Read(string path)
        => _files[Path.GetFileName(path)];
}
```

### Using a custom reader

Pass your reader via `ParseOptions.IncludeReader`:

```csharp
var reader = new InMemoryIncludeReader(new Dictionary<string, string>
{
    ["chapter1.adoc"] = "== Chapter 1\n\nContent here.",
    ["chapter2.adoc"] = "== Chapter 2\n\nMore content.",
});

var result = AdocParser.Parse(mainText, new ParseOptions
{
    SourceFilePath = "book.adoc",
    IncludeReader = reader,
});
```

### How includes work

Include expansion is a preprocessing step that runs before block parsing. The built-in
`FileIncludeReader` resolves `include::path[]` directives from the filesystem. Include
expansion is triggered when `ParseOptions.SourceFilePath` or `ParseOptions.BaseDirectory`
is set.

The reader receives the resolved absolute path. Partial includes (`lines=`, `tags=`,
`leveloffset=`) are handled by the parser after the reader returns the file content.

See `examples/CustomIncludeReader/` for a complete working example.

## Extension Template

The `examples/ExtensionTemplate/` project provides a minimal skeleton for a custom renderer.
Copy it as a starting point for your own format:

```
examples/ExtensionTemplate/
  ExtensionTemplate.csproj
  MyRenderer.cs          -- Skeleton renderer with TODO placeholders
  Program.cs             -- Parse and render sample
```

## Best Practices

1. **No static mutable state.** Renderers may be called concurrently from different threads.
   Store per-render state in `RenderContext.GetOrCreate<T>()`.

2. **Deterministic output.** Given the same AST, your renderer should produce identical output
   every time. Avoid timestamps, random IDs, or environment-dependent values unless explicitly
   configured.

3. **Handle all node types gracefully.** If your renderer encounters a node type it doesn't
   support, the base class throws `NotSupportedException`. For production renderers, consider
   overriding unsupported node methods to emit a fallback (e.g. plain text) rather than crashing.

4. **Flush your writer.** If you use a `StreamWriter`, call `Flush()` at the end of
   `RenderDocument` so all output reaches the stream before it is read.

5. **Use `leaveOpen: true`** when wrapping the output `Stream` in a `StreamWriter`, so the
   caller retains ownership of the stream lifecycle.

6. **Test with real documents.** Parse a variety of AsciiDoc inputs (sections, lists, code
   blocks, inline markup) to ensure your renderer handles the node combinations that appear
   in practice.

## Dynamic Extension Loading (beta.6)

Extensions can be loaded from external DLLs at runtime using `LoadExtension()` and
`LoadExtensions()`:

```csharp
engine.LoadExtension("path/to/MyExtension.dll");
engine.LoadExtensions("./extensions/");  // loads all *.dll files
```

The CLI supports `--extensions` and `--extension-dir` flags:

```bash
adocnet input.adoc --extensions my-ext.dll --extension-dir ./plugins/
```

See [Dynamic Extensions Guide](DYNAMIC_EXTENSIONS.md) for full documentation on
building, distributing, and loading extension DLLs.

## Extension Packaging (beta.7)

Extensions can be packaged with an `extension.json` manifest and installed to
`~/.adocnet/extensions/` for automatic loading. Use `adocnet ext install` to
install, `adocnet ext list` to view, and `adocnet ext remove` to uninstall.

See [Extension Packaging Guide](EXTENSION_PACKAGING.md) for full documentation.

## Extension Safety (beta.9)

Extensions are hardened for production use with per-extension state tracking,
failure-based disabling (configurable via `MaxProcessorFailures`), API version
compatibility, and structured load reporting via `LoadExtensionSafe()`.
Use `adocnet ext status` to check the load state of all installed extensions.

See [Extension Safety Guide](EXTENSION_SAFETY.md) for full documentation.

## Extension Registry (beta.8)

Installed extensions are tracked in a local registry (`~/.adocnet/registry.json`)
for fast querying. Use `adocnet ext info <name>` for detailed extension info,
`adocnet ext search <keyword>` to find extensions, and the programmatic API
(`AdocEngine.GetInstalledExtensions()`, `AdocEngine.FindExtension()`) for
registry queries from code.

Extensions can also declare dependencies on other extensions:

```json
{
  "name": "diagram",
  "entry": "DiagramExtension.dll",
  "dependencies": ["syntax-highlight >= 1.0.0"]
}
```

Dependencies are validated on install with warnings for missing or incompatible
versions, but never block loading.

See [Extension Registry Guide](EXTENSION_REGISTRY.md) for full documentation.

## Output Processors (beta.11+)

Output processors transform rendered output **after** the renderer completes.
The pipeline becomes: Parse → Extensions → Render → **Output Processors**.

```csharp
public class HtmlMinifier : IOutputProcessor
{
    public byte[] Process(byte[] renderedOutput, string format)
    {
        if (format != "html") return renderedOutput;
        var html = Encoding.UTF8.GetString(renderedOutput);
        var minified = MinifyHtml(html);
        return Encoding.UTF8.GetBytes(minified);
    }
}

engine.RegisterOutputProcessor(new HtmlMinifier());
```

Multiple output processors chain sequentially in registration order.
The render cache stores pre-processor output; processors always run.

## Kroki Diagram Runner (beta.11+)

`KrokiDiagramToolRunner` generates diagrams via the Kroki HTTP API instead of requiring
local tool installations. Pass it to `DiagramBlockProcessor`:

```csharp
var kroki = new KrokiDiagramToolRunner("https://kroki.io");
engine.RegisterBlockProcessor(new DiagramBlockProcessor(kroki, "./images"));
```

Supports all Kroki-supported languages: PlantUML, Mermaid, Graphviz, Ditaa, and more.
This is opt-in — it introduces a network dependency.

## Extension Lifecycle (beta.11+)

Extensions that hold resources can implement `IExtensionLifecycle`:

```csharp
public class MyExtension : IBlockProcessor, IExtensionLifecycle
{
    public void Initialize() { /* open connections */ }
    public void Dispose() { /* release resources */ }
    // ... IBlockProcessor members
}
```

`Initialize()` is called during dynamic loading. `Dispose()` is called via `engine.Shutdown()`.

## Zip Install (beta.11+)

Extensions can be installed from `.zip` files:

```bash
adocnet ext install myext.zip
```

The zip is extracted, validated for an `extension.json` manifest, and installed normally.

## Enable/Disable Extensions (beta.11+)

Extensions can be disabled without removing them:

```bash
adocnet ext disable myext    # stops loading, keeps files
adocnet ext enable myext     # re-enables loading
adocnet ext list             # shows [disabled] indicator
```

## Extension Diagnostics (beta.11+)

Extensions can emit structured diagnostics during processing:

```csharp
context.AddDiagnostic(new Diagnostic(
    DiagnosticSeverity.Warning, "Tool not found", node.Source));
```

After `Convert()`, read `engine.LastExtensionDiagnostics`.

## Extension Capabilities (beta.12)

Processors can declare their determinism to enable render cache optimizations:

```csharp
public class MyProcessor : IBlockProcessor, IExtensionCapabilities
{
    public bool IsDeterministic => true;
    // ... IBlockProcessor members
}
```

When ALL registered processors implement `IExtensionCapabilities` and return
`IsDeterministic = true`, the render cache is enabled even with extensions.
Processors that don't implement the interface are treated as non-deterministic
(safe default — render cache disabled).

## Extension Priority (beta.12)

Processors can declare their execution priority. Lower values execute first:

```csharp
public class EarlyProcessor : IDocumentProcessor, IExtensionPriority
{
    public int Priority => 100;  // runs before default (1000)
    public bool Process(DocumentNode document, RenderContext context) { /* ... */ return false; }
}
```

- Default priority (no `IExtensionPriority`): **1000**
- Same priority: FIFO registration order preserved
- Output processors are NOT sorted by priority (always FIFO)
- Typical ranges: 0-100 (early), 500 (normal), 900-1000 (late)

## Max Engine Version (beta.12)

Extensions can declare a maximum compatible AdocNet version in `extension.json`:

```json
{
  "name": "my-extension",
  "entry": "MyExtension.dll",
  "minAdocNetVersion": "1.0.0-beta.7",
  "maxAdocNetVersion": "2.0.0"
}
```

If the current engine version exceeds `maxAdocNetVersion`, the extension is
skipped with a warning and state `Incompatible`. This allows extension authors
to declare forward-compatibility boundaries.

## Dependency-Ordered Loading (beta.14)

Extensions are loaded in dependency order using topological sort. If extension A
depends on extension B, B is guaranteed to load before A:

```json
{
  "name": "my-extension",
  "entry": "MyExtension.dll",
  "dependencies": ["base-utils >= 1.0.0"]
}
```

The loader reads all manifests first, builds a dependency graph, and sorts
using Kahn's algorithm. If a dependency cycle is detected, a warning is emitted
and extensions fall back to alphabetical loading order.

Dependencies referencing extensions not installed are ignored during ordering
(but `DependencyValidator` still emits a warning).

## Extension Signing (beta.14)

Extensions can declare an expected strong-name public key token in `extension.json`:

```json
{
  "name": "trusted-extension",
  "entry": "TrustedExtension.dll",
  "publicKeyToken": "ab40020b151f4aae"
}
```

On load, the engine reads the DLL's public key token via `AssemblyName.GetAssemblyName()`
and compares it to the manifest value. If the tokens don't match (or the DLL is
unsigned but a token is expected), the extension is skipped with a warning.

This is strong-name token verification — it proves the DLL was signed with a
specific key pair. It is **not** full PKI or certificate-based signing.

To get your extension's token: sign with `<SignAssembly>true</SignAssembly>` and
`<AssemblyOriginatorKeyFile>key.snk</AssemblyOriginatorKeyFile>` in your `.csproj`,
then read the token with `sn -T MyExtension.dll`.

## Extension Validation Tool (beta.14)

Validate an extension directory before publishing:

```bash
adocnet ext validate ./my-extension/
adocnet ext validate myext.zip          # also accepts zip files
```

Checks performed:
1. `extension.json` exists and is valid
2. Required fields (name, version, entry) present
3. Entry DLL exists
4. DLL loads and contains processor types
5. API version compatible
6. minAdocNetVersion / maxAdocNetVersion compatible
7. Dependencies satisfiable (checked against local registry)
8. Public key token matches (if specified)

Output shows `[PASS]`, `[FAIL]`, `[WARN]`, or `[SKIP]` per check with an
overall verdict. Exit code 0 = all pass, 1 = any failure.

## See Also

- [Usage Guide](USAGE.md) — parsing and rendering API
- [Renderers Guide](RENDERERS.md) — built-in renderer options
- [Diagrams Guide](DIAGRAMS.md) — diagram block processing with external tools
- [Dynamic Extensions Guide](DYNAMIC_EXTENSIONS.md) — loading extensions from external DLLs
- [Extension Packaging Guide](EXTENSION_PACKAGING.md) — manifest-based packaging and installation
- [Extension Registry Guide](EXTENSION_REGISTRY.md) — registry, search, and dependency validation
- [Extension Safety Guide](EXTENSION_SAFETY.md) — failure disabling, API version, structured loading
- [CLI Reference](CLI.md) — command-line tool
