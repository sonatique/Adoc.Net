namespace AdocNet.Layout;

/// <summary>
/// Abstract base class for all block-level layout nodes.
/// </summary>
public abstract class BlockLayout
{
    /// <summary>
    /// Source range of the originating AsciiDoc node, as recorded on the AST
    /// during parsing. Populated by <see cref="Builders.LayoutBuilder"/> from
    /// each emitted block's source <see cref="Ast.AstNode.Source"/>. Defaults
    /// to <see cref="SourceRange.None"/> for blocks constructed directly
    /// (without an originating AST node).
    /// </summary>
    /// <remarks>
    /// Consumers use this to map document blocks back to their source
    /// positions — e.g. for editor sync-scroll, mapping a layout block's
    /// rendered Y coordinate to a source line.
    /// </remarks>
    public SourceRange Source { get; init; } = SourceRange.None;
}
