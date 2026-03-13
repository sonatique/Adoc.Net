using AdocNet;

namespace AdocNet.Ast;

/// <summary>Inline passthrough: content passed through without formatting.</summary>
public sealed class PassthroughInlineNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.InlinePassthrough;
    public required string Content { get; init; }

    /// <summary>
    /// Substitutions to apply to the passthrough content (e.g., <c>pass:quotes[*bold*]</c>).
    /// Defaults to <see cref="SubstitutionKind.None"/> (raw passthrough).
    /// </summary>
    public SubstitutionKind Substitutions { get; init; } = SubstitutionKind.None;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Content", Content);
        if (Substitutions != SubstitutionKind.None)
            yield return new("Substitutions", Substitutions.ToString());
    }
}
