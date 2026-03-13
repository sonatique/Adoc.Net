using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ReplacementsProcessorTests
{
    [Test] public void Copyright_symbol() => Assert.That(ReplacementsProcessor.Apply("(C)"), Is.EqualTo("\u00A9"));
    [Test] public void Trademark_symbol() => Assert.That(ReplacementsProcessor.Apply("(TM)"), Is.EqualTo("\u2122"));
    [Test] public void Registered_symbol() => Assert.That(ReplacementsProcessor.Apply("(R)"), Is.EqualTo("\u00AE"));
    [Test] public void Right_arrow() => Assert.That(ReplacementsProcessor.Apply("->"), Is.EqualTo("\u2192"));
    [Test] public void Left_arrow() => Assert.That(ReplacementsProcessor.Apply("<-"), Is.EqualTo("\u2190"));
    [Test] public void Double_right_arrow() => Assert.That(ReplacementsProcessor.Apply("=>"), Is.EqualTo("\u21D2"));
    [Test] public void Double_left_arrow() => Assert.That(ReplacementsProcessor.Apply("<="), Is.EqualTo("\u21D0"));
    [Test] public void Decimal_character_entity() => Assert.That(ReplacementsProcessor.Apply("&#169;"), Is.EqualTo("\u00A9"));
    [Test] public void Hex_character_entity() => Assert.That(ReplacementsProcessor.Apply("&#xa0;"), Is.EqualTo("\u00A0"));
    [Test] public void Named_entity_amp() => Assert.That(ReplacementsProcessor.Apply("&amp;"), Is.EqualTo("&"));
    [Test] public void Named_entity_lt() => Assert.That(ReplacementsProcessor.Apply("&lt;"), Is.EqualTo("<"));
    [Test] public void Named_entity_gt() => Assert.That(ReplacementsProcessor.Apply("&gt;"), Is.EqualTo(">"));
    [Test] public void Named_entity_nbsp() => Assert.That(ReplacementsProcessor.Apply("&nbsp;"), Is.EqualTo("\u00A0"));
    [Test] public void Named_entity_quot() => Assert.That(ReplacementsProcessor.Apply("&quot;"), Is.EqualTo("\""));
    [Test] public void No_trigger_chars_returns_original_instance()
    {
        var input = "hello world";
        Assert.That(ReplacementsProcessor.Apply(input), Is.SameAs(input));
    }
    [Test] public void Mixed_replacements_in_sentence() => Assert.That(
        ReplacementsProcessor.Apply("Copyright (C) 2026 -> All rights reserved (TM)"),
        Is.EqualTo("Copyright \u00A9 2026 \u2192 All rights reserved \u2122"));
    [Test] public void Empty_string_returns_original() => Assert.That(ReplacementsProcessor.Apply(""), Is.SameAs(string.Empty));

    // Edge cases from code review
    [Test] public void Surrogate_code_point_left_as_literal() => Assert.That(ReplacementsProcessor.Apply("&#xD800;"), Is.EqualTo("&#xD800;"));
    [Test] public void Unterminated_entity_left_as_literal() => Assert.That(ReplacementsProcessor.Apply("&amp"), Is.EqualTo("&amp"));
    [Test] public void Empty_numeric_entity_left_as_literal() => Assert.That(ReplacementsProcessor.Apply("&#;"), Is.EqualTo("&#;"));
    [Test] public void Invalid_hex_entity_left_as_literal() => Assert.That(ReplacementsProcessor.Apply("&#xZZZ;"), Is.EqualTo("&#xZZZ;"));
    [Test] public void Entity_at_end_of_string() => Assert.That(ReplacementsProcessor.Apply("text&amp;"), Is.EqualTo("text&"));
    [Test] public void Lowercase_copyright_not_replaced() => Assert.That(ReplacementsProcessor.Apply("(c)"), Is.EqualTo("(c)"));
    [Test] public void Lowercase_trademark_not_replaced() => Assert.That(ReplacementsProcessor.Apply("(tm)"), Is.EqualTo("(tm)"));
}
