namespace AdocNet.Layout;

/// <summary>
/// A STEM (math) block: the formula source and its notation. The UI typesets it (see
/// <c>AvaloniaRenderer.StemRenderer</c>) or shows the source.
/// </summary>
public sealed class StemLayout : BlockLayout
{
    /// <summary>The formula source, without delimiters.</summary>
    public string Content { get; }

    /// <summary><c>latexmath</c> or <c>asciimath</c>.</summary>
    public string StemType { get; }

    /// <summary>The block title, if any.</summary>
    public string? Title { get; init; }

    public StemLayout(string content, string stemType)
    {
        Content = content;
        StemType = stemType;
    }
}
