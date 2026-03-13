using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class AdmonitionTests
{
    // ── Inline admonitions ───────────────────────────────────────────────────────

    [Test]
    public void Inline_note()
    {
        var result = BlockParser.Parse("NOTE: This is important.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var adm = (AdmonitionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(adm.AdmonitionType, Is.EqualTo("NOTE"));
            Assert.That(adm.Text, Is.EqualTo("This is important."));
            Assert.That(adm.Children, Has.Count.EqualTo(0));
        });
    }

    [TestCase("NOTE")]
    [TestCase("TIP")]
    [TestCase("IMPORTANT")]
    [TestCase("WARNING")]
    [TestCase("CAUTION")]
    public void All_inline_admonition_types(string type)
    {
        var result = BlockParser.Parse($"{type}: Some text.");

        var adm = (AdmonitionNode)result.Document.Children[0];
        Assert.That(adm.AdmonitionType, Is.EqualTo(type));
    }

    [Test]
    public void Inline_admonition_with_formatting()
    {
        var result = BlockParser.Parse("WARNING: Has *bold* and _italic_.");

        var adm = (AdmonitionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(adm.Inlines, Has.Count.GreaterThan(1));
            Assert.That(adm.Inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
            Assert.That(adm.Inlines.OfType<EmphasisInlineNode>().Count(), Is.EqualTo(1));
        });
    }

    // ── Block admonitions ────────────────────────────────────────────────────────

    [Test]
    public void Block_note()
    {
        var result = BlockParser.Parse("[NOTE]\n====\nBlock content.\n====");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var adm = (AdmonitionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(adm.AdmonitionType, Is.EqualTo("NOTE"));
            Assert.That(adm.Text, Is.Null);
            Assert.That(adm.Children, Has.Count.EqualTo(1));
            Assert.That(adm.Children[0], Is.InstanceOf<ParagraphNode>());
        });
    }

    [Test]
    public void Block_admonition_with_multiple_paragraphs()
    {
        var result = BlockParser.Parse("[WARNING]\n====\nFirst.\n\nSecond.\n====");

        var adm = (AdmonitionNode)result.Document.Children[0];
        Assert.That(adm.Children, Has.Count.EqualTo(2));
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    [Test]
    public void Renders_inline_admonition()
    {
        var result = BlockParser.Parse("NOTE: Hello world.");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"admonitionblock note\">"));
        Assert.That(html, Does.Contain("Hello world.\n"));
        Assert.That(html, Does.Contain("</div>"));
    }

    [Test]
    public void Renders_block_admonition()
    {
        var result = BlockParser.Parse("[TIP]\n====\nA tip.\n====");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"admonitionblock tip\">"));
        Assert.That(html, Does.Contain("<p>A tip.</p>"));
    }

    [Test]
    public void Renders_inline_formatting_in_admonition()
    {
        var result = BlockParser.Parse("NOTE: Has *bold* text.");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<strong>bold</strong>"));
    }
}
