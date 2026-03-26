using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Block processor that detects diagram source blocks (PlantUML, Mermaid, etc.),
/// invokes an external tool via <see cref="IDiagramToolRunner"/>, and replaces
/// the source block with a <see cref="BlockImageNode"/> pointing to the generated image.
/// When the tool is unavailable or fails, the block is left unchanged as source code.
/// </summary>
public sealed class DiagramBlockProcessor : IBlockProcessor
{
    private readonly IDiagramToolRunner _runner;
    private readonly string _outputDirectory;

    /// <summary>
    /// Initializes the diagram processor.
    /// </summary>
    /// <param name="toolRunner">The tool runner that generates images from diagram source.</param>
    /// <param name="outputDirectory">Directory to write generated image files.</param>
    public DiagramBlockProcessor(IDiagramToolRunner toolRunner, string outputDirectory)
    {
        _runner = toolRunner ?? throw new ArgumentNullException(nameof(toolRunner));
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
    }

    /// <inheritdoc />
    public bool CanProcess(BlockNode node)
    {
        return node is DelimitedBlockNode { BlockKind: DelimitedBlockKind.Source } block
            && IsDiagramLanguage(block.Language);
    }

    /// <inheritdoc />
    public void Process(BlockNode node, RenderContext context)
    {
        var block = (DelimitedBlockNode)node;

        if (!_runner.IsAvailable)
            return; // Fallback: leave as code block

        string? imagePath;
        try
        {
            imagePath = _runner.Generate(block.Language!, block.Content ?? "", _outputDirectory);
        }
        catch
        {
            return; // Fallback: leave as code block (pipeline catches and warns)
        }

        if (imagePath is null)
            return; // Fallback: tool returned nothing

        var imageNode = new BlockImageNode
        {
            Target = imagePath,
            Alt = block.Title ?? "Diagram",
            Title = block.Title,
            Id = block.Id,
        };

        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, imageNode);
    }

    private static bool IsDiagramLanguage(string? language)
        => language is "plantuml" or "mermaid" or "ditaa" or "graphviz" or "dot";
}
