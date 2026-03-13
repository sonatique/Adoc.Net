namespace AdocNet.Ast;

/// <summary>
/// A single row in a table. Children are <see cref="TableCellNode"/> instances.
/// </summary>
public sealed class TableRowNode : AstNode
{
    public override AstNodeKind Kind => AstNodeKind.TableRow;
}
