namespace AdocNet.Ast;

/// <summary>
/// A block-level <c>image::target[alt]</c> macro.
/// </summary>
public sealed class BlockImageNode : BlockNode
{
    public required string Target { get; init; }

    /// <summary>
    /// The alt text from the bracket content. Empty string if brackets were empty.
    /// </summary>
    public required string Alt { get; init; }

    /// <summary>
    /// Optional block title set via <c>.Title</c> line preceding the macro.
    /// </summary>
    public string? Title { get; init; }

    public override AstNodeKind Kind => AstNodeKind.BlockImage;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        yield return new("Alt", Alt);
        if (Title is not null)
            yield return new("Title", Title);
    }
}
