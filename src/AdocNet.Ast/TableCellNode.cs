namespace AdocNet.Ast;

/// <summary>
/// A single cell in a table row. Holds raw text and inline-parsed content.
/// </summary>
public sealed class TableCellNode : AstNode
{
    public override AstNodeKind Kind => AstNodeKind.TableCell;

    public required string Text { get; init; }

    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    /// <summary>Number of columns this cell spans (default 1).</summary>
    public int ColSpan { get; init; } = 1;

    /// <summary>Number of rows this cell spans (default 1).</summary>
    public int RowSpan { get; init; } = 1;

    /// <summary>Per-cell alignment override, or null to inherit from column spec.</summary>
    public TableAlignment? Alignment { get; init; }

    /// <summary>Per-cell vertical alignment override, or null to inherit from column spec.</summary>
    public TableVerticalAlignment? VerticalAlignment { get; init; }

    /// <summary>Cell content style (e.g. emphasis, header, monospace). Default means no special style.</summary>
    public TableCellStyle ContentStyle { get; init; } = TableCellStyle.Default;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Text", Text);
        if (ColSpan > 1)
            yield return new("ColSpan", ColSpan.ToString());
        if (RowSpan > 1)
            yield return new("RowSpan", RowSpan.ToString());
        if (Alignment is not null)
            yield return new("Alignment", Alignment.Value.ToString());
        if (VerticalAlignment is not null)
            yield return new("VerticalAlignment", VerticalAlignment.Value.ToString());
        if (ContentStyle != TableCellStyle.Default)
            yield return new("ContentStyle", ContentStyle.ToString());
    }

    /// <inheritdoc />
    protected override IEnumerable<AstNode> GetStructuralInlines() => Inlines;
}
