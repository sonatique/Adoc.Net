using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests that the parser and renderer do not crash on malformed, edge-case,
/// or adversarial input. Assertions focus on structural correctness rather
/// than exact output, giving the parser freedom to improve graceful degradation.
/// </summary>
[TestFixture]
public class RobustnessTests
{
    // ── Whitespace and line-ending edge cases ────────────────────────────

    [Test]
    public void Whitespace_only_document_produces_empty_document()
    {
        var result = BlockParser.Parse("   \t  \n  \t  \n   ");
        Assert.That(result.Document.Children, Is.Empty);
        Assert.That(result.Document.Title, Is.Null);
    }

    [Test]
    public void CR_only_line_endings_are_handled()
    {
        var result = BlockParser.Parse("= Title\r\rParagraph text");
        Assert.That(result.Document.Title, Is.EqualTo("Title"));
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void Mixed_line_endings_are_handled()
    {
        var result = BlockParser.Parse("Line one\r\nLine two\rLine three\nLine four");
        // All four lines should merge into a single paragraph.
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void CRLF_input_produces_LF_only_html_output()
    {
        var result = BlockParser.Parse("= Title\r\n\r\nParagraph\r\n");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Not.Contain("\r"));
        Assert.That(html, Does.Contain("\n"));
    }

    // ── Inline marker edge cases ─────────────────────────────────────────

    [Test]
    public void Empty_strong_markers_become_plain_text()
    {
        var inlines = InlineParser.Parse("**");
        // Two adjacent * markers with nothing between: fall through to plain text.
        Assert.That(inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Empty_emphasis_markers_become_plain_text()
    {
        var inlines = InlineParser.Parse("__");
        Assert.That(inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Empty_monospace_markers_become_plain_text()
    {
        var inlines = InlineParser.Parse("``");
        Assert.That(inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Back_to_back_inline_markers_render_correctly()
    {
        // Asciidoctor behavior: closing `*` followed by word char `_` is not a valid
        // constrained close, so `*bold*` stays literal; only `_italic_` parses.
        var inlines = InlineParser.Parse("*bold*_italic_");
        Assert.That(inlines, Has.Count.EqualTo(2));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("*bold*"));
        Assert.That(inlines[1], Is.InstanceOf<EmphasisInlineNode>());
    }

    [Test]
    public void Multiple_URLs_in_one_paragraph()
    {
        var inlines = InlineParser.Parse("See https://a.com and https://b.com for details.");
        var links = inlines.OfType<LinkInlineNode>().ToList();
        Assert.That(links, Has.Count.EqualTo(2));
        Assert.That(links[0].Url, Is.EqualTo("https://a.com"));
        Assert.That(links[1].Url, Is.EqualTo("https://b.com"));
    }

    // ── Block parser edge cases ──────────────────────────────────────────

    [Test]
    public void Multiple_consecutive_blank_lines_do_not_create_empty_nodes()
    {
        var result = BlockParser.Parse("Paragraph one\n\n\n\n\nParagraph two");
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Short_dash_line_is_paragraph_not_delimiter()
    {
        var result = BlockParser.Parse("---");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Short_dot_line_is_paragraph_not_delimiter()
    {
        var result = BlockParser.Parse("...");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Unclosed_literal_block_produces_warning_and_paragraph()
    {
        var result = BlockParser.Parse("....\nsome content\nmore content");
        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        // The delimiter line is treated as paragraph text when unclosed.
        Assert.That(result.Document.Children, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Multiple_unclosed_blocks_consume_to_eof()
    {
        // With consume-to-EOF, the first unclosed literal block captures everything.
        var result = BlockParser.Parse("....\ntext\n\n----\nmore text");
        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        var blocks = result.Document.Children.OfType<DelimitedBlockNode>().ToList();
        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].Content, Does.Contain("more text"));
    }

    [Test]
    public void Unclosed_block_consumes_remaining_content()
    {
        var result = BlockParser.Parse("----\nunclosed\n\nA normal paragraph.");
        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        // Unclosed blocks consume all remaining content to EOF (matching Asciidoctor).
        var blocks = result.Document.Children.OfType<DelimitedBlockNode>().ToList();
        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].Content, Does.Contain("A normal paragraph."));
    }

    [Test]
    public void Empty_delimited_block_content()
    {
        var result = BlockParser.Parse("----\n----");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = result.Document.Children[0] as DelimitedBlockNode;
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.Content, Is.EqualTo(""));
    }

    [Test]
    public void Very_deep_list_nesting()
    {
        var input = "* Level 1\n** Level 2\n*** Level 3\n**** Level 4\n***** Level 5";
        var result = BlockParser.Parse(input);
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ListNode>());
    }

    [Test]
    public void Section_with_whitespace_only_title()
    {
        var result = BlockParser.Parse("==   ");
        // Should either parse as a section with empty/whitespace title or degrade safely.
        Assert.That(result.Document.Children, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Trailing_whitespace_on_delimiter_lines()
    {
        var result = BlockParser.Parse("----   \ncontent\n----   ");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = result.Document.Children[0] as DelimitedBlockNode;
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.Content, Is.EqualTo("content"));
    }

    // ── Phase 2: Boundary and transition edge cases ──────────────────────

    [Test]
    public void Block_title_before_paragraph_is_silently_lost()
    {
        // .Title followed by a plain paragraph line: the block title is consumed
        // but has nothing to attach to. The paragraph text should still appear.
        var result = BlockParser.Parse(".My Title\nJust a paragraph.");
        var paragraphs = result.Document.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(1));
        Assert.That(paragraphs[0].Text, Is.EqualTo("Just a paragraph."));
    }

    [Test]
    public void Source_attribute_before_literal_block_promotes_to_source()
    {
        // [source] promotes both ---- (listing) and .... (literal) to source, matching Asciidoctor.
        var result = BlockParser.Parse("[source]\n....\nsome code\n....");
        var block = result.Document.Children[0] as DelimitedBlockNode;
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
    }

    [Test]
    public void Section_immediately_after_list_without_blank_line()
    {
        var result = BlockParser.Parse("* item one\n== Section Title");
        // Both the list and section should be parsed without crashing.
        Assert.That(result.Document.Children.OfType<ListNode>().Count(), Is.EqualTo(1));
        Assert.That(result.Document.Children.OfType<SectionNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Paragraph_immediately_after_list_without_blank_line()
    {
        // Without a blank line, text after list items continues as paragraph text.
        // The parser clears list frames when a non-list line appears.
        var result = BlockParser.Parse("* item\nNot a list item.");
        Assert.That(result.Document.Children, Has.Count.GreaterThanOrEqualTo(1));
        // Parser should not crash regardless of how it interprets this boundary.
    }

    [Test]
    public void Url_with_multiple_trailing_punctuation_chars()
    {
        // Multiple trailing punctuation chars should all be stripped and preserved.
        var inlines = InlineParser.Parse("https://example.com/page?x=1).");
        var link = inlines.OfType<LinkInlineNode>().Single();
        Assert.That(link.Url, Is.EqualTo("https://example.com/page?x=1"));
        // The stripped ")." should appear as trailing text.
        var trailingText = inlines.OfType<TextInlineNode>().LastOrDefault();
        Assert.That(trailingText, Is.Not.Null);
        Assert.That(trailingText!.Value, Is.EqualTo(")."));
    }

    [Test]
    public void Document_starting_with_blank_lines_then_content()
    {
        var result = BlockParser.Parse("\n\n\nHello world.");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Consecutive_different_list_kinds_without_blank_line()
    {
        // Without a blank line, a different list kind nests inside the last item
        var result = BlockParser.Parse("* unordered\n. ordered");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var ul = (ListNode)result.Document.Children[0];
        Assert.That(ul.ListKind, Is.EqualTo(ListKind.Unordered));
        var item = (ListItemNode)ul.Children[0];
        Assert.That(item.Text, Is.EqualTo("unordered"));
        var nestedOl = (ListNode)item.Children[^1];
        Assert.That(nestedOl.ListKind, Is.EqualTo(ListKind.Ordered));
        Assert.That(((ListItemNode)nestedOl.Children[0]).Text, Is.EqualTo("ordered"));
    }

    [Test]
    public void Delimited_block_immediately_after_paragraph_without_blank_line()
    {
        var result = BlockParser.Parse("Some text.\n----\ncontent\n----");
        // The paragraph should be flushed and the block should be parsed.
        Assert.That(result.Document.Children.OfType<ParagraphNode>().Count(), Is.EqualTo(1));
        Assert.That(result.Document.Children.OfType<DelimitedBlockNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Paragraph_immediately_after_delimited_block_without_blank_line()
    {
        var result = BlockParser.Parse("----\ncontent\n----\nSome text.");
        Assert.That(result.Document.Children.OfType<DelimitedBlockNode>().Count(), Is.EqualTo(1));
        Assert.That(result.Document.Children.OfType<ParagraphNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Inline_marker_at_very_end_of_text()
    {
        // Unmatched marker at end of string should become plain text.
        var inlines = InlineParser.Parse("hello *");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello *"));
    }

    [Test]
    public void Single_character_strong_content()
    {
        var inlines = InlineParser.Parse("*x*");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<StrongInlineNode>());
        Assert.That(((StrongInlineNode)inlines[0]).Content, Is.EqualTo("x"));
    }

    [Test]
    public void Source_attribute_with_extra_whitespace_in_language()
    {
        var result = BlockParser.Parse("[source,  csharp  ]\n----\nvar x = 1;\n----");
        var block = result.Document.Children[0] as DelimitedBlockNode;
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
        Assert.That(block.Language, Is.EqualTo("csharp"));
    }

    [Test]
    public void Empty_document_renders_empty_html()
    {
        var result = BlockParser.Parse("");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Is.EqualTo(""));
    }

    [Test]
    public void Equals_sign_line_shorter_than_section_is_paragraph()
    {
        // "=" alone fails IsDocTitle (requires length > 2), falls through to body
        // as paragraph text. Not a section, not a title.
        var result = BlockParser.Parse("=");
        Assert.That(result.Document.Title, Is.Null);
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Renderer_handles_document_with_only_title()
    {
        var result = BlockParser.Parse("= Just a Title");
        var html = new HtmlRenderer().RenderToString(result.Document);
        // Document title is suppressed in embedded mode (FullDocument=false).
        Assert.That(html, Is.EqualTo(""));
    }
}
