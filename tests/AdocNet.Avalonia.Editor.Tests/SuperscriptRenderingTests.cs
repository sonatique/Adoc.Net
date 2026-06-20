using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless.NUnit;
using global::Avalonia.Media;
using AdocNet.Avalonia;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Rendering tests for issue #71: the Avalonia preview must draw superscript and
/// subscript runs (incl. footnote markers) smaller and shifted off the baseline,
/// and render the footnote marker as a clickable link — not as plain inline text.
/// </summary>
[TestFixture]
public class SuperscriptRenderingTests
{
    private static StackPanel Render(string adoc, AvaloniaRenderer renderer)
    {
        var layout = new AdocNet.Layout.Builders.LayoutBuilder().Build(AdocParser.Parse(adoc).Document);
        return (StackPanel)renderer.Render(layout);
    }

    private static TextBlock FirstParagraph(StackPanel panel) =>
        panel.Children.OfType<TextBlock>().First();

    [AvaloniaTest]
    public void Superscript_renders_as_a_raised_smaller_span()
    {
        var theme = new AvaloniaRenderTheme();
        var panel = Render("E=mc^2^.", new AvaloniaRenderer { Theme = theme, WrapInScrollViewer = false });

        var span = FirstParagraph(panel).Inlines!.OfType<Span>()
            .First(s => s.BaselineAlignment == BaselineAlignment.Superscript);
        Assert.That(span.FontSize, Is.EqualTo(theme.SubSuperscriptFontSize));
        Assert.That(span.FontSize, Is.LessThan(theme.BodyFontSize), "superscript should be smaller than body");
    }

    [AvaloniaTest]
    public void Subscript_renders_as_a_lowered_smaller_span()
    {
        var theme = new AvaloniaRenderTheme();
        var panel = Render("H~2~O.", new AvaloniaRenderer { Theme = theme, WrapInScrollViewer = false });

        var span = FirstParagraph(panel).Inlines!.OfType<Span>()
            .First(s => s.BaselineAlignment == BaselineAlignment.Subscript);
        Assert.That(span.FontSize, Is.EqualTo(theme.SubSuperscriptFontSize));
        Assert.That(span.FontSize, Is.LessThan(theme.BodyFontSize), "subscript should be smaller than body");
    }

    [AvaloniaTest]
    public void Footnote_marker_renders_as_a_superscript_clickable_link()
    {
        var panel = Render("x footnote:[note body].", new AvaloniaRenderer { WrapInScrollViewer = false });

        // The marker is a superscript span ...
        var span = FirstParagraph(panel).Inlines!.OfType<Span>()
            .First(s => s.BaselineAlignment == BaselineAlignment.Superscript);

        // ... wrapping a clickable link (rendered as an InlineUIContainer hosting a TextBlock).
        var container = span.Inlines.OfType<InlineUIContainer>().First();
        var linkText = (TextBlock)container.Child!;
        var run = linkText.Inlines!.OfType<Run>().First();
        Assert.That(run.Text, Does.Contain("[1]"), "the marker text is the [n] reference");
    }
}
