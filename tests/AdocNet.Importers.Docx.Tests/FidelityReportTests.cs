using AdocNet.Ast;

namespace AdocNet.Importers.Docx.Tests;

/// <summary>
/// Exercises the whole importer on a document that uses every construct the
/// mapping covers, and pins the reported fidelity for it. The metric counts
/// content-bearing WordprocessingML units that reached the AST as an
/// equivalent AsciiDoc construct — see <see cref="DocxImportReport"/>.
/// </summary>
[TestFixture]
public class FidelityReportTests
{
    private static DocxBuilder KitchenSink()
    {
        var body =
            DocxBuilder.Paragraph("Field Guide", "Title") +
            DocxBuilder.Paragraph("Second Edition", "Subtitle") +
            DocxBuilder.Heading(1, "Getting started") +
            DocxBuilder.ParagraphOf(
                DocxBuilder.Run("Install the "),
                DocxBuilder.Run("adocnet", "<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/>"),
                DocxBuilder.Run(" tool, then run "),
                DocxBuilder.Run("build", "<w:b/>"),
                DocxBuilder.Run(" once.")) +
            DocxBuilder.Paragraph("NOTE: Requires the .NET SDK.") +
            DocxBuilder.ListItem("Download the package", "1") +
            DocxBuilder.ListItem("Verify the signature", "1", 1) +
            DocxBuilder.ListItem("Install it", "1") +
            DocxBuilder.Heading(2, "Configuration") +
            DocxBuilder.ListItem("First step", "2") +
            DocxBuilder.ListItem("Second step", "2") +
            "<w:p><w:pPr><w:pStyle w:val=\"HTMLPreformatted\"/></w:pPr><w:r><w:t xml:space=\"preserve\">adocnet build .</w:t></w:r></w:p>" +
            DocxBuilder.Paragraph("The pipeline runs in three stages.", "Quote") +
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"4800\"/></w:tblGrid>" +
            "<w:tr><w:trPr><w:tblHeader/></w:trPr>" +
            "<w:tc><w:p><w:r><w:t>Option</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>Meaning</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr><w:tc><w:p><w:r><w:t>--strict</w:t></w:r></w:p></w:tc>" +
            "<w:tc><w:p><w:r><w:t>Fail on warnings</w:t></w:r></w:p></w:tc></w:tr>" +
            "</w:tbl>" +
            "<w:p>" + DocxBuilder.Drawing("rId10", 1905000, 952500, "Pipeline overview") + "</w:p>" +
            DocxBuilder.Paragraph("Figure 1. Pipeline overview", "Caption") +
            "<w:p><w:hyperlink r:id=\"rId5\"><w:r><w:t>online documentation</w:t></w:r></w:hyperlink>" +
            "<w:r><w:t xml:space=\"preserve\"> covers the rest.</w:t></w:r>" +
            "<w:r><w:footnoteReference w:id=\"2\"/></w:r></w:p>";

        var footnotes =
            "<w:footnotes xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:footnote w:id=\"0\" w:type=\"separator\"><w:p/></w:footnote>" +
            "<w:footnote w:id=\"2\"><w:p><w:r><w:t xml:space=\"preserve\"> Updated quarterly.</w:t></w:r></w:p></w:footnote>" +
            "</w:footnotes>";

        return new DocxBuilder()
            .Numbering(DocxBuilder.DefaultNumbering)
            .Footnotes(footnotes)
            .Hyperlink("rId5", "https://example.com/docs")
            .Image("rId10", "image1.png", DocxBuilder.SamplePng)
            .CoreProperties(creator: "Jane Roe", revision: "3", modified: "2026-01-15T09:00:00Z")
            .Body(body);
    }

    [Test]
    public void KitchenSinkDocumentMapsWithoutLoss()
    {
        var result = ImportHarness.Import(KitchenSink());
        TestContext.Out.WriteLine(result.Report.ToSummary());

        var losses = result.Report.Issues.Where(i => i.Severity == DocxIssueSeverity.Loss).ToList();
        Assert.That(losses, Is.Empty, string.Join("\n", losses.Select(i => i.ToString())));
        Assert.That(result.Report.Fidelity, Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void KitchenSinkStructureIsComplete()
    {
        var result = ImportHarness.Import(KitchenSink());
        var document = result.Document;

        Assert.That(document.Title, Is.EqualTo("Field Guide: Second Edition"));
        Assert.That(result.Report.Sections, Is.EqualTo(2));
        Assert.That(result.Report.ListItems, Is.EqualTo(5));
        Assert.That(result.Report.Tables, Is.EqualTo(1));
        Assert.That(result.Report.Images, Is.EqualTo(1));
        Assert.That(result.Report.Footnotes, Is.EqualTo(1));
        Assert.That(result.Report.Hyperlinks, Is.EqualTo(1));

        var section = (SectionNode)document.Children[0];
        Assert.That(section.Title, Is.EqualTo("Getting started"));
    }

    [Test]
    public void KitchenSinkTextSurvivesRendering()
    {
        var adoc = ImportHarness.ToAsciiDoc(KitchenSink());
        var rendered = ImportHarness.RenderedText(adoc);

        foreach (var fragment in new[]
                 {
                     // The document title lives in the header, which the HTML
                     // fragment renderer does not emit; it is asserted on the AST.
                     "Getting started", "Install the adocnet tool",
                     "Requires the .NET SDK.", "Download the package", "Verify the signature",
                     "Configuration", "First step", "adocnet build .",
                     "The pipeline runs in three stages.", "Option", "--strict", "Fail on warnings",
                     "Pipeline overview", "online documentation", "covers the rest.", "Updated quarterly.",
                 })
        {
            Assert.That(rendered, Does.Contain(fragment), $"emitted AsciiDoc was:\n{adoc}");
        }
    }

    [Test]
    public void UnsupportedContentIsReportedNotSilentlyDropped()
    {
        var body =
            "<w:p><w:r><w:object><w:objectEmbed/></w:object></w:r></w:p>" +
            "<w:p><w:r><w:rPr><w:color w:val=\"FF0000\"/></w:rPr><w:t>red text</w:t></w:r></w:p>" +
            "<w:p><w:r><w:br w:type=\"column\"/></w:r></w:p>" +
            "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"100\" cy=\"100\"/>" +
            "<a:graphic><a:graphicData/></a:graphic></wp:inline></w:drawing></w:r></w:p>";

        var result = ImportHarness.Import(new DocxBuilder().Body(body),
            new DocxImportOptions { PreserveFormattingAsRoles = false });
        var codes = result.Report.Issues.Select(i => i.Code).ToList();

        Assert.That(codes, Does.Contain("embedded-object.dropped"));
        Assert.That(codes, Does.Contain("run.color-dropped"));
        Assert.That(codes, Does.Contain("column-break.dropped"));
        Assert.That(codes, Does.Contain("drawing.unsupported"));
        Assert.That(result.Report.Fidelity, Is.LessThan(1.0));

        // The text of the run whose colour was dropped is still there.
        Assert.That(ImportHarness.RenderedText(new AdocNet.Emitter.AsciidocEmitter().Emit(result.Document)),
            Does.Contain("red text"));
    }

    [Test]
    public void ReportSummaryGroupsRepeatedIssues()
    {
        var body = string.Concat(Enumerable.Repeat(
            "<w:p><w:r><w:rPr><w:color w:val=\"3366FF\"/></w:rPr><w:t>tinted</w:t></w:r></w:p>", 5));

        var result = ImportHarness.Import(new DocxBuilder().Body(body));
        var summary = result.Report.ToSummary();

        Assert.That(summary, Does.Contain("run.color-as-role ×5"));
        Assert.That(summary, Does.Contain("fidelity:"));
    }
}
