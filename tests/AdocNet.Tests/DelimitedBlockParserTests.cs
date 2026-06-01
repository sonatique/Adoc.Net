using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class DelimitedBlockParserTests
{
    // ── Literal blocks ──────────────────────────────────────────────────────────

    [Test]
    public void Simple_literal_block()
    {
        var result = BlockParser.Parse("....\ncontent line\n....");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Literal));
            Assert.That(block.Content, Is.EqualTo("content line"));
            Assert.That(block.Title, Is.Null);
            Assert.That(block.Language, Is.Null);
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    // ── Listing blocks ──────────────────────────────────────────────────────────

    [Test]
    public void Simple_listing_block()
    {
        var result = BlockParser.Parse("----\ncode here\n----");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
            Assert.That(block.Content, Is.EqualTo("code here"));
            Assert.That(block.Language, Is.Null);
        });
    }

    [Test]
    public void Listing_block_preserves_internal_line_breaks()
    {
        var result = BlockParser.Parse("----\nline one\nline two\nline three\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("line one\nline two\nline three"));
    }

    // ── Source blocks ───────────────────────────────────────────────────────────

    [Test]
    public void Source_block_with_bare_source_attribute()
    {
        var result = BlockParser.Parse("[source]\n----\nsome code\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Content, Is.EqualTo("some code"));
            Assert.That(block.Language, Is.Null);
        });
    }

    [Test]
    public void Source_block_with_language()
    {
        var result = BlockParser.Parse("[source,csharp]\n----\nint x = 1;\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Content, Is.EqualTo("int x = 1;"));
            Assert.That(block.Language, Is.EqualTo("csharp"));
        });
    }

    // ── Block titles ────────────────────────────────────────────────────────────

    [Test]
    public void Block_title_before_listing_block()
    {
        var result = BlockParser.Parse(".My Title\n----\ncode\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
            Assert.That(block.Title, Is.EqualTo("My Title"));
            Assert.That(block.Content, Is.EqualTo("code"));
        });
    }

    [Test]
    public void Block_title_before_source_block()
    {
        var result = BlockParser.Parse(".Example Code\n[source,csharp]\n----\nint x = 1;\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Title, Is.EqualTo("Example Code"));
            Assert.That(block.Language, Is.EqualTo("csharp"));
            Assert.That(block.Content, Is.EqualTo("int x = 1;"));
        });
    }

    [Test]
    public void Block_title_separated_by_blank_line_is_not_applied()
    {
        // Blank line between .Title and block cancels the title.
        var result = BlockParser.Parse(".Title\n\n----\ncode\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Title, Is.Null);
    }

    // ── Example blocks ──────────────────────────────────────────────────────────

    [Test]
    public void Example_block_content_is_recursively_parsed()
    {
        var result = BlockParser.Parse("====\nHello.\n====");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Example));
            Assert.That(block.Content, Is.Null);
            Assert.That(block.Children, Has.Count.EqualTo(1));
            Assert.That(((ParagraphNode)block.Children[0]).Text, Is.EqualTo("Hello."));
        });
    }

    [Test]
    public void Example_block_with_list_inside()
    {
        var result = BlockParser.Parse("====\n* alpha\n* beta\n====");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Example));
        Assert.That(block.Children, Has.Count.EqualTo(1));
        Assert.That(block.Children[0], Is.InstanceOf<ListNode>());
    }

    // ── Blocks inside sections ───────────────────────────────────────────────────

    [Test]
    public void Delimited_block_inside_a_section()
    {
        var input = "= Doc\n\n== Section\n\n----\ncode\n----";
        var result = BlockParser.Parse(input);

        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Children, Has.Count.EqualTo(1));

        var block = (DelimitedBlockNode)section.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
        Assert.That(block.Content, Is.EqualTo("code"));
    }

    // ── Paragraph adjacency ──────────────────────────────────────────────────────

    [Test]
    public void Paragraph_before_and_after_delimited_block()
    {
        var input = "Before.\n\n----\ncode\n----\n\nAfter.";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<DelimitedBlockNode>());
        Assert.That(result.Document.Children[2], Is.InstanceOf<ParagraphNode>());

        Assert.That(((ParagraphNode)result.Document.Children[0]).Text, Is.EqualTo("Before."));
        Assert.That(((ParagraphNode)result.Document.Children[2]).Text, Is.EqualTo("After."));
    }

    // ── Error tolerance ──────────────────────────────────────────────────────────

    [Test]
    public void Unclosed_delimiter_does_not_crash_and_emits_diagnostic()
    {
        var result = BlockParser.Parse("----\norphaned content");

        Assert.DoesNotThrow(() => BlockParser.Parse("----\norphaned content"));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public void Three_dash_delimiter_is_a_paragraph()
    {
        // "---" (3 dashes) must not trigger block parsing.
        var result = BlockParser.Parse("---");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Source_attribute_not_followed_by_listing_delimiter_does_not_crash()
    {
        // [source] followed by non----- content: hasPendingSource flag is cleared
        // by the paragraph fallthrough, leaving just a paragraph.
        var result = BlockParser.Parse("[source]\nThis is just text.");

        Assert.DoesNotThrow(() => BlockParser.Parse("[source]\nThis is just text."));
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(((ParagraphNode)result.Document.Children[0]).Text, Is.EqualTo("This is just text."));
    }

    [Test]
    public void Source_attribute_at_end_of_input_does_not_crash()
    {
        Assert.DoesNotThrow(() => BlockParser.Parse("[source]"));
    }

    [Test]
    public void Mixed_delimiter_chars_are_not_recognised_as_delimiters()
    {
        // A line like "--=--" is not a valid delimiter (mixed chars).
        var result = BlockParser.Parse("--=--\nsome text\n--=--");

        // All three lines are treated as paragraphs (one paragraph since no blank lines).
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    // ── Source ranges ────────────────────────────────────────────────────────────

    [Test]
    public void Block_source_range_spans_opening_to_closing_delimiter()
    {
        // Lines: 1=Before, 2=blank, 3=----, 4=code, 5=----
        var input = "Before.\n\n----\ncode\n----";
        var result = BlockParser.Parse(input);

        var block = (DelimitedBlockNode)result.Document.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(block.Source.Start.Line, Is.EqualTo(3));
            Assert.That(block.Source.End.Line, Is.EqualTo(5));
        });
    }

    // ── Open blocks ──────────────────────────────────────────────────────────

    [Test]
    public void Open_block_parsed_with_nested_content()
    {
        var result = BlockParser.Parse("--\nParagraph inside open block.\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Open));
        Assert.That(block.Children, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Open_block_with_source_style_acts_as_source()
    {
        var result = BlockParser.Parse("[source,csharp]\n--\nvar x = 1;\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
        Assert.That(block.Language, Is.EqualTo("csharp"));
    }

    [Test]
    public void Open_block_with_quote_style_acts_as_quote()
    {
        var result = BlockParser.Parse("[quote, \"Author\", \"Source\"]\n--\nQuoted text.\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
    }

    // ── Verse blocks ─────────────────────────────────────────────────────────

    [Test]
    public void Verse_block_with_quote_delimiters()
    {
        var result = BlockParser.Parse("[verse, \"Author\", \"Source\"]\n____\nLine one\nLine two\n____");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Verse));
        Assert.That(block.Content, Does.Contain("Line one"));
        Assert.That(block.Content, Does.Contain("Line two"));
    }

    [Test]
    public void Verse_block_with_open_delimiters()
    {
        var result = BlockParser.Parse("[verse]\n--\nLine one\nLine two\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Verse));
    }

    // ── Open block chameleon routing ──────────────────────────────────────────

    [Test]
    public void Open_block_with_listing_style_acts_as_listing()
    {
        var result = BlockParser.Parse("[listing]\n--\nsome code\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
    }

    [Test]
    public void Open_block_with_literal_style_acts_as_literal()
    {
        var result = BlockParser.Parse("[literal]\n--\nsome text\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Literal));
    }

    [Test]
    public void Open_block_with_example_style_acts_as_example()
    {
        var result = BlockParser.Parse("[example]\n--\nExample content.\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Example));
    }

    [Test]
    public void Open_block_with_sidebar_style_acts_as_sidebar()
    {
        var result = BlockParser.Parse("[sidebar]\n--\nSidebar content.\n--");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Sidebar));
    }

    // ── Verse inline formatting ──────────────────────────────────────────────

    [Test]
    public void Verse_block_applies_inline_formatting()
    {
        var result = BlockParser.Parse("[verse]\n____\nTo *be* or _not_ to be\n____");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<strong>be</strong>"));
        Assert.That(html, Does.Contain("<em>not</em>"));
    }

    // ── Closing-delimiter length matching ──────────────────────────────────
    // Per the AsciiDoc spec, a delimited block's closer must match the opener's
    // length. A differently-sized run of the same char is content, which is
    // what lets a longer rule sit inside a verbatim block and same-type blocks
    // nest. Verified against Asciidoctor 2.0.26.

    [Test]
    public void Longer_rule_inside_listing_is_content_not_a_closer()
    {
        var result = BlockParser.Parse("----\nline1\n------\nline2\n----");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
        Assert.That(block.Content, Is.EqualTo("line1\n------\nline2"));
    }

    [Test]
    public void Same_type_example_blocks_nest_when_delimiter_lengths_differ()
    {
        var result = BlockParser.Parse("====\nouter\n=====\ninner\n=====\n====");
        var html = new HtmlRenderer().RenderToString(result.Document);

        // Two example blocks => nesting (inner =====, outer ====).
        var count = System.Text.RegularExpressions.Regex.Matches(html, "class=\"exampleblock\"").Count;
        Assert.That(count, Is.EqualTo(2));
        Assert.That(html, Does.Contain("outer"));
        Assert.That(html, Does.Contain("inner"));
    }

    [Test]
    public void Comment_block_spans_to_same_length_closer()
    {
        // The 5-slash line in the middle does not close the 4-slash comment;
        // everything through the final //// is comment (no 'a'/'b' in output).
        var result = BlockParser.Parse("before\n////\na\n/////\nb\n////\nafter");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("before"));
        Assert.That(html, Does.Contain("after"));
        Assert.That(html, Does.Not.Contain(">a<"));
        Assert.That(html, Does.Not.Contain(">b<"));
    }
}
