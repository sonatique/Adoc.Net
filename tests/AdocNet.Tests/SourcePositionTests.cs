using AdocNet;

namespace AdocNet.Tests;

[TestFixture]
public class SourcePositionTests
{
    [Test]
    public void None_has_zero_line_and_column()
    {
        var pos = SourcePosition.None;
        Assert.Multiple(() =>
        {
            Assert.That(pos.Line, Is.EqualTo(0));
            Assert.That(pos.Column, Is.EqualTo(0));
            Assert.That(pos.IsNone, Is.True);
        });
    }

    [Test]
    public void Valid_position_is_not_none()
    {
        var pos = new SourcePosition(1, 1);
        Assert.That(pos.IsNone, Is.False);
    }

    [Test]
    public void Comparison_orders_by_line_then_column()
    {
        var a = new SourcePosition(1, 5);
        var b = new SourcePosition(2, 1);
        var c = new SourcePosition(1, 10);

        Assert.Multiple(() =>
        {
            Assert.That(a < b, Is.True);
            Assert.That(a < c, Is.True);
            Assert.That(b > c, Is.True);
        });
    }

    [Test]
    public void Equal_positions_compare_as_equal()
    {
        var a = new SourcePosition(3, 7);
        var b = new SourcePosition(3, 7);

        Assert.Multiple(() =>
        {
            Assert.That(a <= b, Is.True);
            Assert.That(a >= b, Is.True);
            Assert.That(a == b, Is.True);
        });
    }

    [Test]
    public void ToString_formats_line_colon_column()
    {
        Assert.That(new SourcePosition(5, 12).ToString(), Is.EqualTo("5:12"));
    }

    [Test]
    public void ToString_none_shows_none()
    {
        Assert.That(SourcePosition.None.ToString(), Is.EqualTo("(none)"));
    }
}
