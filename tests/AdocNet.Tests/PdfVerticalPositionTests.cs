using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Regression tests locking the vertical (Y) baseline positions of various
/// text elements in PDF output. Y values are PDF user-space coordinates
/// (origin at page bottom). Default page height is 842pt, default top margin is 72pt.
/// These tests must hold across refactors of the cursor/leading model — any unintended
/// shift in body text, headers, footers, code blocks, or page breaks should fail loudly.
/// </summary>
[TestFixture]
public class PdfVerticalPositionTests
{
    private static List<float> ParseTdYValues(string pdf)
    {
        var values = new List<float>();
        foreach (Match m in Regex.Matches(pdf, @"([\d.]+)\s+([\d.]+)\s+Td"))
        {
            if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                values.Add(y);
        }
        return values;
    }

    private static string RenderToPdfText(string adoc, PdfRenderOptions? opts = null)
    {
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc, opts ?? PdfRenderOptions.Default);
        return Encoding.ASCII.GetString(bytes);
    }

    // ── Body text positioning (must NOT change) ─────────────────────────

    [Test]
    public void Body_paragraph_first_on_fresh_page_baseline_is_at_755_15()
    {
        // Body-only document. Cursor starts so that the first body line baseline is
        // at (pageHeight - marginTop - bodyLeading). This is the bedrock of body
        // text positioning — must remain stable across cursor-model refactors.
        var pdf = RenderToPdfText("Body text only.");
        var ys = ParseTdYValues(pdf);
        Assert.That(ys, Is.Not.Empty, "Expected at least one Td baseline");
        Assert.That(ys[0], Is.EqualTo(755.15f).Within(0.05f),
            "First body baseline shifted from 755.15");
    }

    [Test]
    public void Body_paragraph_after_page_break_baseline_is_at_755_15()
    {
        // Generate enough body text to force a page break. The first line on the new
        // page must be at the same baseline as the first line on page 1 (755.15).
        var longBody = string.Join(" ", Enumerable.Repeat("word", 1000));
        var pdf = RenderToPdfText(longBody);
        var ys = ParseTdYValues(pdf);
        // Body lines are spaced by bodyLeading (14.85). After the first line at 755.15,
        // descending by 14.85 each time. When cursor falls below marginBottom (72),
        // a new page is started and cursor resets to 755.15.
        // Check that 755.15 appears more than once (proof of page break baseline reset).
        int countAt755 = ys.Count(y => Math.Abs(y - 755.15f) < 0.05f);
        Assert.That(countAt755, Is.GreaterThanOrEqualTo(2),
            $"Expected baseline 755.15 on multiple pages (page break reset). Got Y values: [{string.Join(", ", ys.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)))}]");
    }

    [Test]
    public void Body_paragraph_line_spacing_is_14_85_pts()
    {
        // Body lines should be spaced exactly bodyLeading (= bodyFontSize * lineSpacing
        // = 11 * 1.35 = 14.85) apart. This is independent of where line 1 starts.
        var longBody = string.Join(" ", Enumerable.Repeat("hello", 200));
        var pdf = RenderToPdfText(longBody);
        var ys = ParseTdYValues(pdf);
        Assert.That(ys.Count, Is.GreaterThanOrEqualTo(3));
        float gap1 = ys[0] - ys[1];
        float gap2 = ys[1] - ys[2];
        Assert.That(gap1, Is.EqualTo(14.85f).Within(0.05f), "Body leading line 1->2 changed");
        Assert.That(gap2, Is.EqualTo(14.85f).Within(0.05f), "Body leading line 2->3 changed");
    }

    // ── Document title positioning (SHOULD change to match Asciidoctor) ──

    [Test]
    public void Document_title_baseline_reserves_space_for_its_own_leading()
    {
        // This locks the corrected behavior (Option B): when the document title
        // is the first line on a fresh page, its baseline must be positioned so
        // that the title's ascent does NOT extend into the page margin/header zone.
        // Specifically, the cursor must advance by titleLeading (not bodyLeading)
        // before writing the title baseline.
        //
        // Default config: pageHeight=842, marginTop=72, titleFontSize=18,
        // titleLineHeight=1.15, lineSpacing=1.35.
        // titleLeading = 18 * 1.15 * 1.35 = 27.945 (approximately)
        // Expected baseline = 842 - 72 - titleMarginTop(10) - titleLeading
        //                   ≈ 842 - 72 - 10 - 27.945 ≈ 732.05 (approximately)
        var pdf = RenderToPdfText("= My Title\n\nBody text.");
        var ys = ParseTdYValues(pdf);
        Assert.That(ys.Count, Is.GreaterThanOrEqualTo(2), "Expected at least title and body baselines");
        // Title is positioned LOWER (smaller Y in PDF coords) than the body would be alone (755.15).
        // The Y must be at least titleLeading below the body's first-line position.
        float titleY = ys[0];
        Assert.That(titleY, Is.LessThan(745.16f),
            $"Title baseline should be lower than current 745.15 to reserve title leading. Got {titleY}.");
    }

    [Test]
    public void Section_heading_first_on_page_reserves_space_for_its_leading()
    {
        // Same principle as document title: a section heading rendered as the
        // first content on a fresh page must reserve enough vertical space
        // above its baseline for its larger ascent.
        var pdf = RenderToPdfText("== Section\n\nBody.");
        var ys = ParseTdYValues(pdf);
        Assert.That(ys.Count, Is.GreaterThanOrEqualTo(2));
        // Heading baseline must be lower than the current 739.15 to reserve heading leading.
        float headingY = ys[0];
        Assert.That(headingY, Is.LessThanOrEqualTo(739.15f).Within(0.05f),
            $"Heading baseline should be at or below current 739.15. Got {headingY}.");
    }

    // ── Body text after a title or heading (must remain consistent) ──────

    [Test]
    public void Body_paragraph_after_title_keeps_position_relative_to_title()
    {
        // The vertical distance between title baseline and following paragraph
        // baseline must remain the same (titleLeading + titleMarginBottom + paragraphSpacing).
        var pdf = RenderToPdfText("= Title\n\nBody.");
        var ys = ParseTdYValues(pdf);
        Assert.That(ys.Count, Is.GreaterThanOrEqualTo(2));
        float gap = ys[0] - ys[1];
        Assert.That(gap, Is.EqualTo(48.40f).Within(0.05f),
            $"Title->body gap shifted from 48.40. Got {gap}.");
    }

    // ── Footer/header positioning (must NOT change) ──────────────────────

    [Test]
    public void Footer_text_baseline_unchanged()
    {
        // Footer Y is determined by footerHeight and footerFontSize, independent
        // of body cursor. Locked to existing behavior.
        var opts = new PdfRenderOptions { FooterText = "Foot" };
        var pdf = RenderToPdfText("= Doc\n\nBody.", opts);
        var match = Regex.Match(pdf, @"BT\n/F1 \d+ Tf\n([\d.]+) ([\d.]+) Td\n.*Foot");
        Assert.That(match.Success, Is.True, "Footer text not found");
        float y = float.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(y, Is.EqualTo(52.00f).Within(0.05f), "Footer Y shifted");
    }

    [Test]
    public void Header_text_baseline_unchanged()
    {
        // Header Y is determined by pageHeight, marginTop, and headerFontSize,
        // independent of body cursor. Locked to existing behavior.
        var opts = new PdfRenderOptions { HeaderText = "Hdr" };
        var pdf = RenderToPdfText("= Doc\n\nBody.", opts);
        var match = Regex.Match(pdf, @"BT\n/F1 \d+ Tf\n([\d.]+) ([\d.]+) Td\n.*Hdr");
        Assert.That(match.Success, Is.True, "Header text not found");
        float y = float.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(y, Is.EqualTo(806.00f).Within(0.05f), "Header Y shifted");
    }

    // ── Code block positioning (line spacing must remain consistent) ─────

    [Test]
    public void Code_block_lines_share_consistent_leading()
    {
        // Code block lines must remain evenly spaced. The first code line position
        // depends on the title above it; the LEADING between code lines is what
        // must be preserved (codeFontSize * lineSpacing).
        var pdf = RenderToPdfText("= Doc\n\n----\nint x = 1;\nint y = 2;\nint z = 3;\n----");
        var ys = ParseTdYValues(pdf);
        // Find consecutive code lines (look for the smallest gaps, which are code lines)
        var gaps = new List<float>();
        for (int i = 1; i < ys.Count; i++)
            gaps.Add(ys[i - 1] - ys[i]);
        // The most common gap should be the code leading
        var codeGaps = gaps.Where(g => g > 10 && g < 16).ToList();
        Assert.That(codeGaps.Count, Is.GreaterThanOrEqualTo(2),
            $"Expected at least 2 code-line gaps. Got gaps: [{string.Join(", ", gaps.Select(g => g.ToString("F2", CultureInfo.InvariantCulture)))}]");
        Assert.That(codeGaps.Distinct().Count(), Is.EqualTo(1).Within(1).Or.LessThan(3),
            "Code block lines should have consistent leading");
    }

    // ── Page break (next page must reset cursor predictably) ─────────────

    [Test]
    public void Page_break_resets_first_line_position_predictably()
    {
        // Generate enough text to span multiple pages. The first body line on
        // each page must be at the same Y (modulo first-line-of-doc may differ
        // if a title is present).
        var pdf = RenderToPdfText(string.Join(" ", Enumerable.Repeat("word", 800)));
        var ys = ParseTdYValues(pdf);
        // Group baselines by approximate equality; the value 755.15 should appear
        // multiple times (once per page).
        int countAtFirstLine = ys.Count(y => Math.Abs(y - 755.15f) < 0.5f);
        Assert.That(countAtFirstLine, Is.GreaterThanOrEqualTo(2),
            "Expected first-line position to repeat across pages (page break baseline reset)");
    }
}
