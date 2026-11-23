namespace AdocNet.Layout;

/// <summary>An inline STEM (math) formula: its source and notation.</summary>
public sealed class StemRun : InlineLayout
{
    /// <summary>The formula source, without delimiters.</summary>
    public string Content { get; }

    /// <summary><c>latexmath</c> or <c>asciimath</c>.</summary>
    public string StemType { get; }

    public StemRun(string content, string stemType)
    {
        Content = content;
        StemType = stemType;
    }
}
