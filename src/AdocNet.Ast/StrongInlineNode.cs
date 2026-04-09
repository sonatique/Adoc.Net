namespace AdocNet.Ast;

/// <summary>Bold/strong text delimited by <c>*content*</c>.</summary>
public sealed class StrongInlineNode : InlineNode
{
    public new required IReadOnlyList<InlineNode> Children { get; init; }

    /// <summary>
    /// Optional roles from <c>[.role]*text*</c> syntax. When non-empty, the renderer
    /// adds a <c>class="..."</c> attribute to the <c>&lt;strong&gt;</c> element.
    /// </summary>
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// Plain-text content extracted from children for backward compatibility.
    /// </summary>
    public string Content => GetPlainText(Children);

    public override AstNodeKind Kind => AstNodeKind.InlineStrong;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties() => [];

    /// <inheritdoc />
    protected override IEnumerable<AstNode> GetStructuralInlines() => Children;

    /// <inheritdoc />
    protected override int MixAdditionalState(int hash)
    {
        if (Roles is not null)
            for (int i = 0; i < Roles.Count; i++)
                hash = FnvMixString(hash, Roles[i]);
        return hash;
    }

    private static string GetPlainText(IReadOnlyList<InlineNode> children)
    {
        if (children.Count == 1 && children[0] is TextInlineNode t)
            return t.Value;

        var sb = new System.Text.StringBuilder();
        AppendPlainText(sb, children);
        return sb.ToString();
    }

    private static void AppendPlainText(System.Text.StringBuilder sb, IReadOnlyList<InlineNode> children)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case TextInlineNode text:
                    sb.Append(text.Value);
                    break;
                case StrongInlineNode strong:
                    AppendPlainText(sb, strong.Children);
                    break;
                case EmphasisInlineNode emphasis:
                    AppendPlainText(sb, emphasis.Children);
                    break;
                case MonospaceInlineNode monospace:
                    AppendPlainText(sb, monospace.Children);
                    break;
            }
        }
    }
}
