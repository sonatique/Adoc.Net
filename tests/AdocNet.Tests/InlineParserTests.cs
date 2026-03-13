using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class InlineParserTests
{
    // ── Direct InlineParser tests ────────────────────────────────────────────────

    [Test]
    public void Plain_text_only()
    {
        var inlines = InlineParser.Parse("Hello world");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("Hello world"));
    }

    [Test]
    public void Empty_string_produces_no_nodes()
    {
        Assert.That(InlineParser.Parse(""), Is.Empty);
    }

    [Test]
    public void Emphasis_only()
    {
        var inlines = InlineParser.Parse("_italic_");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<EmphasisInlineNode>());
        Assert.That(((EmphasisInlineNode)inlines[0]).Content, Is.EqualTo("italic"));
    }

    [Test]
    public void Strong_only()
    {
        var inlines = InlineParser.Parse("*bold*");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<StrongInlineNode>());
        Assert.That(((StrongInlineNode)inlines[0]).Content, Is.EqualTo("bold"));
    }

    [Test]
    public void Monospace_only()
    {
        var inlines = InlineParser.Parse("`code`");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<MonospaceInlineNode>());
        Assert.That(((MonospaceInlineNode)inlines[0]).Content, Is.EqualTo("code"));
    }

    [Test]
    public void Bare_https_url_becomes_link()
    {
        var inlines = InlineParser.Parse("https://example.com");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<LinkInlineNode>());
        Assert.That(((LinkInlineNode)inlines[0]).Url, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void Bare_http_url_becomes_link()
    {
        var inlines = InlineParser.Parse("http://example.com");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.InstanceOf<LinkInlineNode>());
        Assert.That(((LinkInlineNode)inlines[0]).Url, Is.EqualTo("http://example.com"));
    }

    [Test]
    public void Url_with_surrounding_text()
    {
        var inlines = InlineParser.Parse("Visit https://example.com now.");

        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("Visit "));
        Assert.That(((LinkInlineNode)inlines[1]).Url, Is.EqualTo("https://example.com"));
        Assert.That(((TextInlineNode)inlines[2]).Value, Is.EqualTo(" now."));
    }

    [Test]
    public void Trailing_punctuation_stripped_from_url_and_preserved_as_text()
    {
        // Common case: URL at end of sentence followed by a period.
        // The period is stripped from the URL but preserved as plain text.
        var inlines = InlineParser.Parse("https://example.com.");

        Assert.That(inlines, Has.Count.EqualTo(2));
        Assert.That(((LinkInlineNode)inlines[0]).Url, Is.EqualTo("https://example.com"));
        Assert.That(((TextInlineNode)inlines[1]).Value, Is.EqualTo("."));
    }

    [Test]
    public void Mixed_inline_content_in_one_paragraph()
    {
        // "Mix *bold*, _italic_, and `code` in one paragraph."
        // Nodes: Text("Mix ") Strong("bold") Text(", ") Emphasis("italic") Text(", and ") Monospace("code") Text(" in one paragraph.")
        var inlines = InlineParser.Parse("Mix *bold*, _italic_, and `code` in one paragraph.");

        Assert.That(inlines, Has.Count.EqualTo(7));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(inlines[1], Is.InstanceOf<StrongInlineNode>());
        Assert.That(((StrongInlineNode)inlines[1]).Content, Is.EqualTo("bold"));
        Assert.That(inlines[2], Is.InstanceOf<TextInlineNode>());
        Assert.That(inlines[3], Is.InstanceOf<EmphasisInlineNode>());
        Assert.That(((EmphasisInlineNode)inlines[3]).Content, Is.EqualTo("italic"));
        Assert.That(inlines[4], Is.InstanceOf<TextInlineNode>());
        Assert.That(inlines[5], Is.InstanceOf<MonospaceInlineNode>());
        Assert.That(((MonospaceInlineNode)inlines[5]).Content, Is.EqualTo("code"));
        Assert.That(inlines[6], Is.InstanceOf<TextInlineNode>());
    }

    [Test]
    public void Unmatched_star_stays_plain_text()
    {
        var inlines = InlineParser.Parse("hello *world");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello *world"));
    }

    [Test]
    public void Unmatched_underscore_stays_plain_text()
    {
        var inlines = InlineParser.Parse("hello _world");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello _world"));
    }

    [Test]
    public void Unmatched_backtick_stays_plain_text()
    {
        var inlines = InlineParser.Parse("hello `world");

        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(((TextInlineNode)inlines[0]).Value, Is.EqualTo("hello `world"));
    }

    [Test]
    public void All_kinds_represented_correctly()
    {
        var inlines = InlineParser.Parse("*bold* _italic_ `code` https://x.com plain");

        Assert.Multiple(() =>
        {
            Assert.That(inlines.Any(n => n.Kind == AstNodeKind.InlineStrong),    Is.True);
            Assert.That(inlines.Any(n => n.Kind == AstNodeKind.InlineEmphasis),  Is.True);
            Assert.That(inlines.Any(n => n.Kind == AstNodeKind.InlineMonospace), Is.True);
            Assert.That(inlines.Any(n => n.Kind == AstNodeKind.InlineLink),      Is.True);
            Assert.That(inlines.Any(n => n.Kind == AstNodeKind.InlineText),      Is.True);
        });
    }

    [Test]
    public void Inline_nodes_have_no_source_range()
    {
        // Source ranges are not computed for inline nodes in M5.
        var inlines = InlineParser.Parse("*bold*");
        Assert.That(inlines[0].Source.IsNone, Is.True);
    }

    // ── BlockParser integration tests ────────────────────────────────────────────

    [Test]
    public void Section_title_gets_inline_parsing()
    {
        var result = BlockParser.Parse("== Use *git status*");

        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.TitleInlines, Has.Count.EqualTo(2));
        Assert.That(section.TitleInlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(((TextInlineNode)section.TitleInlines[0]).Value, Is.EqualTo("Use "));
        Assert.That(section.TitleInlines[1], Is.InstanceOf<StrongInlineNode>());
        Assert.That(((StrongInlineNode)section.TitleInlines[1]).Content, Is.EqualTo("git status"));
    }

    [Test]
    public void Section_title_raw_string_still_accessible()
    {
        var result = BlockParser.Parse("== Use *git status*");
        var section = (SectionNode)result.Document.Children[0];

        // Title must still work as a plain string (backward compat).
        Assert.That(section.Title, Is.EqualTo("Use *git status*"));
    }

    [Test]
    public void List_item_gets_inline_parsing()
    {
        var result = BlockParser.Parse("* item with _italic_");

        var list  = (ListNode)result.Document.Children[0];
        var item  = (ListItemNode)list.Children[0];
        Assert.That(item.Inlines, Has.Count.EqualTo(2));
        Assert.That(item.Inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(item.Inlines[1], Is.InstanceOf<EmphasisInlineNode>());
        Assert.That(((EmphasisInlineNode)item.Inlines[1]).Content, Is.EqualTo("italic"));
    }

    [Test]
    public void List_item_raw_text_still_accessible()
    {
        var result = BlockParser.Parse("* item with _italic_");
        var list   = (ListNode)result.Document.Children[0];
        var item   = (ListItemNode)list.Children[0];

        Assert.That(item.Text, Is.EqualTo("item with _italic_"));
    }

    [Test]
    public void Paragraph_in_example_block_gets_inline_parsing()
    {
        var result = BlockParser.Parse("====\nParagraph with *bold*.\n====");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        var para  = (ParagraphNode)block.Children[0];
        Assert.That(para.Inlines, Has.Count.EqualTo(3));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(para.Inlines[1], Is.InstanceOf<StrongInlineNode>());
        Assert.That(para.Inlines[2], Is.InstanceOf<TextInlineNode>());
    }

    [Test]
    public void Listing_block_content_is_not_inline_parsed()
    {
        var result = BlockParser.Parse("----\n*not bold*\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
        Assert.That(block.Content, Is.EqualTo("*not bold*"));
        // No inline children: raw verbatim content only.
        Assert.That(block.Children, Is.Empty);
    }

    [Test]
    public void Source_block_content_is_not_inline_parsed()
    {
        var result = BlockParser.Parse("[source]\n----\n*not bold*\n----");

        var block = (DelimitedBlockNode)result.Document.Children[0];
        Assert.That(block.Content, Is.EqualTo("*not bold*"));
        Assert.That(block.Children, Is.Empty);
    }

    [Test]
    public void Paragraph_inlines_appear_in_pretty_printer_output()
    {
        var result = BlockParser.Parse("Hello *world*.");
        var output = AstPrettyPrinter.Print(result.Document);

        // Inline nodes should appear as children of the paragraph.
        Assert.That(output, Does.Contain("InlineText"));
        Assert.That(output, Does.Contain("InlineStrong"));
        Assert.That(output, Does.Contain("Value=\"Hello \""));
        Assert.That(output, Does.Contain("Value=\"world\""));
    }

    [Test]
    public void Section_title_inlines_appear_in_pretty_printer_output()
    {
        var result = BlockParser.Parse("== Hello *world*");
        var output = AstPrettyPrinter.Print(result.Document);

        Assert.That(output, Does.Contain("InlineText"));
        Assert.That(output, Does.Contain("InlineStrong"));
    }

    // ── Unconstrained monospace ────────────────────────────────────────────────

    [Test]
    public void Unconstrained_monospace_double_backtick()
    {
        var inlines = InlineParser.Parse("``text``");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<MonospaceInlineNode>());
    }

    [Test]
    public void Unconstrained_monospace_mid_word()
    {
        var inlines = InlineParser.Parse("some``code``thing");
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(inlines[0], Is.TypeOf<TextInlineNode>());
        Assert.That(inlines[1], Is.TypeOf<MonospaceInlineNode>());
        Assert.That(inlines[2], Is.TypeOf<TextInlineNode>());
    }

    [Test]
    public void Single_backtick_still_works_as_constrained()
    {
        var inlines = InlineParser.Parse("`code`");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<MonospaceInlineNode>());
    }

    // ── Inter-document cross-references ─────────────────────────────────────────

    [Test]
    public void Xref_macro_with_path_and_id()
    {
        var inlines = InlineParser.Parse("See xref:other.adoc#section[the section].");
        var xref = inlines.OfType<InterDocumentXrefNode>().FirstOrDefault();
        Assert.That(xref, Is.Not.Null);
        Assert.That(xref!.Path, Is.EqualTo("other.adoc"));
        Assert.That(xref.Id, Is.EqualTo("section"));
        Assert.That(xref.Label, Is.EqualTo("the section"));
    }

    [Test]
    public void Xref_macro_without_fragment()
    {
        var inlines = InlineParser.Parse("See xref:other.adoc[other doc].");
        var xref = inlines.OfType<InterDocumentXrefNode>().FirstOrDefault();
        Assert.That(xref, Is.Not.Null);
        Assert.That(xref!.Path, Is.EqualTo("other.adoc"));
        Assert.That(xref.Id, Is.Null);
        Assert.That(xref.Label, Is.EqualTo("other doc"));
    }

    [Test]
    public void Double_angle_bracket_inter_document_xref()
    {
        var inlines = InlineParser.Parse("See <<other.adoc#section,the label>>.");
        var xref = inlines.OfType<InterDocumentXrefNode>().FirstOrDefault();
        Assert.That(xref, Is.Not.Null);
        Assert.That(xref!.Path, Is.EqualTo("other.adoc"));
        Assert.That(xref.Id, Is.EqualTo("section"));
        Assert.That(xref.Label, Is.EqualTo("the label"));
    }

    [Test]
    public void Double_angle_bracket_inter_document_xref_no_label()
    {
        var inlines = InlineParser.Parse("<<subdir/file.adoc#id>>");
        var xref = inlines.OfType<InterDocumentXrefNode>().FirstOrDefault();
        Assert.That(xref, Is.Not.Null);
        Assert.That(xref!.Path, Is.EqualTo("subdir/file.adoc"));
        Assert.That(xref.Id, Is.EqualTo("id"));
        Assert.That(xref.Label, Is.Null);
    }

    // ── Inline anchors ──────────────────────────────────────────────────────────

    [Test]
    public void Inline_anchor_parsed()
    {
        var inlines = InlineParser.Parse("before [[myid]] after");
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(inlines[1], Is.TypeOf<InlineAnchorNode>());
        Assert.That(((InlineAnchorNode)inlines[1]).Id, Is.EqualTo("myid"));
    }

    [Test]
    public void Inline_anchor_at_start_of_text()
    {
        var inlines = InlineParser.Parse("[[anchor]]text");
        Assert.That(inlines[0], Is.TypeOf<InlineAnchorNode>());
    }

    // ── Highlight / mark ─────────────────────────────────────────────────────

    [Test]
    public void Constrained_highlight_parsed()
    {
        var inlines = InlineParser.Parse("This is #highlighted# text");
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(inlines[1], Is.TypeOf<HighlightInlineNode>());
    }

    [Test]
    public void Unconstrained_highlight_mid_word()
    {
        var inlines = InlineParser.Parse("un##mark##ed");
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(inlines[1], Is.TypeOf<HighlightInlineNode>());
    }

    [Test]
    public void Highlight_with_nested_formatting()
    {
        var inlines = InlineParser.Parse("#*bold highlight*#");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<HighlightInlineNode>());
        var highlight = (HighlightInlineNode)inlines[0];
        Assert.That(highlight.Children[0], Is.TypeOf<StrongInlineNode>());
    }

    // ── Custom span roles: [.role]#text# ──────────────────────────────────────

    [Test]
    public void Custom_span_role_single()
    {
        var inlines = InlineParser.Parse("[.underline]#text#");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<HighlightInlineNode>());
        var node = (HighlightInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "underline" }));
        Assert.That(node.Content, Is.EqualTo("text"));
    }

    [Test]
    public void Custom_span_role_multiple()
    {
        var inlines = InlineParser.Parse("[.big.red]#text#");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<HighlightInlineNode>());
        var node = (HighlightInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "big", "red" }));
        Assert.That(node.Content, Is.EqualTo("text"));
    }

    [Test]
    public void Custom_span_role_unconstrained()
    {
        var inlines = InlineParser.Parse("[.highlight]##some text##");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<HighlightInlineNode>());
        var node = (HighlightInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "highlight" }));
        Assert.That(node.Content, Is.EqualTo("some text"));
    }

    [Test]
    public void Highlight_without_roles_has_null_roles()
    {
        var inlines = InlineParser.Parse("#text#");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<HighlightInlineNode>());
        var node = (HighlightInlineNode)inlines[0];
        Assert.That(node.Roles, Is.Null);
    }

    // ── Index terms ──────────────────────────────────────────────────────────

    [Test]
    public void Visible_index_term_parsed()
    {
        var inlines = InlineParser.Parse("This is a ((term)) in text");
        Assert.That(inlines, Has.Count.EqualTo(3));
        Assert.That(inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(inlines[1], Is.InstanceOf<IndexTermNode>());
        Assert.That(inlines[2], Is.InstanceOf<TextInlineNode>());
        var idx = (IndexTermNode)inlines[1];
        Assert.That(idx.Terms, Has.Count.EqualTo(1));
        Assert.That(idx.Terms[0], Is.EqualTo("term"));
    }

    [Test]
    public void Hidden_index_term_parsed()
    {
        var inlines = InlineParser.Parse("This has a (((hidden,term))) marker");
        Assert.That(inlines.OfType<IndexTermHiddenNode>().Count(), Is.EqualTo(1));
        var idx = inlines.OfType<IndexTermHiddenNode>().Single();
        Assert.That(idx.Terms, Has.Count.EqualTo(2));
        Assert.That(idx.Terms[0], Is.EqualTo("hidden"));
        Assert.That(idx.Terms[1], Is.EqualTo("term"));
    }

    [Test]
    public void Visible_index_term_renders_text()
    {
        var result = BlockParser.Parse("This is a ((term)) in text");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("This is a term in text"));
    }

    [Test]
    public void Hidden_index_term_renders_nothing()
    {
        var result = BlockParser.Parse("This has a (((hidden,term))) marker");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("This has a  marker"));
    }

    [Test]
    public void Visible_index_term_with_multiple_terms()
    {
        var inlines = InlineParser.Parse("((primary, secondary, tertiary))");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var idx = (IndexTermNode)inlines[0];
        Assert.That(idx.Terms, Has.Count.EqualTo(3));
        Assert.That(idx.Terms[0], Is.EqualTo("primary"));
        Assert.That(idx.Terms[1], Is.EqualTo("secondary"));
        Assert.That(idx.Terms[2], Is.EqualTo("tertiary"));
    }

    [Test]
    public void Hidden_index_term_with_three_terms()
    {
        var inlines = InlineParser.Parse("(((a, b, c)))");
        var idx = inlines.OfType<IndexTermHiddenNode>().Single();
        Assert.That(idx.Terms, Has.Count.EqualTo(3));
        Assert.That(idx.Terms[0], Is.EqualTo("a"));
        Assert.That(idx.Terms[1], Is.EqualTo("b"));
        Assert.That(idx.Terms[2], Is.EqualTo("c"));
    }

    // ── Custom roles on bold/italic/mono: [.role]*text*, [.role]_text_, [.role]`text` ──

    [Test]
    public void Role_on_strong()
    {
        var inlines = InlineParser.Parse("[.big]*bold*");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<StrongInlineNode>());
        var node = (StrongInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "big" }));
        Assert.That(node.Content, Is.EqualTo("bold"));
    }

    [Test]
    public void Role_on_emphasis()
    {
        var inlines = InlineParser.Parse("[.italic-custom]_text_");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<EmphasisInlineNode>());
        var node = (EmphasisInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "italic-custom" }));
        Assert.That(node.Content, Is.EqualTo("text"));
    }

    [Test]
    public void Role_on_monospace()
    {
        var inlines = InlineParser.Parse("[.code-highlight]`code`");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<MonospaceInlineNode>());
        var node = (MonospaceInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "code-highlight" }));
        Assert.That(node.Content, Is.EqualTo("code"));
    }

    [Test]
    public void Multiple_roles_on_strong()
    {
        var inlines = InlineParser.Parse("[.a.b]*text*");
        Assert.That(inlines, Has.Count.EqualTo(1));
        Assert.That(inlines[0], Is.TypeOf<StrongInlineNode>());
        var node = (StrongInlineNode)inlines[0];
        Assert.That(node.Roles, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Strong_without_roles_has_null_roles()
    {
        var inlines = InlineParser.Parse("*text*");
        Assert.That(inlines, Has.Count.EqualTo(1));
        var node = (StrongInlineNode)inlines[0];
        Assert.That(node.Roles, Is.Null);
    }

    // ── Inline counter expansion ──────────────────────────────────────────

    [Test]
    public void Counter_increments_three_times()
    {
        var attrs = new Dictionary<string, string>();
        var result1 = InlineParser.ExpandAttributes("{counter:num}", attrs);
        var result2 = InlineParser.ExpandAttributes("{counter:num}", attrs);
        var result3 = InlineParser.ExpandAttributes("{counter:num}", attrs);
        Assert.That(result1, Is.EqualTo("1"));
        Assert.That(result2, Is.EqualTo("2"));
        Assert.That(result3, Is.EqualTo("3"));
    }

    [Test]
    public void Counter2_increments_silently()
    {
        var attrs = new Dictionary<string, string>();
        var result = InlineParser.ExpandAttributes("{counter2:hidden}", attrs);
        Assert.That(result, Is.EqualTo(""));
        Assert.That(attrs["hidden"], Is.EqualTo("1"));
    }

    [Test]
    public void Counter_with_letter_seed()
    {
        var attrs = new Dictionary<string, string>();
        var r1 = InlineParser.ExpandAttributes("{counter:letter:a}", attrs);
        var r2 = InlineParser.ExpandAttributes("{counter:letter}", attrs);
        var r3 = InlineParser.ExpandAttributes("{counter:letter}", attrs);
        Assert.That(r1, Is.EqualTo("a"));
        Assert.That(r2, Is.EqualTo("b"));
        Assert.That(r3, Is.EqualTo("c"));
    }

    // ── Index generation (index::[]) ────────────────────────────────────

    [Test]
    public void Index_macro_produces_IndexNode()
    {
        var result = BlockParser.Parse("((apple))\n\n(((banana)))\n\nindex::[]");
        var indexNode = result.Document.Children.OfType<IndexNode>().FirstOrDefault();
        Assert.That(indexNode, Is.Not.Null);
    }

    [Test]
    public void Index_macro_collects_visible_and_hidden_terms()
    {
        var adoc = "This has ((apple)) and ((cherry)).\n\n(((banana)))\n\nindex::[]";
        var result = BlockParser.Parse(adoc);
        var indexNode = result.Document.Children.OfType<IndexNode>().Single();

        Assert.That(indexNode.Entries, Has.Count.EqualTo(3));
        // Sorted alphabetically
        Assert.That(indexNode.Entries[0].Term, Is.EqualTo("apple"));
        Assert.That(indexNode.Entries[1].Term, Is.EqualTo("banana"));
        Assert.That(indexNode.Entries[2].Term, Is.EqualTo("cherry"));
    }

    [Test]
    public void Index_macro_deduplicates_terms()
    {
        var adoc = "((apple)) and ((apple)) again.\n\nindex::[]";
        var result = BlockParser.Parse(adoc);
        var indexNode = result.Document.Children.OfType<IndexNode>().Single();

        Assert.That(indexNode.Entries, Has.Count.EqualTo(1));
        Assert.That(indexNode.Entries[0].Term, Is.EqualTo("apple"));
    }

    [Test]
    public void Index_renders_grouped_html()
    {
        var adoc = "((apple)) and ((cherry)).\n\n(((banana)))\n\nindex::[]";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<div class=\"index\">"));
        Assert.That(html, Does.Contain("<h3>A</h3>"));
        Assert.That(html, Does.Contain("<li>apple</li>"));
        Assert.That(html, Does.Contain("<h3>B</h3>"));
        Assert.That(html, Does.Contain("<li>banana</li>"));
        Assert.That(html, Does.Contain("<h3>C</h3>"));
        Assert.That(html, Does.Contain("<li>cherry</li>"));
        Assert.That(html, Does.Contain("</div>"));
    }

    [Test]
    public void Index_entry_with_subterms()
    {
        var adoc = "(((fruit, apple, red)))\n\nindex::[]";
        var result = BlockParser.Parse(adoc);
        var indexNode = result.Document.Children.OfType<IndexNode>().Single();

        Assert.That(indexNode.Entries, Has.Count.EqualTo(1));
        Assert.That(indexNode.Entries[0].Term, Is.EqualTo("fruit"));
        Assert.That(indexNode.Entries[0].SubTerms, Has.Count.EqualTo(2));
        Assert.That(indexNode.Entries[0].SubTerms[0], Is.EqualTo("apple"));
        Assert.That(indexNode.Entries[0].SubTerms[1], Is.EqualTo("red"));
    }
}
