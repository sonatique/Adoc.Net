using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for footnotes inside table cells in the PDF renderer (issue
/// #57): a <c>footnote:[…]</c> in a cell must produce a reference marker in the
/// cell and an entry in the document footnote list — not be inlined as body text.
/// </summary>
[TestFixture]
public class PdfTableFootnoteTests
{
    private const string Doc =
        ":myfn: footnote:mine[Body defined via attribute]\n\n" +
        "= FN in tables\n\n" +
        "Paragraph with attr ref {myfn} and direct footnote:[direct in paragraph].\n\n" +
        ".Table\n" +
        "|===\n" +
        "| Direct in cell footnote:[cell direct body] | Attr in cell {myfn}\n" +
        "| x | y\n" +
        "|===\n";

    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    [Test]
    public void Cell_footnote_gets_marker_and_list_entry_not_inlined_body()
    {
        var pdf = Render(Doc);
        var frags = PdfTextExtractor.ExtractText(pdf);
        var norm = PdfTextExtractor.NormalizeText(frags);

        // The cell's direct footnote is the 3rd registered (after the two paragraph
        // ones), so it must get a [3] marker — and its body must land in the list.
        Assert.That(norm, Does.Contain("[3]"), "cell footnote should have a reference marker");
        Assert.That(norm, Does.Contain("cell direct body"), "cell footnote body should appear in the list");

        // The body belongs in the footnote list AFTER the table, not inlined in the
        // cell: it must come after the last table body cell ('y').
        int lastBodyCell = frags.FindLastIndex(f => f.Trim() == "y");
        int cellFnBody = frags.FindIndex(f => f.Contains("cell direct body"));
        Assert.That(lastBodyCell, Is.GreaterThanOrEqualTo(0), "expected the 'y' body cell");
        Assert.That(cellFnBody, Is.GreaterThan(lastBodyCell),
            "cell footnote body must be in the footnote list (after the table), not inlined in the cell");

        // The cell that holds the footnote must not inline the body next to its text.
        Assert.That(frags.Any(f => f.Contains("Direct in cell") && f.Contains("cell direct body")),
            Is.False, "footnote body must not be inlined into the cell content");
    }

    [Test]
    public void Attribute_footnote_reused_in_cell_dedupes_to_same_number()
    {
        // {myfn} resolves to footnote:mine[...]; used in the paragraph (→ [1]) and
        // again in a cell — the cell reference must reuse [1], not add a new entry.
        var pdf = Render(Doc);
        var norm = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        // Exactly three distinct footnotes: para attr [1], para direct [2], cell [3].
        Assert.That(norm, Does.Contain("[1]").And.Contain("[2]").And.Contain("[3]"));
        Assert.That(norm, Does.Not.Contain("[4]"), "the reused attribute footnote must not create a 4th entry");
        // The attribute body appears once (one list entry), even though referenced twice.
        int firstBody = norm.IndexOf("Body defined via attribute", System.StringComparison.Ordinal);
        Assert.That(firstBody, Is.GreaterThanOrEqualTo(0));
        Assert.That(norm.IndexOf("Body defined via attribute", firstBody + 1, System.StringComparison.Ordinal),
            Is.EqualTo(-1), "the reused attribute footnote body should appear only once in the list");
    }
}
