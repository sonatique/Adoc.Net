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
    public void Paragraph_rendered_as_para()
    {
        var result = BlockParser.Parse("Hello world");
        var xml = new DocBookRenderer().RenderToString(result.Document);
        Assert.That(xml, Does.Contain("<para>Hello world</para>"));
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
        Assert.That(xml, Does.Contain("<orderedlist>"));
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
        Assert.That(xml, Does.Contain("<entry>"));
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
}
