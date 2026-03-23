namespace AdocNet.Layout;

/// <summary>
/// A code block containing raw text and an optional language identifier.
/// </summary>
public sealed class CodeBlockLayout : BlockLayout
{
    /// <summary>
    /// The raw code text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The source language identifier (e.g. "csharp"), or null if unspecified.
    /// </summary>
    public string? Language { get; }

    /// <summary>
    /// Creates a new code block layout.
    /// </summary>
    /// <param name="text">The raw code text.</param>
    /// <param name="language">The source language identifier, or null.</param>
    public CodeBlockLayout(string text, string? language)
    {
        Text = text;
        Language = language;
    }
}
