using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Collects node replacement and removal requests during processor execution.
/// Processors register replacements; the pipeline applies them after each pass.
/// </summary>
public sealed class NodeReplacements
{
    private readonly Dictionary<AstNode, AstNode?> _replacements = new();

    /// <summary>
    /// Registers a replacement: the original node will be replaced by the
    /// replacement node in its parent's children list.
    /// </summary>
    /// <param name="original">The node to replace.</param>
    /// <param name="replacement">The node to insert in its place.</param>
    public void Replace(AstNode original, AstNode replacement)
    {
        _replacements[original ?? throw new ArgumentNullException(nameof(original))]
            = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    /// <summary>
    /// Registers a removal: the original node will be removed from its
    /// parent's children list.
    /// </summary>
    /// <param name="original">The node to remove.</param>
    public void Remove(AstNode original)
    {
        _replacements[original ?? throw new ArgumentNullException(nameof(original))] = null;
    }

    /// <summary>Gets whether any replacements or removals have been registered.</summary>
    internal bool HasPending => _replacements.Count > 0;

    /// <summary>
    /// Tries to get the replacement for the given node.
    /// Returns true if the node has a pending replacement or removal.
    /// When <paramref name="replacement"/> is null, the node should be removed.
    /// </summary>
    internal bool TryGet(AstNode original, out AstNode? replacement)
        => _replacements.TryGetValue(original, out replacement);

    /// <summary>Clears all pending replacements.</summary>
    internal void Clear() => _replacements.Clear();
}
