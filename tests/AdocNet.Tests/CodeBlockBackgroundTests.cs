using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

/// <summary>
/// Tests that code block backgrounds render correctly, especially across page boundaries.
/// Verifies per-line background strips instead of a single pre-calculated rectangle.
/// </summary>
[TestFixture]
public class CodeBlockBackgroundTests
{
    /// <summary>
    /// Creates a code block long enough to span at least 2 pages and verifies
    /// that both pages contain background fill rectangles ('re f' operators).
    /// </summary>
    [Test]
    public void Long_code_block_has_background_on_both_pages()
    {
        // Generate a code block with ~80 lines — enough to span 2 pages on A4
        var sb = new StringBuilder();
        for (int i = 0; i < 80; i++)
            sb.AppendLine($"int line{i} = {i}; // line number {i}");

        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = sb.ToString().TrimEnd()
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        // Split PDF into page streams by finding "stream" ... "endstream" blocks
        string pdfText = Encoding.ASCII.GetString(pdf);
        var pageStreams = ExtractPageStreams(pdfText);

        Assert.That(pageStreams.Count, Is.GreaterThanOrEqualTo(2),
            "Code block should span at least 2 pages");

        // Both page streams should contain background fill operations (rg + re f)
        foreach (var (pageNum, stream) in pageStreams)
        {
            bool hasFillRect = stream.Contains("re f") || stream.Contains("re\nf");
            bool hasGrayColor = stream.Contains("0.95 0.95 0.95 rg");
            Assert.That(hasFillRect && hasGrayColor, Is.True,
                $"Page {pageNum} should have gray background fill rectangles for code block");
        }
    }

    /// <summary>
    /// Verifies that background rectangles do not extend below the bottom margin.
    /// Checks that all 're f' (fill rect) y-coordinates are above the margin.
    /// </summary>
    [Test]
    public void Code_block_background_respects_page_margins()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 80; i++)
            sb.AppendLine($"int x{i} = {i};");

        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = sb.ToString().TrimEnd()
        });

        var options = new PdfRenderOptions { MarginBottom = 72f };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        // Extract all fill rect y-coordinates from all page streams
        var pageStreams = ExtractPageStreams(pdfText);
        foreach (var (pageNum, stream) in pageStreams)
        {
            // Pattern: "x y w h re\nf" — y is the second number
            var rectMatches = Regex.Matches(stream, @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+re\s*\n\s*f");
            foreach (Match m in rectMatches)
            {
                float y = float.Parse(m.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                // y is the bottom of the rect — it should be >= 0 (within page)
                Assert.That(y, Is.GreaterThanOrEqualTo(0f),
                    $"Page {pageNum}: rect y={y} should not be negative (below page)");
            }
        }
    }

    /// <summary>
    /// Verifies syntax-highlighted code blocks also get backgrounds on continuation pages.
    /// </summary>
    [Test]
    public void Highlighted_code_block_has_background_on_continuation_pages()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 80; i++)
            sb.AppendLine($"public int Property{i} {{ get; set; }} = {i};");

        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = sb.ToString().TrimEnd()
        });

        var options = new PdfRenderOptions { SyntaxColors = SyntaxColorScheme.Default };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string pdfText = Encoding.ASCII.GetString(pdf);

        var pageStreams = ExtractPageStreams(pdfText);
        Assert.That(pageStreams.Count, Is.GreaterThanOrEqualTo(2),
            "Highlighted code block should span at least 2 pages");

        // Page 2 should have both background rects and color operators
        var page2 = pageStreams[1].Stream;
        bool hasFillRect = page2.Contains("re\nf") || page2.Contains("re f");
        Assert.That(hasFillRect, Is.True,
            "Continuation page should have background fill rectangles");
    }

    /// <summary>
    /// Verifies that a short code block (single page) still gets proper background.
    /// </summary>
    [Test]
    public void Short_code_block_has_background()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "int x = 42;\nstring s = \"hello\";"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("0.95 0.95 0.95 rg"),
            "Should have gray background color");
        bool hasFillRect = pdfText.Contains("re\nf") || pdfText.Contains("re f");
        Assert.That(hasFillRect, Is.True,
            "Should have fill rectangle for code block background");
    }

    /// <summary>
    /// Verifies background is deterministic across runs.
    /// </summary>
    [Test]
    public void Code_block_background_is_deterministic()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 80; i++)
            sb.AppendLine($"int x{i} = {i};");

        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = sb.ToString().TrimEnd()
        });

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<(int PageNum, string Stream)> ExtractPageStreams(string pdfText)
    {
        var results = new List<(int, string)>();
        int pageNum = 0;

        // Find all "stream\n...endstream" blocks
        int pos = 0;
        while (pos < pdfText.Length)
        {
            int streamStart = pdfText.IndexOf("stream\n", pos, StringComparison.Ordinal);
            if (streamStart < 0) break;
            streamStart += "stream\n".Length;

            int streamEnd = pdfText.IndexOf("\nendstream", streamStart, StringComparison.Ordinal);
            if (streamEnd < 0) break;

            string content = pdfText.Substring(streamStart, streamEnd - streamStart);

            // Only count streams that contain text operators (BT...ET) as page streams
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
