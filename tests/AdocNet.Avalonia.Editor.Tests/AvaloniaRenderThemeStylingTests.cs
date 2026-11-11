using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless.NUnit;
using global::Avalonia.Media;
using AdocNet.Avalonia;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Heading, body and table-header styling from <see cref="AvaloniaRenderTheme"/>
/// across the full, paged and incremental renderers.
/// </summary>
[TestFixture]
public class AvaloniaRenderThemeStylingTests
{
    private const string Source = "= Doc Title\n\n== First\n\npara\n\n=== Second\n\n|===\n|Head\n\n|cell\n|===\n";

    private static readonly IBrush Red = Brushes.Red, Blue = Brushes.Blue, Green = Brushes.Green, Orange = Brushes.Orange;
    private static readonly FontFamily HeadingFont = new("Heading Face"), BodyFont = new("Body Face");

    private static AvaloniaRenderTheme StyledTheme() => new()
    {
        HeadingForegrounds = new IBrush?[] { Red, null },
        FallbackHeadingForeground = Blue,
        HeadingFont = HeadingFont,
        BodyFont = BodyFont,
        BodyForeground = Green,
        TableHeaderForeground = Orange,
        DocumentTitleFontSize = 31,
    };

    private static AdocNet.Layout.DocumentLayout Layout(string asciidoc) =>
        new LayoutBuilder().Build(AdocParser.Parse(asciidoc).Document);

    private static IEnumerable<TextBlock> TextBlocks(Control root)
    {
        switch (root)
        {
            case TextBlock tb: yield return tb; break;
            case Panel p: foreach (var c in p.Children.SelectMany(TextBlocks)) yield return c; break;
            case Decorator { Child: { } child }: foreach (var c in TextBlocks(child)) yield return c; break;
            case ContentControl { Content: Control content }: foreach (var c in TextBlocks(content)) yield return c; break;
        }
    }

    private static string TextOf(TextBlock tb) =>
        tb.Text ?? string.Concat(tb.Inlines?.OfType<Run>().Select(r => r.Text) ?? Enumerable.Empty<string>());

    private static TextBlock Find(Control root, string text) =>
        TextBlocks(root).First(tb => TextOf(tb).Contains(text));

    [AvaloniaTest]
    public void Default_theme_leaves_fonts_and_foregrounds_to_the_host()
    {
        var root = new AvaloniaRenderer().Render(Layout(Source));

        foreach (var text in new[] { "Doc Title", "First", "para", "Head" })
        {
            var tb = Find(root, text);
            Assert.That(tb.IsSet(TextBlock.ForegroundProperty), Is.False, text);
            Assert.That(tb.IsSet(TextBlock.FontFamilyProperty), Is.False, text);
        }
    }

    [AvaloniaTest]
    public void Theme_styles_headings_per_level_body_and_table_header()
    {
        var root = new AvaloniaRenderer { Theme = StyledTheme() }.Render(Layout(Source));

        var title = Find(root, "Doc Title");
        Assert.That(title.Foreground, Is.SameAs(Blue), "title uses the fallback heading brush");
        Assert.That(title.FontFamily, Is.EqualTo(HeadingFont));
        Assert.That(title.FontSize, Is.EqualTo(31));

        Assert.That(Find(root, "First").Foreground, Is.SameAs(Red), "level 1 from the list");
        Assert.That(Find(root, "Second").Foreground, Is.SameAs(Blue), "null entry falls back");
        Assert.That(Find(root, "First").FontFamily, Is.EqualTo(HeadingFont));

        var para = Find(root, "para");
        Assert.That(para.Foreground, Is.SameAs(Green));
        Assert.That(para.FontFamily, Is.EqualTo(BodyFont));

        Assert.That(Find(root, "Head").Foreground, Is.SameAs(Orange));
        Assert.That(Find(root, "cell").Foreground, Is.SameAs(Green), "body cells inherit the body brush");
        Assert.That(Find(root, "cell").FontFamily, Is.EqualTo(BodyFont));
    }

    [AvaloniaTest]
    public void Paged_and_incremental_renderers_apply_the_theme()
    {
        var paged = new PagedAvaloniaRenderer(new AvaloniaRenderer { Theme = StyledTheme() })
            .RenderPages(Layout(Source), PageLayoutOptions.A4);
        var pagedTitle = Find(paged[0], "Doc Title");
        Assert.That(pagedTitle.Foreground, Is.SameAs(Blue));
        Assert.That(pagedTitle.FontSize, Is.EqualTo(31));
        Assert.That(Find(paged[0], "para").Foreground, Is.SameAs(Green));

        var incremental = new IncrementalAvaloniaRenderer(new AvaloniaRenderer { Theme = StyledTheme() })
            .Render(AdocParser.Parse(Source).Document);
        var incTitle = Find(incremental, "Doc Title");
        Assert.That(incTitle.Foreground, Is.SameAs(Blue));
        Assert.That(incTitle.FontSize, Is.EqualTo(31), "title size comes from the theme, not a hard-coded 28");
        Assert.That(Find(incremental, "First").Foreground, Is.SameAs(Red));
        Assert.That(Find(incremental, "para").FontFamily, Is.EqualTo(BodyFont));
    }

    [AvaloniaTest] // AvaloniaRenderTheme creates SolidColorBrush defaults (UI-thread bound)
    public void HeadingForeground_resolves_list_then_fallback()
    {
        var theme = new AvaloniaRenderTheme { HeadingForegrounds = new IBrush?[] { Red, null } };
        Assert.That(theme.HeadingForeground(1), Is.SameAs(Red));
        Assert.That(theme.HeadingForeground(2), Is.Null);
        Assert.That(theme.HeadingForeground(0), Is.Null);

        theme.FallbackHeadingForeground = Blue;
        Assert.That(theme.HeadingForeground(2), Is.SameAs(Blue));
        Assert.That(theme.HeadingForeground(0), Is.SameAs(Blue));
        Assert.That(theme.HeadingForeground(9), Is.SameAs(Blue));
    }
}
