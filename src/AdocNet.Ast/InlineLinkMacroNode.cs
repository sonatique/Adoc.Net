namespace AdocNet.Ast;

/// <summary>
/// An explicit <c>link:URL[label]</c> inline macro.
/// Distinguished from <see cref="LinkInlineNode"/> which represents bare auto-detected URLs.
/// </summary>
public sealed class InlineLinkMacroNode : InlineNode
{
    public required string Url { get; init; }

    /// <summary>
    /// The display label from the bracket content. Empty string if brackets were empty.
    /// </summary>
    public required string Label { get; init; }

    public override AstNodeKind Kind => AstNodeKind.InlineLinkMacro;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Url", Url);
        yield return new("Label", Label);
    }
}
