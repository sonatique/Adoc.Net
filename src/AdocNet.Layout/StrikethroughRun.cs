using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// Struck-through inline content (the <c>line-through</c> role, Markdown <c>~~text~~</c>).
/// </summary>
public sealed class StrikethroughRun : InlineLayout
{
    /// <summary>The struck-through content.</summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    public StrikethroughRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
