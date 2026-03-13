namespace AdocNet.Ast;

public sealed class TocNode : BlockNode
{
    public TocPlacement Placement { get; init; } = TocPlacement.Auto;
    public IReadOnlyList<TocEntry> Entries { get; init; } = [];
    public override AstNodeKind Kind => AstNodeKind.Toc;
}
