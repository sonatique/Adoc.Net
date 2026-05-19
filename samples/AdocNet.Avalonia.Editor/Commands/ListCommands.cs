using AvaloniaEdit;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Bullet (<c>* </c>) and numbered (<c>. </c>) list commands. The
/// <see cref="SourceEdit.ToggleLinePrefix"/> primitive toggles the marker
/// on every line of the selection: adding it when missing, removing it
/// when uniformly present.
/// </summary>
internal static class ListCommands
{
    public static void BulletList(TextEditor editor)   => SourceEdit.ToggleLinePrefix(editor, "* ");
    public static void NumberedList(TextEditor editor) => SourceEdit.ToggleLinePrefix(editor, ". ");
}
