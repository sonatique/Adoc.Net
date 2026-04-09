namespace AdocNet.Ast;

/// <summary>
/// A single item in an unordered or ordered list.
/// <see cref="Text"/> holds the raw item text for backward-compatible access.
/// <see cref="Inlines"/> holds the parsed inline nodes of the item text.
/// Any nested lists are stored as <see cref="AstNode.Children"/> of this node.
/// </summary>
public sealed class ListItemNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.ListItem;

    public required string Text { get; set; }

    /// <summary>Parsed inline nodes of the item text. Empty when set directly in tests.</summary>
    public IReadOnlyList<InlineNode> Inlines { get; set; } = [];

    /// <summary>
    /// For checklist items: true = checked ([x]), false = unchecked ([ ]), null = not a checklist item.
    /// </summary>
    public bool? Checked { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Text", Text);
        if (Checked is not null)
            yield return new("Checked", Checked.Value.ToString());
    }

    /// <inheritdoc />
    protected override IEnumerable<AstNode> GetStructuralInlines() => Inlines;
}
