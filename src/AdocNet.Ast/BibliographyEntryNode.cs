namespace AdocNet.Ast;

/// <summary>
/// A bibliography entry: <c>- [[[ref-id]]] text</c> or <c>- [[[ref-id,Label]]] text</c>.
/// Bibliography entries appear as children of a section marked with <c>[bibliography]</c>.
/// </summary>
public sealed class BibliographyEntryNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.BibliographyEntry;
    public required string RefId { get; init; }
    public string? Label { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("RefId", RefId);
        if (Label is not null)
            yield return new("Label", Label);
        yield return new("Text", Text);
    }
}
