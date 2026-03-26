using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Processes individual block nodes in the AST.
/// Runs after document processors, before inline processors.
/// </summary>
public interface IBlockProcessor
{
    /// <summary>
    /// Returns true if this processor should handle the given block node.
    /// Called for every block node during the tree walk.
    /// </summary>
    /// <param name="node">The block node to test.</param>
    /// <returns>True if <see cref="Process"/> should be called for this node.</returns>
    bool CanProcess(BlockNode node);

    /// <summary>
    /// Processes the block node. May mutate the node's properties or use
    /// <see cref="RenderContext.GetOrCreate{T}"/> to register node replacements.
    /// </summary>
    /// <param name="node">The block node to process.</param>
    /// <param name="context">The render context for per-render state.</param>
    void Process(BlockNode node, RenderContext context);
}
