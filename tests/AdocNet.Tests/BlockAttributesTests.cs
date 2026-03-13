using AdocNet;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class BlockAttributesTests
{
    [Test] public void Non_bracket_line_returns_null() => Assert.That(BlockAttributes.Parse("hello world"), Is.Null);

    [Test] public void Empty_brackets_returns_empty_attributes()
    {
        var attrs = BlockAttributes.Parse("[]");
        Assert.That(attrs, Is.Not.Null);
        Assert.That(attrs!.Positional, Is.Empty);
        Assert.That(attrs.Named, Is.Empty);
    }

    [Test] public void Single_positional_becomes_style()
    {
        var attrs = BlockAttributes.Parse("[source]");
        Assert.That(attrs!.Style, Is.EqualTo("source"));
        Assert.That(attrs.Positional, Has.Count.EqualTo(1));
    }

    [Test] public void Two_positional_values()
    {
        var attrs = BlockAttributes.Parse("[source,java]");
        Assert.That(attrs!.Style, Is.EqualTo("source"));
        Assert.That(attrs.Positional[1], Is.EqualTo("java"));
    }

    [Test] public void Three_positional_values_for_quote()
    {
        var attrs = BlockAttributes.Parse("[quote, Author Name, Book Title]");
        Assert.That(attrs!.Style, Is.EqualTo("quote"));
        Assert.That(attrs.Positional[1], Is.EqualTo("Author Name"));
        Assert.That(attrs.Positional[2], Is.EqualTo("Book Title"));
    }

    [Test] public void Comma_prefix_shorthand()
    {
        var attrs = BlockAttributes.Parse("[,java]");
        Assert.That(attrs!.Style, Is.Null.Or.EqualTo(""));
        Assert.That(attrs.Positional.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(attrs.Positional[1], Is.EqualTo("java"));
    }

    [Test] public void Named_attribute_with_double_quotes()
    {
        var attrs = BlockAttributes.Parse("[role=\"primary\"]");
        Assert.That(attrs!.Named["role"], Is.EqualTo("primary"));
    }

    [Test] public void Named_attribute_with_single_quotes()
    {
        var attrs = BlockAttributes.Parse("[role='primary']");
        Assert.That(attrs!.Named["role"], Is.EqualTo("primary"));
    }

    [Test] public void Named_attribute_bare_value()
    {
        var attrs = BlockAttributes.Parse("[width=80]");
        Assert.That(attrs!.Named["width"], Is.EqualTo("80"));
    }

    [Test] public void Multiple_named_attributes()
    {
        var attrs = BlockAttributes.Parse("[cols=\"1,2,3\", options=\"header\"]");
        Assert.That(attrs!.Named["cols"], Is.EqualTo("1,2,3"));
        Assert.That(attrs!.Named["options"], Is.EqualTo("header"));
    }

    [Test] public void Id_shorthand()
    {
        var attrs = BlockAttributes.Parse("[#myid]");
        Assert.That(attrs!.Id, Is.EqualTo("myid"));
    }

    [Test] public void Role_shorthand()
    {
        var attrs = BlockAttributes.Parse("[.myrole]");
        Assert.That(attrs!.Roles, Contains.Item("myrole"));
    }

    [Test] public void Option_shorthand()
    {
        var attrs = BlockAttributes.Parse("[%header]");
        Assert.That(attrs!.Options, Contains.Item("header"));
    }

    [Test] public void Combined_shorthands()
    {
        var attrs = BlockAttributes.Parse("[#myid.role1.role2%autowidth]");
        Assert.That(attrs!.Id, Is.EqualTo("myid"));
        Assert.That(attrs!.Roles, Is.EqualTo(new[] { "role1", "role2" }));
        Assert.That(attrs!.Options, Contains.Item("autowidth"));
    }

    [Test] public void Mixed_positional_and_named()
    {
        var attrs = BlockAttributes.Parse("[source,java,role=\"primary\",subs=\"attributes\"]");
        Assert.That(attrs!.Style, Is.EqualTo("source"));
        Assert.That(attrs.Positional[1], Is.EqualTo("java"));
        Assert.That(attrs.Named["role"], Is.EqualTo("primary"));
        Assert.That(attrs.Named["subs"], Is.EqualTo("attributes"));
    }

    [Test] public void Options_named_attribute_populates_options_list()
    {
        var attrs = BlockAttributes.Parse("[options=\"header,footer\"]");
        Assert.That(attrs!.Options, Contains.Item("header"));
        Assert.That(attrs!.Options, Contains.Item("footer"));
    }

    [Test] public void Opts_is_alias_for_options()
    {
        var attrs = BlockAttributes.Parse("[opts=\"header\"]");
        Assert.That(attrs!.Options, Contains.Item("header"));
    }

    [Test] public void Role_named_attribute_populates_roles_list()
    {
        var attrs = BlockAttributes.Parse("[role=\"primary secondary\"]");
        Assert.That(attrs!.Roles, Is.EqualTo(new[] { "primary", "secondary" }));
    }

    [Test] public void Subs_named_attribute_parses_substitution_kind()
    {
        var attrs = BlockAttributes.Parse("[subs=\"attributes\"]");
        Assert.That(attrs!.Subs, Is.EqualTo(SubstitutionKind.Attributes));
    }

    [Test] public void Subs_normal_preset() { Assert.That(BlockAttributes.Parse("[subs=\"normal\"]")!.Subs, Is.EqualTo(SubstitutionKind.Normal)); }
    [Test] public void Subs_none_preset() { Assert.That(BlockAttributes.Parse("[subs=\"none\"]")!.Subs, Is.EqualTo(SubstitutionKind.None)); }
    [Test] public void Subs_verbatim_preset() { Assert.That(BlockAttributes.Parse("[subs=\"verbatim\"]")!.Subs, Is.EqualTo(SubstitutionKind.Verbatim)); }

    [Test] public void Subs_multiple_phases()
    {
        var attrs = BlockAttributes.Parse("[subs=\"specialcharacters,attributes\"]");
        Assert.That(attrs!.Subs, Is.EqualTo(SubstitutionKind.SpecialCharacters | SubstitutionKind.Attributes));
    }

    [Test] public void Quoted_value_with_commas_preserved()
    {
        var attrs = BlockAttributes.Parse("[cols=\"1,2,3\"]");
        Assert.That(attrs!.Named["cols"], Is.EqualTo("1,2,3"));
    }

    [Test] public void Whitespace_trimmed_from_positional()
    {
        var attrs = BlockAttributes.Parse("[ source , java ]");
        Assert.That(attrs!.Style, Is.EqualTo("source"));
        Assert.That(attrs.Positional[1], Is.EqualTo("java"));
    }

    [Test] public void Admonition_types_as_style()
    {
        foreach (var type in new[] { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" })
        {
            var attrs = BlockAttributes.Parse($"[{type}]");
            Assert.That(attrs!.Style, Is.EqualTo(type));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Incremental subs modifiers (+name / -name)
    // ══════════════════════════════════════════════════════════════════════════

    [Test] public void Subs_plus_prefix_is_incremental()
    {
        var attrs = BlockAttributes.Parse("[subs=\"+attributes\"]");
        Assert.That(attrs!.SubsIsIncremental, Is.True);
        Assert.That(attrs.SubsToAdd, Is.EqualTo(SubstitutionKind.Attributes));
        Assert.That(attrs.SubsToRemove, Is.EqualTo(SubstitutionKind.None));
    }

    [Test] public void Subs_minus_prefix_is_incremental()
    {
        var attrs = BlockAttributes.Parse("[subs=\"-specialcharacters\"]");
        Assert.That(attrs!.SubsIsIncremental, Is.True);
        Assert.That(attrs.SubsToRemove, Is.EqualTo(SubstitutionKind.SpecialCharacters));
        Assert.That(attrs.SubsToAdd, Is.EqualTo(SubstitutionKind.None));
    }

    [Test] public void Subs_mixed_incremental()
    {
        var attrs = BlockAttributes.Parse("[subs=\"+attributes,-specialcharacters\"]");
        Assert.That(attrs!.SubsIsIncremental, Is.True);
        Assert.That(attrs.SubsToAdd, Is.EqualTo(SubstitutionKind.Attributes));
        Assert.That(attrs.SubsToRemove, Is.EqualTo(SubstitutionKind.SpecialCharacters));
    }

    [Test] public void Subs_absolute_is_not_incremental()
    {
        var attrs = BlockAttributes.Parse("[subs=\"attributes\"]");
        Assert.That(attrs!.SubsIsIncremental, Is.False);
        Assert.That(attrs.Subs, Is.EqualTo(SubstitutionKind.Attributes));
    }
}
