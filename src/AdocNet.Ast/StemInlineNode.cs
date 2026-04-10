namespace AdocNet.Ast;

/// <summary>
/// An inline mathematical formula, rendered by MathJax.
/// Created from <c>stem:[formula]</c>, <c>latexmath:[formula]</c>, or <c>asciimath:[formula]</c> macros.
/// </summary>
public sealed class StemInlineNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.StemInline;

    /// <summary>The raw math formula content (verbatim, no substitutions).</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The stem type: <c>"latexmath"</c> or <c>"asciimath"</c>.
    /// </summary>
    public required string StemType { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("StemType", StemType);
        yield return new("Content", Content);
    }
}
