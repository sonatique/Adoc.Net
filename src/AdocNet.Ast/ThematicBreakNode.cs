namespace AdocNet.Ast;

/// <summary>
/// A thematic break (<c>'''</c>), rendered as a horizontal rule.
/// </summary>
public sealed class ThematicBreakNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.ThematicBreak;
}
