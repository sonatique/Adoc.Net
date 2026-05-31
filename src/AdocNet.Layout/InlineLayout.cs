namespace AdocNet.Layout;

/// <summary>
/// Abstract base class for all inline-level layout nodes.
/// </summary>
public abstract class InlineLayout
{
    /// <summary>
    /// Source range of the originating AsciiDoc inline node, as recorded on the
    /// AST during parsing. Populated by <see cref="Builders.LayoutBuilder"/> from
    /// each inline node's <see cref="Ast.AstNode.Source"/>; defaults to
    /// <see cref="SourceRange.None"/> for inlines built without an originating
    /// AST node.
    /// </summary>
    /// <remarks>
    /// Lets an editor map a rendered inline run back to its source span — and
    /// hit-test a click position to a source offset — at inline (not just block)
    /// granularity, which block-level <see cref="BlockLayout.Source"/> alone
    /// cannot provide.
    /// </remarks>
    public SourceRange Source { get; internal set; } = SourceRange.None;
}
