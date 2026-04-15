namespace AdocNet.Ast;

/// <summary>
/// A single term-description pair in a description list.
/// </summary>
public sealed class DescriptionItemNode : BlockNode
{
    /// <summary>
    /// All terms sharing this definition. Most items have a single term.
    /// Multiple terms occur when consecutive term-only lines precede a definition.
    /// </summary>
    public required IReadOnlyList<string> Terms { get; init; }

    public required string Description { get; init; }

    /// <summary>Inline nodes for the first term (primary term for backward compat).</summary>
    public required IReadOnlyList<InlineNode> TermInlines { get; init; }

    /// <summary>Inline nodes for all terms, one list per term.</summary>
    public IReadOnlyList<IReadOnlyList<InlineNode>>? AllTermInlines { get; init; }

    public required IReadOnlyList<InlineNode> DescriptionInlines { get; init; }

    public override AstNodeKind Kind => AstNodeKind.DescriptionItem;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Terms", string.Join(", ", Terms));
        yield return new("Description", Description);
    }

    /// <inheritdoc />
    protected override IEnumerable<AstNode> GetStructuralInlines()
    {
        foreach (var i in TermInlines) yield return i;
        foreach (var i in DescriptionInlines) yield return i;
    }
}
