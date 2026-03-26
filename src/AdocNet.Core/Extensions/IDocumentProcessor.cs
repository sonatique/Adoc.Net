using AdocNet.Ast;

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
    /// <param name="document">The root document node.</param>
    void Process(DocumentNode document);
}
