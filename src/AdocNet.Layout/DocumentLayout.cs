using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// Root of the layout tree. Contains an optional title and top-level block children.
/// </summary>
public sealed class DocumentLayout
{
    /// <summary>
    /// The document title, or null if the document has no title.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// The top-level block children of the document.
    /// </summary>
    public IReadOnlyList<BlockLayout> Children { get; }

    /// <summary>
    /// Creates a new document layout.
    /// </summary>
    /// <param name="title">The document title, or null.</param>
    /// <param name="children">The top-level block children.</param>
    public DocumentLayout(string? title, IReadOnlyList<BlockLayout> children)
    {
        Title = title;
        Children = children;
    }
}
