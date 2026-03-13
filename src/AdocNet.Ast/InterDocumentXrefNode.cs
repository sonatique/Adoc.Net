namespace AdocNet.Ast;

/// <summary>
/// An inter-document cross-reference: <c>xref:path#id[label]</c> or <c>&lt;&lt;path#id,label&gt;&gt;</c>.
/// Rendered as a link to another document.
/// </summary>
public sealed class InterDocumentXrefNode : InlineNode
{
    public required string Path { get; init; }
    public string? Id { get; init; }
    public string? Label { get; init; }
    public override AstNodeKind Kind => AstNodeKind.InterDocumentXref;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Path", Path);
        if (Id is not null)
            yield return new("Id", Id);
        if (Label is not null)
            yield return new("Label", Label);
    }
}
