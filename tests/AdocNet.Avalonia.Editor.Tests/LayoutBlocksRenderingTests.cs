using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless.NUnit;
using global::Avalonia.Media;
using AdocNet.Avalonia;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// The preview draws images, quotes, strikethrough, checklists and hard line breaks.
/// </summary>
[TestFixture]
public class LayoutBlocksRenderingTests
{
    // A 2x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGP4z8DAwPAfAAcAAf9+CLHQAAAAAElFTkSuQmCC");

    private static StackPanel Render(string adoc, string? baseDirectory = null) =>
        (StackPanel)new AvaloniaRenderer { WrapInScrollViewer = false, BaseDirectory = baseDirectory }
            .Render(new LayoutBuilder().Build(AdocParser.Parse(adoc).Document));

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        if (root is T match) yield return match;
        var children = root switch
        {
            Panel panel => panel.Children.ToList(),
            Decorator { Child: { } child } => [child],
            _ => new List<Control>(),
        };
        foreach (var child in children)
            foreach (var found in All<T>(child))
                yield return found;
    }

    [AvaloniaTest]
    public void Strikethrough_is_a_struck_span()
    {
        var paragraph = Render("a [.line-through]#gone# b").Children.OfType<TextBlock>().Single();
        var span = paragraph.Inlines!.OfType<Span>().Single();
        Assert.That(span.TextDecorations, Is.EqualTo(TextDecorations.Strikethrough));
        Assert.That(span.Inlines.OfType<Run>().Single().Text, Is.EqualTo("gone"));
    }

    [AvaloniaTest]
    public void Checklist_items_show_their_boxes()
    {
        var list = Render("* [x] done\n* [ ] todo\n* plain").Children.OfType<StackPanel>().Single();
        var prefixes = All<TextBlock>(list).Select(t => t.Inlines!.OfType<Run>().First().Text).ToList();
        Assert.That(prefixes, Is.EqualTo(new[] { "\u2611 ", "\u2610 ", "\u2022 " }));
    }

    [AvaloniaTest]
    public void Quote_has_its_content_and_attribution()
    {
        var border = Render("[quote, Ann, Book]\n____\nWise words.\n____").Children.OfType<Border>().Single();
        var texts = All<TextBlock>(border).ToList();
        Assert.That(texts[0].Inlines!.OfType<Run>().Single().Text, Is.EqualTo("Wise words."));
        Assert.That(texts[1].Text, Is.EqualTo("\u2014 Ann, Book"));
        Assert.That(border.BorderThickness.Left, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public void Hard_breaks_render_line_breaks_and_soft_breaks_do_not()
    {
        var hard = Render("one +\ntwo").Children.OfType<TextBlock>().Single();
        Assert.That(hard.Inlines!.OfType<LineBreak>().Count(), Is.EqualTo(1));
        var soft = Render("one\ntwo").Children.OfType<TextBlock>().Single();
        Assert.That(soft.Inlines!.OfType<LineBreak>(), Is.Empty);
        Assert.That(soft.Inlines!.OfType<Run>().Single().Text, Is.EqualTo("one two"));
    }

    [AvaloniaTest]
    public void Local_image_loads_relative_to_the_base_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "dot.png"), Png);
            var panel = Render(".Caption\nimage::dot.png[A dot]\n\nInline image:dot.png[d] here.", dir);
            var image = All<Image>(panel).Single();
            Assert.That(image.Source, Is.Not.Null);
            Assert.That(All<TextBlock>(panel).Any(t => t.Text == "Caption"), Is.True);

            var paragraph = panel.Children.OfType<TextBlock>().Last();
            var inlineImage = paragraph.Inlines!.OfType<InlineUIContainer>().Single();
            Assert.That(((Image)inlineImage.Child!).Source, Is.Not.Null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Missing_remote_and_network_images_show_their_alt_text()
    {
        foreach (var target in new[] { "missing.png", "https://example.com/a.png", "//server/share/a.png", @"\\server\share\a.png" })
        {
            var panel = Render($"image::{target}[Alt for {target}]", Path.GetTempPath());
            Assert.That(All<Image>(panel), Is.Empty, target);
            Assert.That(All<TextBlock>(panel).Single().Text, Is.EqualTo("Alt for " + target), target);
        }

        var paragraph = Render("See image:https://example.com/b.png[badge].").Children.OfType<TextBlock>().Single();
        Assert.That(paragraph.Inlines!.OfType<Run>().Select(r => r.Text), Does.Contain("[image: badge]"));
    }

    [AvaloniaTest]
    public void Relative_image_without_base_directory_and_svg_are_not_loaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "dot.png"), Png);
            File.WriteAllText(Path.Combine(dir, "a.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
            Assert.That(All<Image>(Render("image::dot.png[x]")), Is.Empty, "no base directory");
            Assert.That(All<Image>(Render("image::a.svg[x]", dir)), Is.Empty, "svg");
            Assert.That(All<Image>(Render("image::" + Path.Combine(dir, "dot.png") + "[x]")), Has.Exactly(1).Items, "absolute path");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
