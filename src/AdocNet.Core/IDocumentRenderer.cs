using AdocNet.Ast;

namespace AdocNet;

public interface IDocumentRenderer
{
    string Format { get; }
    void Render(DocumentNode document, Stream output, RenderOptions options);
}
