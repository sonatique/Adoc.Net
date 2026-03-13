namespace AdocNet.Ast;

/// <summary>
/// A description list (definition list) containing term-description pairs.
/// Children are <see cref="DescriptionItemNode"/> instances.
/// </summary>
public sealed class DescriptionListNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.DescriptionList;
}
