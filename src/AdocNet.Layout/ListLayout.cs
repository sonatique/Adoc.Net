using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A list block containing ordered or unordered items.
/// </summary>
public sealed class ListLayout : BlockLayout
{
    /// <summary>
    /// Whether this is an ordered (numbered) list.
    /// </summary>
    public bool Ordered { get; }

    /// <summary>
    /// The list items.
    /// </summary>
    public IReadOnlyList<ListItemLayout> Items { get; }

    /// <summary>
    /// Creates a new list layout.
    /// </summary>
    /// <param name="ordered">True for ordered lists, false for unordered.</param>
    /// <param name="items">The list items.</param>
    public ListLayout(bool ordered, IReadOnlyList<ListItemLayout> items)
    {
        Ordered = ordered;
        Items = items;
    }
}
