namespace AdocNet.Layout;

/// <summary>
/// A run of plain text with no formatting.
/// </summary>
public sealed class TextRun : InlineLayout
{
    /// <summary>
    /// The plain text content.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Creates a new text run.
    /// </summary>
    /// <param name="text">The plain text content.</param>
    public TextRun(string text)
    {
        Text = text;
    }
}
