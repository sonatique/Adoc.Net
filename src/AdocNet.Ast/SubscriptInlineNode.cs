namespace AdocNet.Ast;

/// <summary>Subscript text delimited by <c>~content~</c>.</summary>
public sealed class SubscriptInlineNode : InlineNode
{
    public required string Content { get; init; }
    public override AstNodeKind Kind => AstNodeKind.InlineSubscript;
    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Content", Content);
    }
}
