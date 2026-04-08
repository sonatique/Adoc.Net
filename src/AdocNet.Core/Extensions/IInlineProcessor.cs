using AdocNet.Ast;

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
    /// <param name="node">The inline node to test.</param>
    /// <returns>True if <see cref="Process"/> should be called for this node.</returns>
    bool CanProcess(InlineNode node);

    /// <summary>
    /// Processes the inline node. May mutate the node's properties or use
    /// <see cref="RenderContext.GetOrCreate{T}"/> to register node replacements.
    /// Returns true if this processor handled the node and remaining inline
    /// processors should be skipped for this node.
    /// </summary>
    /// <param name="node">The inline node to process.</param>
    /// <param name="context">The render context for per-render state.</param>
    /// <returns>True to skip remaining inline processors for this node; false to continue.</returns>
    bool Process(InlineNode node, RenderContext context);
}
