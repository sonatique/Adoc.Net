using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for issue #69: a footnote referenced inside a table cell must
/// render in PDF the same way as one in body text — a smaller superscript marker
/// that is a clickable internal link to the footnote entry (which back-links to
/// the reference) — rather than a full-size, non-clickable inline <c>[n]</c> merged
/// into the cell text. Extends the body-text fix in #64.
/// </summary>
[TestFixture]
public class PdfCellFootnoteLinkTests
{
    private const string Doc =
        "= T\n\n" +
        "A paragraph footnote:[para note body here].\n\n" +
        "|===\n" +
        "| Cell footnote:[cell note body here] | Second\n" +
        "| x | y\n" +
        "|===\n";

    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    private static string Raw(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    private static float F(string s) =>
        float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Asserts the marker shown by <c>(marker) Tj</c> is a reduced-size, raised superscript.</summary>
    private static void AssertSuperscript(string raw, string markerShow)
    {
        int at = raw.IndexOf(markerShow, System.StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"expected {markerShow}");
        string before = raw.Substring(0, at);
        string after = raw.Substring(at, System.Math.Min(80, raw.Length - at));

        var bodyM = Regex.Match(raw, @"/F\d+ ([0-9.]+) Tf\s*\(Cell ", RegexOptions.Singleline);
        Assert.That(bodyM.Success, Is.True);
        float body = F(bodyM.Groups[1].Value);

        var tfs = Regex.Matches(before, @"/F\d+ ([0-9.]+) Tf");
        Assert.That(F(tfs[^1].Groups[1].Value), Is.LessThan(body), "marker font should be smaller than the cell text");

        var tss = Regex.Matches(before, @"(-?[0-9.]+) Ts");
        Assert.That(tss.Count, Is.GreaterThan(0));
        Assert.That(F(tss[^1].Groups[1].Value), Is.GreaterThan(0f), "marker should be raised above the baseline");
        Assert.That(after, Does.Contain("0 Ts"), "the text-rise should be reset after the marker");
    }

    [Test]
    public void Cell_footnote_marker_is_superscript_like_body()
    {
        AssertSuperscript(Raw(Render(Doc)), "([2]) Tj");
    }

    [Test]
    public void Cell_footnote_marker_is_a_clickable_internal_link_with_back_link()
    {
        var raw = Raw(Render(Doc));

        // Four link annotations: each of the two footnotes contributes a marker link
        // and a back-link (paragraph [1] + cell [2]).
        Assert.That(Regex.Matches(raw, @"/Subtype /Link").Count, Is.GreaterThanOrEqualTo(4),
            "the in-cell marker must add its own link annotations");

        // The cell footnote's destinations exist in both directions.
        Assert.That(raw, Does.Contain("(_footnotedef_2)"), "cell footnote entry destination");
        Assert.That(raw, Does.Contain("(_footnoteref_2)"), "cell footnote reference (back-link) destination");
    }

    [Test]
    public void Cell_marker_is_a_separate_run_not_merged_into_cell_text()
    {
        var frags = PdfTextExtractor.ExtractText(Render(Doc));

        // The marker is its own show operation, distinct from the cell text — not a
        // single "Cell [2]" run as before the fix.
        Assert.That(frags, Does.Contain("[2]"));
        Assert.That(frags.Any(f => f.Contains("Cell") && f.Contains("[2]")), Is.False,
            "the marker must not be merged into the cell text run");
    }

    [Test]
    public void Cell_footnote_body_stays_in_the_list_not_inlined_in_the_cell()
    {
        var frags = PdfTextExtractor.ExtractText(Render(Doc));
        var norm = PdfTextExtractor.NormalizeText(frags);

        Assert.That(norm, Does.Contain("cell note body here"), "the body belongs in the footnote list");
        Assert.That(frags.Any(f => f.Contains("Cell") && f.Contains("cell note body")), Is.False,
            "the footnote body must not be inlined into the cell");
    }
}
