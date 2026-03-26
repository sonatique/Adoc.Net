# Diagram Processing

AdocNet supports rendering diagram source blocks (PlantUML, Mermaid, etc.) by invoking
external tools. This is implemented as a block processor extension.

## How It Works

1. The parser produces a `DelimitedBlockNode` with `BlockKind = Source` and a `Language`
   like `"plantuml"` or `"mermaid"`.
2. `DiagramBlockProcessor` detects these blocks during the processing pipeline.
3. The processor invokes an `IDiagramToolRunner` to generate an image from the source.
4. On success, the source block is replaced with a `BlockImageNode` pointing to the image.

## Supported Languages

- `plantuml`
- `mermaid`
- `ditaa`
- `graphviz`
- `dot`

## Usage

```csharp
using AdocNet;
using AdocNet.Extensions;
using AdocNet.Parser;
using AdocNet.Converters.Html;

var runner = new ProcessDiagramToolRunner("plantuml", "-tpng {input} -o {output}");
var engine = new AdocEngine(new HtmlRenderer(), s => BlockParser.Parse(s).Document);

engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "./images/generated"));

using var output = File.Create("output.html");
engine.Convert(adocSource, output);
```

## IDiagramToolRunner

The `IDiagramToolRunner` interface abstracts tool invocation for testability:

```csharp
public interface IDiagramToolRunner
{
    bool IsAvailable { get; }
    string? Generate(string language, string source, string outputDirectory);
}
```

- `IsAvailable` — returns `true` if the tool can be found on the system.
- `Generate` — writes the diagram source to a file, runs the tool, and returns the
  path to the generated image. Returns `null` if generation fails.

## ProcessDiagramToolRunner

The built-in `ProcessDiagramToolRunner` spawns an external process:

```csharp
var runner = new ProcessDiagramToolRunner(
    executablePath: "plantuml",              // tool command
    arguments: "-tpng {input} -o {output}"   // {input} and {output} are replaced
);
```

Output filenames are deterministic (SHA256 hash of the source text), so identical
diagram source always produces the same file path.

## Fallback Behavior

When the diagram tool is unavailable or fails:

- The source block is left unchanged (rendered as a code block).
- No exception is thrown.
- If `AdocEngine.OnWarning` is set, it is not invoked for fallback (the block silently
  stays as-is). Exceptions from `IDiagramToolRunner.Generate` are caught by the
  pipeline and routed to `OnWarning`.

## Writing a Custom Tool Runner

Implement `IDiagramToolRunner` for your specific tool setup:

```csharp
public class KrokiToolRunner : IDiagramToolRunner
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public KrokiToolRunner(string baseUrl)
    {
        _baseUrl = baseUrl;
        _client = new HttpClient();
    }

    public bool IsAvailable => true; // always available if server is up

    public string? Generate(string language, string source, string outputDirectory)
    {
        // POST source to Kroki server, save response as PNG
        // Return the local file path
    }
}
```

## AsciiDoc Syntax

Standard AsciiDoc diagram block syntax is supported by the parser:

```asciidoc
[source,plantuml]
----
@startuml
Alice -> Bob: Hello
Bob -> Alice: Hi!
@enduml
----
```

With a title:

```asciidoc
.Sequence Diagram
[source,mermaid]
----
sequenceDiagram
    Alice->>Bob: Hello
    Bob->>Alice: Hi!
----
```

## See Also

- [Extensions Guide](EXTENSIONS.md) — writing custom processors
- [Renderers Guide](RENDERERS.md) — built-in renderer options
