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
    /// </summary>
    /// <param name="node">The inline node to process.</param>
    /// <param name="context">The render context for per-render state.</param>
    void Process(InlineNode node, RenderContext context);
}
