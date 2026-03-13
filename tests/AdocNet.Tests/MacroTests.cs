using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for inline macros (link:, image:) and block macros (image::).
/// Covers parsing, AST generation, HTML rendering, attribute substitution,
/// edge cases, and safe degradation of malformed/unknown macros.
/// </summary>
[TestFixture]
public class MacroTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Part 1: Inline link macro — link:URL[label]
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Link_macro_with_label()
    {
        var inlines = InlineParser.Parse("link:https://example.com[Example]");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var link = (InlineLinkMacroNode)inlines[0];
        Assert.That(link.Url, Is.EqualTo("https://example.com"));
        Assert.That(link.Label, Is.EqualTo("Example"));
    }

    [Test]
    public void Link_macro_with_empty_label_uses_url()
    {
        var inlines = InlineParser.Parse("link:https://example.com[]");
        var link = (InlineLinkMacroNode)inlines[0];
        Assert.That(link.Url, Is.EqualTo("https://example.com"));
        Assert.That(link.Label, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void Link_macro_in_paragraph_context()
    {
        var result = BlockParser.Parse("Visit link:https://example.com[Example] for more.");
        var para = (ParagraphNode)result.Document.Children[0];

        Assert.That(para.Inlines, Has.Count.EqualTo(3));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(para.Inlines[1], Is.InstanceOf<InlineLinkMacroNode>());
        Assert.That(para.Inlines[2], Is.InstanceOf<TextInlineNode>());
    }

    [Test]
    public void Link_macro_renders_to_anchor()
    {
        var result = BlockParser.Parse("link:https://example.com[Click here]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<a href=\"https://example.com\">Click here</a>"));
    }

    [Test]
    public void Link_macro_with_relative_url()
    {
        var inlines = InlineParser.Parse("link:page.html[Page]");
        var link = (InlineLinkMacroNode)inlines[0];
        Assert.That(link.Url, Is.EqualTo("page.html"));
        Assert.That(link.Label, Is.EqualTo("Page"));
    }

    [Test]
    public void Link_macro_in_list_item()
    {
        var result = BlockParser.Parse("* See link:https://example.com[docs]");
        var list = (ListNode)result.Document.Children[0];
        var item = (ListItemNode)list.Children[0];
        Assert.That(item.Inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Link_macro_in_table_cell()
    {
        var result = BlockParser.Parse("|===\n|link:url.html[Link] |text\n|===");
        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        var cell = (TableCellNode)row.Children[0];
        Assert.That(cell.Inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(1));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 2: Inline image macro — image:target[alt]
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Inline_image_macro()
    {
        var inlines = InlineParser.Parse("image:logo.png[Logo]");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var img = (InlineImageNode)inlines[0];
        Assert.That(img.Target, Is.EqualTo("logo.png"));
        Assert.That(img.Alt, Is.EqualTo("Logo"));
    }

    [Test]
    public void Inline_image_with_empty_alt()
    {
        var inlines = InlineParser.Parse("image:photo.jpg[]");
        var img = (InlineImageNode)inlines[0];
        Assert.That(img.Target, Is.EqualTo("photo.jpg"));
        Assert.That(img.Alt, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Inline_image_renders_to_img_tag()
    {
        var result = BlockParser.Parse("See image:icon.png[Icon] here.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<img src=\"icon.png\" alt=\"Icon\">"));
    }

    [Test]
    public void Inline_image_in_paragraph()
    {
        var result = BlockParser.Parse("Before image:x.png[alt] after.");
        var para = (ParagraphNode)result.Document.Children[0];

        Assert.That(para.Inlines, Has.Count.EqualTo(3));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(para.Inlines[1], Is.InstanceOf<InlineImageNode>());
        Assert.That(para.Inlines[2], Is.InstanceOf<TextInlineNode>());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 3: Block image macro — image::target[alt]
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Block_image_macro()
    {
        var result = BlockParser.Parse("image::diagram.png[Architecture]");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var img = (BlockImageNode)result.Document.Children[0];
        Assert.That(img.Target, Is.EqualTo("diagram.png"));
        Assert.That(img.Alt, Is.EqualTo("Architecture"));
    }

    [Test]
    public void Block_image_with_title()
    {
        var result = BlockParser.Parse(".My Diagram\nimage::arch.png[Architecture]");
        var img = (BlockImageNode)result.Document.Children[0];
        Assert.That(img.Title, Is.EqualTo("My Diagram"));
        Assert.That(img.Target, Is.EqualTo("arch.png"));
    }

    [Test]
    public void Block_image_with_empty_alt()
    {
        var result = BlockParser.Parse("image::photo.jpg[]");
        var img = (BlockImageNode)result.Document.Children[0];
        Assert.That(img.Alt, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Block_image_renders_div_wrapper()
    {
        var result = BlockParser.Parse("image::diagram.png[Architecture]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"imageblock\">"));
        Assert.That(html, Does.Contain("<img src=\"diagram.png\" alt=\"Architecture\">"));
        Assert.That(html, Does.Contain("</div>"));
    }

    [Test]
    public void Block_image_with_title_renders_title_div()
    {
        var result = BlockParser.Parse(".Caption\nimage::img.png[Alt]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"title\">Figure 1. Caption</div>"));
    }

    [Test]
    public void Block_image_in_section()
    {
        var result = BlockParser.Parse("= Doc\n\n== Section\n\nimage::pic.png[Pic]");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Children[0], Is.InstanceOf<BlockImageNode>());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 4: Attribute substitution in macro labels
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_expanded_in_link_label()
    {
        var result = BlockParser.Parse("= Doc\n:project: AdocNet\n\nlink:https://example.com[{project}]");
        var para = (ParagraphNode)result.Document.Children[0];
        var link = para.Inlines.OfType<InlineLinkMacroNode>().Single();
        Assert.That(link.Label, Is.EqualTo("AdocNet"));
    }

    [Test]
    public void Attribute_expanded_in_link_target()
    {
        // Attribute expansion happens as a text pre-pass, so it also expands in targets.
        var result = BlockParser.Parse("= Doc\n:site: https://example.com\n\nlink:{site}[Visit]");
        var para = (ParagraphNode)result.Document.Children[0];
        var link = para.Inlines.OfType<InlineLinkMacroNode>().Single();
        Assert.That(link.Url, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void Attribute_expanded_in_image_alt()
    {
        var result = BlockParser.Parse("= Doc\n:alt: My Logo\n\nimage:logo.png[{alt}]");
        var para = (ParagraphNode)result.Document.Children[0];
        var img = para.Inlines.OfType<InlineImageNode>().Single();
        Assert.That(img.Alt, Is.EqualTo("My Logo"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 5: Inline macro vs bare URL priority
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Link_macro_takes_priority_over_bare_url()
    {
        var inlines = InlineParser.Parse("link:https://example.com[Example]");
        // Should produce a link macro, not a bare URL.
        Assert.That(inlines[0], Is.InstanceOf<InlineLinkMacroNode>());
        Assert.That(inlines.OfType<LinkInlineNode>().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Bare_url_still_works_without_link_prefix()
    {
        var inlines = InlineParser.Parse("Visit https://example.com today.");
        Assert.That(inlines.OfType<LinkInlineNode>().Count(), Is.EqualTo(1));
        Assert.That(inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(0));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 6: Inline macro does not fire in double-colon context
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Image_double_colon_is_block_not_inline()
    {
        // image::target[alt] on its own line should be a block macro.
        var result = BlockParser.Parse("image::photo.jpg[Photo]");
        Assert.That(result.Document.Children[0], Is.InstanceOf<BlockImageNode>());
    }

    [Test]
    public void Image_single_colon_in_text_is_inline()
    {
        var result = BlockParser.Parse("See image:photo.jpg[Photo] here.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(para.Inlines.OfType<InlineImageNode>().Count(), Is.EqualTo(1));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 7: Edge cases — safe degradation
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Link_without_brackets_is_plain_text()
    {
        var inlines = InlineParser.Parse("link:target without brackets");
        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Image_without_brackets_is_plain_text()
    {
        var inlines = InlineParser.Parse("image:target without brackets");
        Assert.That(inlines.All(n => n is TextInlineNode), Is.True);
    }

    [Test]
    public void Link_with_space_before_colon_is_plain_text()
    {
        var inlines = InlineParser.Parse("not a link :target[text]");
        Assert.That(inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Unknown_block_macro_is_paragraph_text()
    {
        var result = BlockParser.Parse("unknown::target[content]");
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Malformed_block_image_no_brackets_is_paragraph()
    {
        var result = BlockParser.Parse("image::target without brackets");
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Link_empty_target_is_plain_text()
    {
        // link:[] has no target — should not match.
        var inlines = InlineParser.Parse("link:[]");
        Assert.That(inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Image_empty_target_is_valid()
    {
        // image:[] — empty target is allowed (could be a placeholder).
        var inlines = InlineParser.Parse("image:[]");
        var img = inlines.OfType<InlineImageNode>().SingleOrDefault();
        Assert.That(img, Is.Not.Null);
        Assert.That(img!.Target, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Verbatim_mode_does_not_parse_macros()
    {
        var inlines = InlineParser.Parse("link:url[text]", SubstitutionKind.Verbatim, null);
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
    }

    [Test]
    public void Multiple_inline_macros_in_one_line()
    {
        var inlines = InlineParser.Parse("link:a.html[A] and link:b.html[B]");
        var links = inlines.OfType<InlineLinkMacroNode>().ToList();
        Assert.That(links, Has.Count.EqualTo(2));
        Assert.That(links[0].Url, Is.EqualTo("a.html"));
        Assert.That(links[1].Url, Is.EqualTo("b.html"));
    }

    [Test]
    public void Link_macro_mixed_with_formatting()
    {
        var inlines = InlineParser.Parse("*bold* and link:url[text] end");
        Assert.That(inlines.OfType<StrongInlineNode>().Count(), Is.EqualTo(1));
        Assert.That(inlines.OfType<InlineLinkMacroNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void No_crash_on_unclosed_bracket_in_macro()
    {
        Assert.DoesNotThrow(() => InlineParser.Parse("image:file.png[no close"));
        Assert.DoesNotThrow(() => InlineParser.Parse("link:url[no close"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 8: Deterministic AST and HTML
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Macro_produces_deterministic_ast()
    {
        var input = "link:https://x.com[X] image:y.png[Y]";
        var ast1 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);
        var ast2 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);
        Assert.That(ast1, Is.EqualTo(ast2));
    }

    [Test]
    public void Macro_produces_deterministic_html()
    {
        var input = "link:https://x.com[X] image:y.png[Y]";
        var html1 = new HtmlRenderer().RenderToString(BlockParser.Parse(input).Document);
        var html2 = new HtmlRenderer().RenderToString(BlockParser.Parse(input).Document);
        Assert.That(html1, Is.EqualTo(html2));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 9: Inline icon macro — icon:name[]
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Icon_macro_parsed_as_InlineMacroNode()
    {
        var inlines = InlineParser.Parse("icon:heart[]");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var macro = (InlineMacroNode)inlines[0];
        Assert.That(macro.Name, Is.EqualTo("icon"));
        Assert.That(macro.Target, Is.EqualTo("heart"));
    }

    [Test]
    public void Icon_with_icons_font_renders_i_tag()
    {
        var result = BlockParser.Parse("= Doc\n:icons: font\n\nicon:heart[]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<i class=\"fa fa-heart\"></i>"));
    }

    [Test]
    public void Icon_with_icons_font_and_size_renders_size_class()
    {
        var result = BlockParser.Parse("= Doc\n:icons: font\n\nicon:heart[size=2x]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<i class=\"fa fa-heart fa-2x\"></i>"));
    }

    [Test]
    public void Icon_without_icons_attribute_renders_plain_text()
    {
        var result = BlockParser.Parse("= Doc\n\nicon:heart[]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("[heart]"));
        Assert.That(html, Does.Not.Contain("<i "));
    }

    [Test]
    public void Icon_with_icons_font_and_rotate_renders_rotate_class()
    {
        var result = BlockParser.Parse("= Doc\n:icons: font\n\nicon:shield[rotate=90]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("fa-rotate-90"));
    }

    [Test]
    public void Icon_with_icons_font_and_flip_renders_flip_class()
    {
        var result = BlockParser.Parse("= Doc\n:icons: font\n\nicon:shield[flip=horizontal]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("fa-flip-horizontal"));
    }

    [Test]
    public void Icon_with_icons_image_renders_img_tag()
    {
        var result = BlockParser.Parse("= Doc\n:icons: image\n\nicon:heart[]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<img src=\"./images/icons/heart.png\" alt=\"heart\">"));
    }
}
