using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// An admonition block (note, tip, warning, important, caution) containing body blocks.
/// </summary>
public sealed class AdmonitionLayout : BlockLayout
{
    /// <summary>
    /// The kind of admonition.
    /// </summary>
    public AdmonitionKind Kind { get; }

    /// <summary>
    /// The body content of the admonition as blocks.
    /// </summary>
    public IReadOnlyList<BlockLayout> Blocks { get; }

    /// <summary>
    /// Creates a new admonition layout.
    /// </summary>
    /// <param name="kind">The admonition kind.</param>
    /// <param name="blocks">The body content blocks.</param>
    public AdmonitionLayout(AdmonitionKind kind, IReadOnlyList<BlockLayout> blocks)
    {
        Kind = kind;
        Blocks = blocks;
    }
}
