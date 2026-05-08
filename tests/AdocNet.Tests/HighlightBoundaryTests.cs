using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class HighlightBoundaryTests
{
    // ── Regression: existing #text# highlight cases must keep working ──────────

    [Test]
    public void Word_boundaries_around_highlight_still_match()
    {
        var inlines = InlineParser.Parse("This is #marked# text");
        var mark = inlines.OfType<HighlightInlineNode>().Single();
        Assert.That(mark.Children, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)mark.Children[0]).Value, Is.EqualTo("marked"));
    }

    [Test]
    public void Highlight_at_start_of_string_works()
    {
        var inlines = InlineParser.Parse("#marked# text");
        Assert.That(inlines.OfType<HighlightInlineNode>().Single(), Is.Not.Null);
    }

    [Test]
    public void Highlight_followed_by_punctuation_works()
    {
        var inlines = InlineParser.Parse("It is #marked#.");
        Assert.That(inlines.OfType<HighlightInlineNode>().Single(), Is.Not.Null);
    }

    [Test]
    public void Unconstrained_highlight_inside_word_still_works()
    {
        var inlines = InlineParser.Parse("un##mark##ed");
        Assert.That(inlines.OfType<HighlightInlineNode>().Single(), Is.Not.Null);
    }

    // ── Bug: highlight should NOT match across non-boundary positions ──────────

    [Test]
    public void Hash_after_word_char_does_not_open_highlight()
    {
        // Asciidoctor: # preceded by a word character cannot open a constrained highlight.
        var inlines = InlineParser.Parse("User#name#methods");
        Assert.That(inlines.OfType<HighlightInlineNode>(), Is.Empty,
            "constrained # preceded by 'r' (word char) must NOT open highlight");
    }

    [Test]
    public void Hash_inside_macro_target_does_not_open_highlight()
    {
        // Real spring-security-auth case: javadoc macro contains #methodName(...) in the target.
        var inlines = InlineParser.Parse("See javadoc:org.foo.User#withMethod()[User#withMethod] for details.");
        Assert.That(inlines.OfType<HighlightInlineNode>(), Is.Empty);
    }

    [Test]
    public void Hash_followed_by_whitespace_does_not_close_highlight()
    {
        // # at start could open, but next # is followed by 'b' (word char) and preceded by ' '
        // — not a valid close. Without a valid close, the opening # stays literal.
        var inlines = InlineParser.Parse("a # b # c");
        Assert.That(inlines.OfType<HighlightInlineNode>(), Is.Empty);
    }

    [Test]
    public void Hash_between_word_chars_no_match()
    {
        var inlines = InlineParser.Parse("foo#bar#baz");
        Assert.That(inlines.OfType<HighlightInlineNode>(), Is.Empty);
    }
}
