using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class ConfigurationTests
{
    // ── Cross-interaction: HTML theme + syntax highlighting ─────────────

    [Test]
    public void Html_theme_with_syntax_highlighting_produces_both()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Github,
            EnableSyntaxHighlighting = true
        };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<style>"), "Should have theme CSS");
        Assert.That(html, Does.Contain("hl-kw"), "Should have syntax highlighting spans");
        Assert.That(html, Does.Contain("#d1d9e0"), "Should have Github theme colors");
    }

    // ── Cross-interaction: PDF hyphenation + heading color + spacing ────

    [Test]
    public void Pdf_hyphenation_with_heading_color_and_spacing()
    {
        var doc = new DocumentNode { Title = "Styled Doc" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Section" });
        doc.AddChild(new ParagraphNode
        {
            Text = "The internationalization of documentation requires careful " +
                   "consideration of multiple factors including readability."
        });

        var options = new PdfRenderOptions
        {
            EnableHyphenation = true,
            HeadingColor = new PdfColor(0.2f, 0f, 0.6f),
            ParagraphSpacingBefore = 4f,
            ParagraphSpacingAfter = 10f,
            SectionSpacing = 20f,
            PageWidth = 350f, MarginLeft = 36f, MarginRight = 36f
        };

        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string content = Encoding.ASCII.GetString(pdf);

        Assert.That(content, Does.Contain("0.2 0 0.6 rg"), "Should have heading color");
        Assert.That(content, Does.Contain("%PDF-1.4"), "Should be valid PDF");
    }

    // ── Cross-interaction: PDF syntax + compact preset ──────────────────

    [Test]
    public void Pdf_compact_preset_with_syntax_highlighting()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "python",
            Content = "def hello():\n    print('world')"
        });

        // Compact preset + syntax highlighting
        var options = new PdfRenderOptions
        {
            FontSize = 10f, LineSpacing = 1.25f,
            ParagraphSpacingAfter = 6f, MarginTop = 54f, MarginBottom = 54f,
            SectionSpacing = 12f,
            SyntaxColors = SyntaxColorScheme.Default
        };

        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string content = Encoding.ASCII.GetString(pdf);

        Assert.That(content, Does.Contain(" rg\n"), "Should have syntax color operators");
        Assert.That(content, Does.Contain("%PDF-1.4"));
    }

    // ── Backward compatibility ──────────────────────────────────────────

    [Test]
    public void Default_HtmlRenderOptions_matches_beta3()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Hello." });

        // Default (no options) vs explicit default
        var html1 = new HtmlRenderer().RenderToString(doc);
        var html2 = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions());

        Assert.That(html1, Is.EqualTo(html2));
    }

    [Test]
    public void Default_PdfRenderOptions_matches_beta3()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Hello." });

        var pdf1 = new PdfRenderer().RenderToBytes(doc);
        var pdf2 = new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions());

        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    // ── All new options have defaults ───────────────────────────────────

    [Test]
    public void Html_EnableSyntaxHighlighting_defaults_to_false()
    {
        Assert.That(new HtmlRenderOptions().EnableSyntaxHighlighting, Is.False);
    }

    [Test]
    public void Pdf_new_options_have_backward_compat_defaults()
    {
        var opts = new PdfRenderOptions();
        Assert.That(opts.EnableHyphenation, Is.False, "Hyphenation off by default");
        Assert.That(opts.ParagraphSpacingBefore, Is.EqualTo(0f), "No spacing before");
        Assert.That(opts.ParagraphSpacingAfter, Is.EqualTo(8f), "8pt spacing after (beta.3)");
        Assert.That(opts.SyntaxColors, Is.Null, "No syntax highlighting by default");
        Assert.That(opts.HeadingColor, Is.Null, "Black headings by default");
        Assert.That(opts.BodyColor, Is.Null, "Black body by default");
        Assert.That(opts.TableHeaderBackground, Is.Null, "No table header bg by default");
        Assert.That(opts.SectionSpacing, Is.EqualTo(16f), "16pt section spacing (beta.3)");
        Assert.That(opts.BlockIndent, Is.EqualTo(24f), "24pt block indent (beta.3)");
    }

    // ── Determinism with combined options ────────────────────────────────

    [Test]
    public void Combined_options_produce_deterministic_output()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Heading" });
        doc.AddChild(new ParagraphNode { Text = "Body text content." });

        var htmlOpts = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            EnableSyntaxHighlighting = true,
            CustomCss = "body { margin: 0; }"
        };

        var html1 = new HtmlRenderer().RenderToString(doc, htmlOpts);
        var html2 = new HtmlRenderer().RenderToString(doc, htmlOpts);
        Assert.That(html1, Is.EqualTo(html2));

        var pdfOpts = new PdfRenderOptions
        {
            HeadingColor = new PdfColor(0.5f, 0, 0),
            EnableHyphenation = true,
            SectionSpacing = 20f
        };

        var pdf1 = new PdfRenderer().RenderToBytes(doc, pdfOpts);
        var pdf2 = new PdfRenderer().RenderToBytes(doc, pdfOpts);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }
}
