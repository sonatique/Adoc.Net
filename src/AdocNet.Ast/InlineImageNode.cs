namespace AdocNet.Ast;

/// <summary>
/// An inline <c>image:target[alt]</c> macro.
/// </summary>
public sealed class InlineImageNode : InlineNode
{
    public required string Target { get; init; }

    /// <summary>
    /// The alt text from the bracket content. Empty string if brackets were empty.
    /// </summary>
    public required string Alt { get; init; }

    /// <summary>Optional image width from positional or <c>width=</c> attribute.</summary>
    public string? Width { get; init; }

    /// <summary>Optional image height from positional or <c>height=</c> attribute.</summary>
    public string? Height { get; init; }

    public override AstNodeKind Kind => AstNodeKind.InlineImage;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        yield return new("Alt", Alt);
        if (Width is not null)
            yield return new("Width", Width);
        if (Height is not null)
            yield return new("Height", Height);
    }
}
