using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A bold/strong inline run containing nested inline content.
/// </summary>
public sealed class BoldRun : InlineLayout
{
    /// <summary>
    /// The nested inline content.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new bold run.
    /// </summary>
    /// <param name="children">The nested inline content.</param>
    public BoldRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
