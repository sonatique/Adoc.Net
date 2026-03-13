namespace AdocNet.Ast;

public sealed class TocEntry
{
    public required int Level { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<TocEntry> Children { get; init; } = [];
}
