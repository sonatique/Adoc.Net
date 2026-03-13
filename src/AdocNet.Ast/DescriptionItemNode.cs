namespace AdocNet.Ast;

/// <summary>
/// A single term-description pair in a description list.
/// </summary>
public sealed class DescriptionItemNode : BlockNode
{
    public required string Term { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<InlineNode> TermInlines { get; init; }
    public required IReadOnlyList<InlineNode> DescriptionInlines { get; init; }

    public override AstNodeKind Kind => AstNodeKind.DescriptionItem;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Term", Term);
        yield return new("Description", Description);
    }
}
