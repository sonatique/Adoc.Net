using AvaloniaEdit;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Bold, italic, monospace, and highlight commands.
/// Each one wraps the current selection in the corresponding AsciiDoc
/// delimiter pair, or inserts an empty pair at the caret.
/// </summary>
internal static class FormattingCommands
{
    public static void Bold(TextEditor editor)      => SourceEdit.WrapSelection(editor, "*", "*");
    public static void Italic(TextEditor editor)    => SourceEdit.WrapSelection(editor, "_", "_");
    public static void Monospace(TextEditor editor) => SourceEdit.WrapSelection(editor, "`", "`");
    public static void Highlight(TextEditor editor) => SourceEdit.WrapSelection(editor, "#", "#");
}
