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
    /// Returns true if this processor handled the document and remaining document
    /// processors should be skipped.
    /// </summary>
    /// <param name="document">The root document node.</param>
    /// <param name="context">The render context for per-render state and diagnostics.</param>
    /// <returns>True to skip remaining document processors; false to continue.</returns>
    bool Process(DocumentNode document, RenderContext context);
}
