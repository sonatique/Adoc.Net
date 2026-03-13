namespace AdocNet.Ast;

/// <summary>
/// A hidden index term <c>(((term,subterm,subsubterm)))</c> that does NOT appear in the output text.
/// </summary>
public sealed class IndexTermHiddenNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.IndexTermHidden;

    /// <summary>1–3 comma-separated terms (primary, secondary, tertiary).</summary>
    public required IReadOnlyList<string> Terms { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        for (int i = 0; i < Terms.Count; i++)
            yield return new($"Term[{i}]", Terms[i]);
    }
}
