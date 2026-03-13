using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class QuoteBlockTests
{
    [Test]
    public void Simple_quote_block()
    {
        var result = BlockParser.Parse("____\nA quoted paragraph.\n____");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Content, Is.Null);
            Assert.That(block.Children, Has.Count.EqualTo(1));
            Assert.That(block.Children[0], Is.InstanceOf<ParagraphNode>());
        });
    }

    [Test]
    public void Quote_block_with_multiple_paragraphs()
    {
        var result = BlockParser.Parse("____\nFirst.\n\nSecond.\n____");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void Quote_block_with_title()
    {
        var result = BlockParser.Parse(".My Quote\n____\nSome text.\n____");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
            Assert.That(block.Title, Is.EqualTo("My Quote"));
        });
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    [Test]
    public void Renders_blockquote()
    {
        var result = BlockParser.Parse("____\nQuoted text.\n____");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<blockquote>"));
        Assert.That(html, Does.Contain("<p>Quoted text.</p>"));
        Assert.That(html, Does.Contain("</blockquote>"));
    }

    [Test]
    public void Renders_titled_quote_block()
    {
        var result = BlockParser.Parse(".Famous Quote\n____\nTo be or not to be.\n____");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"title\">Famous Quote</div>"));
        Assert.That(html, Does.Contain("<blockquote>"));
    }
}
