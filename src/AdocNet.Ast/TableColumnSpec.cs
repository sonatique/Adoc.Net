namespace AdocNet.Ast;

public sealed class TableColumnSpec
{
    public required int Width { get; init; }
    public TableAlignment Alignment { get; init; } = TableAlignment.Left;
    public TableVerticalAlignment VerticalAlignment { get; init; } = TableVerticalAlignment.Top;
}
