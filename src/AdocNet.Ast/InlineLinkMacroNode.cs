namespace AdocNet.Ast;

/// <summary>
/// An explicit <c>link:URL[label]</c> inline macro.
/// Distinguished from <see cref="LinkInlineNode"/> which represents bare auto-detected URLs.
/// </summary>
public sealed class InlineLinkMacroNode : InlineNode
{
    public required string Url { get; init; }

    /// <summary>
    /// The display label from the bracket content. Empty string if brackets were empty.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>Target window for the link. Set when :linkattrs: is enabled.</summary>
    public string? Window { get; init; }

    /// <summary>Additional CSS role for the link. Set when :linkattrs: is enabled.</summary>
    public string? Role { get; init; }

    public override AstNodeKind Kind => AstNodeKind.InlineLinkMacro;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Url", Url);
        yield return new("Label", Label);
        if (Window is not null) yield return new("Window", Window);
        if (Role is not null) yield return new("Role", Role);
    }
}
