using System.Linq;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Headless.NUnit;
using AdocNet.Ast;
using AdocNet.Avalonia;
using AdocNet.Converters.Pdf;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Behavioural tests for <see cref="PagedAvaloniaRenderer"/>: content flows
/// into fixed-size pages by measurement, explicit page breaks force
/// boundaries, and degenerate inputs (empty document, over-tall block) stay
/// well-formed.
/// </summary>
[TestFixture]
public class PagedAvaloniaRendererTests
{
    private static AdocNet.Layout.DocumentLayout Layout(string asciidoc)
    {
        var result = AdocParser.Parse(asciidoc);
        return new LayoutBuilder().Build(result.Document);
    }

    private static StackPanel Blocks(Border page) => (StackPanel)page.Child!;

    [AvaloniaTest]
    public void Empty_document_yields_a_single_empty_page()
    {
        var pages = new PagedAvaloniaRenderer().RenderPages(Layout(""), PageLayoutOptions.A4);

        Assert.That(pages, Has.Count.EqualTo(1));
        Assert.That(Blocks(pages[0]).Children, Is.Empty);
    }

    [AvaloniaTest]
    public void Pages_have_the_configured_dimensions()
    {
        var options = PageLayoutOptions.Letter;
        var pages = new PagedAvaloniaRenderer().RenderPages(Layout("Hello."), options);

        Assert.That(pages[0].Width, Is.EqualTo(options.PageWidth));
        Assert.That(pages[0].Height, Is.EqualTo(options.PageHeight));
    }

    [AvaloniaTest]
    public void Long_document_flows_onto_multiple_pages_without_losing_blocks()
    {
        var source = new StringBuilder();
        for (var i = 0; i < 120; i++)
            source.AppendLine($"Paragraph number {i} with a little bit of content.").AppendLine();

        var pages = new PagedAvaloniaRenderer().RenderPages(Layout(source.ToString()), PageLayoutOptions.A4);

        Assert.That(pages, Has.Count.GreaterThan(1));
        var totalBlocks = pages.Sum(p => Blocks(p).Children.Count);
        Assert.That(totalBlocks, Is.EqualTo(120));
        // Every page but the last should hold at least one block (greedy fill
        // never emits intermediate blank pages).
        foreach (var page in pages)
            Assert.That(Blocks(page).Children, Is.Not.Empty);
    }

    [AvaloniaTest]
    public void Explicit_page_break_forces_a_new_page()
    {
        var pages = new PagedAvaloniaRenderer().RenderPages(
            Layout("Before.\n\n<<<\n\nAfter."), PageLayoutOptions.A4);

        Assert.That(pages, Has.Count.EqualTo(2));
        Assert.That(Blocks(pages[0]).Children, Has.Count.EqualTo(1));
        Assert.That(Blocks(pages[1]).Children, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public void Page_break_at_document_start_does_not_emit_a_blank_leading_page()
    {
        var pages = new PagedAvaloniaRenderer().RenderPages(
            Layout("<<<\n\nContent."), PageLayoutOptions.A4);

        Assert.That(pages, Has.Count.EqualTo(1));
        Assert.That(Blocks(pages[0]).Children, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public void Overtall_block_gets_its_own_page_and_following_content_continues()
    {
        var huge = string.Join(" ", Enumerable.Repeat("word", 3000));
        var pages = new PagedAvaloniaRenderer().RenderPages(
            Layout($"Intro.\n\n{huge}\n\nOutro."), PageLayoutOptions.A4);

        // Intro | huge (alone, clipped) | outro — the over-tall paragraph must
        // not swallow its neighbours.
        Assert.That(pages, Has.Count.EqualTo(3));
        Assert.That(Blocks(pages[1]).Children, Has.Count.EqualTo(1));
        Assert.That(pages[1].ClipToBounds, Is.True);
    }

    [AvaloniaTest]
    public void Document_title_renders_on_the_first_page()
    {
        var pages = new PagedAvaloniaRenderer().RenderPages(
            Layout("= The Title\n\nBody."), PageLayoutOptions.A4);

        var first = Blocks(pages[0]).Children;
        Assert.That(first, Has.Count.EqualTo(2));
        Assert.That(first[0], Is.InstanceOf<TextBlock>());
        Assert.That(((TextBlock)first[0]).Text, Is.EqualTo("The Title"));
    }

    [AvaloniaTest]
    public void FromPdfPoints_converts_points_to_dips()
    {
        // A4 in points (595 × 842 @72dpi) → DIPs (@96dpi) is a 4/3 scale.
        var options = PageLayoutOptions.FromPdfPoints(595, 842);

        Assert.That(options.PageWidth, Is.EqualTo(595 * 96.0 / 72.0).Within(0.001));
        Assert.That(options.PageHeight, Is.EqualTo(842 * 96.0 / 72.0).Within(0.001));
        Assert.That(options.PageMargin.Left, Is.EqualTo(96).Within(0.001));
    }

    [AvaloniaTest]
    public void Degenerate_margins_are_rejected()
    {
        var options = new PageLayoutOptions
        {
            PageWidth = 100,
            PageHeight = 100,
            PageMargin = new global::Avalonia.Thickness(60),
        };

        Assert.Throws<System.ArgumentException>(() =>
            new PagedAvaloniaRenderer().RenderPages(Layout("x"), options));
    }

    // ── Pages that follow a PDF layout (PdfRenderer.LayoutPages) ──────────

    private static (AdocNet.Layout.DocumentLayout Layout, PdfPageLayout Pdf, PageLayoutOptions Options)
        Themed(string asciidoc, PdfRenderOptions pdfOptions)
    {
        var document = AdocParser.Parse(asciidoc).Document;
        return (new LayoutBuilder().Build(document),
            new PdfRenderer().LayoutPages(document, pdfOptions),
            PageLayoutOptions.FromPdf(pdfOptions));
    }

    private static List<StackPanel> Pieces(Border page) =>
        ((Canvas)page.Child!).Children
            .Select(c => c is Border { Child: StackPanel clipped } ? clipped : c as StackPanel)
            .Where(p => p?.Tag is SourceRange)
            .Select(p => p!)
            .ToList();

    private static IEnumerable<string> PageTexts(Border page) =>
        ((Canvas)page.Child!).Children.OfType<TextBlock>().Select(t => t.Text ?? "");

    [AvaloniaTest]
    public void FromPdf_takes_the_pdf_page_size_and_margins()
    {
        var options = PageLayoutOptions.FromPdf(new PdfRenderOptions
        {
            PageWidth = 612, PageHeight = 792, MarginLeft = 36, MarginTop = 48, MarginRight = 54, MarginBottom = 60,
        });

        Assert.That(options.PageWidth, Is.EqualTo(816).Within(0.001));
        Assert.That(options.PageHeight, Is.EqualTo(1056).Within(0.001));
        Assert.That(options.PageMargin, Is.EqualTo(new global::Avalonia.Thickness(48, 64, 72, 80)));
    }

    [AvaloniaTest]
    public void Pdf_pages_keep_the_page_count_title_page_toc_and_running_content()
    {
        var (layout, pdf, options) = Themed(
            "= Main Title: Subtitle\nAda Writer\n:title-page:\n:toc: macro\n\ntoc::[]\n\n<<<\n== Alpha\n\nText.\n\n<<<\n== Beta\n\nMore.\n",
            new PdfRenderOptions { FooterText = "{page-number} of {page-count}" });

        var pages = new PagedAvaloniaRenderer().RenderPages(layout, pdf, options);

        Assert.That(pages, Has.Count.EqualTo(pdf.Pages.Count).And.Count.EqualTo(4));
        Assert.That(PageTexts(pages[0]), Does.Contain("Main Title").And.Contain("Subtitle").And.Contain("Ada Writer"));
        Assert.That(Pieces(pages[0]), Is.Empty, "the title page has no body blocks (no duplicate title)");
        Assert.That(PageTexts(pages[1]), Does.Contain("Table of Contents").And.Contain("Alpha"));
        Assert.That(Pieces(pages[1]), Is.Empty, "the TOC is drawn from page items, not from the TOC list block");
        Assert.That(PageTexts(pages[2]), Does.Contain("2 of 3"));
        Assert.That(PageTexts(pages[3]), Does.Contain("3 of 3"));
        var beta = layout.Children.OfType<AdocNet.Layout.HeadingLayout>().Last();
        Assert.That(Pieces(pages[3]).Select(p => p.Tag), Has.Member(beta.Source));
    }

    [AvaloniaTest]
    public void Pdf_pages_start_each_block_on_its_pdf_page()
    {
        var source = new StringBuilder("= Doc\n\n");
        for (var i = 0; i < 60; i++)
            source.AppendLine($"Paragraph {i} with a few words of content.").AppendLine();
        var (layout, pdf, options) = Themed(source.ToString(), new PdfRenderOptions());

        var pages = new PagedAvaloniaRenderer().RenderPages(layout, pdf, options);

        Assert.That(pdf.Pages, Has.Count.GreaterThan(1));
        var firstPiecePage = new Dictionary<SourceRange, int>();
        for (var i = 0; i < pages.Count; i++)
            foreach (var piece in Pieces(pages[i]))
                firstPiecePage.TryAdd((SourceRange)piece.Tag!, i + 1);
        foreach (var block in pdf.Blocks)
            Assert.That(firstPiecePage[block.Source], Is.GreaterThanOrEqualTo(block.PageNumber),
                "never earlier than the PDF; later only when screen text runs longer");
        var onPdfPage = pdf.Blocks.Count(b => firstPiecePage[b.Source] == b.PageNumber);
        Assert.That(onPdfPage, Is.GreaterThan(pdf.Blocks.Count * 9 / 10), $"{onPdfPage}/{pdf.Blocks.Count} on the PDF's page");
    }

    [AvaloniaTest]
    public void A_block_split_by_the_pdf_continues_on_the_next_page_between_lines()
    {
        // Hard line breaks: headless drawing does not wrap, but it does lay out explicit lines.
        var words = string.Join(" +\n", Enumerable.Range(0, 70).Select(i => $"line {i} of a long paragraph"));
        var (layout, pdf, options) = Themed($"= Doc\n\n{words}\n\n{words}\n", new PdfRenderOptions());

        var pages = new PagedAvaloniaRenderer().RenderPages(layout, pdf, options);

        var continued = pages.Skip(1).SelectMany(Pieces).Where(p => p.RenderTransform is not null).ToList();
        Assert.That(continued, Is.Not.Empty, "a paragraph continues onto a following page");
        foreach (var piece in continued)
        {
            Assert.That(((Border)piece.Parent!).ClipToBounds, Is.True);
            var shift = -((global::Avalonia.Media.TranslateTransform)piece.RenderTransform!).Y;
            // Re-parenting reset the layout: lay the piece out again, alone, to read its lines.
            piece.Measure(new global::Avalonia.Size(piece.Width, double.PositiveInfinity));
            piece.Arrange(new global::Avalonia.Rect(piece.DesiredSize));
            var text = (TextBlock)piece.Children[0];
            Assert.That(text.TextLayout.TextLines, Has.Count.GreaterThan(1), "laid out at the content width");
            var lines = text.TextLayout.TextLines;
            var lineTops = Enumerable.Range(1, lines.Count - 1).Select(i => text.Margin.Top + lines.Take(i).Sum(l => l.Height));
            Assert.That(lineTops, Has.Some.EqualTo(shift).Within(0.01), "the cut falls between two lines");
        }
    }

    [AvaloniaTest]
    public void Pdf_pages_add_a_page_rather_than_drop_text_that_runs_longer_on_screen()
    {
        var (layout, pdf, _) = Themed("= Doc\n\n" + string.Join("\n\n", Enumerable.Range(0, 30).Select(i => $"Para {i}.")),
            new PdfRenderOptions());
        // Screen pages much shorter than the PDF's: everything must still be placed.
        var small = new PageLayoutOptions { PageWidth = 794, PageHeight = 374, PageMargin = new global::Avalonia.Thickness(96) };

        var pages = new PagedAvaloniaRenderer().RenderPages(layout, pdf, small);

        Assert.That(pages, Has.Count.GreaterThan(pdf.Pages.Count));
        Assert.That(pages.SelectMany(Pieces).Select(p => p.Tag).Distinct().Count(), Is.EqualTo(31), "title + 30 paragraphs");
    }
}
