using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for wide-table column overlap in the PDF renderer (issue #48).
/// A table whose natural width exceeds the page must never render columns that
/// visually overlap: columns are widened to fit their widest word where possible,
/// and the whole table's font is scaled down as a last resort.
/// </summary>
[TestFixture]
public class PdfWideTableTests
{
    // The issue's repro: 7 weighted columns crammed into A4 width, with words
    // (and a long identifier) wider than their weight-proportional share.
    private const string WideTable =
        "= Wide table\n\n" +
        "[cols=\"1,1,2,1,2,1,3\"]\n" +
        "|===\n" +
        "| PG Lzikit Wira Qgch | Xnpryhw | Puwpozacj Xnpryhw | YRNT xehfzz | Hmwhj vslprq xehfzz | Hmwhj Inxrkl xehfzz | Puwpozacj Wira Qgch long header that wraps\n\n" +
        "| WOI_JIHDXGK_DXR | fjdsajs | fjdsajs | a b K | c d K | e M | XQN bphgo fom rofxpo iyhuiy\n" +
        "|===";

    private sealed record TextOp(string Font, float Size, float X, float Y, string Text);

    // Parses uncompressed content-stream text draws of the form
    //   BT /F{n} {size} Tf {x} {y} Td [{tw} Tw] (text) Tj ... ET
    private static readonly Regex TextOpRegex = new(
        @"/(F\d+)\s+(-?[\d.]+)\s+Tf\s+(-?[\d.]+)\s+(-?[\d.]+)\s+Td\s*(?:-?[\d.]+\s+Tw\s*)?\(((?:\\.|[^\\()])*)\)\s*Tj",
        RegexOptions.Compiled);

    private static List<TextOp> ParseTextOps(byte[] pdf)
    {
        var s = Encoding.ASCII.GetString(pdf);
        var ops = new List<TextOp>();
        foreach (Match m in TextOpRegex.Matches(s))
        {
            ops.Add(new TextOp(
                m.Groups[1].Value,
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                UnescapePdfString(m.Groups[5].Value)));
        }
        return ops;
    }

    private static string UnescapePdfString(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length) { sb.Append(raw[++i]); }
            else sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    [Test]
    public void Wide_table_columns_do_not_overlap()
    {
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(WideTable).Document, PdfRenderOptions.A4);
        var ops = ParseTextOps(pdf);
        Assert.That(ops, Is.Not.Empty, "expected to parse text-drawing operators from the PDF");

        // Fragments sharing a baseline belong to different columns of one row line.
        // Each fragment's drawn right edge must not reach the next fragment's x —
        // i.e. no column's text spills into the next column's cell.
        foreach (var lineGroup in ops.GroupBy(o => MathF.Round(o.Y, 1)))
        {
            var frags = lineGroup.OrderBy(o => o.X).ToList();
            for (int i = 0; i + 1 < frags.Count; i++)
            {
                float rightEdge = frags[i].X + PdfWriter.MeasureStandardText(frags[i].Text, frags[i].Font, frags[i].Size);
                Assert.That(rightEdge, Is.LessThanOrEqualTo(frags[i + 1].X + 0.5f),
                    $"'{frags[i].Text}' (x={frags[i].X:F1}, right={rightEdge:F1}) overlaps "
                    + $"'{frags[i + 1].Text}' (x={frags[i + 1].X:F1}) at y={frags[i].Y:F1}");
            }
        }
    }

    [Test]
    public void Wide_table_renders_all_cell_text()
    {
        // Overlap-avoidance must not drop content.
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(WideTable).Document, PdfRenderOptions.A4);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));
        foreach (var token in new[] { "WOI_JIHDXGK_DXR", "Puwpozacj", "rofxpo", "Xnpryhw" })
            Assert.That(text, Does.Contain(token), $"token '{token}' should survive in the PDF");
    }

    // ── Column-width allocation (FitWidthsToMinimums) ────────────────────

    [Test]
    public void FitWidths_leaves_columns_untouched_when_content_fits()
    {
        var desired = new[] { 100f, 100f, 100f };
        var min = new[] { 20f, 30f, 25f };
        var result = PdfRenderer.FitWidthsToMinimums(desired, min, 300f);
        Assert.That(result, Is.EqualTo(desired));
    }

    [Test]
    public void FitWidths_grows_starved_columns_by_borrowing_from_slack()
    {
        // col 0 and col 5 need more than their weighted share; the rest have slack.
        float content = 440f;
        var desired = new[] { 40f, 40f, 80f, 40f, 80f, 40f, 120f };   // sums to 440
        var min = new[] { 120f, 50f, 60f, 35f, 45f, 48f, 60f };
        var result = PdfRenderer.FitWidthsToMinimums(desired, min, content);

        for (int c = 0; c < result.Length; c++)
            Assert.That(result[c], Is.GreaterThanOrEqualTo(min[c] - 0.01f),
                $"column {c} must be at least its minimum width");
        Assert.That(result.Sum(), Is.EqualTo(content).Within(0.1f),
            "total width is preserved (slack columns absorb the shortfall)");
    }

    [Test]
    public void FitWidths_pins_to_minimums_scaled_to_fill_when_page_too_narrow()
    {
        // Minimums sum to 600 but only 300 is available — scale to fill 300.
        float content = 300f;
        var desired = new[] { 100f, 100f, 100f };
        var min = new[] { 300f, 200f, 100f }; // sums to 600
        var result = PdfRenderer.FitWidthsToMinimums(desired, min, content);

        Assert.That(result.Sum(), Is.EqualTo(content).Within(0.1f));
        // Proportions follow the minimums: 300:200:100 == 3:2:1.
        Assert.That(result[0] / result[2], Is.EqualTo(3f).Within(0.01f));
        Assert.That(result[1] / result[2], Is.EqualTo(2f).Within(0.01f));
    }
}
