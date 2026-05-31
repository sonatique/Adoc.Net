using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class DocBookConverterGapTests
{
    private static string Render(string adoc)
    {
        var result = BlockParser.Parse(adoc);
        return new DocBookRenderer().RenderToString(result.Document);
    }

    // ── Root xml:id from [[anchor]] before document title ─────────────────────

    [Test]
    public void Document_anchor_before_title_emits_xml_id_on_root()
    {
        var xml = Render("[[my-doc-id]]\n= My Title\n\nContent");
        Assert.That(xml, Does.Contain("xml:id=\"my-doc-id\""));
    }

    [Test]
    public void Document_without_anchor_omits_xml_id_on_root()
    {
        var xml = Render("= My Title\n\nContent");
        Assert.That(xml, Does.Not.Contain("xml:id="));
    }

    // ── Backtick monospace inside link/xref label ─────────────────────────────

    [Test]
    public void Backtick_inside_link_label_renders_as_literal()
    {
        var xml = Render("See link:http://example.com[the `Foo` class] for more.");
        // Asciidoctor: <link xl:href="..."><phrase>the </phrase><literal>Foo</literal>...
        Assert.That(xml, Does.Contain("<literal"));
        Assert.That(xml, Does.Contain("Foo"));
        // Backtick characters must NOT appear in the rendered output.
        Assert.That(xml, Does.Not.Contain("`Foo`"));
    }

    [Test]
    public void Backtick_inside_xref_label_renders_as_literal()
    {
        var xml = Render("xref:other.adoc[my `Foo` link]");
        Assert.That(xml, Does.Contain("<literal"));
        Assert.That(xml, Does.Not.Contain("`Foo`"));
    }

    // ── Regression: link without backticks renders normally ───────────────────

    [Test]
    public void Plain_link_label_renders_unchanged()
    {
        var xml = Render("See link:http://example.com[plain text label] for more.");
        Assert.That(xml, Does.Contain("plain text label"));
    }

    // ── <screen> linenumbering attribute ──────────────────────────────────────

    [Test]
    public void Listing_block_without_language_omits_linenumbering()
    {
        var xml = Render("----\nplain text content\n----");
        // No language → <screen> with NO linenumbering attribute (Asciidoctor behavior).
        Assert.That(xml, Does.Not.Contain("linenumbering=\"unnumbered\""),
            "screen without language should not declare linenumbering");
    }

    [Test]
    public void Source_block_with_language_keeps_linenumbering()
    {
        var xml = Render("[source,html]\n----\n<p>x</p>\n----");
        // Language → <programlisting language="html" linenumbering="unnumbered">
        Assert.That(xml, Does.Contain("linenumbering=\"unnumbered\""));
    }

    // ── Block titles parsed for inline formatting ─────────────────────────────

    [Test]
    public void Example_block_title_parses_backticks_as_literal()
    {
        var xml = Render(".Publish `MyClass` bean\n====\nSome content.\n====");
        Assert.That(xml, Does.Contain("<literal"));
        Assert.That(xml, Does.Not.Contain("`MyClass`"));
    }

    [Test]
    public void Sidebar_block_title_parses_backticks_as_literal()
    {
        var xml = Render(".Note about `MyClass`\n****\nSidebar content.\n****");
        Assert.That(xml, Does.Contain("<literal"));
        Assert.That(xml, Does.Not.Contain("`MyClass`"));
    }
}
