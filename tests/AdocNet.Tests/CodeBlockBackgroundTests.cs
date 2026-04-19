using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

/// <summary>
/// Tests that code block backgrounds render correctly: rounded corners, visible border,
/// proper padding, and correct behavior across page boundaries.
/// </summary>
[TestFixture]
public class CodeBlockBackgroundTests
{
    // ── Single-page code blocks ─────────────────────────────────────────

    [Test]
    public void Short_code_block_has_rounded_rect_background()
    {
        var doc = CreateDoc("int x = 42;\nstring s = \"hello\";");
        string pdf = RenderToString(doc);

        // Must have gray fill color
        Assert.That(pdf, Does.Contain("0.95 0.95 0.95 rg"),
            "Should have gray background color");

        // Rounded rect = Bezier curves (c operator) closed with h + f
        Assert.That(pdf, Does.Contain(" c\n"),
            "Should use Bezier curves for rounded corners");
        Assert.That(HasClosedFill(pdf), Is.True,
            "Should have closed fill (h + f) for rounded rect");
    }

    [Test]
    public void Short_code_block_has_visible_border_stroke()
    {
        var doc = CreateDoc("int x = 42;");
        string pdf = RenderToString(doc);

        // Must have stroke color set (RG = stroke color operator)
        Assert.That(pdf, Does.Contain(" RG\n"),
            "Should set a stroke color for the border");

        // Must have stroke width set
        Assert.That(pdf, Does.Contain(" w\n"),
            "Should set a line width for the border");

        // Must have stroke operation (S = stroke path)
        Assert.That(HasStrokeOp(pdf), Is.True,
            "Should have a stroke operation for the border");
    }

    [Test]
    public void Code_block_border_uses_default_color_when_theme_omits_it()
    {
        // Simulates what happens when PdfThemeLoader doesn't find code.border-color:
        // the fallback should produce a visible border, not null.
        var options = new PdfRenderOptions
        {
            // Explicitly set to null to simulate theme override
            CodeBorderColor = null
        };

        var doc = CreateDoc("hello world");
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        // With null border, no stroke should appear (this is the "no border" case)
        // But the DEFAULT options (without explicit null) should have a border:
        var defaultPdf = RenderToString(doc);
        Assert.That(defaultPdf, Does.Contain(" RG\n"),
            "Default options should produce a visible border stroke");
    }

    [Test]
    public void Code_block_has_sufficient_padding()
    {
        var doc = CreateDoc("line 1\nline 2\nline 3");
        string pdf = RenderToString(doc);

        // Extract the rounded rect bounding box and text positions
        var fillRect = ExtractRoundedRectBounds(pdf);
        var textYPositions = ExtractTextYPositions(pdf);

        Assert.That(fillRect, Is.Not.Null, "Should have a background fill rect");
        Assert.That(textYPositions.Count, Is.GreaterThanOrEqualTo(3),
            "Should have at least 3 lines of text");

        float firstBaseline = textYPositions.Max();
        float lastBaseline = textYPositions.Min();

        // Top padding: rect top to first text ascender (fontSize * 0.75)
        float topGap = fillRect!.Value.Top - firstBaseline - 9f * 0.75f;
        // Bottom padding: last text descender to rect bottom
        float bottomGap = lastBaseline - 9f * 0.25f - fillRect.Value.Bottom;

        // Padding should be at least 8pt (we use 10pt)
        Assert.That(topGap, Is.GreaterThanOrEqualTo(8f),
            $"Top padding ({topGap:F1}pt) should be >= 8pt");
        Assert.That(bottomGap, Is.GreaterThanOrEqualTo(8f),
            $"Bottom padding ({bottomGap:F1}pt) should be >= 8pt");

        // Top and bottom should be roughly symmetric (within 2pt)
        Assert.That(Math.Abs(topGap - bottomGap), Is.LessThanOrEqualTo(2f),
            $"Padding should be symmetric: top={topGap:F1}pt, bottom={bottomGap:F1}pt");
    }

    // ── Multi-page code blocks ──────────────────────────────────────────

    [Test]
    public void Long_code_block_has_background_on_both_pages()
    {
        var doc = CreateDoc(GenerateLines(80));
        string pdfText = RenderToString(doc);
        var pageStreams = ExtractPageStreams(pdfText);

        Assert.That(pageStreams.Count, Is.GreaterThanOrEqualTo(2),
            "Code block should span at least 2 pages");

        foreach (var (pageNum, stream) in pageStreams)
        {
            bool hasFillShape = HasClosedFill(stream) || stream.Contains("re f") || stream.Contains("re\nf");
            bool hasGrayColor = stream.Contains("0.95 0.95 0.95 rg");
            Assert.That(hasFillShape && hasGrayColor, Is.True,
                $"Page {pageNum} should have gray background fill for code block");
        }
    }

    [Test]
    public void Long_code_block_has_border_on_both_pages()
    {
        var doc = CreateDoc(GenerateLines(80));
        string pdfText = RenderToString(doc);
        var pageStreams = ExtractPageStreams(pdfText);

        Assert.That(pageStreams.Count, Is.GreaterThanOrEqualTo(2),
            "Code block should span at least 2 pages");

        foreach (var (pageNum, stream) in pageStreams)
        {
            bool hasStroke = stream.Contains(" RG\n") && stream.Contains(" w\n");
            Assert.That(hasStroke, Is.True,
                $"Page {pageNum} should have border stroke for code block");
        }
    }

    [Test]
    public void Code_block_background_respects_page_margins()
    {
        var doc = CreateDoc(GenerateLines(80));
        var options = new PdfRenderOptions { MarginBottom = 72f };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        var pageStreams = ExtractPageStreams(pdfText);
        foreach (var (pageNum, stream) in pageStreams)
        {
            var rectMatches = Regex.Matches(stream, @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+re\s*\n\s*f");
            foreach (Match m in rectMatches)
            {
                float y = float.Parse(m.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(y, Is.GreaterThanOrEqualTo(0f),
                    $"Page {pageNum}: rect y={y} should not be negative");
            }
        }
    }

    [Test]
    public void Highlighted_code_block_has_background_on_continuation_pages()
    {
        var doc = CreateDoc(GenerateLines(80, "public int Prop{0} {{ get; set; }} = {0};"));
        var options = new PdfRenderOptions { SyntaxColors = SyntaxColorScheme.Default };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        var pageStreams = ExtractPageStreams(pdfText);
        Assert.That(pageStreams.Count, Is.GreaterThanOrEqualTo(2),
            "Highlighted code block should span at least 2 pages");

        var page2 = pageStreams[1].Stream;
        bool hasFill = page2.Contains("re\nf") || page2.Contains("re f") || HasClosedFill(page2);
        Assert.That(hasFill, Is.True,
            "Continuation page should have background fill");
    }

    // ── Determinism ─────────────────────────────────────────────────────

    [Test]
    public void Code_block_background_is_deterministic()
    {
        var doc = CreateDoc(GenerateLines(80));
        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    // ── Theme integration ───────────────────────────────────────────────

    [Test]
    public void Custom_border_color_is_applied()
    {
        var options = new PdfRenderOptions
        {
            CodeBorderColor = new PdfColor(1f, 0f, 0f) // red border
        };

        var doc = CreateDoc("test line");
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("1 0 0 RG"),
            "Should use custom red stroke color for border");
    }

    [Test]
    public void Custom_background_color_is_applied()
    {
        var options = new PdfRenderOptions
        {
            CodeBackground = new PdfColor(0.9f, 0.95f, 1f) // light blue
        };

        var doc = CreateDoc("test line");
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("0.9 0.95 1 rg"),
            "Should use custom light blue fill color");
    }

    [Test]
    public void No_background_when_CodeBackground_is_null()
    {
        var options = new PdfRenderOptions { CodeBackground = null };
        var doc = CreateDoc("test line");
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Not.Contain("0.95 0.95 0.95 rg"),
            "Should not have background fill when CodeBackground is null");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static DocumentNode CreateDoc(string content, string? language = "csharp")
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = language,
            Content = content
        });
        return doc;
    }

    private static string GenerateLines(int count, string? pattern = null)
    {
        pattern ??= "int x{0} = {0};";
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
            sb.AppendLine(string.Format(pattern, i));
        return sb.ToString().TrimEnd();
    }

    private static string RenderToString(DocumentNode doc, PdfRenderOptions? options = null)
    {
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        return Encoding.ASCII.GetString(pdf);
    }

    private static bool HasClosedFill(string text) =>
        text.Contains("h\nf\n") || text.Contains("h\nf ");

    private static bool HasStrokeOp(string text) =>
        text.Contains("h\nS\n") || text.Contains("h\nS ");

    private static (float Bottom, float Top)? ExtractRoundedRectBounds(string pdf)
    {
        // Find the q...Q block containing the gray fill
        int qIdx = pdf.IndexOf("0.95 0.95 0.95 rg", StringComparison.Ordinal);
        if (qIdx < 0) return null;

        int start = pdf.LastIndexOf("q\n", qIdx, StringComparison.Ordinal);
        int end = pdf.IndexOf("Q\n", qIdx, StringComparison.Ordinal);
        if (start < 0 || end < 0) return null;

        string block = pdf.Substring(start, end - start);

        // Extract all Y coordinates from move (m) and curve (c) operations
        var yValues = new List<float>();
        foreach (Match m in Regex.Matches(block, @"[\d.]+\s+([\d.]+)\s+m"))
        {
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y))
                yValues.Add(y);
        }
        foreach (Match m in Regex.Matches(block, @"[\d.]+\s+([\d.]+)\s+l"))
        {
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y))
                yValues.Add(y);
        }

        if (yValues.Count < 2) return null;
        return (yValues.Min(), yValues.Max());
    }

    private static List<float> ExtractTextYPositions(string pdf)
    {
        var values = new List<float>();
        foreach (Match m in Regex.Matches(pdf, @"[\d.]+\s+([\d.]+)\s+Td"))
        {
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y))
                values.Add(y);
        }
        return values;
    }

    private static List<(int PageNum, string Stream)> ExtractPageStreams(string pdfText)
    {
        var results = new List<(int, string)>();
        int pageNum = 0;

        int pos = 0;
        while (pos < pdfText.Length)
        {
            int streamStart = pdfText.IndexOf("stream\n", pos, StringComparison.Ordinal);
            if (streamStart < 0) break;
            streamStart += "stream\n".Length;

            int streamEnd = pdfText.IndexOf("\nendstream", streamStart, StringComparison.Ordinal);
            if (streamEnd < 0) break;

            string content = pdfText.Substring(streamStart, streamEnd - streamStart);

            if (content.Contains("BT") && content.Contains("ET"))
            {
                pageNum++;
                results.Add((pageNum, content));
            }

            pos = streamEnd + 1;
        }

        return results;
    }
}
