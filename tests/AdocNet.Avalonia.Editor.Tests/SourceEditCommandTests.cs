using AvaloniaEdit;
using AvaloniaEdit.Document;
using global::Avalonia.Headless.NUnit;
using AdocNet.Avalonia.Editor.Commands;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Unit tests for the toolbar command primitives. Each test creates a
/// headless <see cref="TextEditor"/>, applies the command, and asserts the
/// resulting document text + caret/selection state.
/// </summary>
[TestFixture]
public class SourceEditCommandTests
{
    private static TextEditor MakeEditor(string text, int caret = 0, int selectStart = -1, int selectLen = 0)
    {
        var editor = new TextEditor { Document = new TextDocument(text) };
        editor.CaretOffset = caret;
        if (selectStart >= 0 && selectLen > 0)
            editor.Select(selectStart, selectLen);
        return editor;
    }

    // ── Formatting commands ───────────────────────────────────────────────

    [AvaloniaTest]
    public void Bold_wraps_selection_in_asterisks()
    {
        var editor = MakeEditor("hello world", caret: 0, selectStart: 0, selectLen: 5);
        FormattingCommands.Bold(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("*hello* world"));
    }

    [AvaloniaTest]
    public void Bold_on_empty_selection_inserts_empty_pair_at_caret()
    {
        var editor = MakeEditor("xy", caret: 1);
        FormattingCommands.Bold(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("x**y"));
        Assert.That(editor.CaretOffset, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public void Italic_wraps_selection_in_underscores()
    {
        var editor = MakeEditor("hello world", caret: 0, selectStart: 6, selectLen: 5);
        FormattingCommands.Italic(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("hello _world_"));
    }

    [AvaloniaTest]
    public void Monospace_wraps_selection_in_backticks()
    {
        var editor = MakeEditor("inline code here", caret: 0, selectStart: 7, selectLen: 4);
        FormattingCommands.Monospace(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("inline `code` here"));
    }

    // ── List commands ─────────────────────────────────────────────────────

    [AvaloniaTest]
    public void Bullet_list_adds_marker_to_caret_line()
    {
        var editor = MakeEditor("line one\nline two\nline three", caret: 0);
        ListCommands.BulletList(editor);
        Assert.That(editor.Document.Text,
            Is.EqualTo("* line one\nline two\nline three"));
    }

    [AvaloniaTest]
    public void Bullet_list_toggles_off_when_all_selected_lines_already_have_marker()
    {
        var editor = MakeEditor("* a\n* b\n* c", caret: 0, selectStart: 0, selectLen: 11);
        ListCommands.BulletList(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("a\nb\nc"));
    }

    [AvaloniaTest]
    public void Bullet_list_adds_marker_to_lines_missing_it_when_mixed()
    {
        // First and third lines have the marker, second doesn't. Toggle
        // should ADD to the missing one (not strip).
        var editor = MakeEditor("* a\nb\n* c", caret: 0, selectStart: 0, selectLen: 9);
        ListCommands.BulletList(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("* a\n* b\n* c"));
    }

    [AvaloniaTest]
    public void Numbered_list_uses_dot_marker()
    {
        var editor = MakeEditor("one\ntwo", caret: 0, selectStart: 0, selectLen: 7);
        ListCommands.NumberedList(editor);
        Assert.That(editor.Document.Text, Is.EqualTo(". one\n. two"));
    }

    // ── Heading commands ──────────────────────────────────────────────────

    [AvaloniaTest]
    public void H1_prepends_one_equals_marker()
    {
        var editor = MakeEditor("Title here", caret: 0);
        HeadingCommands.H1(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("== Title here"));
    }

    [AvaloniaTest]
    public void H2_prepends_two_equals_markers()
    {
        var editor = MakeEditor("Subtitle", caret: 0);
        HeadingCommands.H2(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("=== Subtitle"));
    }

    [AvaloniaTest]
    public void Heading_replaces_existing_marker()
    {
        var editor = MakeEditor("== Already H1", caret: 0);
        HeadingCommands.H3(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("==== Already H1"));
    }

    [AvaloniaTest]
    public void Heading_none_strips_existing_marker()
    {
        var editor = MakeEditor("=== H2 line", caret: 0);
        HeadingCommands.None(editor);
        Assert.That(editor.Document.Text, Is.EqualTo("H2 line"));
    }

    // ── Block / insert commands ───────────────────────────────────────────

    [AvaloniaTest]
    public void Code_block_inserts_source_skeleton()
    {
        var editor = MakeEditor("before", caret: 6);
        BlockCommands.CodeBlock(editor);
        Assert.That(editor.Document.Text, Does.Contain("[source]"));
        Assert.That(editor.Document.Text, Does.Contain("----"));
    }

    [AvaloniaTest]
    public void Quote_block_inserts_quote_skeleton()
    {
        var editor = MakeEditor("", caret: 0);
        BlockCommands.QuoteBlock(editor);
        Assert.That(editor.Document.Text, Does.Contain("[quote]"));
        Assert.That(editor.Document.Text, Does.Contain("____"));
    }

    [AvaloniaTest]
    public void Admonition_inserts_note_skeleton()
    {
        var editor = MakeEditor("", caret: 0);
        BlockCommands.Admonition(editor);
        Assert.That(editor.Document.Text, Does.Contain("[NOTE]"));
        Assert.That(editor.Document.Text, Does.Contain("===="));
    }

    [AvaloniaTest]
    public void Thematic_break_inserts_triple_apostrophe()
    {
        var editor = MakeEditor("", caret: 0);
        InsertCommands.ThematicBreak(editor);
        Assert.That(editor.Document.Text, Does.Contain("'''"));
    }

    [AvaloniaTest]
    public void Table_insert_creates_table_skeleton()
    {
        var editor = MakeEditor("", caret: 0);
        InsertCommands.Table(editor);
        Assert.That(editor.Document.Text, Does.StartWith("|===").Or.Contain("|==="));
        Assert.That(editor.Document.Text, Does.Contain("| Cell 1"));
    }

    [AvaloniaTest]
    public void Image_insert_creates_image_macro()
    {
        var editor = MakeEditor("", caret: 0);
        InsertCommands.Image(editor);
        Assert.That(editor.Document.Text, Does.Contain("image::"));
    }
}
