namespace AdocNet.Ast;

/// <summary>
/// A page break block (<c>&lt;&lt;&lt;</c>).
/// </summary>
public sealed class PageBreakNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.PageBreak;
}
