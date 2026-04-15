namespace AdocNet.Ast;

/// <summary>
/// A delimited block: literal (....),  listing/source (----), example (====),
/// quote (____), or sidebar (****).
/// For verbatim blocks (Literal, Listing, Source) the raw text is in <see cref="Content"/>.
/// For structural blocks (Example, Quote, Sidebar), <see cref="Content"/> is null and
/// parsed child nodes are in <see cref="AstNode.Children"/>.
/// </summary>
public sealed class DelimitedBlockNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.DelimitedBlock;

    public required DelimitedBlockKind BlockKind { get; init; }

    /// <summary>Raw text content for Literal, Listing, and Source blocks. Null for Example blocks.</summary>
    public string? Content { get; init; }

    /// <summary>Optional block title from a preceding <c>.Title</c> line.</summary>
    public string? Title { get; init; }

    /// <summary>Source language for Source blocks (e.g. "csharp"). Null for all other kinds.</summary>
    public string? Language { get; init; }

    /// <summary>Attribution for Quote blocks (e.g. "Werner Vogels"). Null for all other kinds.</summary>
    public string? Attribution { get; init; }

    /// <summary>Citation source for Quote blocks. Null when not specified.</summary>
    public string? CitationSource { get; init; }

    /// <summary>
    /// Optional block style such as <c>"abstract"</c>, set via <c>[abstract]</c>.
    /// </summary>
    public string? Style { get; init; }

    /// <summary>
    /// Whether this block was marked with <c>[%collapsible]</c>.
    /// When true, the HTML renderer wraps the block in &lt;details&gt;/&lt;summary&gt;.
    /// </summary>
    public bool IsCollapsible { get; init; }

    /// <summary>Callout explanations associated with source/listing blocks. Null when no callouts present.</summary>
    public IReadOnlyList<CalloutEntry>? Callouts { get; init; }

    /// <summary>
    /// Line highlight specification from <c>[highlight="1,3,5-7"]</c> on source blocks.
    /// Raw string — parsed into line numbers by the renderer.
    /// </summary>
    public string? Highlight { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("BlockKind", BlockKind.ToString());
        if (IsCollapsible)              yield return new("IsCollapsible", "true");
        if (Style is not null)          yield return new("Style", Style);
        if (Title is not null)          yield return new("Title", Title);
        if (Language is not null)       yield return new("Language", Language);
        if (Attribution is not null)    yield return new("Attribution", Attribution);
        if (CitationSource is not null) yield return new("CitationSource", CitationSource);
        if (Content is not null)        yield return new("Content", Content);
        if (Highlight is not null)      yield return new("Highlight", Highlight);
        if (Callouts is not null)
        {
            foreach (var c in Callouts)
                yield return new($"Callout[{c.Number}]", c.Text);
        }
    }
}
