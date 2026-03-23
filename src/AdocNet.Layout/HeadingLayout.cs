using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A heading block with a level (1–6) and inline title content.
/// </summary>
public sealed class HeadingLayout : BlockLayout
{
    /// <summary>
    /// The heading level (1–6).
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// The inline content of the heading title.
    /// </summary>
    public IReadOnlyList<InlineLayout> Inlines { get; }

    /// <summary>
    /// Creates a new heading layout.
    /// </summary>
    /// <param name="level">The heading level (1–6).</param>
    /// <param name="inlines">The inline title content.</param>
    public HeadingLayout(int level, IReadOnlyList<InlineLayout> inlines)
    {
        Level = level;
        Inlines = inlines;
    }
}
