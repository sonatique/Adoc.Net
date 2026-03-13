namespace AdocNet.Ast;

/// <summary>Superscript text delimited by <c>^content^</c>.</summary>
public sealed class SuperscriptInlineNode : InlineNode
{
    public required string Content { get; init; }
    public override AstNodeKind Kind => AstNodeKind.InlineSuperscript;
    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Content", Content);
    }
}
