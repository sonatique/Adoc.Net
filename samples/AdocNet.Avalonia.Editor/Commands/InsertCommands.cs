using AvaloniaEdit;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Inline-link, image, table, and thematic-break insertions.
/// </summary>
internal static class InsertCommands
{
    public static void Link(TextEditor editor)
    {
        // If a selection exists, treat it as the label and prompt the URL.
        var sel = editor.TextArea.Selection;
        if (!sel.IsEmpty)
        {
            var seg = sel.Segments.First();
            int start = seg.StartOffset;
            int end = seg.EndOffset;
            var label = editor.Document.GetText(start, end - start);
            editor.Document.Replace(start, end - start, $"link:https://[{label}]");
            // Place the caret on the URL placeholder so the user can fill it in.
            editor.CaretOffset = start + "link:".Length;
            editor.Select(start + "link:".Length, "https://".Length);
            return;
        }

        var caret = editor.CaretOffset;
        const string snippet = "link:https://[text]";
        editor.Document.Insert(caret, snippet);
        // Caret on the URL placeholder.
        editor.Select(caret + "link:".Length, "https://".Length);
    }

    public static void Image(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor, "image::path/to/file[alt text]\n");

    public static void Table(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor,
            "|===\n| Cell 1 | Cell 2\n| Cell 3 | Cell 4\n|===\n");

    public static void ThematicBreak(TextEditor editor) =>
        SourceEdit.InsertBlockSnippet(editor, "'''\n");
}
