using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class Beta22ParityTests
{
    private static string RenderHtml(string input, bool fullDoc = false)
    {
        var result = AdocParser.Parse(input);
        var renderer = new HtmlRenderer();
        var options = new HtmlRenderOptions { FullDocument = fullDoc };
        return renderer.RenderToString(result.Document, options);
    }

    private static DocumentNode Parse(string input)
        => AdocParser.Parse(input).Document;

    // ── 1. Image width/height ──────────────────────────────────────────────

    [Test]
    public void Block_image_positional_width_height()
    {
        var doc = Parse("image::photo.jpg[Alt, 640, 480]");
        var img = doc.Children.OfType<BlockImageNode>().Single();
        Assert.That(img.Alt, Is.EqualTo("Alt"));
        Assert.That(img.Width, Is.EqualTo("640"));
        Assert.That(img.Height, Is.EqualTo("480"));
    }

    [Test]
    public void Block_image_named_width_height()
    {
        var doc = Parse("image::photo.jpg[Alt, width=320, height=240]");
        var img = doc.Children.OfType<BlockImageNode>().Single();
        Assert.That(img.Width, Is.EqualTo("320"));
        Assert.That(img.Height, Is.EqualTo("240"));
    }

    [Test]
    public void Block_image_width_height_rendered()
    {
        var html = RenderHtml("image::photo.jpg[Alt, 640, 480]");
        Assert.That(html, Does.Contain("width=\"640\""));
        Assert.That(html, Does.Contain("height=\"480\""));
    }

    // ── 2. Image link ──────────────────────────────────────────────────────

    [Test]
    public void Block_image_with_link()
    {
        var doc = Parse("image::photo.jpg[Alt, link=https://example.org]");
        var img = doc.Children.OfType<BlockImageNode>().Single();
        Assert.That(img.Link, Is.EqualTo("https://example.org"));
    }

    [Test]
    public void Block_image_with_link_rendered()
    {
        var html = RenderHtml("image::photo.jpg[Alt, link=https://example.org]");
        Assert.That(html, Does.Contain("<a class=\"image\" href=\"https://example.org\">"));
        Assert.That(html, Does.Contain("</a>"));
    }

    // ── 3. idprefix / idseparator ──────────────────────────────────────────

    [Test]
    public void Custom_idprefix()
    {
        var doc = Parse(":idprefix: id_\n\n== My Section");
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Id, Does.StartWith("id_"));
    }

    [Test]
    public void Empty_idprefix()
    {
        var doc = Parse(":idprefix:\n\n== My Section");
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Id, Does.Not.StartWith("_"));
        Assert.That(section.Id, Is.EqualTo("my_section"));
    }

    [Test]
    public void Custom_idseparator()
    {
        var doc = Parse(":idseparator: -\n\n== My Section Title");
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Id, Does.Contain("-"));
        Assert.That(section.Id, Does.Not.Contain("_section"));
    }

    [Test]
    public void Empty_idseparator()
    {
        var doc = Parse(":idseparator:\n\n== My Section");
        var section = doc.Children.OfType<SectionNode>().First();
        // With empty separator, spaces are just dropped
        Assert.That(section.Id, Is.EqualTo("_mysection"));
    }

    // ── 4. Image roles/positioning ─────────────────────────────────────────

    [Test]
    public void Block_image_with_role_rendered()
    {
        var html = RenderHtml("[.left]\nimage::photo.jpg[Alt]");
        Assert.That(html, Does.Contain("imageblock left"));
    }

    [Test]
    public void Block_image_multiple_roles_rendered()
    {
        var html = RenderHtml("[.text-center.thumb]\nimage::photo.jpg[Alt]");
        Assert.That(html, Does.Contain("text-center"));
        Assert.That(html, Does.Contain("thumb"));
    }

    // ── 5. :noheader: ──────────────────────────────────────────────────────

    [Test]
    public void Noheader_suppresses_title()
    {
        var html = RenderHtml("= Document Title\n:noheader:\n\nBody text.", fullDoc: true);
        Assert.That(html, Does.Not.Contain("<h1>Document Title</h1>"));
        Assert.That(html, Does.Contain("Body text."));
    }

    [Test]
    public void Without_noheader_shows_title()
    {
        var html = RenderHtml("= Document Title\n\nBody text.", fullDoc: true);
        Assert.That(html, Does.Contain("<h1>Document Title</h1>"));
    }

    // ── 6. :reproducible: ──────────────────────────────────────────────────

    [Test]
    public void Reproducible_suppresses_last_updated()
    {
        var html = RenderHtml("= Doc\n:reproducible:\n\nText.", fullDoc: true);
        Assert.That(html, Does.Not.Contain("Last updated"));
    }

    // ── 7. xrefstyle ──────────────────────────────────────────────────────

    [Test]
    public void Xrefstyle_basic()
    {
        var input = ":sectnums:\n:xrefstyle: basic\n\n== Introduction\n\nSee <<_introduction>>.";
        var html = RenderHtml(input);
        Assert.That(html, Does.Contain(">Introduction</a>"));
    }

    [Test]
    public void Xrefstyle_short()
    {
        var input = ":sectnums:\n:xrefstyle: short\n\n== Introduction\n\nSee <<_introduction>>.";
        var html = RenderHtml(input);
        Assert.That(html, Does.Contain("Section 1"));
    }

    [Test]
    public void Xrefstyle_full()
    {
        var input = ":sectnums:\n:xrefstyle: full\n\n== Introduction\n\nSee <<_introduction>>.";
        var html = RenderHtml(input);
        Assert.That(html, Does.Contain("Section 1"));
        Assert.That(html, Does.Contain("Introduction"));
    }

    // ── 8. Source highlight ────────────────────────────────────────────────

    [Test]
    public void Source_block_highlight_parsed()
    {
        var doc = Parse("[source,java,highlight=\"1,3\"]\n----\nline1\nline2\nline3\n----");
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(block.Highlight, Is.EqualTo("1,3"));
    }

    [Test]
    public void Source_block_highlight_rendered()
    {
        var html = RenderHtml("[source,java,highlight=\"1,3\"]\n----\nline1\nline2\nline3\n----");
        Assert.That(html, Does.Contain("<span class=\"highlight\">line1</span>"));
        Assert.That(html, Does.Not.Contain("<span class=\"highlight\">line2</span>"));
        Assert.That(html, Does.Contain("<span class=\"highlight\">line3</span>"));
    }

    [Test]
    public void Source_block_highlight_range()
    {
        var doc = Parse("[source,java,highlight=\"2-4\"]\n----\na\nb\nc\nd\ne\n----");
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(block.Highlight, Is.EqualTo("2-4"));
    }

    // ── 9. [#id]#text# inline anchor ───────────────────────────────────────

    [Test]
    public void Inline_anchor_with_id()
    {
        var doc = Parse("Some [#myid]#highlighted# text.");
        var para = doc.Children.OfType<ParagraphNode>().First();
        var highlight = para.Inlines.OfType<HighlightInlineNode>().First();
        Assert.That(highlight.Id, Is.EqualTo("myid"));
    }

    [Test]
    public void Inline_anchor_with_id_and_role()
    {
        var doc = Parse("Some [#myid.warning]#highlighted# text.");
        var para = doc.Children.OfType<ParagraphNode>().First();
        var highlight = para.Inlines.OfType<HighlightInlineNode>().First();
        Assert.That(highlight.Id, Is.EqualTo("myid"));
        Assert.That(highlight.Roles, Does.Contain("warning"));
    }

    [Test]
    public void Inline_anchor_rendered_with_id()
    {
        var html = RenderHtml("Some [#myid]#highlighted# text.");
        Assert.That(html, Does.Contain("id=\"myid\""));
    }

    // ── 10. Description list multiple terms ────────────────────────────────

    [Test]
    public void Multiple_terms_parsed()
    {
        var input = "Term1::\nTerm2::\nShared definition.";
        var doc = Parse(input);
        var dl = doc.Children.OfType<DescriptionListNode>().First();
        var item = dl.Children.OfType<DescriptionItemNode>().First();
        Assert.That(item.Terms.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(item.Terms[0], Is.EqualTo("Term1"));
        Assert.That(item.Terms[1], Is.EqualTo("Term2"));
    }

    [Test]
    public void Multiple_terms_rendered()
    {
        var html = RenderHtml("Term1::\nTerm2::\nTerm3:: Shared definition.");
        // Each term should get its own <dt>
        Assert.That(html, Does.Contain("Term1"));
        Assert.That(html, Does.Contain("Term2"));
        Assert.That(html, Does.Contain("Term3"));
    }

    // ── 11. Audio width ────────────────────────────────────────────────────

    [Test]
    public void Audio_width_parsed()
    {
        var doc = Parse("audio::track.mp3[width=300]");
        var audio = doc.Children.OfType<AudioNode>().First();
        Assert.That(audio.Width, Is.EqualTo("300"));
    }

    [Test]
    public void Audio_width_rendered()
    {
        var html = RenderHtml("audio::track.mp3[width=300]");
        Assert.That(html, Does.Contain("width=\"300\""));
    }

    // ── Inline image width/height ──────────────────────────────────────────

    [Test]
    public void Inline_image_width_height()
    {
        var doc = Parse("Text image:icon.png[Icon, 16, 16] here.");
        var para = doc.Children.OfType<ParagraphNode>().First();
        var img = para.Inlines.OfType<InlineImageNode>().First();
        Assert.That(img.Width, Is.EqualTo("16"));
        Assert.That(img.Height, Is.EqualTo("16"));
    }

    [Test]
    public void Inline_image_width_height_rendered()
    {
        var html = RenderHtml("Text image:icon.png[Icon, 16, 16] here.");
        Assert.That(html, Does.Contain("width=\"16\""));
        Assert.That(html, Does.Contain("height=\"16\""));
    }

    // ── Content div wrapper on block images ─────────────────────────────

    [Test]
    public void Block_image_has_content_div()
    {
        var html = RenderHtml("image::photo.jpg[Alt]");
        Assert.That(html, Does.Contain("<div class=\"content\">"));
    }
}
