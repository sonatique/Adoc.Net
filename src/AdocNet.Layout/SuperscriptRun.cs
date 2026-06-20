using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A superscript inline run (<c>^text^</c>, and footnote reference markers)
/// containing nested inline content. Renderers draw it smaller and raised above
/// the baseline.
/// </summary>
public sealed class SuperscriptRun : InlineLayout
{
    /// <summary>
    /// The nested inline content.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new superscript run.
    /// </summary>
    /// <param name="children">The nested inline content.</param>
    public SuperscriptRun(IReadOnlyList<InlineLayout> children)
    {
        Children = children;
    }
}
