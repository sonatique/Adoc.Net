namespace AdocNet.Ast;

/// <summary>
/// A block-level <c>video::target[attrs]</c> macro.
/// </summary>
public sealed class VideoNode : BlockNode
{
    public required string Target { get; init; }
    public string? Width { get; init; }
    public string? Height { get; init; }
    public string? Poster { get; init; }
    public bool Autoplay { get; init; }
    public bool Loop { get; init; }
    public bool Controls { get; init; }

    public override AstNodeKind Kind => AstNodeKind.Video;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        if (Width is not null) yield return new("Width", Width);
        if (Height is not null) yield return new("Height", Height);
        if (Poster is not null) yield return new("Poster", Poster);
        if (Autoplay) yield return new("Autoplay", "true");
        if (Loop) yield return new("Loop", "true");
        if (Controls) yield return new("Controls", "true");
    }
}
