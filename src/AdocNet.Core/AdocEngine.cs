using AdocNet.Ast;

namespace AdocNet;

public sealed class AdocEngine
{
    public IDocumentRenderer Renderer { get; init; }
    public Func<string, DocumentNode> Parser { get; init; }

    public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public void Convert(string input, Stream output, RenderOptions? options = null)
    {
        var doc = Parser(input);
        Renderer.Render(doc, output, options ?? RenderOptions.Default);
    }

    public void ConvertFile(string inputPath, Stream output, RenderOptions? options = null)
    {
        var text = File.ReadAllText(inputPath);
        Convert(text, output, options);
    }
}
