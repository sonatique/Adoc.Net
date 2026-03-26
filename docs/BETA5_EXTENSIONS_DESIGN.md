# Beta.5 Extension Architecture Design

> Public API contract for the AdocNet processing extension system.
> Phase P01 deliverable. All interface signatures are final.

---

## 1. Processing Pipeline

### Insertion Point

The pipeline inserts between parsing and rendering in `AdocEngine.Convert()`:

```
Before (beta.4):
  1. doc = Parser(input)
  2. Renderer.Render(doc, output, options)

After (beta.5):
  1. doc = Parser(input)
  2. context = new RenderContext(doc, options)
  3. Run DocumentProcessors(doc)               — FIFO, registration order
  4. Run BlockProcessors(doc, context)          — FIFO per block node, depth-first
  5. Run InlineProcessors(doc, context)         — FIFO per inline node, depth-first
  6. Renderer.Render(doc, output, options)
```

### Execution Order Guarantee (public contract)

1. All `IDocumentProcessor` instances run first, in registration order (FIFO).
2. All `IBlockProcessor` instances run next. The pipeline walks the AST depth-first,
   and for each `BlockNode`, runs all registered block processors in FIFO order.
3. All `IInlineProcessor` instances run last. Same depth-first walk, FIFO per node.

This order is guaranteed, documented, and tested.

### RenderContext Sharing

The `RenderContext` is created once at the start of the pipeline (step 2) and passed
to both Block/Inline processors and then to the renderer. This allows processors to
store state that the renderer can later read via `GetOrCreate<T>()`.

### Zero Extensions = No Overhead

When no processors are registered, the pipeline is skipped entirely. The flow is
identical to beta.4: `Parser(input)` → `Renderer.Render(doc, output, options)`.

---

## 2. Extension Interfaces

Three interfaces, all in `AdocNet.Extensions` namespace (files in `src/AdocNet.Core/Extensions/`):

### IDocumentProcessor

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Processes the entire document AST before rendering.
/// Runs before block and inline processors.
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Processes the document. May mutate the tree (add/remove/replace children,
    /// modify attributes, set title).
    /// </summary>
    void Process(DocumentNode document);
}
```

### IBlockProcessor

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Processes individual block nodes in the AST.
/// Runs after document processors, before inline processors.
/// </summary>
public interface IBlockProcessor
{
    /// <summary>
    /// Returns true if this processor should handle the given block node.
    /// Called for every block node during the tree walk.
    /// </summary>
    bool CanProcess(BlockNode node);

    /// <summary>
    /// Processes the block node. May mutate the node's properties or use
    /// <see cref="RenderContext.GetOrCreate{T}"/> to register node replacements
    /// via <see cref="NodeReplacements"/>.
    /// </summary>
    void Process(BlockNode node, RenderContext context);
}
```

### IInlineProcessor

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Processes individual inline nodes in the AST.
/// Runs after document and block processors.
/// </summary>
public interface IInlineProcessor
{
    /// <summary>
    /// Returns true if this processor should handle the given inline node.
    /// Called for every inline node during the tree walk.
    /// </summary>
    bool CanProcess(InlineNode node);

    /// <summary>
    /// Processes the inline node. May mutate the node's properties or use
    /// <see cref="RenderContext.GetOrCreate{T}"/> to register node replacements
    /// via <see cref="NodeReplacements"/>.
    /// </summary>
    void Process(InlineNode node, RenderContext context);
}
```

---

## 3. AST Mutation Model

### What Extensions CAN Do

- **Add children** to a node — via `AstNode.AddChild(child)`
- **Remove children** from a node — via `NodeReplacements` (see below)
- **Replace a node** with a different node — via `NodeReplacements` (see below)
- **Modify node properties** — via setters (e.g., `blockNode.Id = "new-id"`)
- **Set/remove document attributes** — via `document.SetAttribute()` / `document.RemoveAttribute()`

### What Extensions CANNOT Do

- Add custom properties or fields to existing AST node types (types are in `AdocNet.Ast`, immutable contract)
- Create new AST node type classes (all node types are sealed)
- Modify `AstNode.Source` ranges (undefined behavior)

### Node Replacement Mechanism — `NodeReplacements`

Since `AstNode` has no `RemoveChild` or `ReplaceChild` methods, and `src/AdocNet.Ast/`
is immutable, the pipeline provides a replacement mechanism through `RenderContext` state:

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Collects node replacement requests during processor execution.
/// Processors register replacements; the pipeline applies them after each pass.
/// </summary>
public sealed class NodeReplacements
{
    /// <summary>
    /// Registers a replacement: the original node will be replaced by the
    /// replacement node in its parent's children list.
    /// </summary>
    public void Replace(AstNode original, AstNode replacement);

    /// <summary>
    /// Registers a removal: the original node will be removed from its
    /// parent's children list.
    /// </summary>
    public void Remove(AstNode original);
}
```

**Usage pattern** (diagram processor replacing a code block with an image):

```csharp
public void Process(BlockNode node, RenderContext context)
{
    var block = (DelimitedBlockNode)node;
    var imagePath = RunDiagramTool(block.Language!, block.Content!);

    var imageNode = new BlockImageNode
    {
        Target = imagePath,
        Alt = block.Title ?? "Diagram",
        Title = block.Title,
    };

    var replacements = context.GetOrCreate(() => new NodeReplacements());
    replacements.Replace(node, imageNode);
}
```

**Pipeline applies replacements** after all processors of a given type have run for
a node. The pipeline walker holds a reference to the parent node and the child index,
so it can perform the swap directly on the backing `List<AstNode>` via `InsertChild`
+ index manipulation (or by rebuilding the children list).

### Implementation Detail — List Mutation

The pipeline casts `AstNode.Children` (which is `IReadOnlyList<AstNode>` backed by
`List<AstNode>`) and uses the existing `AddChild` / `InsertChild` methods along with
internal knowledge of the list structure to apply replacements. This is contained
within `ProcessingPipeline` — extension authors never see it.

---

## 4. Registration Model

### API Surface on `AdocEngine`

```csharp
public sealed class AdocEngine
{
    // Existing (unchanged)
    public IDocumentRenderer Renderer { get; init; }
    public Func<string, DocumentNode> Parser { get; init; }
    public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser);
    public void Convert(string input, Stream output, RenderOptions? options = null);
    public void ConvertFile(string inputPath, Stream output, RenderOptions? options = null);

    // New — beta.5
    public AdocEngine RegisterDocumentProcessor(IDocumentProcessor processor);
    public AdocEngine RegisterBlockProcessor(IBlockProcessor processor);
    public AdocEngine RegisterInlineProcessor(IInlineProcessor processor);

    /// <summary>
    /// Optional warning callback. Invoked when a processor throws an exception.
    /// </summary>
    public Action<string>? OnWarning { get; set; }
}
```

### Registration Rules

- **FIFO ordering**: processors execute in the order they were registered.
- **Register before Convert()**: the processor lists become immutable after the first
  `Convert()` call. Registering after `Convert()` throws `InvalidOperationException`.
- **Fluent API**: `Register*` methods return `this` for chaining.

### Usage Pattern

```csharp
var engine = new AdocEngine(renderer, AdocParser.Parse);

engine
    .RegisterBlockProcessor(new DiagramBlockProcessor(new PlantUmlToolRunner()))
    .RegisterInlineProcessor(new IconMacroProcessor())
    .RegisterDocumentProcessor(new TocInjectorProcessor());

engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");

using var output = File.Create("output.pdf");
engine.Convert(adocSource, output);
```

### Thread Safety

- `AdocEngine` is safe to use from multiple threads for concurrent `Convert()` calls
  after registration is complete.
- Each `Convert()` call creates its own `RenderContext` — no shared mutable state.
- Processor instances must be stateless or use `RenderContext.GetOrCreate<T>()` for
  per-render state. Storing mutable state in processor fields is not thread-safe.

---

## 5. Diagram Strategy

### Overview

Diagram support works by intercepting `DelimitedBlockNode` nodes with recognized
diagram languages, invoking an external tool to generate an image, and replacing
the source block with a `BlockImageNode`.

### Matching

```csharp
public bool CanProcess(BlockNode node)
{
    return node is DelimitedBlockNode { BlockKind: DelimitedBlockKind.Source } block
        && IsDiagramLanguage(block.Language);
}

private static bool IsDiagramLanguage(string? language)
    => language is "plantuml" or "mermaid" or "ditaa" or "graphviz" or "dot";
```

### Tool Abstraction — `IDiagramToolRunner`

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Abstracts invocation of an external diagram tool (PlantUML, Mermaid, etc.).
/// Implementations invoke the tool and return the path to the generated image.
/// </summary>
public interface IDiagramToolRunner
{
    /// <summary>
    /// Returns true if the tool is available on this system.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generates an image from diagram source text.
    /// </summary>
    /// <param name="language">The diagram language (e.g., "plantuml").</param>
    /// <param name="source">The diagram source text.</param>
    /// <param name="outputDirectory">Directory to write the generated image.</param>
    /// <returns>The path to the generated image file.</returns>
    string Generate(string language, string source, string outputDirectory);
}
```

### Fallback Behavior

When `IDiagramToolRunner.IsAvailable` returns `false`:
- The processor logs a warning via `OnWarning`
- The source block is left unchanged (rendered as a code block)
- No exception is thrown

### Output Directory

The diagram processor needs an output directory for generated images. This is
provided via constructor injection:

```csharp
var diagramProcessor = new DiagramBlockProcessor(
    toolRunner: new PlantUmlToolRunner("/usr/bin/plantuml"),
    outputDirectory: "./images/generated"
);
```

### Determinism

Diagram output depends on the external tool, which is outside AdocNet's control.
The generated image path is deterministic (derived from a hash of the source text),
so identical input produces the same file path. The image content depends on the
tool version.

---

## 6. Macro Strategy

### How It Works

The parser already handles inline macros: `name:target[content]` → `InlineMacroNode`
with `Name`, `Target`, and `Content` properties. An `IInlineProcessor` matches on
`InlineMacroNode.Name` and transforms or annotates the node.

### Example: Icon Macro Processor

Handles `icon:heart[]`, `icon:warning[size=2x]`:

```csharp
public class IconMacroProcessor : IInlineProcessor
{
    public bool CanProcess(InlineNode node)
        => node is InlineMacroNode { Name: "icon" };

    public void Process(InlineNode node, RenderContext context)
    {
        var macro = (InlineMacroNode)node;
        // Store icon references for the renderer to pick up
        var icons = context.GetOrCreate(() => new IconRegistry());
        icons.Register(macro.Target, macro.Content);
    }
}
```

### Example: Issue Link Processor

Handles `issue:123[]` → transforms into a link:

```csharp
public class IssueLinkProcessor : IInlineProcessor
{
    private readonly string _baseUrl;

    public IssueLinkProcessor(string baseUrl) => _baseUrl = baseUrl;

    public bool CanProcess(InlineNode node)
        => node is InlineMacroNode { Name: "issue" };

    public void Process(InlineNode node, RenderContext context)
    {
        var macro = (InlineMacroNode)node;
        var link = new InlineLinkMacroNode
        {
            Url = $"{_baseUrl}/{macro.Target}",
            Label = $"#{macro.Target}",
        };

        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, link);
    }
}
```

---

## 7. State Management and Thread Safety

### Per-Render State

All extension state MUST use `RenderContext.GetOrCreate<T>(Func<T> factory)`:

```csharp
// In a processor:
var state = context.GetOrCreate(() => new MyProcessorState());
state.Counter++;
```

Each `Convert()` call creates a fresh `RenderContext`, so state is naturally isolated
between concurrent renders.

### Prohibited Patterns

- **No static mutable fields** in processor classes
- **No state on AST nodes** (AST types are in `AdocNet.Ast`, sealed contract)
- **No `ThreadLocal<T>`** or `AsyncLocal<T>` — use `RenderContext` instead
- **No locking** in processors — each render gets its own state

### Processor Instance Lifecycle

Processor instances are registered once and shared across all `Convert()` calls.
They must be stateless (all state in `RenderContext`) or immutable (configuration
set at construction time, never changed).

```csharp
// OK — configuration is immutable
public class DiagramBlockProcessor : IBlockProcessor
{
    private readonly IDiagramToolRunner _runner;      // set once in constructor
    private readonly string _outputDirectory;         // set once in constructor

    // CanProcess and Process use _runner and _outputDirectory (read-only)
    // Per-render state goes in RenderContext
}
```

---

## 8. Error Handling and Warning Surface

### Warning Callback

```csharp
public sealed class AdocEngine
{
    /// <summary>
    /// Optional callback invoked when a processor throws or a non-fatal issue occurs.
    /// When null, warnings are silently discarded.
    /// </summary>
    public Action<string>? OnWarning { get; set; }
}
```

### Decision: `Action<string>?` on `AdocEngine`

- No `ILogger` dependency — keeps the library dependency-free
- Nullable — callers opt in to receiving warnings
- Simple string messages — no structured logging, no log levels
- Set before `Convert()`, used during pipeline execution

### Processor Exception Handling

When a processor's `Process()` method throws:

1. The pipeline catches the exception
2. Invokes `OnWarning` with: `"Processor {typeName} threw {exceptionType}: {message}"`
3. Continues to the next processor (the failing processor's changes are not rolled back)
4. The current node is left in whatever state it was in when the exception occurred

When a processor's `CanProcess()` method throws:

1. The pipeline catches the exception
2. Invokes `OnWarning` with the message
3. Treats the result as `false` (skips the processor for this node)

### Diagram Tool Failures

- Tool not found: `IsAvailable` returns `false`, warning emitted, block left unchanged
- Tool execution fails: `Generate()` throws, caught by pipeline, warning emitted,
  block left unchanged

---

## 9. Testing Strategy

### Unit Tests — Processor Interfaces

- Verify FIFO execution order with multiple registered processors
- Verify `CanProcess` filtering (processors only called when `CanProcess` returns true)
- Verify `DocumentProcessor` runs before `BlockProcessor` before `InlineProcessor`
- Verify zero processors = no pipeline overhead

### Unit Tests — Node Replacement

- Verify `NodeReplacements.Replace()` swaps a node in the tree
- Verify `NodeReplacements.Remove()` removes a node from the tree
- Verify replacement works at different tree depths
- Verify multiple replacements in a single pass

### Unit Tests — Error Handling

- Verify processor exception is caught and `OnWarning` is called
- Verify pipeline continues after processor exception
- Verify `CanProcess` exception treated as false

### Unit Tests — Diagram Extension

- Mock `IDiagramToolRunner` — verify tool is called with correct language/source
- Verify `IsAvailable == false` → block left unchanged, warning emitted
- Verify successful generation → `DelimitedBlockNode` replaced with `BlockImageNode`

### Integration Tests

- End-to-end: AsciiDoc with diagram block → parse → process → render (HTML/PDF)
- Verify output is deterministic (same input → same output, modulo tool availability)

### Thread Safety Tests

- Concurrent `Convert()` calls with processors → no cross-contamination of state

---

## 10. Explicit Non-Goals

The following are **out of scope** for beta.5:

| Feature | Reason | When |
|---------|--------|------|
| Dynamic plugin loading | Complexity, security | beta.6 |
| CLI `--extensions` flag | Requires dynamic loading | beta.6 |
| AssemblyLoadContext | Complexity, not needed for static registration | beta.6 |
| Sandboxing / permissions | Complexity | beta.6+ |
| Dependency injection | Over-engineering — constructor injection is sufficient | Never |
| Processor lifecycle (Init/Dispose) | Over-engineering — processors are simple | Deferred |
| `bool Process()` return for suppress | Additional complexity — use `NodeReplacements.Remove()` instead | Deferred |
| Built-in Kroki integration | External tool only — users implement `IDiagramToolRunner` | Deferred |
| Extension marketplace / registry | Over-engineering | Never |
| Processor priority / ordering hints | FIFO is simple and predictable | Deferred |
| Conditional processor registration | Users can check conditions before registering | Never |
| AST visitor / walker abstraction | Pipeline handles walking — no public walker needed | Deferred |

### CLI Extension Support — Deferred to beta.6

Beta.5 provides static registration through the C# API only. The CLI (`adocnet` tool)
does not expose extension registration. Users who need extensions use the API directly:

```csharp
var engine = new AdocEngine(renderer, AdocParser.Parse);
engine.RegisterBlockProcessor(new MyProcessor());
engine.Convert(input, output);
```

CLI integration (e.g., `adocnet --extension MyExtension.dll`) requires dynamic assembly
loading and is deferred to beta.6.

---

## Appendix A — File Plan

### New Files

| Path | Content |
|------|---------|
| `src/AdocNet.Core/Extensions/IDocumentProcessor.cs` | Interface |
| `src/AdocNet.Core/Extensions/IBlockProcessor.cs` | Interface |
| `src/AdocNet.Core/Extensions/IInlineProcessor.cs` | Interface |
| `src/AdocNet.Core/Extensions/IDiagramToolRunner.cs` | Interface |
| `src/AdocNet.Core/Extensions/NodeReplacements.cs` | Replacement collector |
| `src/AdocNet.Core/Extensions/ProcessingPipeline.cs` | Pipeline execution |
| `tests/AdocNet.Core.Tests/Extensions/ProcessingPipelineTests.cs` | Pipeline tests |
| `tests/AdocNet.Core.Tests/Extensions/NodeReplacementsTests.cs` | Replacement tests |

### Modified Files (additive only)

| Path | Change |
|------|--------|
| `src/AdocNet.Core/AdocEngine.cs` | Add Register* methods, OnWarning, run pipeline |
| `Directory.Build.props` | Version → `1.0.0-beta.5` |

---

## Appendix B — Pipeline Walk Algorithm

```
WalkBlocks(node, processors, context):
    for each child in node.Children where child is BlockNode:
        for each processor in processors:
            if processor.CanProcess(child):
                try: processor.Process(child, context)
                catch: OnWarning(message), continue
        ApplyReplacements(node, context)     // apply any Replace/Remove requests
        WalkBlocks(child, processors, context)  // recurse into children

WalkInlines(node, processors, context):
    // Similar depth-first walk for InlineNode children
    // Also walks into inline containers (Strong, Emphasis, etc.)
```

Replacements are applied **after all processors run for a given node** but **before
recursing into that node's children**. This means:
- A replacement node's children are walked (the replacement is part of the tree now)
- A removed node's children are NOT walked
- The original node is no longer in the tree after replacement

---

## Appendix C — Complete Convert() Flow

```csharp
public void Convert(string input, Stream output, RenderOptions? options = null)
{
    var doc = Parser(input);
    var opts = options ?? RenderOptions.Default;

    if (HasProcessors)
    {
        FreezeRegistration();
        var context = new RenderContext(doc, opts);
        ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors,
                               _inlineProcessors, OnWarning);
        Renderer.Render(doc, output, opts);
    }
    else
    {
        Renderer.Render(doc, output, opts);
    }
}
```

Note: when processors are registered, `RenderContext` is created early (before pipeline)
so processors and the renderer share the same context. When no processors are registered,
the renderer creates its own `RenderContext` internally (existing beta.4 behavior).
