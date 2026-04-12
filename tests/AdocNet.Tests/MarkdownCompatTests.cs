using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Regression tests for existing heading + quote parsing (Step 0),
/// plus feature tests for Markdown-compatible headings and blockquotes (Steps 2+4).
/// </summary>
[TestFixture]
public class MarkdownCompatTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Step 0 — Regression tests (existing behavior MUST remain unchanged)
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Regression_equals_doc_title_level_0()
    {
        var result = BlockParser.Parse("= Title");
        Assert.That(result.Document.Title, Is.EqualTo("Title"));
    }

    [Test]
    public void Regression_equals_section_level_1()
    {
        var result = BlockParser.Parse("== Section");
        var section = (SectionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(section.Level, Is.EqualTo(1));
            Assert.That(section.Title, Is.EqualTo("Section"));
        });
    }

    [Test]
    public void Regression_equals_section_level_2()
    {
        var result = BlockParser.Parse("=== Subsection");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Level, Is.EqualTo(2));
    }

    [Test]
    public void Regression_equals_no_space_not_a_heading()
    {
        var result = BlockParser.Parse("==NotASection");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Regression_quote_with_underscores()
    {
        var result = BlockParser.Parse("[quote]\n____\nQuoted text.\n____");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Children, Has.Count.EqualTo(1));
            Assert.That(block.Children[0], Is.InstanceOf<ParagraphNode>());
        });
    }

    [Test]
    public void Regression_quote_with_attribution_and_citation()
    {
        var result = BlockParser.Parse("[quote, Albert Einstein, Speech]\n____\nImagination.\n____");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Attribution, Is.EqualTo("Albert Einstein"));
            Assert.That(block.CitationSource, Is.EqualTo("Speech"));
        });
    }

    [Test]
    public void Regression_discrete_heading_with_equals()
    {
        var result = BlockParser.Parse("[discrete]\n== Discrete");
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(section.IsDiscrete, Is.True);
            Assert.That(section.Title, Is.EqualTo("Discrete"));
            Assert.That(section.Level, Is.EqualTo(1));
        });
    }

    [Test]
    public void Regression_paragraph_quote_with_attribution()
    {
        var result = BlockParser.Parse("[quote, Mark Twain]\nThe report of my death was an exaggeration.");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Attribution, Is.EqualTo("Mark Twain"));
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Step 2 — Markdown heading tests
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Hash_single_is_doc_title()
    {
        var result = BlockParser.Parse("# Title");
        Assert.That(result.Document.Title, Is.EqualTo("Title"));
    }

    [Test]
    public void Hash_double_is_level_1()
    {
        var result = BlockParser.Parse("## Section");
        var section = (SectionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(section.Level, Is.EqualTo(1));
            Assert.That(section.Title, Is.EqualTo("Section"));
        });
    }

    [Test]
    public void Hash_triple_is_level_2()
    {
        var result = BlockParser.Parse("### Subsection");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Level, Is.EqualTo(2));
    }

    [Test]
    public void Hash_quadruple_is_level_3()
    {
        var result = BlockParser.Parse("#### Level 3");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Level, Is.EqualTo(3));
    }

    [Test]
    public void Hash_sextuple_is_level_5()
    {
        var result = BlockParser.Parse("###### Deep");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Level, Is.EqualTo(5));
    }

    [Test]
    public void Seven_hashes_not_a_heading()
    {
        var result = BlockParser.Parse("####### TooDeep");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Hash_no_space_not_a_heading()
    {
        var result = BlockParser.Parse("##NoSpace");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Hash_trailing_hashes_stripped()
    {
        var result = BlockParser.Parse("## Title ##");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Title, Is.EqualTo("Title"));
    }

    [Test]
    public void Mixed_equals_and_hash_headings()
    {
        var result = BlockParser.Parse("= Doc\n\n== Section One\n\n### Sub via Hash");
        Assert.That(result.Document.Title, Is.EqualTo("Doc"));
        var s1 = (SectionNode)result.Document.Children[0];
        Assert.That(s1.Title, Is.EqualTo("Section One"));
        Assert.That(s1.Level, Is.EqualTo(1));
        var s2 = (SectionNode)result.Document.Children[1];
        Assert.That(s2.Title, Is.EqualTo("Sub via Hash"));
        Assert.That(s2.Level, Is.EqualTo(2));
    }

    [Test]
    public void Hash_heading_generates_auto_id()
    {
        var result = BlockParser.Parse("## My Section");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Id, Is.EqualTo("_my_section"));
    }

    [Test]
    public void Discrete_hash_heading()
    {
        var result = BlockParser.Parse("[discrete]\n## Discrete Hash");
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(section.IsDiscrete, Is.True);
            Assert.That(section.Title, Is.EqualTo("Discrete Hash"));
            Assert.That(section.Level, Is.EqualTo(1));
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Step 4 — Markdown blockquote tests
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Single_line_blockquote()
    {
        var result = BlockParser.Parse("> Single line quote");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
        var para = (ParagraphNode)block.Children[0];
        Assert.That(para.Text, Is.EqualTo("Single line quote"));
    }

    [Test]
    public void Multi_line_blockquote()
    {
        var result = BlockParser.Parse("> Line one\n> Line two\n> Line three");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
        Assert.That(block.Children, Has.Count.EqualTo(1));
        var para = (ParagraphNode)block.Children[0];
        Assert.That(para.Text, Does.Contain("Line one"));
        Assert.That(para.Text, Does.Contain("Line three"));
    }

    [Test]
    public void Blockquote_with_attribution()
    {
        var result = BlockParser.Parse("> Imagination is more important than knowledge.\n> -- Albert Einstein");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Attribution, Is.EqualTo("Albert Einstein"));
        });
    }

    [Test]
    public void Blockquote_ends_at_blank_line()
    {
        var result = BlockParser.Parse("> Quoted text.\n\nNon-quoted paragraph.");
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children[0], Is.InstanceOf<DelimitedBlockNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Blockquote_ends_at_non_gt_line()
    {
        var result = BlockParser.Parse("> Quoted.\nNon-quoted.");
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Gt_no_space_not_a_blockquote()
    {
        var result = BlockParser.Parse(">no space after gt");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Empty_gt_line_inside_blockquote()
    {
        var result = BlockParser.Parse("> First paragraph.\n>\n> Second paragraph.");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
        // The empty > line creates a paragraph break — two paragraphs inside the quote
        Assert.That(block.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void Traditional_quote_blocks_still_work()
    {
        var result = BlockParser.Parse("[quote, Author]\n____\nQuoted.\n____");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Attribution, Is.EqualTo("Author"));
        });
    }

    [Test]
    public void Blockquote_renders_as_html_blockquote()
    {
        var result = BlockParser.Parse("> Quoted text.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<blockquote>"));
        Assert.That(html, Does.Contain("Quoted text."));
        Assert.That(html, Does.Contain("</blockquote>"));
    }
}
