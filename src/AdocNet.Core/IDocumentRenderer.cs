using AdocNet.Ast;

namespace AdocNet;

/// <summary>
/// Renders a parsed AsciiDoc document to an output format (e.g. HTML, PDF).
/// </summary>
public interface IDocumentRenderer
{
    /// <summary>Gets the output format name (e.g. "html", "pdf").</summary>
    string Format { get; }

    /// <summary>
    /// Renders the document AST to the specified output stream.
    /// </summary>
    /// <param name="document">The parsed document AST.</param>
    /// <param name="output">The stream to write the rendered output to.</param>
    /// <param name="options">Options controlling the rendering.</param>
    void Render(DocumentNode document, Stream output, RenderOptions options);
}
