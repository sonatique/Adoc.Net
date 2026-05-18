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
}
