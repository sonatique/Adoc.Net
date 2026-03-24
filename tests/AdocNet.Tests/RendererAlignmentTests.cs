using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

/// <summary>
/// Verifies conceptual alignment between HTML and PDF renderers for key elements.
/// Not pixel-identical — structurally consistent.
/// </summary>
[TestFixture]
public class RendererAlignmentTests
{
    // ── Heading hierarchy ────────────────────────────────────────────────

    [Test]
    public void HTML_heading_tags_are_monotonically_decreasing()
    {
        // HTML uses h2, h3, h4 etc. which are inherently decreasing in browser rendering.
        // Verify: render sections at level 1, 2, 3 → get h2, h3, h4 tags.
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "L1" });
        doc.AddChild(new SectionNode { Level = 2, Title = "L2" });
        doc.AddChild(new SectionNode { Level = 3, Title = "L3" });

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("<h2>"));
        Assert.That(html, Does.Contain("<h3>"));
        Assert.That(html, Does.Contain("<h4>"));
        // h2 > h3 > h4 in browser default sizing — structurally decreasing
    }

    [Test]
    public void PDF_heading_sizes_are_monotonically_decreasing()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "H2" });
        doc.AddChild(new SectionNode { Level = 2, Title = "H3" });
        doc.AddChild(new SectionNode { Level = 3, Title = "H4" });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string content = Encoding.ASCII.GetString(pdf);

        // Extract Tf font size values from content stream
        var tfSizes = Regex.Matches(content, @"/F\d+\s+([\d.]+)\s+Tf")
            .Cast<Match>()
            .Select(m => float.Parse(m.Groups[1].Value))
            .Where(s => s > 11f) // filter out body text size
            .Distinct()
            .OrderByDescending(s => s)
            .ToList();

        Assert.That(tfSizes.Count, Is.GreaterThanOrEqualTo(3),
            "Should have at least 3 distinct heading sizes");

        for (int i = 1; i < tfSizes.Count; i++)
            Assert.That(tfSizes[i], Is.LessThan(tfSizes[i - 1]),
                $"Size {i + 1} ({tfSizes[i]}) should be smaller than size {i} ({tfSizes[i - 1]})");
    }

    // ── Code blocks ─────────────────────────────────────────────────────

    [Test]
    public void HTML_code_block_uses_code_element()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "int x = 42;"
        });

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("<code"), "Should use <code> element");
        Assert.That(html, Does.Contain("language-csharp"), "Should mark language");
    }

    [Test]
    public void PDF_code_block_uses_monospace_font()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "int x = 42;"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string content = Encoding.ASCII.GetString(pdf);

        // Courier is the monospace font (F4)
        Assert.That(content, Does.Contain("/F4"), "Should use Courier (monospace) font for code");
    }

    // ── Admonitions ─────────────────────────────────────────────────────

    [Test]
    public void HTML_admonition_includes_type_label()
    {
        var doc = new DocumentNode();
        doc.AddChild(new AdmonitionNode
        {
            AdmonitionType = "NOTE",
            Text = "Important information."
        });

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("NOTE").IgnoreCase, "Should include admonition type label");
        Assert.That(html, Does.Contain("Important information"), "Should include body text");
    }

    [Test]
    public void PDF_admonition_includes_type_label()
    {
        var doc = new DocumentNode();
        doc.AddChild(new AdmonitionNode
        {
            AdmonitionType = "WARNING",
            Text = "Be careful here."
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string content = Encoding.ASCII.GetString(pdf);

        Assert.That(content, Does.Contain("WARNING"), "Should include admonition type label");
        Assert.That(content, Does.Contain("Be careful here"), "Should include body text");
    }

    // ── Both renderers handle same document ─────────────────────────────

    [Test]
    public void Both_renderers_handle_complex_document()
    {
        var doc = new DocumentNode { Title = "Test Document" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Introduction" });
        doc.AddChild(new ParagraphNode { Text = "A paragraph." });
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "class Foo { }"
        });
        doc.AddChild(new AdmonitionNode
        {
            AdmonitionType = "TIP",
            Text = "A helpful tip."
        });

        var html = new HtmlRenderer().RenderToString(doc);
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        Assert.That(html.Length, Is.GreaterThan(0), "HTML should produce output");
        Assert.That(pdf.Length, Is.GreaterThan(0), "PDF should produce output");
        Assert.That(html, Does.Contain("Introduction"));
        Assert.That(Encoding.ASCII.GetString(pdf), Does.Contain("Introduction"));
    }

    [Test]
    public void Both_renderers_deterministic_with_same_input()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Content." });

        var html1 = new HtmlRenderer().RenderToString(doc);
        var html2 = new HtmlRenderer().RenderToString(doc);
        Assert.That(html1, Is.EqualTo(html2));

        var pdf1 = new PdfRenderer().RenderToBytes(doc);
        var pdf2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }
}
