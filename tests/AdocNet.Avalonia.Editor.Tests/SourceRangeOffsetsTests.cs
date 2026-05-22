using AdocNet;
using AdocNet.Avalonia.Editor;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

[TestFixture]
public class SourceRangeOffsetsTests
{
    [Test]
    public void Resolve_returns_zero_for_None_range()
    {
        var (start, length) = SourceRangeOffsets.Resolve("hello", SourceRange.None);
        Assert.That(start, Is.EqualTo(0));
        Assert.That(length, Is.EqualTo(0));
    }

    [Test]
    public void Resolve_single_line_range_covers_inclusive_end()
    {
        // Range covers cols 1..5 on line 1 → 5 chars: "hello".
        var range = new SourceRange(new SourcePosition(1, 1), new SourcePosition(1, 5));
        var (start, length) = SourceRangeOffsets.Resolve("hello world", range);
        Assert.That(start, Is.EqualTo(0));
        Assert.That(length, Is.EqualTo(5));
    }

    [Test]
    public void Resolve_mid_line_range()
    {
        // "hello WORLD", cols 7..11 → "WORLD".
        var range = new SourceRange(new SourcePosition(1, 7), new SourcePosition(1, 11));
        var src = "hello WORLD";
        var (start, length) = SourceRangeOffsets.Resolve(src, range);
        Assert.That(src.Substring(start, length), Is.EqualTo("WORLD"));
    }

    [Test]
    public void Resolve_multi_line_range_spans_newline()
    {
        // line 1 col 1 .. line 2 col 5 over "abc\ndefgh":
        //   start  = offset 0 ('a')
        //   inclusive end = offset 8 ('h' on line 2 col 5)
        //   length = end - start + 1 = 9 (all 9 chars including the \n)
        var src = "abc\ndefgh";
        var range = new SourceRange(new SourcePosition(1, 1), new SourcePosition(2, 5));
        var (start, length) = SourceRangeOffsets.Resolve(src, range);
        Assert.That(start, Is.EqualTo(0));
        Assert.That(length, Is.EqualTo(9));
        Assert.That(src.Substring(start, length), Is.EqualTo("abc\ndefgh"));
    }

    [Test]
    public void Resolve_handles_paragraph_block_range_round_trip()
    {
        // End-to-end: parse a doc, find a paragraph, resolve its range,
        // and assert that the substring equals the source slice the
        // parser annotated.
        var src = "first para.\n\nsecond para.\n";
        var doc = AdocParser.Parse(src).Document;
        var paragraphs = doc.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(2));

        var p2 = paragraphs[1];
        var (start, length) = SourceRangeOffsets.Resolve(src, p2.Source);
        Assert.That(length, Is.GreaterThan(0));
        Assert.That(src.Substring(start, length), Does.Contain("second para"));
    }

    [Test]
    public void Resolve_clamps_out_of_bounds_end_to_source_length()
    {
        var src = "short";
        // Deliberately overshoot.
        var range = new SourceRange(new SourcePosition(1, 1), new SourcePosition(99, 99));
        var (start, length) = SourceRangeOffsets.Resolve(src, range);
        Assert.That(start, Is.EqualTo(0));
        Assert.That(start + length, Is.LessThanOrEqualTo(src.Length));
    }
}
