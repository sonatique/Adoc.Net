using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A hyperlink inline run containing a URL and display content.
/// </summary>
public sealed class LinkRun : InlineLayout
{
    /// <summary>
    /// The link URL or target.
    /// </summary>
    public string Href { get; }

    /// <summary>
    /// The display content of the link.
    /// </summary>
    public IReadOnlyList<InlineLayout> Children { get; }

    /// <summary>
    /// Creates a new link run.
    /// </summary>
    /// <param name="href">The link URL or target.</param>
    /// <param name="children">The display content.</param>
    public LinkRun(string href, IReadOnlyList<InlineLayout> children)
    {
        Href = href;
        Children = children;
    }
}
