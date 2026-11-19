using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// File-level consistency of the PDF serialization: every cross-reference offset points at
/// its object, startxref points at the xref table, and every stream's /Length is the exact
/// size of its data.
/// </summary>
[TestFixture]
public class PdfFileStructureTests
{
    private static readonly string LongParagraph = string.Join(" ", Enumerable.Repeat(
        "Some *bold*, _italic_ and `mono` text with a link https://example.com/page and more words.", 6));

    private static string MultiPageBody(int sections) => string.Concat(Enumerable.Range(1, sections).Select(i =>
        $"== Section {i}\n\n{LongParagraph}\n\n* item one\n* item two\n\n{LongParagraph}\n\n"));

    private static IEnumerable<TestCaseData> Documents()
    {
        yield return new TestCaseData("= Title\n\nHello world.\n", false).SetName("Simple");
        yield return new TestCaseData("= Title\n:toc:\n\n" + MultiPageBody(12), false).SetName("TocTwoPassMultiPage");
        yield return new TestCaseData(
            "= Title\n\n[[target]]\n== Target\n\nSee <<target>> and https://example.com[a link].footnote:[A note.]\n\n" +
            "Escapes (a) \\b and back\\slash.\n", false).SetName("LinksFootnotesEscapes");
        yield return new TestCaseData(
            "= Title\n\nCafé — “quotes” ✓ → 🛑 end.\n", false).SetName("NonAsciiAndFallbackFonts");
        yield return new TestCaseData("= Title\n:toc:\n\n" + MultiPageBody(6), true).SetName("TaggedMultiPage");
    }

    private static byte[] Render(string adoc, bool tagged) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, new PdfRenderOptions { Accessibility = tagged });

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [TestCaseSource(nameof(Documents))]
    public void Startxref_points_at_the_xref_table(string adoc, bool tagged)
    {
        var text = Latin1(Render(adoc, tagged));
        var m = Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$");
        Assert.That(m.Success, Is.True, "trailer ends with startxref and %%EOF");
        int offset = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.That(text.Substring(offset, 5), Is.EqualTo("xref\n"));
    }

    [TestCaseSource(nameof(Documents))]
    public void Every_xref_offset_points_at_its_object(string adoc, bool tagged)
    {
        var text = Latin1(Render(adoc, tagged));
        int xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n").Groups[1].Value, CultureInfo.InvariantCulture);
        var header = Regex.Match(text.Substring(xref), @"^xref\n0 (\d+)\n0000000000 65535 f \n");
        Assert.That(header.Success, Is.True, "xref subsection header and free entry");
        int count = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.That(count, Is.GreaterThan(1));

        int pos = xref + header.Length;
        for (int id = 1; id < count; id++, pos += 20)
        {
            var entry = text.Substring(pos, 20);
            Assert.That(entry, Does.Match(@"^\d{10} 00000 n \n$"), $"xref entry {id}");
            int objOffset = int.Parse(entry.Substring(0, 10), CultureInfo.InvariantCulture);
            string expected = $"{id} 0 obj\n";
            Assert.That(text.Substring(objOffset, expected.Length), Is.EqualTo(expected), $"object {id} offset");
        }
        Assert.That(Regex.IsMatch(text, $@"/Size {count} "), Is.True, "trailer /Size matches the xref entry count");
    }

    [TestCaseSource(nameof(Documents))]
    public void Every_stream_length_matches_its_data(string adoc, bool tagged)
    {
        var text = Latin1(Render(adoc, tagged));
        var streams = Regex.Matches(text, @"\d+ 0 obj\n<<[^\n]*?/Length (\d+)\b[^\n]*>>\nstream\n");
        Assert.That(streams, Is.Not.Empty);
        foreach (Match s in streams)
        {
            int length = int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture);
            int dataEnd = s.Index + s.Length + length;
            string after = text.Substring(dataEnd, Math.Min(10, text.Length - dataEnd));
            Assert.That(after.StartsWith("endstream") || after.StartsWith("\nendstream"), Is.True,
                $"stream at {s.Index}: {length} data bytes must be followed by endstream, got '{after}'");
        }
    }
}
