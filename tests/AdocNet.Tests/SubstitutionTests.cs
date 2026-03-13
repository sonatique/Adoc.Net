using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for the explicit substitution model: <see cref="SubstitutionKind"/> flags,
/// context-specific behavior, attribute expansion, and inline edge-case correctness.
/// </summary>
[TestFixture]
public class SubstitutionTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Part 1: SubstitutionKind flag behavior
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Verbatim_returns_text_unchanged()
    {
        var inlines = InlineParser.Parse("*bold* _italic_ `code` https://x.com", SubstitutionKind.Verbatim, null);

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("*bold* _italic_ `code` https://x.com"));
    }

    [Test]
    public void Normal_applies_all_substitutions()
    {
        var inlines = InlineParser.Parse("*bold* https://x.com", SubstitutionKind.Normal, null);

        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
        Assert.That(inlines.OfType<LinkInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void InlineFormatting_only_skips_urls()
    {
        var inlines = InlineParser.Parse("*bold* https://x.com", SubstitutionKind.InlineFormatting, null);

        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
        // URL should be plain text, not a link.
        Assert.That(inlines.OfType<LinkInlineNode>().Count(), Is.EqualTo(0));
        var textNodes = inlines.OfType<TextInlineNode>().ToList();
        Assert.That(textNodes.Any(t => t.Value.Contains("https://x.com")), Is.True);
    }

    [Test]
    public void Macros_only_skips_formatting()
    {
        var inlines = InlineParser.Parse("*bold* https://x.com", SubstitutionKind.Macros, null);

        // *bold* should be plain text, not strong.
        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(0));
        // URL should still be detected.
        Assert.That(inlines.OfType<LinkInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Verbatim_equals_SpecialCharacters()
    {
        Assert.That(SubstitutionKind.Verbatim, Is.EqualTo(SubstitutionKind.SpecialCharacters));
    }

    [Test]
    public void Normal_includes_all_six_phases()
    {
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.SpecialCharacters), Is.True);
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.Quotes), Is.True);
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.Attributes), Is.True);
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.Replacements), Is.True);
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.Macros), Is.True);
        Assert.That(SubstitutionKind.Normal.HasFlag(SubstitutionKind.PostReplacements), Is.True);
    }

    [Test]
    public void Quotes_is_alias_for_InlineFormatting()
    {
        Assert.That((int)SubstitutionKind.Quotes, Is.EqualTo((int)SubstitutionKind.InlineFormatting));
        Assert.That((int)SubstitutionKind.Quotes, Is.EqualTo(1));
    }

    [Test]
    public void SpecialCharacters_has_value_16()
    {
        Assert.That((int)SubstitutionKind.SpecialCharacters, Is.EqualTo(16));
    }

    [Test]
    public void Replacements_has_value_32()
    {
        Assert.That((int)SubstitutionKind.Replacements, Is.EqualTo(32));
    }

    [Test]
    public void Header_excludes_PostReplacements()
    {
        Assert.That(SubstitutionKind.Header.HasFlag(SubstitutionKind.PostReplacements), Is.False);
        Assert.That(SubstitutionKind.Header.HasFlag(SubstitutionKind.SpecialCharacters), Is.True);
        Assert.That(SubstitutionKind.Header.HasFlag(SubstitutionKind.Quotes), Is.True);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 2: Context-specific behavior — normal vs verbatim
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Paragraph_receives_inline_substitutions()
    {
        var result = BlockParser.Parse("Hello *world*.");
        var para = (ParagraphNode)result.Document.Children[0];

        Assert.That(para.Inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Section_title_receives_inline_substitutions()
    {
        var result = BlockParser.Parse("== *Bold* title");
        var section = (SectionNode)result.Document.Children[0];

        Assert.That(section.TitleInlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void List_item_receives_inline_substitutions()
    {
        var result = BlockParser.Parse("* item with _emphasis_");
        var list = (ListNode)result.Document.Children[0];
        var item = (ListItemNode)list.Children[0];

        Assert.That(item.Inlines.OfType<EmphasisInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Table_cell_receives_inline_substitutions()
    {
        var result = BlockParser.Parse("|===\n|*bold* |_italic_\n|===");
        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        var cell0 = (TableCellNode)row.Children[0];
        var cell1 = (TableCellNode)row.Children[1];

        Assert.That(cell0.Inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
        Assert.That(cell1.Inlines.OfType<EmphasisInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Literal_block_does_not_receive_substitutions()
    {
        var result = BlockParser.Parse("....\n*not bold* _not italic_\n....");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Literal));
        Assert.That(block.Content, Is.EqualTo("*not bold* _not italic_"));
        Assert.That(block.Children, Is.Empty);
    }

    [Test]
    public void Listing_block_does_not_receive_substitutions()
    {
        var result = BlockParser.Parse("----\n*not bold* {attr}\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
        Assert.That(block.Content, Is.EqualTo("*not bold* {attr}"));
        Assert.That(block.Children, Is.Empty);
    }

    [Test]
    public void Source_block_does_not_receive_substitutions()
    {
        var result = BlockParser.Parse("[source,csharp]\n----\nvar x = *bold*;\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
        Assert.That(block.Content, Is.EqualTo("var x = *bold*;"));
        Assert.That(block.Children, Is.Empty);
    }

    [Test]
    public void Example_block_paragraphs_receive_normal_substitutions()
    {
        var result = BlockParser.Parse("====\nParagraph with *bold*.\n====");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        var para = (ParagraphNode)block.Children[0];

        Assert.That(para.Inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 3: Edge-case correctness
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Adjacent_strong_and_emphasis_parse_correctly()
    {
        var inlines = InlineParser.Parse("*bold*_italic_");

        Assert.That(inlines, Has.Count.EqualTo(2));
        Assert.That(inlines[0], Is.InstanceOf<StrongInlineNode>());
        Assert.That(inlines[1], Is.InstanceOf<EmphasisInlineNode>());
        Assert.That(((StrongInlineNode)inlines[0]).Content, Is.EqualTo("bold"));
        Assert.That(((EmphasisInlineNode)inlines[1]).Content, Is.EqualTo("italic"));
    }

    [Test]
    public void Adjacent_monospace_and_strong()
    {
        var inlines = InlineParser.Parse("`code`*bold*");

        Assert.That(inlines, Has.Count.EqualTo(2));
        Assert.That(inlines[0], Is.InstanceOf<MonospaceInlineNode>());
        Assert.That(inlines[1], Is.InstanceOf<StrongInlineNode>());
    }

    [Test]
    public void Unmatched_star_degrades_to_plain_text()
    {
        var inlines = InlineParser.Parse("hello * world");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello * world"));
    }

    [Test]
    public void Unmatched_underscore_at_end_degrades_to_plain_text()
    {
        var inlines = InlineParser.Parse("hello_");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello_"));
    }

    [Test]
    public void Empty_strong_markers_degrade_to_plain_text()
    {
        var inlines = InlineParser.Parse("**");

        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Empty_emphasis_markers_degrade_to_plain_text()
    {
        var inlines = InlineParser.Parse("__");

        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Empty_monospace_markers_degrade_to_plain_text()
    {
        var inlines = InlineParser.Parse("``");

        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Whitespace_only_strong_content_is_preserved()
    {
        // `* *` has content=" " between stars — this is valid (matches Asciidoctor behavior).
        var inlines = InlineParser.Parse("* *");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<StrongInlineNode>());
        Assert.That(((StrongInlineNode)inlines[0]).Content, Is.EqualTo(" "));
    }

    [Test]
    public void Url_with_trailing_period_preserves_period_as_text()
    {
        var inlines = InlineParser.Parse("Visit https://example.com.");

        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("Visit "));
        Assert.That(((LinkInlineNode)inlines[1]).Url, Is.EqualTo("https://example.com"));
        Assert.That(((TextInlineNode)inlines[2]).Value, Is.EqualTo("."));
    }

    [Test]
    public void Url_with_trailing_comma_and_text()
    {
        var inlines = InlineParser.Parse("See https://example.com, and more.");

        var link = inlines.OfType<LinkInlineNode>().Single();
        Assert.That(link.Url, Is.EqualTo("https://example.com"));
        // The comma should be in trailing text.
        var lastText = inlines.OfType<TextInlineNode>().Last();
        Assert.That(lastText.Value, Does.StartWith(","));
    }

    [Test]
    public void Url_in_parentheses()
    {
        var inlines = InlineParser.Parse("(https://example.com)");

        // Leading '(' is plain text, URL is detected, trailing ')' is stripped and preserved.
        var link = inlines.OfType<LinkInlineNode>().Single();
        Assert.That(link.Url, Is.EqualTo("https://example.com"));
        var texts = inlines.OfType<TextInlineNode>().ToList();
        Assert.That(texts.Any(t => t.Value.Contains("(")), Is.True);
        Assert.That(texts.Any(t => t.Value.Contains(")")), Is.True);
    }

    [Test]
    public void Multiple_urls_in_one_line()
    {
        var inlines = InlineParser.Parse("https://a.com and https://b.com end.");

        var links = inlines.OfType<LinkInlineNode>().ToList();
        Assert.That(links, Has.Count.EqualTo(2));
        Assert.That(links[0].Url, Is.EqualTo("https://a.com"));
        Assert.That(links[1].Url, Is.EqualTo("https://b.com"));
    }

    [Test]
    public void Substitutions_consistent_across_paragraph_and_title_and_list()
    {
        // Same inline markup should produce the same result regardless of context.
        var text = "*bold* and _italic_";

        var result = BlockParser.Parse($"== {text}\n\n{text}\n\n* {text}");

        var section = (SectionNode)result.Document.Children[0];
        var para = (ParagraphNode)section.Children[0];
        var list = (ListNode)section.Children[1];
        var item = (ListItemNode)list.Children[0];

        // All three contexts should produce identical inline counts.
        Assert.That(section.TitleInlines, Has.Count.EqualTo(para.Inlines.Count));
        Assert.That(para.Inlines, Has.Count.EqualTo(item.Inlines.Count));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 4: Attribute substitution
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_reference_expanded_in_paragraph()
    {
        var result = BlockParser.Parse("= Title\n:project: AdocNet\n\nThe {project} library.");

        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(para.Inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)para.Inlines[0]).Value, Is.EqualTo("The AdocNet library."));
        // Raw text preserves original form.
        Assert.That(para.Text, Is.EqualTo("The {project} library."));
    }

    [Test]
    public void Multiple_attribute_references_in_one_line()
    {
        var result = BlockParser.Parse("= Title\n:a: Alpha\n:b: Bravo\n\n{a} and {b}.");

        var para = (ParagraphNode)result.Document.Children[0];
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("Alpha and Bravo."));
    }

    [Test]
    public void Unknown_attribute_reference_left_as_is()
    {
        var result = BlockParser.Parse("= Title\n\n{unknown} stays.");

        var para = (ParagraphNode)result.Document.Children[0];
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("{unknown} stays."));
    }

    [Test]
    public void Attribute_reference_in_section_title()
    {
        var result = BlockParser.Parse("= Doc\n:tool: Git\n\n== Using {tool}");

        var section = (SectionNode)result.Document.Children[0];
        var text = string.Join("", section.TitleInlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("Using Git"));
        // Raw title preserves original.
        Assert.That(section.Title, Is.EqualTo("Using {tool}"));
    }

    [Test]
    public void Attribute_reference_in_list_item()
    {
        var result = BlockParser.Parse("= Doc\n:item: Widget\n\n* Buy a {item}");

        var list = (ListNode)result.Document.Children[0];
        var item = (ListItemNode)list.Children[0];
        var text = string.Join("", item.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("Buy a Widget"));
    }

    [Test]
    public void Attribute_reference_in_table_cell()
    {
        var result = BlockParser.Parse("= Doc\n:val: X\n\n|===\n|{val} |Y\n|===");

        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        var cell = (TableCellNode)row.Children[0];
        var text = string.Join("", cell.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("X"));
    }

    [Test]
    public void Attribute_not_expanded_in_listing_block()
    {
        var result = BlockParser.Parse("= Doc\n:name: Value\n\n----\n{name}\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{name}"));
    }

    [Test]
    public void Attribute_not_expanded_in_source_block()
    {
        var result = BlockParser.Parse("= Doc\n:name: Value\n\n[source]\n----\n{name}\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{name}"));
    }

    [Test]
    public void Attribute_not_expanded_in_literal_block()
    {
        var result = BlockParser.Parse("= Doc\n:name: Value\n\n....\n{name}\n....");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{name}"));
    }

    [Test]
    public void Attribute_with_hyphen_in_name()
    {
        var result = BlockParser.Parse("= Doc\n:my-attr: hello\n\n{my-attr} world.");

        var para = (ParagraphNode)result.Document.Children[0];
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("hello world."));
    }

    [Test]
    public void Invalid_attribute_name_left_as_is()
    {
        var attrs = new Dictionary<string, string> { ["valid"] = "yes" };
        // Names starting with digit or containing spaces are invalid.
        var expanded = InlineParser.ExpandAttributes("{123} and {valid} and {no space}", attrs);

        Assert.That(expanded, Is.EqualTo("{123} and yes and {no space}"));
    }

    [Test]
    public void Empty_braces_left_as_is()
    {
        var expanded = InlineParser.ExpandAttributes("{} text", new Dictionary<string, string>());

        Assert.That(expanded, Is.EqualTo("{} text"));
    }

    [Test]
    public void Attribute_combined_with_inline_formatting()
    {
        var result = BlockParser.Parse("= Doc\n:tool: Git\n\nUse *{tool}* for versioning.");

        var para = (ParagraphNode)result.Document.Children[0];
        // Should have: Text("Use "), Strong("Git"), Text(" for versioning.")
        Assert.That(para.Inlines.OfType<StrongInlineNode>().Single().Content, Is.EqualTo("Git"));
    }

    [Test]
    public void No_attributes_in_document_leaves_references_as_is()
    {
        var result = BlockParser.Parse("{name} is here.");

        var para = (ParagraphNode)result.Document.Children[0];
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Is.EqualTo("{name} is here."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 5: Deterministic AST and HTML output
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_expansion_produces_deterministic_ast()
    {
        var input = "= Doc\n:v: 1.0\n\nVersion {v}.";

        var ast1 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);
        var ast2 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);

        Assert.That(ast1, Is.EqualTo(ast2));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 6: Replacements phase integration
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Normal_substitutions_apply_replacements()
    {
        var inlines = InlineParser.Parse("Copyright (C) 2026", SubstitutionKind.Normal, null);
        var text = string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u00A9"));
    }

    [Test]
    public void Replacements_only_applies_symbol_replacements()
    {
        var inlines = InlineParser.Parse("(C) and *bold*", SubstitutionKind.Replacements, null);
        var text = string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u00A9"));
        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Without_replacements_flag_no_symbol_replacement()
    {
        var inlines = InlineParser.Parse("(C) and *bold*", SubstitutionKind.Quotes, null);
        var text = string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("(C)"));
        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Arrow_replacement_in_normal_text()
    {
        var inlines = InlineParser.Parse("Go -> there", SubstitutionKind.Normal, null);
        var text = string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u2192"));
    }

    [Test]
    public void Character_entity_in_normal_text()
    {
        var inlines = InlineParser.Parse("non&#xa0;breaking", SubstitutionKind.Normal, null);
        var text = string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u00A0"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 5 (continued): Deterministic AST and HTML output
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_expansion_produces_deterministic_html()
    {
        var input = "= Doc\n:v: 1.0\n\nVersion {v}.";

        var html1 = new HtmlRenderer().RenderToString(BlockParser.Parse(input).Document);
        var html2 = new HtmlRenderer().RenderToString(BlockParser.Parse(input).Document);

        Assert.That(html1, Is.EqualTo(html2));
        Assert.That(html1, Does.Contain("Version 1.0."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 7: Incremental subs modifiers
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Plus_attributes_on_verbatim_block_adds_attribute_expansion()
    {
        var result = BlockParser.Parse("[subs=\"+attributes\"]\n----\nHello {name}\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        // Verbatim default is SpecialCharacters; +attributes adds Attributes
        Assert.That(block.Substitutions, Is.EqualTo(SubstitutionKind.SpecialCharacters | SubstitutionKind.Attributes));
    }

    [Test]
    public void Minus_specialcharacters_on_verbatim_block_removes_escaping()
    {
        var result = BlockParser.Parse("[subs=\"-specialcharacters\"]\n----\n<div>raw</div>\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        // Verbatim default is SpecialCharacters; removing it gives None
        Assert.That(block.Substitutions, Is.EqualTo(SubstitutionKind.None));
    }

    [Test]
    public void Absolute_subs_on_verbatim_block_replaces_defaults()
    {
        var result = BlockParser.Parse("[subs=\"attributes\"]\n----\n{name}\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];

        // Absolute mode: replaces defaults entirely
        Assert.That(block.Substitutions, Is.EqualTo(SubstitutionKind.Attributes));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Custom subs on paragraphs
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Subs_minus_replacements_on_paragraph_keeps_copyright_literal()
    {
        var result = BlockParser.Parse("[subs=\"-replacements\"]\nText with (C) symbol");
        var para = (ParagraphNode)result.Document.Children[0];
        var html = new HtmlRenderer().RenderToString(result.Document);

        // (C) should remain literal, not converted to ©
        Assert.That(html, Does.Contain("(C)"));
        Assert.That(html, Does.Not.Contain("\u00a9"));
    }
}
