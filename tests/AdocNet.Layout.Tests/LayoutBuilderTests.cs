using NUnit.Framework;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Layout.Tests;

[TestFixture]
public class LayoutBuilderTests
{
    private readonly LayoutBuilder _builder = new();

    private DocumentLayout Build(string asciidoc)
    {
        var result = AdocParser.Parse(asciidoc);
        return _builder.Build(result.Document);
    }

    // ── Empty document ──────────────────────────────────────────────

    [Test]
    public void Empty_document_produces_empty_layout()
    {
        var layout = Build("");
        Assert.That(layout.Children, Is.Empty);
    }

    // ── Paragraphs ──────────────────────────────────────────────────

    [Test]
    public void Single_paragraph_produces_ParagraphLayout()
    {
        var layout = Build("Hello world.");
        Assert.That(layout.Children, Has.Count.EqualTo(1));
        Assert.That(layout.Children[0], Is.InstanceOf<ParagraphLayout>());

        var para = (ParagraphLayout)layout.Children[0];
        Assert.That(para.Inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextRun>());
        Assert.That(((TextRun)para.Inlines[0]).Text, Does.Contain("Hello world"));
    }

    [Test]
    public void Paragraph_with_bold_contains_BoldRun()
    {
        var layout = Build("Hello *bold* world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is BoldRun), Is.True);
        var bold = (BoldRun)para.Inlines.First(i => i is BoldRun);
        Assert.That(bold.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(bold.Children[0], Is.InstanceOf<TextRun>());
        Assert.That(((TextRun)bold.Children[0]).Text, Does.Contain("bold"));
    }

    [Test]
    public void Paragraph_with_italic_contains_ItalicRun()
    {
        var layout = Build("Hello _italic_ world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is ItalicRun), Is.True);
        var italic = (ItalicRun)para.Inlines.First(i => i is ItalicRun);
        Assert.That(italic.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)italic.Children[0]).Text, Does.Contain("italic"));
    }

    [Test]
    public void Paragraph_with_monospace_contains_MonoRun()
    {
        var layout = Build("Hello `mono` world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is MonoRun), Is.True);
        var mono = (MonoRun)para.Inlines.First(i => i is MonoRun);
        Assert.That(mono.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)mono.Children[0]).Text, Does.Contain("mono"));
    }

    [Test]
    public void Paragraph_with_link_contains_LinkRun()
    {
        var layout = Build("Visit https://example.com today.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is LinkRun), Is.True);
        var link = (LinkRun)para.Inlines.First(i => i is LinkRun);
        Assert.That(link.Href, Does.Contain("example.com"));
    }

    // ── Headings ────────────────────────────────────────────────────

    [Test]
    public void Heading_produces_HeadingLayout_with_correct_level()
    {
        var layout = Build("== My Heading");
        Assert.That(layout.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(layout.Children[0], Is.InstanceOf<HeadingLayout>());

        var heading = (HeadingLayout)layout.Children[0];
        Assert.That(heading.Level, Is.EqualTo(1));
        Assert.That(heading.Inlines, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Lists ───────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_produces_ListLayout_ordered_false()
    {
        var layout = Build("* Item one\n* Item two\n* Item three");

        Assert.That(layout.Children.Any(c => c is ListLayout), Is.True);
        var list = (ListLayout)layout.Children.First(c => c is ListLayout);
        Assert.That(list.Ordered, Is.False);
        Assert.That(list.Items, Has.Count.EqualTo(3));

        var firstItem = list.Items[0];
        Assert.That(firstItem.Inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)firstItem.Inlines[0]).Text, Does.Contain("Item one"));
    }

    [Test]
    public void Ordered_list_produces_ListLayout_ordered_true()
    {
        var layout = Build(". First\n. Second");

        Assert.That(layout.Children.Any(c => c is ListLayout), Is.True);
        var list = (ListLayout)layout.Children.First(c => c is ListLayout);
        Assert.That(list.Ordered, Is.True);
        Assert.That(list.Items, Has.Count.EqualTo(2));
    }

    // ── Code blocks ─────────────────────────────────────────────────

    [Test]
    public void Source_block_produces_CodeBlockLayout()
    {
        var layout = Build("[source,csharp]\n----\nint x = 42;\n----");

        Assert.That(layout.Children.Any(c => c is CodeBlockLayout), Is.True);
        var code = (CodeBlockLayout)layout.Children.First(c => c is CodeBlockLayout);
        Assert.That(code.Text, Does.Contain("int x = 42"));
        Assert.That(code.Language, Is.EqualTo("csharp"));
    }

    [Test]
    public void Listing_block_without_language_has_null_language()
    {
        var layout = Build("----\nsome code\n----");

        Assert.That(layout.Children.Any(c => c is CodeBlockLayout), Is.True);
        var code = (CodeBlockLayout)layout.Children.First(c => c is CodeBlockLayout);
        Assert.That(code.Text, Does.Contain("some code"));
        Assert.That(code.Language, Is.Null);
    }

    // ── Admonitions ─────────────────────────────────────────────────

    [Test]
    public void Inline_admonition_produces_AdmonitionLayout()
    {
        var layout = Build("NOTE: Remember this.");

        Assert.That(layout.Children.Any(c => c is AdmonitionLayout), Is.True);
        var admonition = (AdmonitionLayout)layout.Children.First(c => c is AdmonitionLayout);
        Assert.That(admonition.Kind, Is.EqualTo(AdmonitionKind.Note));
        Assert.That(admonition.Blocks, Has.Count.EqualTo(1));
        Assert.That(admonition.Blocks[0], Is.InstanceOf<ParagraphLayout>());
    }

    [Test]
    public void Block_admonition_produces_AdmonitionLayout()
    {
        var layout = Build("[WARNING]\n====\nBe careful.\n====");

        Assert.That(layout.Children.Any(c => c is AdmonitionLayout), Is.True);
        var admonition = (AdmonitionLayout)layout.Children.First(c => c is AdmonitionLayout);
        Assert.That(admonition.Kind, Is.EqualTo(AdmonitionKind.Warning));
        Assert.That(admonition.Blocks, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Mixed content ───────────────────────────────────────────────

    [Test]
    public void Mixed_content_produces_correct_sequence()
    {
        var layout = Build("== Introduction\n\nSome text.\n\n* Item A\n* Item B");

        Assert.That(layout.Children, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(layout.Children[0], Is.InstanceOf<HeadingLayout>());
        Assert.That(layout.Children[1], Is.InstanceOf<ParagraphLayout>());
        Assert.That(layout.Children[2], Is.InstanceOf<ListLayout>());
    }

    // ── Document title ──────────────────────────────────────────────

    [Test]
    public void Document_with_title_preserves_title()
    {
        var layout = Build("= My Document\n\nContent here.");
        Assert.That(layout.Title, Is.EqualTo("My Document"));
    }

    // ── Tables ──────────────────────────────────────────────────────

    [Test]
    public void Table_produces_TableLayout()
    {
        var layout = Build("|===\n| A | B\n| C | D\n|===");

        Assert.That(layout.Children.Any(c => c is TableLayout), Is.True);
        var table = (TableLayout)layout.Children.First(c => c is TableLayout);
        Assert.That(table.Rows, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Table_with_header_marks_first_row()
    {
        var layout = Build("[options=\"header\"]\n|===\n| H1 | H2\n| A | B\n|===");

        var table = (TableLayout)layout.Children.First(c => c is TableLayout);
        Assert.That(table.HasHeader, Is.True);
        Assert.That(table.Rows[0].Cells[0].IsHeader, Is.True);
    }

    // ── Description lists ───────────────────────────────────────────

    [Test]
    public void Description_list_produces_DescriptionListLayout()
    {
        var layout = Build("Term A:: Description A\nTerm B:: Description B");

        Assert.That(layout.Children.Any(c => c is DescriptionListLayout), Is.True);
        var descList = (DescriptionListLayout)layout.Children.First(c => c is DescriptionListLayout);
        Assert.That(descList.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public void Description_item_has_term_and_description()
    {
        var layout = Build("Foo:: Bar baz");

        var descList = (DescriptionListLayout)layout.Children.First(c => c is DescriptionListLayout);
        var item = descList.Items[0];
        Assert.That(item.Term, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(item.Description, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Cross-references ────────────────────────────────────────────

    [Test]
    public void Cross_reference_renders_as_text()
    {
        var layout = Build("See <<my-target,My Label>>.");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("My Label")), Is.True);
    }

    // ── Footnotes ───────────────────────────────────────────────────

    [Test]
    public void Footnote_renders_as_bracketed_text()
    {
        var layout = Build("Some text.footnote:[This is a footnote.]");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("This is a footnote")), Is.True);
    }

    // ── Passthrough ─────────────────────────────────────────────────

    [Test]
    public void Passthrough_renders_content_as_text()
    {
        var layout = Build("Hello pass:[<b>world</b>] end.");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("<b>world</b>")), Is.True);
    }
}
