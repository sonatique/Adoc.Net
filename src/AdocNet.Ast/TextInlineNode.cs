namespace AdocNet.Ast;

/// <summary>A run of plain text with no inline markup.</summary>
public sealed class TextInlineNode : InlineNode
{
    public required string Value { get; init; }
    public override AstNodeKind Kind => AstNodeKind.InlineText;
    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Value", Value);
    }
}
