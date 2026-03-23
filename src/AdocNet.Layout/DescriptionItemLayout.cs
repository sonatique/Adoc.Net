using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A single term-description pair in a description list.
/// </summary>
public sealed class DescriptionItemLayout
{
    /// <summary>
    /// The term (label) inlines.
    /// </summary>
    public IReadOnlyList<InlineLayout> Term { get; }

    /// <summary>
    /// The description inlines.
    /// </summary>
    public IReadOnlyList<InlineLayout> Description { get; }

    /// <summary>
    /// Creates a new description item layout.
    /// </summary>
    public DescriptionItemLayout(IReadOnlyList<InlineLayout> term, IReadOnlyList<InlineLayout> description)
    {
        Term = term;
        Description = description;
    }
}
