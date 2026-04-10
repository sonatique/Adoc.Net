namespace AdocNet.Ast;

/// <summary>
/// A block-level mathematical formula, rendered by MathJax.
/// Created from <c>[stem]</c>, <c>[latexmath]</c>, or <c>[asciimath]</c> delimited blocks.
/// </summary>
public sealed class StemBlockNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.StemBlock;

    /// <summary>The raw math formula content (verbatim, no substitutions).</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The stem type: <c>"latexmath"</c> or <c>"asciimath"</c>.
    /// Determined by the block style or the <c>:stem:</c> document attribute.
    /// </summary>
    public required string StemType { get; init; }

    /// <summary>Optional block title from a preceding <c>.Title</c> line.</summary>
    public string? Title { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("StemType", StemType);
        yield return new("Content", Content);
        if (Title is not null)
            yield return new("Title", Title);
    }
}
