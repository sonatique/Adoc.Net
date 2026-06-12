namespace AdocNet.Ast;

public sealed class TableColumnSpec
{
    public required int Width { get; init; }
    public TableAlignment Alignment { get; init; } = TableAlignment.Left;
    public TableVerticalAlignment VerticalAlignment { get; init; } = TableVerticalAlignment.Top;

    /// <summary>
    /// Default content style for cells in this column, from a trailing style letter in the column
    /// spec (e.g. the <c>a</c> in <c>cols="1,2a"</c> makes the second column's cells AsciiDoc).
    /// Null means no column-level style; a cell's own style prefix (<c>a|</c>, <c>l|</c>, …) always
    /// overrides the column default.
    /// </summary>
    public TableCellStyle? Style { get; init; }
}
