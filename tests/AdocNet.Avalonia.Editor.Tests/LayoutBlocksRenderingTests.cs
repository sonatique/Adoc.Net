using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless;
using global::Avalonia.Headless.NUnit;
using global::Avalonia.Media;
using global::Avalonia.Threading;
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

    /// <summary>Runs posted UI jobs until <paramref name="done"/> holds (images decode on a worker thread).</summary>
    private static void Settle(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

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
    public void Marked_text_has_the_highlight_background()
    {
        var renderer = new AvaloniaRenderer { WrapInScrollViewer = false };
        var root = (StackPanel)renderer.Render(new LayoutBuilder().Build(AdocParser.Parse("a #marked# b").Document));
        var span = root.Children.OfType<TextBlock>().Single().Inlines!.OfType<Span>().Single();
        Assert.That(span.Background, Is.SameAs(renderer.Theme.HighlightBackground));
        Assert.That(span.Foreground, Is.SameAs(renderer.Theme.HighlightForeground));
        Assert.That(span.Inlines.OfType<Run>().Single().Text, Is.EqualTo("marked"));
    }

    [AvaloniaTest]
    public void Links_are_underlined_spans_of_the_text()
    {
        var paragraph = Render("Some text with https://example.org and https://example.org[named] here.").Children.OfType<TextBlock>().Single();
        var links = paragraph.Inlines!.OfType<LinkSpan>().ToList();
        Assert.That(links.Select(l => l.Href), Is.EqualTo(new[] { "https://example.org", "https://example.org" }));
        Assert.That(links.Select(l => l.TextDecorations), Is.All.EqualTo(TextDecorations.Underline));
        Assert.That(paragraph.Inlines!.OfType<InlineUIContainer>(), Is.Empty, "no embedded control, so links wrap with the text");
    }

    /// <summary>The centre of the character at <paramref name="index"/>, in window coordinates.</summary>
    private static global::Avalonia.Point CharacterCentre(TextBlock text, Window window, int index)
    {
        var rect = text.TextLayout.HitTestTextPosition(index);
        return text.TranslatePoint(rect.Center, window)!.Value;
    }

    [AvaloniaTest]
    public void Clicking_a_link_on_any_line_raises_LinkClicked_once()
    {
        // A hard break first: a LineBreak takes a newline's worth of text positions.
        const string source = "Intro +\nsee https://example.org/a/long/path/that/wraps/over/several/lines/index.html and more";
        var renderer = new AvaloniaRenderer { WrapInScrollViewer = false };
        var clicks = new List<string>();
        renderer.LinkClicked += (_, e) => { clicks.Add(e.Url); e.Handled = true; };
        var panel = (StackPanel)renderer.Render(new LayoutBuilder().Build(AdocParser.Parse(source).Document));
        var window = new Window { Width = 160, Height = 400, Content = panel };
        window.Show();
        window.UpdateLayout();
        var paragraph = panel.Children.OfType<TextBlock>().Single();
        Assert.That(paragraph.TextLayout.TextLines, Has.Count.GreaterThan(3), "the link wraps");
        // Headless drawing does not hit-test bare text; a background makes the block hittable.
        paragraph.Background = Brushes.Transparent;

        int linkStart = "Intro".Length + Environment.NewLine.Length + "see ".Length;
        int linkEnd = linkStart + "https://example.org/a/long/path/that/wraps/over/several/lines/index.html".Length;
        foreach (var index in new[] { 0, linkStart - 1, linkStart, (linkStart + linkEnd) / 2, linkEnd - 1, linkEnd + 1 })
        {
            clicks.Clear();
            var point = CharacterCentre(paragraph, window, index);
            window.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
            bool onLink = index >= linkStart && index < linkEnd;
            Assert.That(clicks, Is.EqualTo(onLink ? new[] { "https://example.org/a/long/path/that/wraps/over/several/lines/index.html" } : Array.Empty<string>()), $"character {index}");
        }

        // Past the end of a line is not on the link, even beside it.
        var lastLine = paragraph.TextLayout.TextLines[^1];
        var beyond = paragraph.TranslatePoint(new global::Avalonia.Point(lastLine.WidthIncludingTrailingWhitespace + 5, paragraph.TextLayout.Height - 2), window)!.Value;
        clicks.Clear();
        window.MouseDown(beyond, global::Avalonia.Input.MouseButton.Left);
        Assert.That(clicks, Is.Empty);
    }

    [AvaloniaTest]
    public void The_hand_cursor_shows_only_over_links()
    {
        var panel = Render("plain https://example.org plain");
        var window = new Window { Width = 400, Height = 200, Content = panel };
        window.Show();
        window.UpdateLayout();
        var paragraph = panel.Children.OfType<TextBlock>().Single();
        paragraph.Background = Brushes.Transparent; // hittable under headless drawing

        window.MouseMove(CharacterCentre(paragraph, window, "plain ".Length + 3));
        Assert.That(paragraph.Cursor, Is.Not.Null);
        window.MouseMove(CharacterCentre(paragraph, window, 1));
        Assert.That(paragraph.IsSet(global::Avalonia.Input.InputElement.CursorProperty), Is.False);
    }

    [AvaloniaTest]
    public void Checklist_items_show_their_boxes()
    {
        var list = Render("* [x] done\n* [ ] todo\n* plain").Children.OfType<StackPanel>().Single();
        var items = All<TextBlock>(list).ToList();

        // Boxes are drawn (a glyph falls back to a colour emoji for the checked one), and take
        // the same two text positions as the "\u2611 " prefix did.
        foreach (var (item, isChecked) in new[] { (items[0], true), (items[1], false) })
        {
            var box = (Viewbox)((InlineUIContainer)item.Inlines![0]).Child!;
            var drawing = (Canvas)box.Child!;
            Assert.That(drawing.Children.OfType<global::Avalonia.Controls.Shapes.Path>().Any(), Is.EqualTo(isChecked));
            Assert.That(((Run)item.Inlines[1]).Text, Is.EqualTo(" "));
        }
        Assert.That(items[2].Inlines!.OfType<Run>().First().Text, Is.EqualTo("\u2022 "));

        var checkbox = (Viewbox)((InlineUIContainer)items[0].Inlines![0]).Child!;
        items[0].FontSize = 20;
        Assert.That(checkbox.Width, Is.EqualTo(15), "three quarters of the font size");
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
            var paragraph = panel.Children.OfType<TextBlock>().Last();
            var inlineHost = (Decorator)paragraph.Inlines!.OfType<InlineUIContainer>().Single().Child!;
            Settle(() => All<Image>(panel).Any() && inlineHost.Child is Image);

            Assert.That(All<Image>(panel).Single().Source, Is.Not.Null);
            Assert.That(All<TextBlock>(panel).Any(t => t.Text == "Caption"), Is.True);
            Assert.That(((Image)inlineHost.Child!).Source, Is.Not.Null);
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
            var absolute = Render("image::" + Path.Combine(dir, "dot.png") + "[x]");
            Settle(() => All<Image>(absolute).Any());
            Assert.That(All<Image>(absolute), Has.Exactly(1).Items, "absolute path");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaTest]
    public void Decoded_images_are_reused_until_the_file_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "dot.png");
            File.WriteAllBytes(file, Png);
            var renderer = new AvaloniaRenderer { WrapInScrollViewer = false, BaseDirectory = dir };
            var layout = new LayoutBuilder().Build(AdocParser.Parse("image::dot.png[x]").Document);
            IImage? Decoded()
            {
                var panel = (StackPanel)renderer.Render(layout);
                Settle(() => All<Image>(panel).Any());
                return All<Image>(panel).Single().Source;
            }

            var first = Decoded();
            var again = (StackPanel)renderer.Render(layout);
            Assert.That(All<Image>(again).Single().Source, Is.SameAs(first), "a cached image shows at once");

            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5));
            Assert.That(Decoded(), Is.Not.SameAs(first), "a changed file decodes again");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
