using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for PDF table grid/frame lines (issue #59): tables must draw
/// vertical cell borders by default (AsciiDoc grid=all), and honour grid=/frame=.
/// </summary>
[TestFixture]
public class PdfTableGridTests
{
    // Parses straight line segments from the content stream: "{x1} {y1} m {x2} {y2} l".
    private static readonly Regex LineRegex = new(
        @"(-?[\d.]+) (-?[\d.]+) m (-?[\d.]+) (-?[\d.]+) l", RegexOptions.Compiled);

    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    private static (List<float> VerticalXs, List<float> HorizontalYs) Lines(byte[] pdf)
    {
        var s = Encoding.Latin1.GetString(pdf);
        var vx = new List<float>();
        var hy = new List<float>();
        foreach (Match m in LineRegex.Matches(s))
        {
            float x1 = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            float y1 = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            float x2 = float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            float y2 = float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (System.Math.Abs(x1 - x2) < 0.01f && System.Math.Abs(y1 - y2) > 0.01f) vx.Add(x1);
            else if (System.Math.Abs(y1 - y2) < 0.01f && System.Math.Abs(x1 - x2) > 0.01f) hy.Add(y1);
        }
        return (vx, hy);
    }

    private const string Table = "= Grid\n\n|===\n| A | B | C\n| 1 | 2 | 3\n| 4 | 5 | 6\n|===\n";

    [Test]
    public void Default_table_draws_vertical_and_horizontal_cell_borders()
    {
        var (vx, hy) = Lines(Render(Table));

        // Distinct vertical positions: left frame + 2 internal + right frame = 4.
        var distinctV = vx.Select(x => MathF.Round(x, 1)).Distinct().ToList();
        Assert.That(distinctV.Count, Is.GreaterThanOrEqualTo(4),
            $"expected ≥4 vertical column borders (frame + internals), got {distinctV.Count}");

        // At least one vertical strictly inside the content box (an internal column
        // border, not just the frame) — this is what was missing before #59.
        float leftMargin = 72f, rightMargin = 72f + 451.28f;
        Assert.That(distinctV.Any(x => x > leftMargin + 1 && x < rightMargin - 1), Is.True,
            "expected an internal vertical column border");

        Assert.That(hy, Is.Not.Empty, "expected horizontal row rules too");
    }

    [Test]
    public void Grid_none_draws_no_table_lines()
    {
        var (vx, hy) = Lines(Render("= G\n\n[grid=none,frame=none]\n|===\n| A | B\n| 1 | 2\n|===\n"));
        Assert.That(vx, Is.Empty, "grid=none/frame=none must draw no vertical lines");
        Assert.That(hy, Is.Empty, "grid=none/frame=none must draw no horizontal lines");
    }

    [Test]
    public void Grid_rows_draws_no_internal_verticals()
    {
        // grid=rows: horizontal internal rules only; frame defaults to all, so the
        // only verticals are the two outer frame sides — no internal column borders.
        var (vx, _) = Lines(Render("= G\n\n[grid=rows]\n|===\n| A | B | C\n| 1 | 2 | 3\n|===\n"));
        float leftMargin = 72f, rightMargin = 72f + 451.28f;
        Assert.That(vx.Any(x => x > leftMargin + 1 && x < rightMargin - 1), Is.False,
            "grid=rows must not draw internal vertical column borders");
    }
}
