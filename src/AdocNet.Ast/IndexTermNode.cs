namespace AdocNet.Ast;

/// <summary>
/// A visible index term <c>((term))</c> that appears in the text and is collected for indexing.
/// </summary>
public sealed class IndexTermNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.IndexTerm;

    /// <summary>1–3 comma-separated terms (primary, secondary, tertiary).</summary>
    public required IReadOnlyList<string> Terms { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        for (int i = 0; i < Terms.Count; i++)
            yield return new($"Term[{i}]", Terms[i]);
    }
}
