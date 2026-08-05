using AdocNet.Parser;

namespace AdocNet.Importers.Docx.Tests;

/// <summary>
/// The importer's correctness criterion: text that was in the Word document
/// must come out of the AsciiDoc pipeline unchanged. Each case builds a .docx
/// holding the literal string, imports it, renders the emitted AsciiDoc
/// through the real parser and HTML renderer, and compares the visible text.
/// </summary>
[TestFixture]
public class RoundTripFidelityTests
{
    private static readonly string[] LiteralTexts =
    {
        "plain sentence with nothing special",
        "2 * 3 = 6 and 4 * 5 = 20",
        "a *star pair* in prose",
        "snake_case_name and another_one_here",
        "trailing underscore at end_",
        "C++ and C# and F#",
        "a backslash \\ and a path C:\\temp\\file.txt",
        "braces {attribute} and {another}",
        "angle brackets <<not an xref>> here",
        "bare url http://example.com/page?x=1 in text",
        "don't stop believing",
        "range 3 -- 4 and spaced -- dash",
        "ellipsis ... continues",
        "copyright (C) 2026, registered (R), trademark (TM)",
        "arrows -> and <- and => and <=",
        "a | pipe character",
        "hash #tag# and caret ^up^ and tilde ~down~",
        "backticks `code` inline",
        "plus +signs+ around",
        "5 < 6 > 4 & 7",
        "footnote:[looks like a macro]",
        "image:not-an-image.png[alt]",
        "link:http://example.com[label]",
        "50% off, 100% sure",
        "callout marker <1> at the end",
        "an [attribute list] in text",
        "emoji 🚀 and accents éàü and CJK 日本語",
    };

    [TestCaseSource(nameof(LiteralTexts))]
    public void LiteralTextSurvivesTheRoundTrip(string text)
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(DocxBuilder.Paragraph(text)));
        var rendered = ImportHarness.RenderedText(adoc);

        Assert.That(rendered, Is.EqualTo(ImportHarness.CollapseWhitespace(text)),
            $"emitted AsciiDoc was:\n{adoc}");
    }

    private static readonly string[] BlockStartTexts =
    {
        "= not a document title",
        "== not a section",
        "* not a bullet",
        "- not a bullet either",
        ". not an ordered item",
        "// not a comment",
        ":attr: not an attribute entry",
        "|=== not a table",
        "---- not a listing fence",
        "[not an attribute line]",
        "term:: not a description list",
    };

    [TestCaseSource(nameof(BlockStartTexts))]
    public void TextThatLooksLikeABlockMarkerStaysText(string text)
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(DocxBuilder.Paragraph(text)));
        var rendered = ImportHarness.RenderedText(adoc);

        Assert.That(rendered, Is.EqualTo(ImportHarness.CollapseWhitespace(text)),
            $"emitted AsciiDoc was:\n{adoc}");
    }

    [Test]
    public void FormattedTextSurvivesWithItsFormatting()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.ParagraphOf(
                DocxBuilder.Run("Total: "),
                DocxBuilder.Run("2 * 3", "<w:b/>"),
                DocxBuilder.Run(" equals "),
                DocxBuilder.Run("six_units", "<w:i/>"))));

        var document = AdocParser.Parse(adoc).Document;
        var html = new Converters.Html.HtmlRenderer().RenderToString(document);

        Assert.That(ImportHarness.HtmlToText(html), Is.EqualTo("Total: 2 * 3 equals six_units"));
        Assert.That(html, Does.Contain("<strong>2 * 3</strong>"));
        Assert.That(html, Does.Contain("<em>six_units</em>"));
    }

    [Test]
    public void TableCellTextWithPipesSurvives()
    {
        var body =
            "<w:tbl><w:tr>" +
            "<w:tc><w:p><w:r><w:t>a | b</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>c * d</w:t></w:r></w:p></w:tc>" +
            "</w:tr></w:tbl>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));
        var rendered = ImportHarness.RenderedText(adoc);

        Assert.That(rendered, Does.Contain("a | b"));
        Assert.That(rendered, Does.Contain("c * d"));
    }

    [Test]
    public void CodeBlockContentIsVerbatim()
    {
        const string code = "if (a < b) { return \"*not bold*\"; } // {attr}";
        var body = "<w:p><w:pPr><w:pStyle w:val=\"HTMLPreformatted\"/></w:pPr>" +
                   $"<w:r><w:t xml:space=\"preserve\">{DocxBuilder.Escape(code)}</w:t></w:r></w:p>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));
        var rendered = ImportHarness.RenderedText(adoc);

        Assert.That(rendered, Is.EqualTo(ImportHarness.CollapseWhitespace(code)));
    }

    [Test]
    public void HeadingTextWithMarkupCharactersSurvives()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Paragraph("Doc", "Title") +
            DocxBuilder.Heading(1, "Using * and _ in a heading")));

        var rendered = ImportHarness.RenderedText(adoc);
        Assert.That(rendered, Does.Contain("Using * and _ in a heading"));
    }

    [Test]
    public void ListItemTextWithMarkupCharactersSurvives()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .Numbering(DocxBuilder.DefaultNumbering)
            .Body(
                DocxBuilder.ListItem("a * b", "1") +
                DocxBuilder.ListItem("c_d_e", "1")));

        var rendered = ImportHarness.RenderedText(adoc);
        Assert.That(rendered, Does.Contain("a * b"));
        Assert.That(rendered, Does.Contain("c_d_e"));
    }
}
