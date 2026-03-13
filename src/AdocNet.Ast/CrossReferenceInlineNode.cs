namespace AdocNet.Ast;

/// <summary>
/// A cross-reference inline node: <c>&lt;&lt;target&gt;&gt;</c> or <c>&lt;&lt;target,label&gt;&gt;</c>.
/// Rendered as an internal document link.
/// </summary>
public sealed class CrossReferenceInlineNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.InlineCrossReference;
    public required string Target { get; init; }
    public string? Label { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        if (Label is not null)
            yield return new("Label", Label);
    }
}
