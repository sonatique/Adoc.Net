using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A description/definition list containing term-description pairs.
/// </summary>
public sealed class DescriptionListLayout : BlockLayout
{
    /// <summary>
    /// The term-description items.
    /// </summary>
    public IReadOnlyList<DescriptionItemLayout> Items { get; }

    /// <summary>
    /// Creates a new description list layout.
    /// </summary>
    public DescriptionListLayout(IReadOnlyList<DescriptionItemLayout> items)
    {
        Items = items;
    }
}
