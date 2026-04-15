namespace AdocNet.Ast;

/// <summary>
/// An admonition block: NOTE, TIP, IMPORTANT, WARNING, or CAUTION.
/// Inline admonitions store text directly. Block admonitions have parsed children.
/// </summary>
public sealed class AdmonitionNode : BlockNode
{
    public required string AdmonitionType { get; init; }

    /// <summary>
    /// Optional block title (set by <c>.Title</c> line above the admonition).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Raw text for inline admonitions (e.g. "NOTE: text"). Null for block admonitions.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Parsed inline nodes for inline admonitions. Empty for block admonitions.
    /// </summary>
    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    public override AstNodeKind Kind => AstNodeKind.Admonition;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("AdmonitionType", AdmonitionType);
        if (Text is not null)
            yield return new("Text", Text);
    }

    /// <inheritdoc />
    protected override IEnumerable<AstNode> GetStructuralInlines() => Inlines;
}
