namespace AdocNet.Layout;

/// <summary>
/// A block image. <see cref="Target"/> is the path or URL as written in the source;
/// the renderer resolves it.
/// </summary>
public sealed class ImageLayout : BlockLayout
{
    /// <summary>The image path or URL as written in the source.</summary>
    public string Target { get; }

    /// <summary>Alternative text, shown when the image cannot be displayed.</summary>
    public string Alt { get; }

    /// <summary>The caption (block title), or null.</summary>
    public string? Title { get; init; }

    /// <summary>The requested width as written (for example <c>300</c> or <c>50%</c>), or null.</summary>
    public string? Width { get; init; }

    /// <summary>The requested height as written, or null.</summary>
    public string? Height { get; init; }

    public ImageLayout(string target, string alt)
    {
        Target = target;
        Alt = alt;
    }
}
