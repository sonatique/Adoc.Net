using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class PdfStylingTests
{
    [Test]
    public void HeadingColor_changes_color_in_pdf()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Heading" });

        var defaultPdf = new PdfRenderer().RenderToBytes(doc);
        var coloredPdf = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { HeadingColor = new PdfColor(0.8f, 0f, 0f) });

        Assert.That(coloredPdf, Is.Not.EqualTo(defaultPdf),
            "HeadingColor should change the PDF output");

        string content = Encoding.ASCII.GetString(coloredPdf);
        Assert.That(content, Does.Contain("0.8 0 0 rg"),
            "Should contain the red heading color operator");
    }

    [Test]
    public void BodyColor_changes_paragraph_color()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello world." });

        var coloredPdf = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { BodyColor = new PdfColor(0.2f, 0.2f, 0.2f) });

        string content = Encoding.ASCII.GetString(coloredPdf);
        Assert.That(content, Does.Contain("0.2 0.2 0.2 rg"),
            "Should contain body color operator");
    }

    [Test]
    public void SectionSpacing_changes_output()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "A" });
        doc.AddChild(new ParagraphNode { Text = "Text." });

        var defaultPdf = new PdfRenderer().RenderToBytes(doc);
        var widePdf = new PdfRenderer().RenderToBytes(doc,
            new PdfRenderOptions { SectionSpacing = 32f });

        Assert.That(widePdf, Is.Not.EqualTo(defaultPdf),
            "Different SectionSpacing should produce different output");
    }

    [Test]
    public void Compact_preset_produces_output()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Content." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.Compact);
        string content = Encoding.ASCII.GetString(pdf);
        Assert.That(content, Does.Contain("%PDF-1.4"));
    }

    [Test]
    public void Presentation_preset_produces_colored_headings()
    {
        var doc = new DocumentNode { Title = "Presentation" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Slide" });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.Presentation);
        string content = Encoding.ASCII.GetString(pdf);

        // Presentation preset has HeadingColor = (0, 0, 0.6)
        Assert.That(content, Does.Contain("0 0 0.6 rg"),
            "Presentation preset should apply heading color");
    }

    [Test]
    public void Default_options_backward_compatible()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Hello." });

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.Default);

        Assert.That(pdf1, Is.EqualTo(pdf2),
            "Default options should be backward compatible");
    }

    [Test]
    public void Styling_is_deterministic()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Heading" });

        var options = new PdfRenderOptions
        {
            HeadingColor = new PdfColor(0.5f, 0f, 0f),
            SectionSpacing = 20f
        };

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc, options);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc, options);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }
}
