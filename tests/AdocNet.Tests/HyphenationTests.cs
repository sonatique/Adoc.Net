using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class HyphenationTests
{
    [Test]
    public void Hyphenation_word_produces_expected_break_points()
    {
        // "hyphenation" should hyphenate as "hy-phen-ation" or similar
        var breaks = Hyphenator.GetBreakPoints("hyphenation");
        Assert.That(breaks, Is.Not.Empty, "Should find break points for 'hyphenation'");
        Assert.That(breaks, Does.Contain(2), "Should break after 'hy' (index 2)");
    }

    [Test]
    public void Short_words_are_not_hyphenated()
    {
        var breaks = Hyphenator.GetBreakPoints("the");
        Assert.That(breaks, Is.Empty);
    }

    [Test]
    public void Four_letter_words_are_not_hyphenated()
    {
        var breaks = Hyphenator.GetBreakPoints("code");
        Assert.That(breaks, Is.Empty);
    }

    [Test]
    public void Algorithm_word_hyphenates()
    {
        var breaks = Hyphenator.GetBreakPoints("algorithm");
        Assert.That(breaks, Is.Not.Empty, "Should find break points for 'algorithm'");
    }

    [Test]
    public void Computer_word_hyphenates()
    {
        var breaks = Hyphenator.GetBreakPoints("computer");
        Assert.That(breaks, Is.Not.Empty, "Should find break points for 'computer'");
    }

    [Test]
    public void Empty_string_returns_empty()
    {
        var breaks = Hyphenator.GetBreakPoints("");
        Assert.That(breaks, Is.Empty);
    }

    [Test]
    public void Hyphenation_is_deterministic()
    {
        var breaks1 = Hyphenator.GetBreakPoints("documentation");
        var breaks2 = Hyphenator.GetBreakPoints("documentation");
        Assert.That(breaks1, Is.EqualTo(breaks2));
    }

    // ── Integration with PDF line breaking ──────────────────────────────

    [Test]
    public void Hyphenated_pdf_contains_hyphen_in_content_stream()
    {
        // A long word that won't fit on a narrow line should be hyphenated
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "The internationalization of documentation is important."
        });

        var options = new PdfRenderOptions
        {
            EnableHyphenation = true,
            // Narrow page to force hyphenation
            PageWidth = 300f, MarginLeft = 36f, MarginRight = 36f
        };

        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string content = Encoding.ASCII.GetString(pdf);

        // The word "internationalization" should be hyphenated with a trailing "-"
        Assert.That(content, Does.Contain("-"),
            "Hyphenated PDF should contain hyphen characters from word breaks");
    }

    [Test]
    public void Hyphenation_disabled_by_default()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello world." });

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { EnableHyphenation = false });

        Assert.That(pdf1, Is.EqualTo(pdf2),
            "Default options should not enable hyphenation");
    }

    // ── Paragraph spacing tests ─────────────────────────────────────────

    [Test]
    public void ParagraphSpacingBefore_adds_space_between_paragraphs()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "First paragraph." });
        doc.AddChild(new ParagraphNode { Text = "Second paragraph." });

        byte[] pdfDefault = new PdfRenderer().RenderToBytes(doc);
        byte[] pdfWithSpacing = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { ParagraphSpacingBefore = 12f });

        // PDF with extra spacing should be different (more vertical space used)
        Assert.That(pdfWithSpacing, Is.Not.EqualTo(pdfDefault),
            "ParagraphSpacingBefore should change the output");
    }

    [Test]
    public void ParagraphSpacingAfter_default_matches_beta3()
    {
        // Default ParagraphSpacingAfter = 8f, same as beta.3's ParagraphSpacing constant
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        byte[] pdfExplicit = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { ParagraphSpacingAfter = 8f });

        Assert.That(pdf, Is.EqualTo(pdfExplicit),
            "Default ParagraphSpacingAfter should match the beta.3 constant (8)");
    }

    [Test]
    public void LineSpacing_change_affects_output()
    {
        var doc = new DocumentNode();
        // Use enough text to force multi-line wrapping so line spacing matters
        doc.AddChild(new ParagraphNode
        {
            Text = "This is a paragraph with enough text that it will definitely wrap to " +
                   "multiple lines on the page. The quick brown fox jumps over the lazy dog. " +
                   "Additional words here to ensure wrapping occurs properly for the test."
        });

        byte[] pdfNormal = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { LineSpacing = 1.35f });
        byte[] pdfWide = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { LineSpacing = 1.5f });

        Assert.That(pdfWide, Is.Not.EqualTo(pdfNormal),
            "Changing LineSpacing should produce different output");
    }

    [Test]
    public void Determinism_with_hyphenation_enabled()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "The documentation is comprehensive." });

        var options = new PdfRenderOptions { EnableHyphenation = true };
        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc, options);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc, options);

        Assert.That(pdf1, Is.EqualTo(pdf2));
    }
}
