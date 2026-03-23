using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A single item in a list, containing inline text and optional nested blocks.
/// </summary>
public sealed class ListItemLayout : BlockLayout
{
    /// <summary>
    /// The inline text content of the list item.
    /// </summary>
    public IReadOnlyList<InlineLayout> Inlines { get; }

    /// <summary>
    /// Nested blocks within the list item (e.g. nested lists). Empty if none.
    /// </summary>
    public IReadOnlyList<BlockLayout> Blocks { get; }

    /// <summary>
    /// Creates a new list item layout.
    /// </summary>
    /// <param name="inlines">The inline text content.</param>
    /// <param name="blocks">Nested blocks within the item.</param>
    public ListItemLayout(IReadOnlyList<InlineLayout> inlines, IReadOnlyList<BlockLayout> blocks)
    {
        Inlines = inlines;
        Blocks = blocks;
    }
}
