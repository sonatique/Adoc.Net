using AvaloniaEdit;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Heading-level commands. H0 strips any existing heading marker, turning
/// the line back into a regular paragraph.
/// </summary>
internal static class HeadingCommands
{
    public static void None(TextEditor editor) => SourceEdit.SetHeadingLevel(editor, 0);
    public static void H1(TextEditor editor)   => SourceEdit.SetHeadingLevel(editor, 1);
    public static void H2(TextEditor editor)   => SourceEdit.SetHeadingLevel(editor, 2);
    public static void H3(TextEditor editor)   => SourceEdit.SetHeadingLevel(editor, 3);
    public static void H4(TextEditor editor)   => SourceEdit.SetHeadingLevel(editor, 4);
}
