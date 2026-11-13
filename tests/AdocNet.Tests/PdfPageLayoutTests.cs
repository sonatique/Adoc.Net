using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>PdfRenderer.LayoutPages: the page structure a paged preview follows.</summary>
[TestFixture]
public class PdfPageLayoutTests
{
    private static int PdfPageCount(DocumentNode doc, PdfRenderOptions options)
    {
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, options));
        return int.Parse(Regex.Match(pdf, @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value);
    }

    private static IEnumerable<string> Texts(PdfLayoutPage page) => page.Items.OfType<PdfPageText>().Select(t => t.Text);

    [Test]
    public void Blocks_record_the_page_they_start_on()
    {
        var doc = AdocParser.Parse("= Doc\n\nFirst.\n\n<<<\n\n== Two\n\nSecond.\n").Document;
        var layout = new PdfRenderer().LayoutPages(doc);

        Assert.That(layout.Pages, Has.Count.EqualTo(2));
        Assert.That(layout.HasTitlePage, Is.False);
        Assert.That(layout.Pages.Select(p => p.Label), Is.EqualTo(new[] { "1", "2" }));
        var section = (SectionNode)doc.Children.OfType<SectionNode>().Single();
        Assert.That(layout.Blocks.Select(b => b.PageNumber), Is.EqualTo(new[] { 1, 1, 2, 2 }), "title, First., == Two, Second.");
        Assert.That(layout.Blocks[0].Source, Is.EqualTo(SourceRange.None), "the document title");
        Assert.That(layout.Blocks[2].Source, Is.EqualTo(section.Source));
        Assert.That(layout.Blocks[0].Top, Is.LessThan(layout.Blocks[1].Top));
    }

    [Test]
    public void A_block_pushed_past_the_page_end_starts_on_the_next_page()
    {
        var source = new StringBuilder("= Doc\n\n");
        for (int i = 0; i < 80; i++) source.Append($"Paragraph {i} with enough words to take a line.\n\n");
        var doc = AdocParser.Parse(source.ToString()).Document;
        var options = new PdfRenderOptions();
        var layout = new PdfRenderer().LayoutPages(doc, options);

        Assert.That(layout.Pages, Has.Count.EqualTo(PdfPageCount(doc, options)).And.Count.GreaterThan(1));
        var paragraphs = layout.Blocks.Skip(1).ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(80));
        Assert.That(paragraphs.Select(b => b.PageNumber), Is.Ordered);
        foreach (var block in paragraphs)
            Assert.That(block.Top, Is.GreaterThanOrEqualTo(options.MarginTop - 1).And.LessThan(options.PageHeight - options.MarginBottom));
    }

    [Test]
    public void Title_page_toc_and_running_content_are_page_items()
    {
        var doc = AdocParser.Parse(
            "= Main Title: The Subtitle\nAda Writer\nv2.1\n:doctype: book\n:toc: macro\n\ntoc::[]\n\n<<<\n== Alpha\n\nText.\n\n<<<\n== Beta\n\nMore.\n").Document;
        var options = new PdfRenderOptions { FooterText = "{page-number} of {page-count}", FooterFontSize = 8 };
        var layout = new PdfRenderer().LayoutPages(doc, options);

        Assert.That(layout.HasTitlePage, Is.True);
        Assert.That(layout.Pages, Has.Count.EqualTo(PdfPageCount(doc, options)));
        Assert.That(layout.Pages.Select(p => p.Label), Is.EqualTo(new[] { "i", "1", "2", "3" }));

        var title = Texts(layout.Pages[0]).ToList();
        Assert.That(title, Does.Contain("Main Title").And.Contain("The Subtitle").And.Contain("Ada Writer").And.Contain("Version 2.1"));
        Assert.That(title, Has.None.Contains(" of "), "no running content on the title page");

        var toc = (TocNode)doc.Children.OfType<TocNode>().Single();
        Assert.That(layout.TocSource, Is.EqualTo(toc.Source));
        var tocTexts = Texts(layout.Pages[1]).ToList();
        Assert.That(tocTexts, Does.Contain("Table of Contents").And.Contain("Alpha").And.Contain("Beta"));
        Assert.That(tocTexts, Does.Contain("2").And.Contain("3"), "TOC page numbers");

        var footer = layout.Pages[3].Items.OfType<PdfPageText>().Single(t => t.Text == "3 of 3");
        Assert.That(footer.FontSize, Is.EqualTo(8));
        Assert.That(footer.Baseline, Is.GreaterThan(options.PageHeight - options.MarginBottom), "below the body");
        Assert.That(footer.Width, Is.GreaterThan(0));

        // Body pages carry the body blocks; the TOC block itself is drawn from items.
        Assert.That(layout.Blocks.First(b => b.Source == doc.Children.OfType<SectionNode>().First().Source).PageNumber, Is.EqualTo(3));
    }

    [Test]
    public void Svg_and_raster_title_page_images_are_recorded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-pagelayout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var svg = Path.Combine(dir, "logo.svg");
            File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 10\"><path fill=\"#ff0000\" d=\"M0 0 H20 V10 H0 Z\"/></svg>");
            var doc = AdocParser.Parse("= Title\n:title-page:\n\nBody.\n").Document;
            var options = new PdfRenderOptions { TitlePage = new PdfTitlePageOptions { LogoImagePath = svg, LogoWidth = 100 } };
            var layout = new PdfRenderer().LayoutPages(doc, options);

            var logo = layout.Pages[0].Items.OfType<PdfPageImage>().Single();
            Assert.That(logo.Path, Is.Null);
            Assert.That(logo.Shapes, Has.Count.EqualTo(1));
            Assert.That(logo.Shapes![0].Fill, Is.EqualTo(new PdfColor(1, 0, 0)));
            Assert.That((logo.Width, logo.Height, logo.ViewBoxWidth), Is.EqualTo((100f, 50f, 20f)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void Blocks_inside_a_sidebar_are_placed_like_the_layout_model_flattens_them()
    {
        var doc = AdocParser.Parse("Before.\n\n****\nInside one.\n\nInside two.\n****\n\nAfter.\n").Document;
        var layout = new PdfRenderer().LayoutPages(doc);
        var sidebar = (DelimitedBlockNode)doc.Children[1];

        var placed = layout.Blocks.Select(b => b.Source).ToList();
        Assert.That(placed, Does.Contain(sidebar.Children[0].Source).And.Contain(sidebar.Children[1].Source));
        Assert.That(placed, Does.Contain(doc.Children[2].Source));
    }

    [Test]
    public void Layout_does_not_change_the_pdf()
    {
        var doc = AdocParser.Parse("= Doc\n:toc:\n\n== A\n\nText.\n").Document;
        var renderer = new PdfRenderer();
        var before = renderer.RenderToBytes(doc);
        renderer.LayoutPages(doc);
        Assert.That(renderer.RenderToBytes(doc), Is.EqualTo(before));
    }
}
