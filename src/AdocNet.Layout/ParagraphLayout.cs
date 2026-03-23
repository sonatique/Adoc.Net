using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A paragraph block containing inline content.
/// </summary>
public sealed class ParagraphLayout : BlockLayout
{
    /// <summary>
    /// The inline content of the paragraph.
    /// </summary>
    public IReadOnlyList<InlineLayout> Inlines { get; }

    /// <summary>
    /// Creates a new paragraph layout.
    /// </summary>
    /// <param name="inlines">The inline content.</param>
    public ParagraphLayout(IReadOnlyList<InlineLayout> inlines)
    {
        Inlines = inlines;
    }
}
