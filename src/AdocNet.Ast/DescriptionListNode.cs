namespace AdocNet.Ast;

/// <summary>
/// A description list (definition list) containing term-description pairs.
/// Children are <see cref="DescriptionItemNode"/> instances.
/// </summary>
public sealed class DescriptionListNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.DescriptionList;

    /// <summary>
    /// Optional style: "qanda" for Q&amp;A, "horizontal" for horizontal layout.
    /// Null for default description list style.
    /// </summary>
    public string? Style { get; set; }

    /// <inheritdoc />
    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        if (Style is not null) yield return new("Style", Style);
    }

    /// <inheritdoc />
    protected override int MixAdditionalState(int hash)
    {
        hash = base.MixAdditionalState(hash);
        if (Style is not null) hash = FnvMixString(hash, Style);
        return hash;
    }
}
