using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for issue #64: PDF footnote reference markers render as a
/// raised superscript that is a clickable internal link to the footnote entry,
/// the footnote entry links back to the reference, and there is no stray space
/// between the marker and following punctuation.
/// </summary>
[TestFixture]
public class PdfFootnoteLinkTests
{
    private const string Doc =
        "= Footnote style\n\n" +
        "A sentence with a footnote:[The footnote body text here].\n";

    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    // Content streams are not compressed, so the raw bytes contain the text
    // operators and annotation dictionaries verbatim. ISO-8859-1 is a 1:1 byte
    // mapping so offsets/regex line up with the bytes.
    private static string Raw(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    private static float F(string s) =>
        float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Footnote_marker_renders_as_raised_superscript()
    {
        var raw = Raw(Render(Doc));

        int markerAt = raw.IndexOf("([1]) Tj", System.StringComparison.Ordinal);
        Assert.That(markerAt, Is.GreaterThanOrEqualTo(0), "expected the [1] marker show operator");
        string before = raw.Substring(0, markerAt);
        string after = raw.Substring(markerAt, System.Math.Min(80, raw.Length - markerAt));

        // Body font size (the size used to show the sentence text).
        var bodyM = Regex.Match(raw, @"/F\d+ ([0-9.]+) Tf\s*\(A sentence", RegexOptions.Singleline);
        Assert.That(bodyM.Success, Is.True);
        float body = F(bodyM.Groups[1].Value);

        // The marker's own font size is the last Tf set before it; it must be smaller.
        var tfs = Regex.Matches(before, @"/F\d+ ([0-9.]+) Tf");
        float sub = F(tfs[^1].Groups[1].Value);
        Assert.That(sub, Is.LessThan(body), "superscript font size should be smaller than body");

        // The last text-rise set before the marker must raise it above the baseline,
        // and the rise must be reset right after.
        var tss = Regex.Matches(before, @"(-?[0-9.]+) Ts");
        Assert.That(tss.Count, Is.GreaterThan(0), "a text-rise should be set before the marker");
        Assert.That(F(tss[^1].Groups[1].Value), Is.GreaterThan(0f), "marker should be raised above the baseline");
        Assert.That(after, Does.Contain("0 Ts"), "the text-rise should be reset after the marker");
    }

    [Test]
    public void Footnote_marker_is_a_clickable_internal_link_with_back_link()
    {
        var raw = Raw(Render(Doc));

        // Two link annotations: marker → footnote entry, and entry number → marker.
        Assert.That(Regex.Matches(raw, @"/Subtype /Link").Count, Is.GreaterThanOrEqualTo(2),
            "expected clickable link annotations for the marker and the back-link");
        // The links are GoTo (internal) destinations, not URI actions.
        Assert.That(raw, Does.Contain("/Dest ["), "links should target internal destinations");
        Assert.That(raw, Does.Contain("/Annots"), "the page must carry an annotations array");

        // Both named destinations exist: the entry (marker target) and the
        // reference (back-link target).
        Assert.That(raw, Does.Contain("(_footnotedef_1)"), "footnote entry destination should be registered");
        Assert.That(raw, Does.Contain("(_footnoteref_1)"), "footnote reference destination should be registered");
    }

    [Test]
    public void No_stray_space_between_marker_and_following_period()
    {
        var frags = PdfTextExtractor.ExtractText(Render(Doc));

        // The marker and the period are separate show operations; the period must
        // hug the marker (no synthesized leading space): "[1]" then "." — never " .".
        int markerIdx = frags.IndexOf("[1]");
        Assert.That(markerIdx, Is.GreaterThanOrEqualTo(0), "expected a [1] marker fragment");
        Assert.That(frags[markerIdx + 1], Is.EqualTo("."), "the period must immediately follow the marker");
        Assert.That(frags, Has.None.EqualTo(" ."), "no stray space before the period");
    }

    [Test]
    public void Repeated_named_footnote_links_back_to_its_first_reference_only()
    {
        // A named footnote defined once and back-referenced once: both markers link
        // to the same entry ([1]), and only the first reference owns the back-link
        // target, so the entry's back-link resolves to a single destination.
        const string doc =
            "= Doc\n\n" +
            "First footnote:fn[the body] and again footnote:fn[].\n";
        var raw = Raw(Render(doc));

        // Exactly one reference destination (the first occurrence), even though the
        // marker [1] is shown twice.
        Assert.That(Regex.Matches(raw, @"\(_footnoteref_1\)").Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(raw, Does.Contain("(_footnotedef_1)"));
        // No second footnote was created by the back-reference.
        Assert.That(raw, Does.Not.Contain("(_footnotedef_2)"));
    }
}
