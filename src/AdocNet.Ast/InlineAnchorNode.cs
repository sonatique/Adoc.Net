namespace AdocNet.Ast;

/// <summary>
/// An inline anchor node: <c>[[id]]</c> inside flowing text creates a referenceable anchor point.
/// Rendered as <c>&lt;a id="the-id"&gt;&lt;/a&gt;</c>.
/// </summary>
public sealed class InlineAnchorNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.InlineAnchor;
    public required string Id { get; init; }
    public string? Reftext { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Id", Id);
        if (Reftext is not null)
            yield return new("Reftext", Reftext);
    }
}
