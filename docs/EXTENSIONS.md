# Extension Developer Guide

This guide covers how to extend AdocNet with custom renderers and include readers.

## Architecture Overview

AdocNet follows a strict **parse-then-render** pipeline:

1. **Parser** (`AdocParser.Parse`) reads AsciiDoc source text and produces an immutable AST rooted at `DocumentNode`.
2. **Renderers** consume the AST and write output to a `Stream`. Renderers never modify the AST.

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

## See Also

- [Usage Guide](USAGE.md) — parsing and rendering API
- [Renderers Guide](RENDERERS.md) — built-in renderer options
- [CLI Reference](CLI.md) — command-line tool
