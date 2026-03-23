using AdocNet.Ast;

namespace AdocNet;

/// <summary>
/// High-level facade that combines parsing and rendering of AsciiDoc source text.
/// </summary>
public sealed class AdocEngine
{
    /// <summary>Gets the renderer used to produce output.</summary>
    public IDocumentRenderer Renderer { get; init; }

    /// <summary>Gets the parser function that converts AsciiDoc source text into a document AST.</summary>
    public Func<string, DocumentNode> Parser { get; init; }

    /// <summary>
    /// Initializes a new <see cref="AdocEngine"/> with the specified renderer and parser.
    /// </summary>
    /// <param name="renderer">The renderer used to produce output.</param>
    /// <param name="parser">A function that parses AsciiDoc source text into a <see cref="DocumentNode"/>.</param>
    public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Parses the AsciiDoc <paramref name="input"/> and writes the rendered output to <paramref name="output"/>.
    /// </summary>
    /// <param name="input">The AsciiDoc source text.</param>
    /// <param name="output">The stream to write the rendered output to.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    public void Convert(string input, Stream output, RenderOptions? options = null)
    {
        var doc = Parser(input);
        Renderer.Render(doc, output, options ?? RenderOptions.Default);
    }

    /// <summary>
    /// Reads an AsciiDoc file from disk, parses it, and writes the rendered output to <paramref name="output"/>.
    /// </summary>
    /// <param name="inputPath">The path to the AsciiDoc source file.</param>
    /// <param name="output">The stream to write the rendered output to.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    public void ConvertFile(string inputPath, Stream output, RenderOptions? options = null)
    {
        var text = File.ReadAllText(inputPath);
        Convert(text, output, options);
    }
}
