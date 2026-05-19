using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Phase-2 coverage: inline nodes coming out of <c>InlineParser</c> must
/// carry a populated <see cref="SourceRange"/>. The ranges are slice-relative
/// (line 1, col 1 at the start of the inline-parser input). Promotion to
/// document coordinates is the block parser's responsibility for the call
/// sites that need it.
///
/// This first cut targets <see cref="TextInlineNode"/> only — the most common
/// inline kind and the one most often hit by caret-to-AST queries. Strong /
/// emphasis / monospace / link / footnote / etc. follow in subsequent commits
/// as the InlineParser's other node-creation sites get instrumented.
/// </summary>
[TestFixture]
public class InlineSourceRangeTests
{
    [Test]
    public void Plain_text_inline_has_a_source_range()
    {
        var inlines = InlineParser.Parse("Hello world.");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var text = (TextInlineNode)inlines[0];
        Assert.That(text.Source.IsNone, Is.False, "TextInlineNode must carry a source range");
        Assert.That(text.Source.Start, Is.EqualTo(new SourcePosition(1, 1)));
    }

    [Test]
    public void Plain_text_around_strong_keeps_its_range()
    {
        var inlines = InlineParser.Parse("foo *bold* bar");
        Assert.That(inlines.Count, Is.GreaterThanOrEqualTo(2));

        var first = inlines.OfType<TextInlineNode>().First();
        Assert.That(first.Source.IsNone, Is.False);
        Assert.That(first.Value, Does.Contain("foo"));
        Assert.That(first.Source.Start, Is.EqualTo(new SourcePosition(1, 1)));
    }

    [Test]
    public void Multi_line_inline_text_advances_line_in_position()
    {
        var inlines = InlineParser.Parse("first line\nsecond line");
        var text = (TextInlineNode)inlines[0];
        Assert.That(text.Source.IsNone, Is.False);
        Assert.That(text.Source.Start.Line, Is.EqualTo(1));
        // End position should be on line 2.
        Assert.That(text.Source.End.Line, Is.EqualTo(2));
    }

    // ── Formatting inlines ────────────────────────────────────────────────

    [Test]
    public void Strong_inline_has_source_range()
    {
        var strong = InlineParser.Parse("*bold*").OfType<StrongInlineNode>().First();
        Assert.That(strong.Source.IsNone, Is.False);
        Assert.That(strong.Source.Start.Column, Is.EqualTo(1));
    }

    [Test]
    public void Emphasis_inline_has_source_range()
    {
        var em = InlineParser.Parse("_italic_").OfType<EmphasisInlineNode>().First();
        Assert.That(em.Source.IsNone, Is.False);
    }

    [Test]
    public void Monospace_inline_has_source_range()
    {
        var mono = InlineParser.Parse("`code`").OfType<MonospaceInlineNode>().First();
        Assert.That(mono.Source.IsNone, Is.False);
    }

    [Test]
    public void Highlight_inline_has_source_range()
    {
        var hl = InlineParser.Parse("##mark##").OfType<HighlightInlineNode>().First();
        Assert.That(hl.Source.IsNone, Is.False);
    }

    [Test]
    public void Subscript_and_superscript_have_source_ranges()
    {
        var inlines = InlineParser.Parse("H~2~O ^th^");
        var sub = inlines.OfType<SubscriptInlineNode>().First();
        var sup = inlines.OfType<SuperscriptInlineNode>().First();
        Assert.That(sub.Source.IsNone, Is.False);
        Assert.That(sup.Source.IsNone, Is.False);
    }

    // ── Links and macros ──────────────────────────────────────────────────

    [Test]
    public void Bare_url_has_source_range()
    {
        var link = InlineParser.Parse("Visit https://example.com today").OfType<LinkInlineNode>().First();
        Assert.That(link.Source.IsNone, Is.False);
    }

    [Test]
    public void Url_with_label_has_source_range()
    {
        var link = InlineParser.Parse("See https://example.com[Example]").OfType<InlineLinkMacroNode>().First();
        Assert.That(link.Source.IsNone, Is.False);
    }

    [Test]
    public void Footnote_macro_has_source_range()
    {
        var fn = InlineParser.Parse("Text footnote:[a note] more").OfType<FootnoteInlineNode>().First();
        Assert.That(fn.Source.IsNone, Is.False);
    }

    [Test]
    public void Cross_reference_has_source_range()
    {
        var xref = InlineParser.Parse("See <<my-section>> for more").OfType<CrossReferenceInlineNode>().First();
        Assert.That(xref.Source.IsNone, Is.False);
    }

    [Test]
    public void Inline_anchor_has_source_range()
    {
        var anchor = InlineParser.Parse("[[my-anchor]]Anchored text").OfType<InlineAnchorNode>().First();
        Assert.That(anchor.Source.IsNone, Is.False);
    }

    [Test]
    public void Passthrough_inline_has_source_range()
    {
        var pass = InlineParser.Parse("Raw +++<b>HTML</b>+++ here").OfType<PassthroughInlineNode>().First();
        Assert.That(pass.Source.IsNone, Is.False);
    }
}
