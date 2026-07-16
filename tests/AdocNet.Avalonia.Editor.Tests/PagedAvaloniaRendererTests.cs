using System.Linq;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Headless.NUnit;
using AdocNet.Avalonia;
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

    [Test]
    public void FromPdfPoints_converts_points_to_dips()
    {
        // A4 in points (595 × 842 @72dpi) → DIPs (@96dpi) is a 4/3 scale.
        var options = PageLayoutOptions.FromPdfPoints(595, 842);

        Assert.That(options.PageWidth, Is.EqualTo(595 * 96.0 / 72.0).Within(0.001));
        Assert.That(options.PageHeight, Is.EqualTo(842 * 96.0 / 72.0).Within(0.001));
        Assert.That(options.PageMargin.Left, Is.EqualTo(96).Within(0.001));
    }

    [Test]
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
}
