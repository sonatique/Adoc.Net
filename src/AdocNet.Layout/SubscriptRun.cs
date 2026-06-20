using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A subscript inline run (<c>~text~</c>) containing nested inline content.
/// Renderers draw it smaller and lowered below the baseline.
/// </summary>
public sealed class SubscriptRun : InlineLayout
{
    /// <summary>
    /// The nested inline content.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new subscript run.
    /// </summary>
    /// <param name="children">The nested inline content.</param>
    public SubscriptRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
