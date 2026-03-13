namespace AdocNet.Ast;

/// <summary>
/// A block-level <c>audio::target[attrs]</c> macro.
/// </summary>
public sealed class AudioNode : BlockNode
{
    public required string Target { get; init; }
    public bool Autoplay { get; init; }
    public bool Loop { get; init; }
    public bool Controls { get; init; }

    public override AstNodeKind Kind => AstNodeKind.Audio;

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Target", Target);
        if (Autoplay) yield return new("Autoplay", "true");
        if (Loop) yield return new("Loop", "true");
        if (Controls) yield return new("Controls", "true");
    }
}
