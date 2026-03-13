namespace AdocNet.Ast;

/// <summary>
/// A section with a heading level (1 = ==, 2 = ===, etc.) and title text.
/// <see cref="Title"/> holds the raw title string for backward-compatible access.
/// <see cref="TitleInlines"/> holds the parsed inline nodes of the title.
/// </summary>
public sealed class SectionNode : BlockNode
{
    public required int Level { get; init; }
    public required string Title { get; init; }

    /// <summary>
    /// When true, the heading is discrete: rendered visually but excluded from
    /// section hierarchy, TOC, and numbering.
    /// </summary>
    public bool IsDiscrete { get; init; }

    /// <summary>
    /// Captures the state of the <c>:sectnums:</c> attribute at the point
    /// this section was parsed. When <c>null</c>, the renderer falls back
    /// to the document-level setting.
    /// </summary>
    public bool? SectnumsEnabled { get; init; }

    /// <summary>Parsed inline nodes of the section title. Empty when set directly in tests.</summary>
    public IReadOnlyList<InlineNode> TitleInlines { get; init; } = [];

    public override AstNodeKind Kind => AstNodeKind.Section;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Level", Level.ToString());
        yield return new("Title", Title);
    }
}
