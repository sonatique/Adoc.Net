namespace AdocNet.Ast;

/// <summary>
/// An inline footnote: <c>footnote:[text]</c> (anonymous),
/// <c>footnote:id[text]</c> (named), or <c>footnote:id[]</c> (back-reference).
/// </summary>
public sealed class FootnoteInlineNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.InlineFootnote;

    /// <summary>
    /// The footnote ID. Null for anonymous footnotes.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The raw footnote text. Null for back-references (footnote:id[]).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Parsed inline content of the footnote text. Empty for back-references.
    /// </summary>
    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        if (Id is not null)
            yield return new("Id", Id);
        if (Text is not null)
            yield return new("Text", Text);
    }
}
