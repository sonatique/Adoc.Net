using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// An italic/emphasis inline run containing nested inline content.
/// </summary>
public sealed class ItalicRun : InlineLayout
{
    /// <summary>
    /// The nested inline content.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new italic run.
    /// </summary>
    /// <param name="children">The nested inline content.</param>
    public ItalicRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
