using AdocNet;

namespace AdocNet.Tests;

[TestFixture]
public class SourceRangeTests
{
    [Test]
    public void None_range_is_none()
    {
        Assert.That(SourceRange.None.IsNone, Is.True);
    }

    [Test]
    public void Contains_returns_true_for_position_within_range()
    {
        var range = new SourceRange(new(1, 1), new(3, 10));

        Assert.Multiple(() =>
        {
            Assert.That(range.Contains(new(1, 1)), Is.True);   // start
            Assert.That(range.Contains(new(2, 5)), Is.True);   // middle
            Assert.That(range.Contains(new(3, 10)), Is.True);  // end
        });
    }

    [Test]
    public void Contains_returns_false_for_position_outside_range()
    {
        var range = new SourceRange(new(2, 1), new(2, 10));

        Assert.Multiple(() =>
        {
            Assert.That(range.Contains(new(1, 5)), Is.False);
            Assert.That(range.Contains(new(2, 11)), Is.False);
            Assert.That(range.Contains(new(3, 1)), Is.False);
        });
    }

    [Test]
    public void None_range_contains_nothing()
    {
        Assert.That(SourceRange.None.Contains(new(1, 1)), Is.False);
    }

    [Test]
    public void ToString_formats_start_dash_end()
    {
        var range = new SourceRange(new(1, 1), new(5, 20));
        Assert.That(range.ToString(), Is.EqualTo("1:1-5:20"));
    }

    [Test]
    public void ToString_none_shows_none()
    {
        Assert.That(SourceRange.None.ToString(), Is.EqualTo("(none)"));
    }
}
