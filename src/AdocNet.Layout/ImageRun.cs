namespace AdocNet.Layout;

/// <summary>
/// An inline image. <see cref="Target"/> is the path or URL as written in the source;
/// the renderer resolves it.
/// </summary>
public sealed class ImageRun : InlineLayout
{
    /// <summary>The image path or URL as written in the source.</summary>
    public string Target { get; }

    /// <summary>Alternative text, shown when the image cannot be displayed.</summary>
    public string Alt { get; }

    public ImageRun(string target, string alt)
    {
        Target = target;
        Alt = alt;
    }
}
