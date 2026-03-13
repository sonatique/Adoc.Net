namespace AdocNet.Ast;

/// <summary>
/// An inline <c>image:target[alt]</c> macro.
/// </summary>
public sealed class InlineImageNode : InlineNode
{
    public required string Target { get; init; }

    /// <summary>
    /// The alt text from the bracket content. Empty string if brackets were empty.
    /// </summary>
    public required string Alt { get; init; }

    public override AstNodeKind Kind => AstNodeKind.InlineImage;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        yield return new("Alt", Alt);
    }
}
