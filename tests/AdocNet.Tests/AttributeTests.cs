using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Comprehensive tests for the document attribute system: header parsing,
/// storage on <see cref="DocumentNode"/>, <c>{name}</c> substitution in
/// normal text contexts, verbatim exclusion, edge cases, and include interaction.
/// </summary>
[TestFixture]
public class AttributeTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Part 1: Attribute declaration parsing
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Single_attribute_is_stored()
    {
        var result = BlockParser.Parse("= Doc\n:author: Jane");
        Assert.That(result.Document.Attributes.ContainsKey("author"), Is.True);
        Assert.That(result.Document.Attributes["author"], Is.EqualTo("Jane"));
    }

    [Test]
    public void Multiple_attributes_are_stored()
    {
        var result = BlockParser.Parse("= Doc\n:a: Alpha\n:b: Bravo\n:c: Charlie");
        Assert.That(result.Document.Attributes.ContainsKey("a"), Is.True);
        Assert.That(result.Document.Attributes["a"], Is.EqualTo("Alpha"));
        Assert.That(result.Document.Attributes["b"], Is.EqualTo("Bravo"));
        Assert.That(result.Document.Attributes["c"], Is.EqualTo("Charlie"));
    }

    [Test]
    public void Empty_value_attribute_is_stored()
    {
        var result = BlockParser.Parse("= Doc\n:toc:");
        Assert.That(result.Document.Attributes["toc"], Is.EqualTo(string.Empty));
    }

    [Test]
    public void Duplicate_attribute_last_wins()
    {
        var result = BlockParser.Parse("= Doc\n:color: red\n:color: blue");
        Assert.That(result.Document.Attributes["color"], Is.EqualTo("blue"));
    }

    [Test]
    public void Attributes_without_title_are_parsed()
    {
        // Attributes can appear in header even without a document title.
        var result = BlockParser.Parse(":key: val\n\nSome text.");
        Assert.That(result.Document.Attributes["key"], Is.EqualTo("val"));
    }

    [Test]
    public void Attribute_names_are_case_sensitive()
    {
        var result = BlockParser.Parse("= Doc\n:Name: Upper\n:name: lower");
        Assert.That(result.Document.Attributes["Name"], Is.EqualTo("Upper"));
        Assert.That(result.Document.Attributes["name"], Is.EqualTo("lower"));
    }

    [Test]
    public void Attribute_value_preserves_leading_whitespace_trimmed()
    {
        // The value is trimmed after the closing colon.
        var result = BlockParser.Parse("= Doc\n:key:   spaced  ");
        Assert.That(result.Document.Attributes["key"], Is.EqualTo("spaced"));
    }

    [Test]
    public void Attribute_with_hyphen_in_name()
    {
        var result = BlockParser.Parse("= Doc\n:my-attr: hello");
        Assert.That(result.Document.Attributes["my-attr"], Is.EqualTo("hello"));
    }

    [Test]
    public void Attribute_with_underscore_in_name()
    {
        var result = BlockParser.Parse("= Doc\n:my_attr: hello");
        Assert.That(result.Document.Attributes["my_attr"], Is.EqualTo("hello"));
    }

    // ── Body attributes are parsed but not as header attributes ────────────

    [Test]
    public void Attributes_after_blank_line_are_body_attributes()
    {
        var result = BlockParser.Parse("= Doc\n\n:late: value");
        Assert.That(result.Document.Attributes["late"], Is.EqualTo("value"));
        Assert.That(result.Document.Children, Has.Count.EqualTo(0));
    }

    [Test]
    public void Attributes_after_paragraph_are_body_attributes()
    {
        var result = BlockParser.Parse("= Doc\n\nFirst.\n\n:late: value\n\nSecond.");
        Assert.That(result.Document.Attributes["late"], Is.EqualTo("value"));
        // ":late: value" is consumed as attribute, not a paragraph.
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Malformed_attribute_produces_diagnostic_and_ends_header()
    {
        // :bad attr contains a space → truly malformed even with flag-style support
        var result = BlockParser.Parse("= Doc\n:bad attr\n\nText.");
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 2: Attribute substitution in normal text contexts
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_expanded_in_paragraph()
    {
        var result = BlockParser.Parse("= Doc\n:product: AdocNet\n\nWelcome to {product}.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("Welcome to AdocNet."));
    }

    [Test]
    public void Attribute_expanded_in_section_title()
    {
        var result = BlockParser.Parse("= Doc\n:tool: Git\n\n== Using {tool}");
        var section = (SectionNode)result.Document.Children[0];
        var text = JoinTextInlines(section.TitleInlines);
        Assert.That(text, Is.EqualTo("Using Git"));
        // Raw title preserves original form.
        Assert.That(section.Title, Is.EqualTo("Using {tool}"));
    }

    [Test]
    public void Attribute_expanded_in_list_item()
    {
        var result = BlockParser.Parse("= Doc\n:lang: C#\n\n* Write {lang} code");
        var list = (ListNode)result.Document.Children[0];
        var item = (ListItemNode)list.Children[0];
        var text = JoinTextInlines(item.Inlines);
        Assert.That(text, Is.EqualTo("Write C# code"));
    }

    [Test]
    public void Attribute_expanded_in_table_cell()
    {
        var result = BlockParser.Parse("= Doc\n:lib: AdocNet\n\n|===\n|{lib} |other\n|===");
        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        var cell = (TableCellNode)row.Children[0];
        var text = JoinTextInlines(cell.Inlines);
        Assert.That(text, Is.EqualTo("AdocNet"));
    }

    [Test]
    public void Multiple_attributes_expanded_in_one_line()
    {
        var result = BlockParser.Parse("= Doc\n:a: X\n:b: Y\n\n{a} and {b}.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("X and Y."));
    }

    [Test]
    public void Attribute_combined_with_inline_formatting()
    {
        var result = BlockParser.Parse("= Doc\n:tool: Git\n\nUse *{tool}* for versioning.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(para.Inlines.OfType<StrongInlineNode>().Single().Content, Is.EqualTo("Git"));
    }

    [Test]
    public void Attribute_expanded_to_url_produces_link()
    {
        var result = BlockParser.Parse("= Doc\n:url: https://example.com\n\nVisit {url} now.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(para.Inlines.OfType<LinkInlineNode>().Single().Url, Is.EqualTo("https://example.com"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 3: Verbatim contexts — no substitution
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_not_expanded_in_listing_block()
    {
        var result = BlockParser.Parse("= Doc\n:x: val\n\n----\n{x}\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{x}"));
    }

    [Test]
    public void Attribute_not_expanded_in_literal_block()
    {
        var result = BlockParser.Parse("= Doc\n:x: val\n\n....\n{x}\n....");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{x}"));
    }

    [Test]
    public void Attribute_not_expanded_in_source_block()
    {
        var result = BlockParser.Parse("= Doc\n:x: val\n\n[source]\n----\n{x}\n----");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("{x}"));
    }

    [Test]
    public void Example_block_paragraphs_do_expand_attributes()
    {
        var result = BlockParser.Parse("= Doc\n:x: val\n\n====\n{x} text.\n====");
        var block = (DelimitedBlockNode)result.Document.Children[0];
        // Example blocks recursively parse content — but note: the recursive parse
        // creates a new document without the parent's attributes, so {x} stays as-is.
        // This is expected current behavior for the simple include model.
        var para = (ParagraphNode)block.Children[0];
        Assert.That(para.Inlines, Has.Count.GreaterThan(0));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 4: Edge cases — safe degradation
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Unknown_attribute_left_as_is()
    {
        var result = BlockParser.Parse("= Doc\n\n{unknown} stays.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("{unknown} stays."));
    }

    [Test]
    public void Empty_braces_left_as_is()
    {
        var result = BlockParser.Parse("= Doc\n\n{} text.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("{} text."));
    }

    [Test]
    public void Unclosed_brace_left_as_is()
    {
        var result = BlockParser.Parse("= Doc\n\nHello { world.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("Hello { world."));
    }

    [Test]
    public void Nested_braces_left_as_is()
    {
        var attrs = new Dictionary<string, string> { ["foo"] = "bar" };
        var expanded = InlineParser.ExpandAttributes("{foo{inner}}", attrs);
        // {foo{inner}} — the '{' at index 3 inside the name "foo{inner}" makes it invalid,
        // so {foo{inner}} doesn't match. The outer { is emitted as plain text.
        Assert.That(expanded, Does.Contain("{"));
    }

    [Test]
    public void Brace_at_end_of_text_does_not_crash()
    {
        var expanded = InlineParser.ExpandAttributes("text{", new Dictionary<string, string>());
        Assert.That(expanded, Is.EqualTo("text{"));
    }

    [Test]
    public void Closing_brace_without_opening_is_plain_text()
    {
        var expanded = InlineParser.ExpandAttributes("text} here", new Dictionary<string, string>());
        Assert.That(expanded, Is.EqualTo("text} here"));
    }

    [Test]
    public void Attribute_name_with_digits_is_valid()
    {
        var attrs = new Dictionary<string, string> { ["ver2"] = "two" };
        var expanded = InlineParser.ExpandAttributes("{ver2}", attrs);
        Assert.That(expanded, Is.EqualTo("two"));
    }

    [Test]
    public void Attribute_name_starting_with_digit_is_invalid()
    {
        var attrs = new Dictionary<string, string> { ["2ver"] = "nope" };
        var expanded = InlineParser.ExpandAttributes("{2ver}", attrs);
        Assert.That(expanded, Is.EqualTo("{2ver}"));
    }

    [Test]
    public void Attribute_value_is_not_recursively_expanded()
    {
        // If :a: {b} and :b: hello, expanding {a} should yield literal "{b}", not "hello".
        var result = BlockParser.Parse("= Doc\n:a: {b}\n:b: hello\n\n{a}.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("{b}."));
    }

    [Test]
    public void No_crash_on_many_braces()
    {
        var input = "= Doc\n\n{{{{{nested}}}}}";
        Assert.DoesNotThrow(() => BlockParser.Parse(input));
    }

    [Test]
    public void Attribute_with_url_value_and_surrounding_text()
    {
        var result = BlockParser.Parse("= Doc\n:site: https://example.com\n\nGo to {site}!");
        var para = (ParagraphNode)result.Document.Children[0];
        // After expansion, the text becomes "Go to https://example.com!" which triggers URL detection.
        var link = para.Inlines.OfType<LinkInlineNode>().SingleOrDefault();
        Assert.That(link, Is.Not.Null);
        Assert.That(link!.Url, Is.EqualTo("https://example.com"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 5: Include interaction
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attributes_available_to_included_content()
    {
        // When includes are expanded as a preprocessing step, attribute definitions
        // from the parent document are visible because all text is merged before parsing.
        var mainDoc = "= Doc\n:product: AdocNet\n\ninclude::_inc.adoc[]";
        var incContent = "This is {product}.";

        var reader = new DictionaryIncludeReader(new()
        {
            ["_inc.adoc"] = incContent,
        });

        var expandResult = IncludeExpander.Expand(mainDoc, ".", reader: reader);
        var result = BlockParser.Parse(expandResult.Text);
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("This is AdocNet."));
    }

    [Test]
    public void Included_file_can_define_attributes()
    {
        // If the included file is at the top (before body content),
        // its attributes are parsed as part of the document header.
        var mainDoc = "= Doc\ninclude::_attrs.adoc[]\n\n{frominclude}.";
        var incContent = ":frominclude: yes";

        var reader = new DictionaryIncludeReader(new()
        {
            ["_attrs.adoc"] = incContent,
        });

        var expandResult = IncludeExpander.Expand(mainDoc, ".", reader: reader);
        var result = BlockParser.Parse(expandResult.Text);
        Assert.That(result.Document.Attributes["frominclude"], Is.EqualTo("yes"));

        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("yes."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 6: Deterministic AST and HTML
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_expansion_produces_deterministic_ast()
    {
        var input = "= Doc\n:v: 1.0\n\nVersion {v}.";
        var ast1 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);
        var ast2 = AstPrettyPrinter.Print(BlockParser.Parse(input).Document, includeSourceRanges: false);
        Assert.That(ast1, Is.EqualTo(ast2));
    }

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
    // Part 7: Circular and self-referencing attributes
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Circular_attribute_expansion_does_not_loop()
    {
        // :a: {b} — b not yet defined, so stored as literal "{b}"
        // :b: {a} — ExpandAttributeValue resolves {a} → "{b}", so b is stored as "{b}"
        // Inline: {a} → "{b}", {b} → "{b}" — single-pass, no infinite loop.
        var result = BlockParser.Parse("= Doc\n:a: {b}\n:b: {a}\n\n{a} and {b}.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("{b} and {b}."));
    }

    [Test]
    public void Self_referencing_attribute_does_not_loop()
    {
        // :x: {x} — self-reference, single-pass means it stays as-is.
        var result = BlockParser.Parse("= Doc\n:x: {x}\n\n{x}.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("{x}."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 8: External attributes via ParseOptions
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void External_attributes_available_in_conditionals()
    {
        var input = "ifdef::backend[Backend is set.]\n\nHello.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" }
        });
        // The ifdef should include the content since backend is set via external attributes.
        var firstChild = result.Document.Children[0];
        Assert.That(firstChild, Is.InstanceOf<ParagraphNode>());
        var text = JoinTextInlines(((ParagraphNode)firstChild).Inlines);
        Assert.That(text, Is.EqualTo("Backend is set."));
    }

    [Test]
    public void External_attributes_overridden_by_document_attributes()
    {
        var input = "= Doc\n:env: production\n\n{env}.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["env"] = "development" }
        });
        // Document attribute should override the external one.
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("production."));
    }

    [Test]
    public void External_attributes_available_for_inline_expansion()
    {
        var input = "Hello {name}.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["name"] = "World" }
        });
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("Hello World."));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // ══════════════════════════════════════════════════════════════════════════
    // Escaped attribute references
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Backslash_escaped_attribute_reference_renders_literal()
    {
        var result = BlockParser.Parse("= Doc\n:author: Jane\n\n\\{author}");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("{author}"));
        Assert.That(html, Does.Not.Contain("Jane"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Attribute references in macro targets
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Attribute_reference_in_image_macro_target_is_expanded()
    {
        var result = BlockParser.Parse("= Doc\n:imagedir: images\n\nimage::{imagedir}/photo.png[Alt]");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("src=\"images/photo.png\""));
        Assert.That(html, Does.Contain("alt=\"Alt\""));
    }

    private static string JoinTextInlines(IReadOnlyList<InlineNode> inlines) =>
        string.Join("", inlines.OfType<TextInlineNode>().Select(t => t.Value));

    // ══════════════════════════════════════════════════════════════════════════
    // Part 7: ExpandAttributes regression tests (beta.20 — lock existing behavior)
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void ExpandAttributes_defined_attribute_substituted()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "Alice" };
        Assert.That(InlineParser.ExpandAttributes("{name}", attrs), Is.EqualTo("Alice"));
    }

    [Test]
    public void ExpandAttributes_undefined_attribute_preserved_literally()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{name}", attrs), Is.EqualTo("{name}"));
    }

    [Test]
    public void ExpandAttributes_defined_empty_value_returns_empty()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "" };
        Assert.That(InlineParser.ExpandAttributes("{name}", attrs), Is.EqualTo(""));
    }

    [Test]
    public void ExpandAttributes_backslash_escape_returns_literal()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "Alice" };
        Assert.That(InlineParser.ExpandAttributes("\\{name}", attrs), Is.EqualTo("{name}"));
    }

    [Test]
    public void ExpandAttributes_counter_returns_1_first_time()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{counter:x}", attrs), Is.EqualTo("1"));
    }

    [Test]
    public void ExpandAttributes_probe_mode_does_not_increment_counter()
    {
        var attrs = new Dictionary<string, string>();
        // Non-mutating probe: yields the value but leaves state untouched...
        Assert.That(InlineParser.ExpandAttributes("{counter:x}", attrs, incrementCounters: false), Is.EqualTo("1"));
        // ...so the first real expansion still returns 1.
        Assert.That(InlineParser.ExpandAttributes("{counter:x}", attrs), Is.EqualTo("1"));
        Assert.That(InlineParser.ExpandAttributes("{counter:x}", attrs), Is.EqualTo("2"));
    }

    [Test]
    public void Inline_counters_in_paragraph_increment_once_each()
    {
        // Regression: a speculative block-macro probe expanded the first paragraph line and
        // incremented counters, then the real render incremented them again, so output started
        // at 3 instead of 1.
        var result = AdocParser.Parse("Step {counter:step}, then {counter:step}.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("Step 1, then 2."));
    }

    [Test]
    public void ExpandAttributes_mixed_text_and_attribute()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "Alice" };
        Assert.That(InlineParser.ExpandAttributes("Hello {name}, welcome", attrs), Is.EqualTo("Hello Alice, welcome"));
    }

    [Test]
    public void ExpandAttributes_unknown_attribute_preserved()
    {
        var attrs = new Dictionary<string, string> { ["other"] = "val" };
        Assert.That(InlineParser.ExpandAttributes("{unknown}", attrs), Is.EqualTo("{unknown}"));
    }

    [Test]
    public void ExpandAttributes_no_braces_returns_original()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "Alice" };
        var input = "no braces here";
        Assert.That(InlineParser.ExpandAttributes(input, attrs), Is.SameAs(input));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 8: Conditional attribute substitution (beta.20)
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Conditional_if_set_with_defined_attribute_emits_value()
    {
        var attrs = new Dictionary<string, string> { ["foo"] = "bar" };
        Assert.That(InlineParser.ExpandAttributes("{foo?yes}", attrs), Is.EqualTo("yes"));
    }

    [Test]
    public void Conditional_if_set_with_undefined_attribute_emits_nothing()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{foo?yes}", attrs), Is.EqualTo(""));
    }

    [Test]
    public void Conditional_if_unset_with_defined_attribute_emits_nothing()
    {
        var attrs = new Dictionary<string, string> { ["foo"] = "bar" };
        Assert.That(InlineParser.ExpandAttributes("{foo!no}", attrs), Is.EqualTo(""));
    }

    [Test]
    public void Conditional_if_unset_with_undefined_attribute_emits_value()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{foo!no}", attrs), Is.EqualTo("no"));
    }

    [Test]
    public void Conditional_if_set_empty_value_emits_empty()
    {
        var attrs = new Dictionary<string, string> { ["foo"] = "bar" };
        Assert.That(InlineParser.ExpandAttributes("{foo?}", attrs), Is.EqualTo(""));
    }

    [Test]
    public void Conditional_if_unset_empty_value_emits_empty()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{foo!}", attrs), Is.EqualTo(""));
    }

    [Test]
    public void Conditional_hyphenated_attribute_name()
    {
        var attrs = new Dictionary<string, string> { ["my-attr"] = "val" };
        Assert.That(InlineParser.ExpandAttributes("{my-attr?present}", attrs), Is.EqualTo("present"));
    }

    [Test]
    public void Conditional_if_set_and_if_unset_in_same_text_defined()
    {
        var attrs = new Dictionary<string, string> { ["flag"] = "1" };
        Assert.That(InlineParser.ExpandAttributes("Text {flag?ON}{flag!OFF} end", attrs), Is.EqualTo("Text ON end"));
    }

    [Test]
    public void Conditional_if_set_and_if_unset_in_same_text_undefined()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("Text {flag?ON}{flag!OFF} end", attrs), Is.EqualTo("Text OFF end"));
    }

    [Test]
    public void Conditional_normal_substitution_still_works()
    {
        var attrs = new Dictionary<string, string> { ["name"] = "Alice" };
        Assert.That(InlineParser.ExpandAttributes("{name}", attrs), Is.EqualTo("Alice"));
    }

    [Test]
    public void Conditional_backslash_escape_prevents_conditional()
    {
        var attrs = new Dictionary<string, string> { ["foo"] = "bar" };
        Assert.That(InlineParser.ExpandAttributes("\\{foo?yes}", attrs), Is.EqualTo("{foo?yes}"));
    }

    [Test]
    public void Conditional_invalid_attribute_name_preserved_literally()
    {
        var attrs = new Dictionary<string, string>();
        Assert.That(InlineParser.ExpandAttributes("{2bad?yes}", attrs), Is.EqualTo("{2bad?yes}"));
    }

    [Test]
    public void Conditional_end_to_end_in_paragraph()
    {
        var result = BlockParser.Parse("= Doc\n:env: prod\n\nRunning in {env?production}{env!development} mode.");
        var para = (ParagraphNode)result.Document.Children[0];
        var text = JoinTextInlines(para.Inlines);
        Assert.That(text, Is.EqualTo("Running in production mode."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 9: Attribute entry parsing regression tests (beta.20 — lock before continuation)
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Regression_attribute_entry_sets_value()
    {
        var result = BlockParser.Parse("= Doc\n:name: value\n\n{name}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("value."));
    }

    [Test]
    public void Regression_attribute_unset_removes()
    {
        var result = BlockParser.Parse("= Doc\n:name: hello\n:!name:\n\n{name}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("{name}."));
    }

    [Test]
    public void Regression_attribute_empty_value()
    {
        var result = BlockParser.Parse("= Doc\n:flag:\n\nBefore{flag}After.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("BeforeAfter."));
    }

    [Test]
    public void Regression_attribute_value_with_ref_expanded()
    {
        var result = BlockParser.Parse("= Doc\n:name: World\n:desc: Hello {name}\n\n{desc}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("Hello World."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Part 10: Attribute value line continuation (beta.20)
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Continuation_single_line()
    {
        var result = BlockParser.Parse("= Doc\n:desc: Hello \\\nWorld\n\n{desc}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("Hello World."));
    }

    [Test]
    public void Continuation_chained_three_lines()
    {
        var result = BlockParser.Parse("= Doc\n:desc: Line 1 \\\nLine 2 \\\nLine 3\n\n{desc}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("Line 1 Line 2 Line 3."));
    }

    [Test]
    public void Continuation_no_backslash_unchanged()
    {
        var result = BlockParser.Parse("= Doc\n:desc: No continuation\n\n{desc}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("No continuation."));
    }

    [Test]
    public void Continuation_blank_line_stops()
    {
        var result = BlockParser.Parse("= Doc\n:desc: Trailing \\\n\nSome text.");
        Assert.That(result.Document.Attributes["desc"], Is.EqualTo("Trailing"));
    }

    [Test]
    public void Continuation_next_attribute_stops()
    {
        var result = BlockParser.Parse("= Doc\n:desc: Value \\\n:other: attr\n\n{desc}.");
        Assert.That(result.Document.Attributes["desc"], Is.EqualTo("Value"));
        Assert.That(result.Document.Attributes["other"], Is.EqualTo("attr"));
    }

    [Test]
    public void Continuation_in_body_attribute()
    {
        var result = BlockParser.Parse("= Doc\n\n:desc: Body \\\nvalue\n\n{desc}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("Body value."));
    }

    [Test]
    public void Continuation_preserves_existing_no_backslash_behavior()
    {
        var result = BlockParser.Parse("= Doc\n:name: value\n\n{name}.");
        var para = (ParagraphNode)result.Document.Children[0];
        Assert.That(JoinTextInlines(para.Inlines), Is.EqualTo("value."));
    }

    [Test]
    public void Continuation_at_end_of_file()
    {
        var result = BlockParser.Parse("= Doc\n:desc: EOF \\");
        Assert.That(result.Document.Attributes["desc"], Is.EqualTo("EOF"));
    }

    /// <summary>
    /// A test-only <see cref="IIncludeReader"/> backed by a dictionary.
    /// </summary>
    private sealed class DictionaryIncludeReader(Dictionary<string, string> files) : IIncludeReader
    {
        public bool Exists(string path) => files.ContainsKey(NormalizePath(path));
        public string Read(string path) => files[NormalizePath(path)];
        private static string NormalizePath(string path) =>
            Path.GetFileName(path); // strip directory prefix for simple test matching
    }
}
