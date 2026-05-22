using AvaloniaEdit;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Code-block, quote-block, and admonition insertion commands. Each
/// inserts a delimited-block skeleton at the caret using
/// <see cref="SourceEdit.InsertBlockSnippet"/>, which adds line breaks
/// when the caret is mid-line so the snippet always sits on its own lines.
/// </summary>
internal static class BlockCommands
{
    public static void CodeBlock(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor,
            "[source]\n----\nyour code here\n----\n");

    public static void QuoteBlock(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor,
            "[quote]\n____\nyour quote here\n____\n");

    public static void Admonition(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor,
            "[NOTE]\n====\nyour note here\n====\n");
}
