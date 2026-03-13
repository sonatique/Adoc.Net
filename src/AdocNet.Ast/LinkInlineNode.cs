namespace AdocNet.Ast;

/// <summary>A bare URL link (http:// or https://).</summary>
public sealed class LinkInlineNode : InlineNode
{
    public required string Url { get; init; }
    public override AstNodeKind Kind => AstNodeKind.InlineLink;
    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Url", Url);
    }
}
