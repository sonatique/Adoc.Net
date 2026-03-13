using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class SidebarBlockTests
{
    [Test]
    public void Simple_sidebar_block()
    {
        var result = BlockParser.Parse("****\nA sidebar paragraph.\n****");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Sidebar));
            Assert.That(block.Content, Is.Null);
            Assert.That(block.Children, Has.Count.EqualTo(1));
            Assert.That(block.Children[0], Is.InstanceOf<ParagraphNode>());
        });
    }

    [Test]
    public void Sidebar_block_with_multiple_paragraphs()
    {
        var result = BlockParser.Parse("****\nFirst.\n\nSecond.\n****");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void Sidebar_block_with_title()
    {
        var result = BlockParser.Parse(".Sidebar Title\n****\nContent here.\n****");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Sidebar));
            Assert.That(block.Title, Is.EqualTo("Sidebar Title"));
        });
    }

    [Test]
    public void Sidebar_not_confused_with_list_marker()
    {
        // Ensure "* item" is a list, not a sidebar delimiter
        var result = BlockParser.Parse("* item one\n* item two");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ListNode>());
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    [Test]
    public void Renders_sidebarblock_div()
    {
        var result = BlockParser.Parse("****\nSidebar text.\n****");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"sidebarblock\">"));
        Assert.That(html, Does.Contain("<p>Sidebar text.</p>"));
        Assert.That(html, Does.Contain("</div>"));
    }

    [Test]
    public void Renders_titled_sidebar_block()
    {
        var result = BlockParser.Parse(".Extra Info\n****\nSome details.\n****");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"title\">Extra Info</div>"));
        Assert.That(html, Does.Contain("<div class=\"sidebarblock\">"));
    }
}
