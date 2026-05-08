using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ParagraphAdmonitionTests
{
    // ── Regression: existing admonition forms must still work ──────────────────

    [Test]
    public void Inline_shorthand_admonition_still_works()
    {
        var doc = BlockParser.Parse("WARNING: Be careful.\n").Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(node.AdmonitionType, Is.EqualTo("WARNING"));
            Assert.That(node.Text, Is.EqualTo("Be careful."));
        });
    }

    [Test]
    public void Block_style_admonition_with_example_delimiter_still_works()
    {
        var src = "[NOTE]\n====\nMulti-line note content.\n====\n";
        var doc = BlockParser.Parse(src).Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.That(node.AdmonitionType, Is.EqualTo("NOTE"));
    }

    // ── New: paragraph-style admonitions ──────────────────────────────────────

    [Test]
    public void Paragraph_style_warning_becomes_admonition()
    {
        var src = "[WARNING]\nBe careful here.\n";
        var doc = BlockParser.Parse(src).Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(node.AdmonitionType, Is.EqualTo("WARNING"));
            Assert.That(node.Text, Is.EqualTo("Be careful here."));
        });
    }

    [TestCase("NOTE")]
    [TestCase("TIP")]
    [TestCase("WARNING")]
    [TestCase("CAUTION")]
    [TestCase("IMPORTANT")]
    public void Paragraph_style_admonition_recognized_for_all_types(string admonType)
    {
        var src = $"[{admonType}]\nSome paragraph text.\n";
        var doc = BlockParser.Parse(src).Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.That(node.AdmonitionType, Is.EqualTo(admonType));
    }

    [Test]
    public void Paragraph_style_admonition_supports_multiline_text()
    {
        var src = "[WARNING]\nLine one.\nLine two.\n";
        var doc = BlockParser.Parse(src).Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.That(node.Text, Is.EqualTo("Line one.\nLine two."));
    }

    [Test]
    public void Paragraph_style_admonition_terminated_by_blank_line()
    {
        var src = "[WARNING]\nWarning text.\n\nA following paragraph.\n";
        var doc = BlockParser.Parse(src).Document;

        var admon = (AdmonitionNode)doc.Children[0];
        var para = (ParagraphNode)doc.Children[1];

        Assert.Multiple(() =>
        {
            Assert.That(admon.AdmonitionType, Is.EqualTo("WARNING"));
            Assert.That(admon.Text, Is.EqualTo("Warning text."));
            Assert.That(para.Text, Is.EqualTo("A following paragraph."));
        });
    }

    [Test]
    public void Paragraph_style_admonition_at_eof_without_trailing_blank_line()
    {
        var src = "[WARNING]\nText right at end.";
        var doc = BlockParser.Parse(src).Document;
        var node = (AdmonitionNode)doc.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(node.AdmonitionType, Is.EqualTo("WARNING"));
            Assert.That(node.Text, Is.EqualTo("Text right at end."));
        });
    }

    [Test]
    public void Paragraph_style_admonition_does_not_consume_following_section()
    {
        // [WARNING] followed directly by section heading: no paragraph collected,
        // no admonition emitted, section parsed normally.
        var src = "= Doc Title\n\n[WARNING]\n== My Section\n";
        var doc = BlockParser.Parse(src).Document;

        Assert.That(doc.Children, Has.Count.EqualTo(1));
        var section = (SectionNode)doc.Children[0];
        Assert.That(section.Title, Is.EqualTo("My Section"));
    }
}
