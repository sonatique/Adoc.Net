using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A monospace/code inline run containing nested inline content.
/// </summary>
public sealed class MonoRun : InlineLayout
{
    /// <summary>
    /// The nested inline content.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new monospace run.
    /// </summary>
    /// <param name="children">The nested inline content.</param>
    public MonoRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
