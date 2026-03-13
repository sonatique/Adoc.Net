namespace AdocNet.Ast;

/// <summary>
/// A paragraph. <see cref="Text"/> holds the raw source string for backward-compatible access.
/// <see cref="Inlines"/> holds the parsed inline nodes when this node was produced by the block
/// parser. When <see cref="Inlines"/> is non-empty the pretty-printer renders them as children
/// instead of the raw <see cref="Text"/> property so inline structure is easy to inspect.
/// </summary>
public sealed class ParagraphNode : BlockNode
{
    public required string Text { get; init; }

    /// <summary>
    /// Optional block style such as <c>"abstract"</c>, set via <c>[abstract]</c>.
    /// </summary>
    public string? Style { get; init; }

    /// <summary>
    /// When true, each source line break within this paragraph is rendered as a <c>&lt;br&gt;</c>
    /// in HTML output. Set by the <c>[%hardbreaks]</c> block option or the
    /// <c>:hardbreaks-option:</c> document attribute.
    /// </summary>
    public bool HasHardbreaks { get; init; }

    /// <summary>
    /// Parsed inline nodes. Defaults to empty when the node is created directly in tests without
    /// going through the block parser.
    /// </summary>
    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    public override AstNodeKind Kind => AstNodeKind.Paragraph;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        // Show raw Text only when there are no parsed inlines (direct construction in tests).
        // When Inlines is populated the pretty-printer renders them as children instead.
        if (Inlines.Count == 0)
            yield return new("Text", Text);
    }
}
