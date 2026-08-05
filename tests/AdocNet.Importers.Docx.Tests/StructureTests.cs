using AdocNet.Ast;

namespace AdocNet.Importers.Docx.Tests;

[TestFixture]
public class StructureTests
{
    [Test]
    public void FirstHeadingBecomesDocumentTitle()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Heading(1, "User Manual") +
            DocxBuilder.Paragraph("Intro text.")));

        Assert.That(adoc, Does.StartWith("= User Manual\n"));
        Assert.That(adoc, Does.Contain("Intro text."));
    }

    [Test]
    public void TitleStyleBecomesDocumentTitleAndSubtitleIsAppended()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Paragraph("Handbook", "Title") +
            DocxBuilder.Paragraph("Second Edition", "Subtitle") +
            DocxBuilder.Heading(1, "Overview")));

        Assert.That(adoc, Does.StartWith("= Handbook: Second Edition\n"));
        Assert.That(adoc, Does.Contain("== Overview"));
    }

    [Test]
    public void HeadingLevelsMapToSectionDepth()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Paragraph("Doc", "Title") +
            DocxBuilder.Heading(1, "One") +
            DocxBuilder.Heading(2, "Two") +
            DocxBuilder.Heading(3, "Three") +
            DocxBuilder.Heading(1, "Back to one")));

        Assert.That(adoc, Does.Contain("== One"));
        Assert.That(adoc, Does.Contain("=== Two"));
        Assert.That(adoc, Does.Contain("==== Three"));
        Assert.That(adoc, Does.Contain("== Back to one"));
    }

    [Test]
    public void SkippedHeadingLevelIsNormalised()
    {
        var result = ImportHarness.Import(new DocxBuilder().Body(
            DocxBuilder.Paragraph("Doc", "Title") +
            DocxBuilder.Heading(1, "One") +
            DocxBuilder.Heading(4, "Jumped")));

        var one = (SectionNode)result.Document.Children[0];
        var jumped = (SectionNode)one.Children[0];
        Assert.That(jumped.Level, Is.EqualTo(2));
        Assert.That(result.Report.Issues.Select(i => i.Code), Does.Contain("heading.level-normalised"));
    }

    [Test]
    public void CharacterFormattingMapsToInlineMarkup()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.ParagraphOf(
                DocxBuilder.Run("plain "),
                DocxBuilder.Run("bold", "<w:b/>"),
                DocxBuilder.Run(" "),
                DocxBuilder.Run("italic", "<w:i/>"),
                DocxBuilder.Run(" "),
                DocxBuilder.Run("code", "<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/>"),
                DocxBuilder.Run(" "),
                DocxBuilder.Run("both", "<w:b/><w:i/>"))));

        Assert.That(adoc, Does.Contain("plain *bold* _italic_ `code` *_both_*"));
    }

    [Test]
    public void SuperscriptSubscriptAndHighlightMap()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.ParagraphOf(
                DocxBuilder.Run("E=mc"),
                DocxBuilder.Run("2", "<w:vertAlign w:val=\"superscript\"/>"),
                DocxBuilder.Run(" H"),
                DocxBuilder.Run("2", "<w:vertAlign w:val=\"subscript\"/>"),
                DocxBuilder.Run("O "),
                DocxBuilder.Run("marked", "<w:highlight w:val=\"yellow\"/>"))));

        Assert.That(adoc, Does.Contain("E=mc^2^ H~2~O #marked#"));
    }

    [Test]
    public void UnderlineAndStrikethroughBecomeRoles()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.ParagraphOf(
                DocxBuilder.Run("under", "<w:u w:val=\"single\"/>"),
                DocxBuilder.Run(" "),
                DocxBuilder.Run("struck", "<w:strike/>"))));

        Assert.That(adoc, Does.Contain("[.underline]#under#"));
        Assert.That(adoc, Does.Contain("[.line-through]#struck#"));
    }

    [Test]
    public void BulletListWithNestingMapsToUnorderedList()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .Numbering(DocxBuilder.DefaultNumbering)
            .Body(
                DocxBuilder.ListItem("first", "1") +
                DocxBuilder.ListItem("nested", "1", 1) +
                DocxBuilder.ListItem("second", "1")));

        Assert.That(adoc, Does.Contain("* first\n** nested\n* second"));
    }

    [Test]
    public void OrderedListUsesNumberFormatStyle()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .Numbering(DocxBuilder.DefaultNumbering)
            .Body(
                DocxBuilder.ListItem("one", "2") +
                DocxBuilder.ListItem("one-a", "2", 1)));

        Assert.That(adoc, Does.Contain(". one"));
        Assert.That(adoc, Does.Contain("[loweralpha]"));
        Assert.That(adoc, Does.Contain(".. one-a"));
    }

    [Test]
    public void ListItemContinuationParagraphAttachesToItem()
    {
        var result = ImportHarness.Import(new DocxBuilder()
            .Numbering(DocxBuilder.DefaultNumbering)
            .Body(
                DocxBuilder.ListItem("item", "1") +
                DocxBuilder.Paragraph("continuation", "ListParagraph")));

        var list = (ListNode)result.Document.Children[0];
        var item = (ListItemNode)list.Children[0];
        Assert.That(item.Children.Count, Is.EqualTo(1));
        Assert.That(item.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void TableMapsRowsColumnsAndHeader()
    {
        var body =
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"4000\"/></w:tblGrid>" +
            "<w:tr><w:trPr><w:tblHeader/></w:trPr>" +
            "<w:tc><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>Description</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr>" +
            "<w:tc><w:p><w:r><w:t>alpha</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>first letter</w:t></w:r></w:p></w:tc></w:tr>" +
            "</w:tbl>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("cols=\"1,2\""));
        Assert.That(adoc, Does.Contain("options=\"header\""));
        Assert.That(adoc, Does.Contain("|Name|Description"));
        Assert.That(adoc, Does.Contain("|alpha|first letter"));
    }

    [Test]
    public void TableSpansMapToColspanAndRowspan()
    {
        var body =
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/></w:tblGrid>" +
            "<w:tr>" +
            "<w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>wide</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr>" +
            "<w:tc><w:tcPr><w:vMerge w:val=\"restart\"/></w:tcPr><w:p><w:r><w:t>tall</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>a</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr>" +
            "<w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc>" +
            "<w:tc><w:p><w:r><w:t>b</w:t></w:r></w:p></w:tc></w:tr>" +
            "</w:tbl>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("2+|wide"));
        Assert.That(adoc, Does.Contain(".2+|tall"));
    }

    [Test]
    public void ImageIsExtractedAndReferencedWithSize()
    {
        var result = ImportHarness.Import(new DocxBuilder()
            .Image("rId10", "image1.png", DocxBuilder.SamplePng)
            .Body("<w:p>" + DocxBuilder.Drawing("rId10", 1905000, 952500, "A diagram") + "</w:p>"));

        var image = (BlockImageNode)result.Document.Children[0];
        Assert.That(image.Target, Is.EqualTo("media/image1.png"));
        Assert.That(image.Alt, Is.EqualTo("A diagram"));
        Assert.That(image.Width, Is.EqualTo("200"));
        Assert.That(image.Height, Is.EqualTo("100"));
        Assert.That(result.Media.Count, Is.EqualTo(1));
        Assert.That(result.Media[0].Content, Is.EqualTo(DocxBuilder.SamplePng));
    }

    [Test]
    public void CaptionAfterImageBecomesBlockTitle()
    {
        var result = ImportHarness.Import(new DocxBuilder()
            .Image("rId10", "image1.png", DocxBuilder.SamplePng)
            .Body("<w:p>" + DocxBuilder.Drawing("rId10") + "</w:p>" +
                  DocxBuilder.Paragraph("Figure 1. The pipeline", "Caption")));

        var image = (BlockImageNode)result.Document.Children[0];
        Assert.That(image.Title, Is.EqualTo("Figure 1. The pipeline"));
    }

    [Test]
    public void CaptionBeforeTableBecomesTableTitle()
    {
        var body = DocxBuilder.Paragraph("Table 1. Results", "Caption") +
                   "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>x</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

        var result = ImportHarness.Import(new DocxBuilder().Body(body));
        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Title, Is.EqualTo("Table 1. Results"));
    }

    [Test]
    public void ExternalHyperlinkBecomesLinkMacro()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .Hyperlink("rId5", "https://example.com/docs")
            .Body("<w:p><w:hyperlink r:id=\"rId5\"><w:r><w:t>the docs</w:t></w:r></w:hyperlink></w:p>"));

        Assert.That(adoc, Does.Contain("link:https://example.com/docs[the docs]"));
    }

    [Test]
    public void InternalHyperlinkBecomesCrossReference()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"target_section\"/><w:r><w:t>Target</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"1\"/></w:p>" +
            "<w:p><w:hyperlink w:anchor=\"target_section\"><w:r><w:t>see above</w:t></w:r></w:hyperlink></w:p>"));

        Assert.That(adoc, Does.Contain("[[target_section]]"));
        Assert.That(adoc, Does.Contain("<<target_section,see above>>"));
    }

    [Test]
    public void HyperlinkFieldBecomesLinkMacro()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            "<w:p>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \"https://example.org\" </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t>example</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "</w:p>"));

        Assert.That(adoc, Does.Contain("link:https://example.org[example]"));
    }

    [Test]
    public void FootnoteReferenceBecomesFootnoteMacro()
    {
        var footnotes =
            "<w:footnotes xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:footnote w:id=\"0\" w:type=\"separator\"><w:p/></w:footnote>" +
            "<w:footnote w:id=\"2\"><w:p><w:r><w:t xml:space=\"preserve\"> See the appendix.</w:t></w:r></w:p></w:footnote>" +
            "</w:footnotes>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .Footnotes(footnotes)
            .Body("<w:p><w:r><w:t>Claim</w:t></w:r><w:r><w:footnoteReference w:id=\"2\"/></w:r></w:p>"));

        // The reference sits directly after the word in Word, and AsciiDoc's
        // footnote macro is unconstrained, so no separator is inserted.
        Assert.That(adoc, Does.Contain("Claimfootnote:[See the appendix.]"));
    }

    [Test]
    public void AdmonitionPrefixBecomesAdmonition()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Paragraph("NOTE: Back up first.")));

        Assert.That(adoc.Trim(), Is.EqualTo("NOTE: Back up first."));
    }

    [Test]
    public void SingleCellTableBecomesAdmonition()
    {
        var body = "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>WARNING: This deletes data.</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc.Trim(), Is.EqualTo("WARNING: This deletes data."));
    }

    [Test]
    public void QuoteStyleParagraphsMergeIntoQuoteBlock()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            DocxBuilder.Paragraph("First line.", "Quote") +
            DocxBuilder.Paragraph("Second line.", "IntenseQuote")));

        Assert.That(adoc, Does.Contain("____\nFirst line.\n\nSecond line.\n____"));
    }

    [Test]
    public void MonospaceParagraphsBecomeListingBlock()
    {
        var body =
            "<w:p><w:pPr><w:pStyle w:val=\"HTMLPreformatted\"/></w:pPr><w:r><w:t xml:space=\"preserve\">var x = 1;</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"HTMLPreformatted\"/></w:pPr><w:r><w:tab/><w:t xml:space=\"preserve\">return x;</w:t></w:r></w:p>";

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("----\nvar x = 1;\n\treturn x;\n----"));
    }

    [Test]
    public void PageAndThematicBreaksAreKept()
    {
        var body =
            DocxBuilder.Paragraph("before") +
            "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>" +
            "<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\"/></w:pBdr></w:pPr></w:p>" +
            DocxBuilder.Paragraph("after");

        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("<<<"));
        Assert.That(adoc, Does.Contain("'''"));
    }

    [Test]
    public void HardLineBreakUsesHardbreaksOption()
    {
        var body = "<w:p><w:r><w:t>line one</w:t><w:br/><w:t>line two</w:t></w:r></w:p>";
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("[%hardbreaks]\nline one\nline two"));
    }

    [Test]
    public void CorePropertiesBecomeHeaderAttributes()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder()
            .CoreProperties(title: "Spec", creator: "Jane Roe", revision: "7", modified: "2026-03-04T10:11:12Z")
            .Body(DocxBuilder.Paragraph("Body text.")));

        Assert.That(adoc, Does.StartWith("= Spec\n"));
        Assert.That(adoc, Does.Contain(":author: Jane Roe"));
        Assert.That(adoc, Does.Contain(":revnumber: 7"));
        Assert.That(adoc, Does.Contain(":revdate: 2026-03-04"));
    }

    [Test]
    public void TrackedChangesFollowTheSelectedSide()
    {
        var body = "<w:p><w:r><w:t xml:space=\"preserve\">kept </w:t></w:r>" +
                   "<w:ins w:id=\"1\"><w:r><w:t xml:space=\"preserve\">added </w:t></w:r></w:ins>" +
                   "<w:del w:id=\"2\"><w:r><w:delText xml:space=\"preserve\">removed </w:delText></w:r></w:del>" +
                   "<w:r><w:t>tail</w:t></w:r></w:p>";

        var accepted = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));
        Assert.That(accepted, Does.Contain("kept added tail"));

        var rejected = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body),
            new DocxImportOptions { TrackedChanges = TrackedChangeHandling.Reject });
        Assert.That(rejected, Does.Contain("kept removed tail"));
    }

    [Test]
    public void TableOfContentsFieldBecomesTocAttribute()
    {
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(
            "<w:p>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t>Chapter 1 ....... 3</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "</w:p>" +
            DocxBuilder.Heading(1, "Chapter 1")));

        Assert.That(adoc, Does.Contain(":toc:"));
        Assert.That(adoc, Does.Not.Contain("......."));
    }

    [Test]
    public void SdtContentIsUnwrapped()
    {
        var body = "<w:sdt><w:sdtContent>" + DocxBuilder.Paragraph("inside a content control") + "</w:sdtContent></w:sdt>";
        var adoc = ImportHarness.ToAsciiDoc(new DocxBuilder().Body(body));

        Assert.That(adoc, Does.Contain("inside a content control"));
    }

    [Test]
    public void NotADocxThrowsImportException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Throws<DocxImportException>(() => new DocxImporter().Import(stream));
    }
}
