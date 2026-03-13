namespace AdocNet.Ast;

public sealed class CalloutEntry
{
    public required int Number { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<InlineNode> Inlines { get; init; } = [];

    /// <summary>Zero-based line number in the source block where this callout marker appeared. -1 if unknown.</summary>
    public int LineNumber { get; init; } = -1;
}
