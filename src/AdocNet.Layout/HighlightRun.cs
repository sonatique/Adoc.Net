using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// Marked (highlighted) inline content: AsciiDoc <c>#text#</c> with no role.
/// </summary>
public sealed class HighlightRun : InlineLayout
{
    /// <summary>The highlighted content.</summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    public HighlightRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
