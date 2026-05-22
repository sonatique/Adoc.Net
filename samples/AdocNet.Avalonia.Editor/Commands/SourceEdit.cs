using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace AdocNet.Avalonia.Editor.Commands;

/// <summary>
/// Low-level primitives for toolbar commands that operate on the AvaloniaEdit
/// <see cref="TextDocument"/> directly. Each method is a pure text mutation:
/// "wrap the selection in delimiters", "toggle a line-start prefix", etc.
/// </summary>
internal static class SourceEdit
{
    /// <summary>
    /// Wraps the current selection (or, if empty, the caret position) in the
    /// given delimiters. When the selection is empty, the caret is placed
    /// between them so the user can type immediately.
    /// </summary>
    public static void WrapSelection(TextEditor editor, string open, string close)
    {
        var doc = editor.Document;
        var sel = editor.TextArea.Selection;
        if (sel.IsEmpty)
        {
            var caret = editor.CaretOffset;
            doc.Insert(caret, open + close);
            editor.CaretOffset = caret + open.Length;
            return;
        }

        // AvaloniaEdit's Selection abstracts rectangular/regular selections;
        // we operate on the simple regular case (start ≤ end, one segment).
        var segments = sel.Segments.ToList();
        if (segments.Count == 0) return;
        var seg = segments[0];
        int start = seg.StartOffset;
        int end = seg.EndOffset;
        var selected = doc.GetText(start, end - start);

        using (doc.RunUpdate())
        {
            doc.Replace(start, end - start, open + selected + close);
        }

        // Restore selection over the wrapped content so the user can re-wrap
        // or remove the delimiters without re-selecting.
        editor.Select(start + open.Length, selected.Length);
    }

    /// <summary>
    /// Toggles a line-start prefix on every line of the selection (or the
    /// caret line when the selection is empty). When all targeted lines
    /// already start with <paramref name="prefix"/>, it is removed;
    /// otherwise it is added to lines that don't have it.
    /// </summary>
    public static void ToggleLinePrefix(TextEditor editor, string prefix)
    {
        var doc = editor.Document;
        int caretLineIndex = doc.GetLineByOffset(editor.CaretOffset).LineNumber;

        int firstLine, lastLine;
        var sel = editor.TextArea.Selection;
        if (sel.IsEmpty)
        {
            firstLine = lastLine = caretLineIndex;
        }
        else
        {
            var seg = sel.Segments.First();
            firstLine = doc.GetLineByOffset(seg.StartOffset).LineNumber;
            lastLine = doc.GetLineByOffset(seg.EndOffset).LineNumber;
        }

        // Decide add-vs-remove based on whether every targeted line already
        // begins with the prefix.
        bool allHavePrefix = true;
        for (int n = firstLine; n <= lastLine; n++)
        {
            var line = doc.GetLineByNumber(n);
            var text = doc.GetText(line.Offset, line.Length);
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                allHavePrefix = false;
                break;
            }
        }

        using (doc.RunUpdate())
        {
            for (int n = firstLine; n <= lastLine; n++)
            {
                var line = doc.GetLineByNumber(n);
                var text = doc.GetText(line.Offset, line.Length);
                if (allHavePrefix && text.StartsWith(prefix, StringComparison.Ordinal))
                    doc.Remove(line.Offset, prefix.Length);
                else if (!allHavePrefix && !text.StartsWith(prefix, StringComparison.Ordinal))
                    doc.Insert(line.Offset, prefix);
            }
        }
    }

    /// <summary>
    /// Replaces (or sets) the heading marker at the start of the caret line.
    /// If the line already has any <c>=+ </c> prefix, it is replaced with the
    /// given level. If no level is provided (zero) the existing prefix is
    /// stripped — toggling the line back to body text.
    /// </summary>
    public static void SetHeadingLevel(TextEditor editor, int level)
    {
        var doc = editor.Document;
        var line = doc.GetLineByOffset(editor.CaretOffset);
        var text = doc.GetText(line.Offset, line.Length);

        // Strip any existing '=+ ' prefix.
        int eqEnd = 0;
        while (eqEnd < text.Length && text[eqEnd] == '=') eqEnd++;
        int stripCount = (eqEnd > 0 && eqEnd < text.Length && text[eqEnd] == ' ')
            ? eqEnd + 1
            : 0;

        var prefix = level <= 0 ? string.Empty : new string('=', level + 1) + " ";
        using (doc.RunUpdate())
        {
            if (stripCount > 0)
                doc.Remove(line.Offset, stripCount);
            if (prefix.Length > 0)
                doc.Insert(line.Offset, prefix);
        }
    }

    /// <summary>
    /// Inserts <paramref name="snippet"/> at the caret position. If the
    /// caret is mid-line, an empty line is inserted before the snippet so
    /// block-level constructs (delimited fences, attribute lines) start
    /// on their own line.
    /// </summary>
    public static void InsertBlockSnippet(TextEditor editor, string snippet)
    {
        var doc = editor.Document;
        int caret = editor.CaretOffset;

        bool needsLeadingNewline = caret > 0 && doc.GetCharAt(caret - 1) != '\n';
        bool needsTrailingNewline = caret < doc.TextLength && doc.GetCharAt(caret) != '\n';

        string text = (needsLeadingNewline ? "\n" : string.Empty)
            + snippet
            + (needsTrailingNewline ? "\n" : string.Empty);

        doc.Insert(caret, text);
        editor.CaretOffset = caret + text.Length;
    }
}
