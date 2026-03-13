using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class DescriptionListTests
{
    // ── Parsing ──────────────────────────────────────────────────────────────────

    [Test]
    public void Simple_description_list()
    {
        var result = BlockParser.Parse("CPU:: The brain.\nRAM:: Temporary memory.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var dl = (DescriptionListNode)result.Document.Children[0];
        Assert.That(dl.Children, Has.Count.EqualTo(2));

        var item1 = (DescriptionItemNode)dl.Children[0];
        var item2 = (DescriptionItemNode)dl.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(item1.Term, Is.EqualTo("CPU"));
            Assert.That(item1.Description, Is.EqualTo("The brain."));
            Assert.That(item2.Term, Is.EqualTo("RAM"));
            Assert.That(item2.Description, Is.EqualTo("Temporary memory."));
        });
    }

    [Test]
    public void Description_list_items_group_into_single_list()
    {
        var result = BlockParser.Parse("A:: one\nB:: two\nC:: three");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var dl = (DescriptionListNode)result.Document.Children[0];
        Assert.That(dl.Children, Has.Count.EqualTo(3));
    }

    [Test]
    public void Description_list_followed_by_paragraph()
    {
        var result = BlockParser.Parse("Term:: Desc\n\nA regular paragraph.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children[0], Is.InstanceOf<DescriptionListNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Description_list_with_inline_formatting()
    {
        var result = BlockParser.Parse("*bold*:: _italic_ text");

        var dl = (DescriptionListNode)result.Document.Children[0];
        var item = (DescriptionItemNode)dl.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.TermInlines, Has.Count.GreaterThan(0));
            Assert.That(item.DescriptionInlines, Has.Count.GreaterThan(0));
        });
    }

    [Test]
    public void Two_separate_description_lists_with_paragraph_between()
    {
        var result = BlockParser.Parse("A:: one\n\nMiddle paragraph.\n\nB:: two");

        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.InstanceOf<DescriptionListNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[2], Is.InstanceOf<DescriptionListNode>());
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    [Test]
    public void Renders_dl_dt_dd()
    {
        var result = BlockParser.Parse("CPU:: The brain.\nRAM:: Memory.");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<dl>"));
        Assert.That(html, Does.Contain("<dt class=\"hdlist1\">CPU</dt>"));
        Assert.That(html, Does.Contain("<dd>\n<p>The brain.</p>\n</dd>"));
        Assert.That(html, Does.Contain("<dt class=\"hdlist1\">RAM</dt>"));
        Assert.That(html, Does.Contain("<dd>\n<p>Memory.</p>\n</dd>"));
        Assert.That(html, Does.Contain("</dl>"));
    }

    [Test]
    public void Renders_inline_formatting_in_term_and_description()
    {
        var result = BlockParser.Parse("*bold*:: _italic_ desc");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<dt class=\"hdlist1\"><strong>bold</strong></dt>"));
        Assert.That(html, Does.Contain("<em>italic</em>"));
    }
}
