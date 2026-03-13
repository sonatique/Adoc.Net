namespace AdocNet.Ast;

/// <summary>
/// An unordered or ordered list block.
/// Direct children are <see cref="ListItemNode"/> instances.
/// </summary>
public sealed class ListNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.List;

    public required ListKind ListKind { get; init; }

    public int? Start { get; init; }

    public string? ListStyle { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("ListKind", ListKind.ToString());
        if (Start is not null)
            yield return new("Start", Start.Value.ToString());
        if (ListStyle is not null)
            yield return new("ListStyle", ListStyle);
    }
}
