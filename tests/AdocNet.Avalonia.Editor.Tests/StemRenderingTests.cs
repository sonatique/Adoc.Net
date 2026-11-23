using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless.NUnit;
using global::Avalonia.Layout;
using AdocNet.Avalonia;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>STEM formulas in the Avalonia preview: drawn by <see cref="AvaloniaRenderer.StemRenderer"/>.</summary>
[TestFixture]
public class StemRenderingTests
{
    /// <summary>Every formula is a 2 em × 1 em box, 0.25 em of it below the baseline.</summary>
    private sealed class BoxRenderer : IStemRenderer
    {
        public StemGraphic? Render(string content, string stemType, bool display) =>
            content == "bad" ? null : new StemGraphic("M0 0L2 0L2 1L0 1Z", 2, 0.75, 0.25);
    }

    private static (StackPanel Panel, Window Window) Show(string source, IStemRenderer? stem)
    {
        var renderer = new AvaloniaRenderer { WrapInScrollViewer = false, StemRenderer = stem };
        var panel = (StackPanel)renderer.Render(new LayoutBuilder().Build(AdocParser.Parse(source).Document));
        var window = new Window { Width = 400, Height = 300, Content = panel };
        window.Show();
        window.UpdateLayout();
        return (panel, window);
    }

    [AvaloniaTest]
    public void Inline_formula_sits_on_the_text_baseline_at_the_text_size()
    {
        var (panel, _) = Show("Energy stem:[E = mc^2] here.", new BoxRenderer());

        var paragraph = panel.Children.OfType<TextBlock>().Single();
        var formula = paragraph.Inlines!.OfType<InlineUIContainer>().Single().Child!;
        double size = paragraph.FontSize;
        var line = paragraph.TextLayout.TextLines[0];
        Assert.Multiple(() =>
        {
            Assert.That(AutomationProperties.GetName(formula), Is.EqualTo("E = mc^2"));
            Assert.That(formula.Bounds.Width, Is.EqualTo(2 * size).Within(0.01));
            Assert.That(formula.Bounds.Height, Is.EqualTo(size).Within(0.01));
            Assert.That(formula.Bounds.Top + 0.75 * size, Is.EqualTo(line.Baseline).Within(0.5), "the formula's baseline is the line's");
        });
    }

    [AvaloniaTest]
    public void Block_formula_is_centred_under_its_title()
    {
        var (panel, _) = Show(".Sum\n[stem]\n++++\nx\n++++\n", new BoxRenderer());

        var block = panel.Children.OfType<StackPanel>().Single();
        Assert.That(((TextBlock)block.Children[0]).Text, Is.EqualTo("Sum"));
        var formula = block.Children[1];
        Assert.That(formula.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Center));
        Assert.That(formula.Bounds.Width, Is.EqualTo(2 * TextElement.GetFontSize(formula)).Within(0.01));
    }

    [AvaloniaTest]
    public void Without_a_renderer_the_source_shows_in_the_monospace_font()
    {
        var (panel, _) = Show("Energy stem:[E = mc^2] here.\n\n[stem]\n++++\nx + y\n++++\n", stem: null);

        var paragraph = panel.Children.OfType<TextBlock>().First();
        var span = paragraph.Inlines!.OfType<Span>().Single();
        Assert.That(((Run)span.Inlines.Single()).Text, Is.EqualTo("E = mc^2"));
        var block = panel.Children.OfType<StackPanel>().Single();
        Assert.That(block.Children.OfType<TextBlock>().Single().Text, Is.EqualTo("x + y"));
    }

    [AvaloniaTest]
    public void A_formula_that_cannot_be_typeset_shows_its_source()
    {
        var (panel, _) = Show("A stem:[bad] one.", new BoxRenderer());

        var paragraph = panel.Children.OfType<TextBlock>().Single();
        Assert.That(paragraph.Inlines!.OfType<InlineUIContainer>(), Is.Empty);
        Assert.That(((Run)paragraph.Inlines!.OfType<Span>().Single().Inlines.Single()).Text, Is.EqualTo("bad"));
    }
}
