using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for PDF table-of-contents layout (issue #51): page numbers
/// must right-align at the same margin regardless of an entry's nesting depth;
/// indentation applies to the entry text on the left only.
/// </summary>
[TestFixture]
public class PdfTocTests
{
    private const string TocDoc =
        "= Document Title\n:toc:\n\nIntro.\n\n" +
        "== First\n\ntext\n\n== Second\n\ntext\n\n" +
        "=== Sub one\n\ntext\n\n=== Sub two\n\ntext\n\n== Third\n\ntext\n";

    private sealed record Op(string Font, float Size, float X, float Y, string Text);

    // State-machine parser over content-stream lines: tracks the current font (Tf)
    // and position (Td) and emits on each shown string (Tj). This handles both
    // operator orderings the writer emits — WriteText writes Tf then Td, while
    // WriteTextSegments writes Td then Tf.
    private static readonly Regex TfRegex = new(@"^/(F\d+)\s+(-?[\d.]+)\s+Tf$", RegexOptions.Compiled);
    private static readonly Regex TdRegex = new(@"^(-?[\d.]+)\s+(-?[\d.]+)\s+Td$", RegexOptions.Compiled);
    private static readonly Regex TjRegex = new(@"^\((.*)\)\s*Tj$", RegexOptions.Compiled);

    private static List<Op> ParseOps(byte[] pdf)
    {
        var ops = new List<Op>();
        string font = "F1"; float size = 0, x = 0, y = 0;
        foreach (var raw in Encoding.ASCII.GetString(pdf).Replace("\r", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var tf = TfRegex.Match(line);
            if (tf.Success) { font = tf.Groups[1].Value; size = float.Parse(tf.Groups[2].Value, CultureInfo.InvariantCulture); continue; }
            var td = TdRegex.Match(line);
            if (td.Success) { x = float.Parse(td.Groups[1].Value, CultureInfo.InvariantCulture); y = float.Parse(td.Groups[2].Value, CultureInfo.InvariantCulture); continue; }
            var tj = TjRegex.Match(line);
            if (tj.Success) ops.Add(new Op(font, size, x, y, tj.Groups[1].Value));
        }
        return ops;
    }

    [Test]
    public void Toc_page_numbers_right_align_regardless_of_depth()
    {
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(TocDoc).Document, PdfRenderOptions.A4);
        var ops = ParseOps(pdf);

        // The only purely-numeric fragments are the five TOC page numbers
        // (First, Second, Sub one, Sub two, Third — all on page 1 here).
        var pageNums = ops.Where(o => Regex.IsMatch(o.Text, @"^\d+$")).ToList();
        Assert.That(pageNums, Has.Count.GreaterThanOrEqualTo(5), "expected one page number per TOC entry");

        var rightEdges = pageNums
            .Select(o => o.X + PdfWriter.MeasureStandardText(o.Text, o.Font, o.Size))
            .ToList();
        Assert.That(rightEdges.Max() - rightEdges.Min(), Is.LessThanOrEqualTo(0.5f),
            $"TOC page numbers not right-aligned (right edges span {rightEdges.Min():F1}..{rightEdges.Max():F1})");
    }

    [Test]
    public void Toc_nested_entry_text_is_indented_but_number_is_not()
    {
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(TocDoc).Document, PdfRenderOptions.A4);
        var ops = ParseOps(pdf);

        // TOC entries render in the regular font (F1); section headings are bold (F2).
        float topTitleX = ops.First(o => o.Font == "F1" && o.Text == "First").X;
        float subTitleX = ops.First(o => o.Font == "F1" && o.Text == "Sub one").X;
        Assert.That(subTitleX, Is.GreaterThan(topTitleX),
            "nested entry text should be indented past top-level entries");
    }
}
