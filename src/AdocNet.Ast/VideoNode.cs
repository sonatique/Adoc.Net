namespace AdocNet.Ast;

/// <summary>
/// A block-level <c>video::target[attrs]</c> macro.
/// </summary>
public sealed class VideoNode : BlockNode
{
    public required string Target { get; init; }
    /// <summary>
    /// Video hosting provider: <c>"youtube"</c>, <c>"vimeo"</c>, or <c>null</c> for local file.
    /// When set, the renderer emits an <c>&lt;iframe&gt;</c> instead of a <c>&lt;video&gt;</c> element.
    /// </summary>
    public string? Provider { get; init; }
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
        if (Provider is not null) yield return new("Provider", Provider);
        if (Width is not null) yield return new("Width", Width);
        if (Height is not null) yield return new("Height", Height);
        if (Poster is not null) yield return new("Poster", Poster);
        if (Autoplay) yield return new("Autoplay", "true");
        if (Loop) yield return new("Loop", "true");
        if (Controls) yield return new("Controls", "true");
    }
}
