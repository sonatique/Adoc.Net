using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class DocBookRendererTests
{
    [Test]
    public void Empty_document_produces_valid_xml()
    {
        var doc = new DocumentNode();
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<?xml version=\"1.0\""));
        Assert.That(xml, Does.Contain("<article"));
        Assert.That(xml, Does.Contain("xmlns=\"http://docbook.org/ns/docbook\""));
    }

    [Test]
    public void Document_title_rendered()
    {
        var result = BlockParser.Parse("= My Document\n\nContent");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<title>My Document</title>"));
    }

    [Test]
    public void Section_rendered_with_title()
    {
        var result = BlockParser.Parse("= Doc\n\n== Section One\n\nText");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<section"));
        Assert.That(xml, Does.Contain("<title>Section One</title>"));
    }

    [Test]
    public void Paragraph_rendered_as_simpara()
    {
        // Asciidoctor emits <simpara> for inline-only body paragraphs (DocBook5 convention).
        // Top-level body paragraphs now match.
        var result = BlockParser.Parse("Hello world");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<simpara>Hello world</simpara>"));
    }

    [Test]
    public void Bold_rendered_as_emphasis_strong()
    {
        var result = BlockParser.Parse("This is *bold* text");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<emphasis role=\"strong\">bold</emphasis>"));
    }

    [Test]
    public void Italic_rendered_as_emphasis()
    {
        var result = BlockParser.Parse("This is _italic_ text");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<emphasis>italic</emphasis>"));
    }

    [Test]
    public void Monospace_rendered_as_literal()
    {
        var result = BlockParser.Parse("This is `code` text");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<literal>code</literal>"));
    }

    [Test]
    public void Unordered_list_rendered()
    {
        var result = BlockParser.Parse("* Item 1\n* Item 2");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<itemizedlist>"));
        Assert.That(xml, Does.Contain("<listitem>"));
    }

    [Test]
    public void Ordered_list_rendered()
    {
        var result = BlockParser.Parse(". First\n. Second");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<orderedlist"));
        Assert.That(xml, Does.Contain("numeration=\"arabic\""));
    }

    [Test]
    public void Source_block_rendered_as_programlisting()
    {
        var result = BlockParser.Parse("[source,csharp]\n----\nvar x = 1;\n----");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<programlisting"));
        Assert.That(xml, Does.Contain("language=\"csharp\""));
    }

    [Test]
    public void Admonition_rendered_as_note()
    {
        var result = BlockParser.Parse("NOTE: Important info");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<note>"));
    }

    [Test]
    public void Table_rendered_with_cals_model()
    {
        var result = BlockParser.Parse("|===\n| A | B\n| C | D\n|===");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<tgroup"));
        Assert.That(xml, Does.Contain("<row>"));
        Assert.That(xml, Does.Contain("<entry"));
        Assert.That(xml, Does.Contain("align=\"left\""));
        Assert.That(xml, Does.Contain("valign=\"top\""));
    }

    [Test]
    public void Image_rendered_as_mediaobject()
    {
        var result = BlockParser.Parse("image::photo.jpg[Alt text]");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<imagedata"));
        Assert.That(xml, Does.Contain("fileref=\"photo.jpg\""));
    }

    [Test]
    public void Link_rendered_with_xlink()
    {
        var result = BlockParser.Parse("Visit https://example.com for info");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("xlink:href=\"https://example.com\""));
    }

    [Test]
    public void Output_is_deterministic()
    {
        var result = BlockParser.Parse("= Title\n\n== Section\n\nParagraph with *bold*");
        var xml1 = new DocBookRenderer().RenderToString(result.Document);
        var xml2 = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml1, Is.EqualTo(xml2));
    }

    [Test]
    public void Description_list_rendered_as_variablelist()
    {
        var result = BlockParser.Parse("Term:: Description");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<variablelist>"));
        Assert.That(xml, Does.Contain("<term>"));
    }

    [Test]
    public void Format_returns_docbook()
    {
        Assert.That(new DocBookRenderer().Format, Is.EqualTo("docbook"));
    }

    [Test]
    public void Version_attribute_present()
    {
        var doc = new DocumentNode();
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("version=\"5.0\""));
    }

    [Test]
    public void Xlink_namespace_declared()
    {
        var doc = new DocumentNode();
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("xmlns:xlink=\"http://www.w3.org/1999/xlink\""));
    }

    [Test]
    public void Tip_admonition_rendered()
    {
        var result = BlockParser.Parse("TIP: Helpful tip");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<tip>"));
    }

    [Test]
    public void Warning_admonition_rendered()
    {
        var result = BlockParser.Parse("WARNING: Be careful");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<warning>"));
    }

    [Test]
    public void Page_break_rendered_as_processing_instruction()
    {
        var doc = new DocumentNode();
        doc.AddChild(new PageBreakNode());
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<?hard-pagebreak"));
    }

    // ── Asciidoctor structural-wrapper parity ─────────────────────────────

    [Test]
    public void Root_article_has_xml_lang_attribute()
    {
        // Asciidoctor adds xml:lang on the root. Defaults to "en".
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("xml:lang=\"en\""));
    }

    [Test]
    public void Root_article_xml_lang_honours_document_lang_attribute()
    {
        var doc = BlockParser.Parse("= T\n:lang: fr\n\nContent").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("xml:lang=\"fr\""));
    }

    [Test]
    public void Document_metadata_wrapped_in_info_element()
    {
        // Asciidoctor wraps the document title (and date when revdate is set) in <info>.
        var doc = BlockParser.Parse("= My Title\n\nContent").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<info>"));
        Assert.That(xml, Does.Contain("<title>My Title</title>"));
        Assert.That(xml, Does.Contain("</info>"));
    }

    [Test]
    public void Document_info_includes_date_when_revdate_set()
    {
        var doc = BlockParser.Parse("= T\nAuthor\nv1.0, 2025-06-15\n\nContent").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<date>2025-06-15</date>"));
    }

    [Test]
    public void Document_info_falls_back_to_docdate_when_revdate_not_set()
    {
        // Asciidoctor parity: when :revdate: is absent, <date> falls back to :docdate:
        // (which the parser always sets — to file mtime via ConvertFile, or today's
        // date as a default). Use :reproducible: to suppress the date entirely.
        var doc = BlockParser.Parse("= T\n\nContent").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<date>"));
    }

    [Test]
    public void List_item_with_inline_content_uses_simpara()
    {
        // List items with only inline content use <simpara>, not <para>.
        var doc = BlockParser.Parse("* Item one").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<simpara>Item one</simpara>"));
    }

    [Test]
    public void List_item_text_uses_simpara_even_when_followed_by_continuation_block()
    {
        // Asciidoctor emits <simpara> for the item text and the continuation
        // block as a sibling inside the <listitem>. AdocNet previously emitted
        // <para> when the item had children, splitting from Asciidoctor.
        var input = "* Item with block\n+\n----\ncode here\n----";
        var doc = BlockParser.Parse(input).Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<simpara>Item with block</simpara>"));
        Assert.That(xml, Does.Not.Contain("<para>Item with block</para>"));
    }

    [Test]
    public void Date_falls_back_from_revdate_to_docdate()
    {
        // When :revdate: is absent but :docdate: is set, asciidoctor still emits
        // <date> from docdate. AdocNet now matches.
        var doc = BlockParser.Parse("= Title\n:docdate: 2026-03-09\n\nText").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<date>2026-03-09</date>"));
    }

    [Test]
    public void Date_uses_revdate_when_both_set()
    {
        // :revdate: takes precedence over :docdate: (Asciidoctor parity).
        var doc = BlockParser.Parse("= T\n:revdate: 2026-04-01\n:docdate: 2026-03-09\n\nText").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<date>2026-04-01</date>"));
        Assert.That(xml, Does.Not.Contain("<date>2026-03-09</date>"));
    }

    [Test]
    public void Date_omitted_when_reproducible_set()
    {
        // :reproducible: opts out of the date entirely (suppresses both revdate and docdate).
        var doc = BlockParser.Parse("= T\n:revdate: 2026-04-01\n:reproducible:\n\nText").Document;
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Not.Contain("<date>"));
    }
}
