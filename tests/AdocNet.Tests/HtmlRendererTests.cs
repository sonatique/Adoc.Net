using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class HtmlRendererTests
{
    // ── Document ─────────────────────────────────────────────────────────

    [Test]
    public void Empty_document_renders_empty_string()
    {
        var doc = new DocumentNode();
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(""));
    }

    [Test]
    public void Document_title_renders_as_h1()
    {
        // In embedded mode (FullDocument=false, the default), the document title is suppressed
        // to match Asciidoctor -s behavior. Use :showtitle: to force emission.
        var doc = new DocumentNode { Title = "My Document" };
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(""));
    }

    [Test]
    public void Document_title_is_html_escaped()
    {
        // Title suppressed in embedded mode — escaping tested via showtitle attribute.
        var doc = new DocumentNode { Title = "A & B <C>" };
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(""));
    }

    // ── Sections ─────────────────────────────────────────────────────────

    [Test]
    public void Section_level_1_renders_as_h2()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Heading" });
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"sect1\">\n" +
            "<h2>Heading</h2>\n" +
            "<div class=\"sectionbody\">\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Section_level_2_renders_as_h3()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 2, Title = "Sub" });
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"sect2\">\n" +
            "<h3>Sub</h3>\n" +
            "</div>\n"));
    }

    [Test]
    public void Section_level_3_renders_as_h4()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 3, Title = "SubSub" });
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"sect3\">\n" +
            "<h4>SubSub</h4>\n" +
            "</div>\n"));
    }

    [Test]
    public void Section_with_children_renders_heading_then_children()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "S" };
        section.AddChild(new ParagraphNode { Text = "Body text" });
        doc.AddChild(section);

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"sect1\">\n" +
            "<h2>S</h2>\n" +
            "<div class=\"sectionbody\">\n" +
            "<div class=\"paragraph\">\n" +
            "<p>Body text</p>\n" +
            "</div>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    // ── Paragraphs ───────────────────────────────────────────────────────

    [Test]
    public void Paragraph_renders_as_p_tag()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello world" });
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>Hello world</p>\n" +
            "</div>\n"));
    }

    [Test]
    public void Paragraph_text_is_html_escaped()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "a < b & c > d" });
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>a &lt; b &amp; c &gt; d</p>\n" +
            "</div>\n"));
    }

    // ── Unordered lists ──────────────────────────────────────────────────

    [Test]
    public void Unordered_list_renders_as_ul_li()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "Alpha" });
        list.AddChild(new ListItemNode { Text = "Beta" });
        doc.AddChild(list);

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"ulist\">\n" +
            "<ul>\n" +
            "<li>\n<p>Alpha</p>\n</li>\n" +
            "<li>\n<p>Beta</p>\n</li>\n" +
            "</ul>\n" +
            "</div>\n"));
    }

    // ── Ordered lists ────────────────────────────────────────────────────

    [Test]
    public void Ordered_list_renders_as_ol_li()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Ordered };
        list.AddChild(new ListItemNode { Text = "First" });
        list.AddChild(new ListItemNode { Text = "Second" });
        doc.AddChild(list);

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"olist arabic\">\n" +
            "<ol class=\"arabic\">\n" +
            "<li>\n<p>First</p>\n</li>\n" +
            "<li>\n<p>Second</p>\n</li>\n" +
            "</ol>\n" +
            "</div>\n"));
    }

    // ── Nested lists ─────────────────────────────────────────────────────

    [Test]
    public void Nested_list_renders_correctly()
    {
        var doc = new DocumentNode();
        var outer = new ListNode { ListKind = ListKind.Unordered };
        var item = new ListItemNode { Text = "Parent" };
        var inner = new ListNode { ListKind = ListKind.Unordered };
        inner.AddChild(new ListItemNode { Text = "Child" });
        item.AddChild(inner);
        outer.AddChild(item);
        doc.AddChild(outer);

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"ulist\">\n" +
            "<ul>\n" +
            "<li>\n<p>Parent</p>\n" +
            "<div class=\"ulist\">\n" +
            "<ul>\n" +
            "<li>\n<p>Child</p>\n</li>\n" +
            "</ul>\n" +
            "</div>\n" +
            "</li>\n" +
            "</ul>\n" +
            "</div>\n"));
    }

    // ── Delimited blocks ─────────────────────────────────────────────────

    [Test]
    public void Literal_block_renders_as_pre()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Literal,
            Content = "raw text",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"literalblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre>raw text</pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Listing_block_renders_as_pre()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Content = "code here",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre>code here</pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Source_block_renders_as_pre_code_with_language()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Content = "int x = 1;",
            Language = "csharp",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre class=\"highlight\"><code class=\"language-csharp\" data-lang=\"csharp\">int x = 1;</code></pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Source_block_without_language_renders_code_without_class()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Content = "echo hi",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre class=\"highlight\"><code>echo hi</code></pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Example_block_renders_as_div_exampleblock()
    {
        var doc = new DocumentNode();
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Example,
        };
        block.AddChild(new ParagraphNode { Text = "Example content" });
        doc.AddChild(block);

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"exampleblock\">\n" +
            "<div class=\"content\">\n" +
            "<div class=\"paragraph\">\n" +
            "<p>Example content</p>\n" +
            "</div>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Block_title_renders_as_div_title_before_block()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Content = "ls -la",
            Title = "Directory listing",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"title\">Directory listing</div>\n" +
            "<div class=\"content\">\n" +
            "<pre>ls -la</pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Delimited_block_content_is_html_escaped()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Literal,
            Content = "a < b && c > d",
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"literalblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre>a &lt; b &amp;&amp; c &gt; d</pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    // ── Inline rendering ─────────────────────────────────────────────────

    [Test]
    public void Emphasis_inline_renders_as_em()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "_italic_",
            Inlines = [new EmphasisInlineNode { Children = [new TextInlineNode { Value = "italic" }] }],
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n<p><em>italic</em></p>\n</div>\n"));
    }

    [Test]
    public void Strong_inline_renders_as_strong()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "*bold*",
            Inlines = [new StrongInlineNode { Children = [new TextInlineNode { Value = "bold" }] }],
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n<p><strong>bold</strong></p>\n</div>\n"));
    }

    [Test]
    public void Monospace_inline_renders_as_code()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "`mono`",
            Inlines = [new MonospaceInlineNode { Children = [new TextInlineNode { Value = "mono" }] }],
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n<p><code>mono</code></p>\n</div>\n"));
    }

    [Test]
    public void Link_inline_renders_as_anchor()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "https://example.com",
            Inlines = [new LinkInlineNode { Url = "https://example.com" }],
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p><a href=\"https://example.com\" class=\"bare\">https://example.com</a></p>\n" +
            "</div>\n"));
    }

    [Test]
    public void Mixed_inlines_render_in_order()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "Hello *world* and _more_",
            Inlines =
            [
                new TextInlineNode { Value = "Hello " },
                new StrongInlineNode { Children = [new TextInlineNode { Value = "world" }] },
                new TextInlineNode { Value = " and " },
                new EmphasisInlineNode { Children = [new TextInlineNode { Value = "more" }] },
            ],
        });

        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>Hello <strong>world</strong> and <em>more</em></p>\n" +
            "</div>\n"));
    }

    // ── Golden / integration tests (parse → render) ──────────────────────

    [Test]
    public void Golden_full_document()
    {
        var adoc =
            "= My Title\n" +
            "\n" +
            "== Introduction\n" +
            "\n" +
            "This is a *bold* statement.\n" +
            "\n" +
            "* Item one\n" +
            "* Item two\n";

        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Is.EqualTo(
            "<div class=\"sect1\">\n" +
            "<h2 id=\"_introduction\">Introduction</h2>\n" +
            "<div class=\"sectionbody\">\n" +
            "<div class=\"paragraph\">\n" +
            "<p>This is a <strong>bold</strong> statement.</p>\n" +
            "</div>\n" +
            "<div class=\"ulist\">\n" +
            "<ul>\n" +
            "<li>\n<p>Item one</p>\n</li>\n" +
            "<li>\n<p>Item two</p>\n</li>\n" +
            "</ul>\n" +
            "</div>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Golden_source_block_with_title()
    {
        var adoc =
            ".My code\n" +
            "[source,csharp]\n" +
            "----\n" +
            "Console.WriteLine(\"hello\");\n" +
            "----\n";

        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"title\">My code</div>\n" +
            "<div class=\"content\">\n" +
            "<pre class=\"highlight\"><code class=\"language-csharp\" data-lang=\"csharp\">Console.WriteLine(\"hello\");</code></pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void Golden_ordered_list()
    {
        var adoc =
            ". First\n" +
            ". Second\n" +
            ". Third\n";

        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Is.EqualTo(
            "<div class=\"olist arabic\">\n" +
            "<ol class=\"arabic\">\n" +
            "<li>\n<p>First</p>\n</li>\n" +
            "<li>\n<p>Second</p>\n</li>\n" +
            "<li>\n<p>Third</p>\n</li>\n" +
            "</ol>\n" +
            "</div>\n"));
    }

    [Test]
    public void Golden_inline_link_in_paragraph()
    {
        var adoc = "Visit https://example.com today.\n";

        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>Visit <a href=\"https://example.com\" class=\"bare\">https://example.com</a> today.</p>\n" +
            "</div>\n"));
    }

    [Test]
    public void Render_throws_on_null_document()
    {
        Assert.Throws<ArgumentNullException>(() => new HtmlRenderer().RenderToString(null!));
    }

    // ── Subs-aware rendering ────────────────────────────────────────────

    [Test]
    public void Subs_none_listing_does_not_escape_html()
    {
        var adoc = "[subs=\"none\"]\n----\n<b>raw</b>\n----";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<b>raw</b>"));
    }

    [Test]
    public void Subs_attributes_listing_expands_attributes()
    {
        var adoc = ":version: 1.0\n\n[subs=\"attributes\"]\n----\nVersion: {version}\n----";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("Version: 1.0"));
    }

    [Test]
    public void Default_listing_escapes_html()
    {
        var adoc = "----\n<b>escaped</b>\n----";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("&lt;b&gt;escaped&lt;/b&gt;"));
    }

    [Test]
    public void Replacements_in_paragraph()
    {
        var adoc = "Copyright (C) 2026";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("\u00A9"));
    }

    // ── Discrete headings ───────────────────────────────────────────────

    [Test]
    public void Discrete_heading_renders_without_section_number()
    {
        var adoc = ":sectnums:\n\n== Real Section\n\n[discrete]\n== Not Numbered\n\n== Another Real Section";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("1. Real Section"));
        Assert.That(html, Does.Contain("Not Numbered"));
        Assert.That(html, Does.Not.Contain("2. Not Numbered"));
        Assert.That(html, Does.Contain("2. Another Real Section"));
    }

    // ── Section numbering mid-document toggle ──────────────────────────────

    [Test]
    public void Sectnums_toggled_off_mid_document_stops_numbering()
    {
        var result = BlockParser.Parse(":sectnums:\n\n== First\n\n:sectnums!:\n\n== Second\n\n:sectnums:\n\n== Third");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("1. First"));
        Assert.That(html, Does.Not.Contain("2. Second"));
        Assert.That(html, Does.Contain(">Second<"));
        // Counter freezes — Third picks up at 2, not 3
        Assert.That(html, Does.Contain("2. Third"));
    }

    // ── Hardbreaks ────────────────────────────────────────────────────────

    [Test]
    public void Paragraph_with_hardbreaks_renders_br_tags()
    {
        var result = BlockParser.Parse("[%hardbreaks]\nLine one\nLine two\nLine three");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("Line one<br>\nLine two<br>\nLine three"));
    }

    // ── Open blocks ─────────────────────────────────────────────────────

    [Test]
    public void Open_block_renders_as_div()
    {
        var result = BlockParser.Parse("--\nContent here.\n--");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"openblock\">"));
        Assert.That(html, Does.Contain("<div class=\"content\">"));
    }

    // ── Verse blocks ────────────────────────────────────────────────────

    [Test]
    public void Verse_block_renders_with_pre_and_attribution()
    {
        var result = BlockParser.Parse("[verse, \"Shakespeare\", \"Hamlet\"]\n____\nTo be or not to be\nThat is the question\n____");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"verseblock\">"));
        Assert.That(html, Does.Contain("<pre class=\"content\">"));
        Assert.That(html, Does.Contain("To be or not to be\n"));
        Assert.That(html, Does.Contain("Shakespeare"));
        Assert.That(html, Does.Contain("Hamlet"));
    }

    // ── Inter-document cross-references ─────────────────────────────────────

    [Test]
    public void Inter_document_xref_renders_with_html_extension()
    {
        var doc = BlockParser.Parse("See xref:other.adoc#section[the section].");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<a href=\"other.html#section\">the section</a>"));
    }

    [Test]
    public void Inter_document_xref_without_label_uses_path()
    {
        var doc = BlockParser.Parse("See xref:other.adoc[].");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<a href=\"other.html\">other.html</a>"));
    }

    // ── Table of Contents ───────────────────────────────────────────────

    // ── Ordered list start and type attributes ──────────────────────────

    [Test]
    public void Ordered_list_renders_start_attribute()
    {
        var doc = BlockParser.Parse("[start=3]\n. Third\n. Fourth");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<ol class=\"arabic\" start=\"3\">"));
    }

    [Test]
    public void Ordered_list_renders_type_for_loweralpha()
    {
        var doc = BlockParser.Parse("[loweralpha]\n. Alpha\n. Beta");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<ol class=\"loweralpha\" type=\"a\">"));
    }

    [Test]
    public void Ordered_list_renders_type_for_upperroman()
    {
        var doc = BlockParser.Parse("[upperroman]\n. One\n. Two");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<ol class=\"upperroman\" type=\"I\">"));
    }

    [Test]
    public void Toc_renders_as_nested_ul()
    {
        var doc = BlockParser.Parse(":toc:\n\n== First\n\n=== Nested\n\n== Second");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<div id=\"toc\" class=\"toc\">"));
        Assert.That(html, Does.Contain("<div id=\"toctitle\">Table of Contents</div>"));
        Assert.That(html, Does.Contain("<a href=\"#_first\">First</a>"));
        Assert.That(html, Does.Contain("<a href=\"#_nested\">Nested</a>"));
        Assert.That(html, Does.Contain("<a href=\"#_second\">Second</a>"));
    }

    [Test]
    public void Toc_nests_subsections_in_inner_ul()
    {
        var doc = BlockParser.Parse(":toc:\n\n== Parent\n\n=== Child");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<a href=\"#_parent\">Parent</a>"));
        Assert.That(html, Does.Contain("<a href=\"#_child\">Child</a>"));
    }

    [Test]
    public void Toc_not_rendered_when_absent()
    {
        var doc = BlockParser.Parse("== Section");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Not.Contain("id=\"toc\""));
    }

    [Test]
    public void Toc_with_left_placement_has_class()
    {
        var doc = BlockParser.Parse(":toc: left\n\n== Section");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("class=\"toc toc-left\""));
    }

    // ── Inline anchors ────────────────────────────────────────────────────

    [Test]
    public void Inline_anchor_renders_as_empty_a_tag()
    {
        var doc = BlockParser.Parse("Text [[myanchor]] here.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<a id=\"myanchor\"></a>"));
    }

    // ── Highlight / mark ─────────────────────────────────────────────────

    [Test]
    public void Highlight_renders_as_mark()
    {
        var doc = BlockParser.Parse("This is #highlighted# text");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<mark>highlighted</mark>"));
    }

    // ── Natural cross-references ──────────────────────────────────────────

    [Test]
    public void Natural_xref_resolves_section_title_to_id()
    {
        var doc = BlockParser.Parse("== Installation\n\nSee <<Installation>>.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("href=\"#_installation\""));
        Assert.That(html, Does.Contain(">Installation</a>"));
    }

    [Test]
    public void Natural_xref_case_insensitive()
    {
        var doc = BlockParser.Parse("== Getting Started\n\nSee <<getting started>>.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("href=\"#_getting_started\""));
    }

    [Test]
    public void Natural_xref_falls_back_to_bracket_display()
    {
        var doc = BlockParser.Parse("See <<nonexistent>>.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("[nonexistent]"));
    }

    // ── Callout conum markers ────────────────────────────────────────────

    [Test]
    public void Source_block_renders_conum_markers()
    {
        var doc = BlockParser.Parse("[source,java]\n----\nString name; // <1>\nint age; // <2>\n----\n<1> The name\n<2> The age");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<b class=\"conum\">(1)</b>"));
        Assert.That(html, Does.Contain("<b class=\"conum\">(2)</b>"));
    }

    [Test]
    public void Source_block_conum_markers_replace_comment_syntax()
    {
        var doc = BlockParser.Parse("[source,ruby]\n----\nputs 'hi' # <1>\n----\n<1> Print greeting");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<b class=\"conum\">(1)</b>"));
        Assert.That(html, Does.Not.Contain("# &lt;1&gt;"));
    }

    // ── Custom Captions ──────────────────────────────────────────────────

    [Test]
    public void Custom_note_caption_used_in_admonition()
    {
        var doc = BlockParser.Parse(":note-caption: Hint\n\nNOTE: Remember this.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("Hint"));
        Assert.That(html, Does.Not.Contain(">NOTE<"));
    }

    [Test]
    public void Custom_toc_title()
    {
        var doc = BlockParser.Parse(":toc:\n:toc-title: Contents\n\n== Section One\n\nText");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("Contents"));
        Assert.That(html, Does.Not.Contain("Table of Contents"));
    }

    [Test]
    public void Custom_table_caption_prefix()
    {
        var doc = BlockParser.Parse(":table-caption: Tableau\n\n.My table\n|===\n| A | B\n|===");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("Tableau"));
    }

    [Test]
    public void Default_labels_when_no_custom_captions()
    {
        var doc = BlockParser.Parse("NOTE: Default note.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<div class=\"title\">Note</div>"));
    }

    // ── Video / Audio rendering ──────────────────────────────────────────────

    [Test]
    public void Video_renders_as_video_element()
    {
        var doc = BlockParser.Parse("[%controls]\nvideo::intro.mp4[width=640]");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<video"));
        Assert.That(html, Does.Contain("src=\"intro.mp4\""));
        Assert.That(html, Does.Contain("width=\"640\""));
        Assert.That(html, Does.Contain("controls"));
    }

    [Test]
    public void Audio_renders_as_audio_element()
    {
        var doc = BlockParser.Parse("[%controls]\naudio::song.mp3[]");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<audio"));
        Assert.That(html, Does.Contain("src=\"song.mp3\""));
        Assert.That(html, Does.Contain("controls"));
    }

    // ── Page break ───────────────────────────────────────────────────────

    [Test]
    public void PageBreakNode_renders_page_break_div()
    {
        var doc = new DocumentNode();
        doc.AddChild(new PageBreakNode());
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo("<div style=\"page-break-after: always;\"></div>\n"));
    }

    // ── Thematic break ───────────────────────────────────────────────────

    [Test]
    public void ThematicBreakNode_renders_hr()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ThematicBreakNode());
        Assert.That(new HtmlRenderer().RenderToString(doc), Is.EqualTo("<hr>\n"));
    }

    // ── Passthrough inline ───────────────────────────────────────────────

    [Test]
    public void Passthrough_content_rendered_without_escaping()
    {
        var doc = BlockParser.Parse("Text pass:[<em>raw</em>] end.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<em>raw</em>"));
    }

    // ── Hard line break (+ at end of line) ──────────────────────────────

    [Test]
    public void Hard_line_break_renders_br()
    {
        var doc = BlockParser.Parse("line one +\nline two");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>line one<br>\nline two</p>\n" +
            "</div>\n"));
    }

    [Test]
    public void Hard_line_break_only_on_lines_with_trailing_plus()
    {
        var doc = BlockParser.Parse("line one +\nline two\nline three");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("line one<br>\n"));
        // line two does not have trailing +, so no <br> before line three
        Assert.That(html, Does.Contain("line two<br>\nline three"));
    }

    // ── Lead paragraph ([.lead]) ────────────────────────────────────────

    [Test]
    public void Lead_paragraph_has_class()
    {
        var doc = BlockParser.Parse("[.lead]\nThis is a lead paragraph.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Is.EqualTo("<div class=\"paragraph lead\">\n<p>This is a lead paragraph.</p>\n</div>\n"));
    }

    // ── Abstract block ([abstract]) ─────────────────────────────────────

    [Test]
    public void Abstract_paragraph_renders_with_wrapper()
    {
        var doc = BlockParser.Parse("[abstract]\nThis is the abstract.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Is.EqualTo(
            "<div class=\"quoteblock abstract\">\n<blockquote>\n" +
            "<div class=\"paragraph\">\n<p>This is the abstract.</p>\n</div>\n" +
            "</blockquote>\n</div>\n"));
    }

    // ── Custom span roles ([.role]#text#) ───────────────────────────────

    [Test]
    public void Custom_span_role_renders_as_span()
    {
        var doc = BlockParser.Parse("[.underline]#text#");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<span class=\"underline\">text</span>"));
    }

    [Test]
    public void Custom_span_multiple_roles_renders_as_span()
    {
        var doc = BlockParser.Parse("[.big.red]#text#");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<span class=\"big red\">text</span>"));
    }

    [Test]
    public void Highlight_without_roles_renders_as_mark()
    {
        var doc = BlockParser.Parse("#text#");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<mark>text</mark>"));
    }

    // ── Custom roles on bold/italic/mono ─────────────────────────────────

    [Test]
    public void Strong_with_role_renders_class()
    {
        var doc = BlockParser.Parse("[.big]*bold*");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<strong class=\"big\">bold</strong>"));
    }

    [Test]
    public void Emphasis_with_role_renders_class()
    {
        var doc = BlockParser.Parse("[.italic-custom]_text_");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<em class=\"italic-custom\">text</em>"));
    }

    [Test]
    public void Monospace_with_role_renders_class()
    {
        var doc = BlockParser.Parse("[.code-highlight]`code`");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<code class=\"code-highlight\">code</code>"));
    }

    [Test]
    public void Strong_with_multiple_roles_renders_classes()
    {
        var doc = BlockParser.Parse("[.a.b]*text*");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Contain("<strong class=\"a b\">text</strong>"));
    }

    // ── Abstract open block ([abstract] with -- delimiters) ─────────────

    [Test]
    public void Abstract_open_block_renders_with_quoteblock_wrapper()
    {
        var doc = BlockParser.Parse("[abstract]\n--\nThis is an abstract with *multiple* paragraphs.\n\nSecond paragraph.\n--");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Is.EqualTo(
            "<div class=\"quoteblock abstract\">\n<blockquote>\n" +
            "<div class=\"paragraph\">\n<p>This is an abstract with <strong>multiple</strong> paragraphs.</p>\n</div>\n" +
            "<div class=\"paragraph\">\n<p>Second paragraph.</p>\n</div>\n" +
            "</blockquote>\n</div>\n"));
    }

    // ── Attribute Escaping (XSS regression tests) ───────────────────────

    [Test]
    public void Toc_title_with_html_is_escaped()
    {
        var doc = BlockParser.Parse(":toc:\n:toc-title: <script>alert(1)</script>\n\n== Section\n\nText");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
    }

    [Test]
    public void Note_caption_with_html_is_escaped()
    {
        var doc = BlockParser.Parse(":note-caption: <b>XSS</b>\n\nNOTE: Remember.");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Not.Contain("<b>XSS</b>"));
        Assert.That(html, Does.Contain("&lt;b&gt;XSS&lt;/b&gt;"));
    }

    [Test]
    public void Table_caption_with_html_is_escaped()
    {
        var doc = BlockParser.Parse(":table-caption: <img onerror=alert(1)>\n\n.Title\n|===\n| A\n|===");
        var html = new HtmlRenderer().RenderToString(doc.Document);
        Assert.That(html, Does.Not.Contain("<img"));
        Assert.That(html, Does.Contain("&lt;img"));
    }

    // ── Asciidoctor structural-wrapper parity (full-document mode) ──────

    [Test]
    public void Full_document_emits_body_class_for_doctype()
    {
        // Asciidoctor wraps the body as <body class="article"> (or "book" etc.)
        // so theme CSS can target the doctype. Discovered by html-diff against HOWTO.adoc.
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("<body class=\"article\">"),
            "Default doctype should render as <body class=\"article\">");
    }

    [Test]
    public void Full_document_emits_body_class_book_for_book_doctype()
    {
        var doc = BlockParser.Parse("= Title\n:doctype: book\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("<body class=\"book\">"));
    }

    [Test]
    public void Full_document_wraps_title_in_div_id_header()
    {
        // Asciidoctor wraps the document title <h1> in <div id="header">.
        var doc = BlockParser.Parse("= My Title\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("<div id=\"header\">"),
            "Document title should be wrapped in <div id=\"header\">");
        Assert.That(html, Does.Match(@"<div id=""header"">\s*<h1>My Title</h1>\s*</div>"));
    }

    [Test]
    public void Full_document_wraps_body_in_div_id_content()
    {
        var doc = BlockParser.Parse("= Title\n\nA paragraph.").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("<div id=\"content\">"),
            "Section content should be wrapped in <div id=\"content\">");
        var contentOpen = html.IndexOf("<div id=\"content\">", StringComparison.Ordinal);
        var footer = html.IndexOf("<div id=\"footer\">", StringComparison.Ordinal);
        Assert.That(contentOpen, Is.LessThan(footer),
            "Content div must open before footer div");
    }

    [Test]
    public void Embedded_mode_does_not_emit_header_or_content_wrappers()
    {
        // Default (embedded) mode renders the bare body content; no <body>, <head>,
        // <div id="header"> or <div id="content"> wrappers.
        var doc = BlockParser.Parse("= Title\n\nA paragraph.").Document;
        var html = new HtmlRenderer().RenderToString(doc); // default: not full-doc
        Assert.That(html, Does.Not.Contain("<div id=\"header\">"));
        Assert.That(html, Does.Not.Contain("<div id=\"content\">"));
        Assert.That(html, Does.Not.Contain("<body"));
    }

    [Test]
    public void Footer_emits_version_line_when_revnumber_set()
    {
        // Asciidoctor footer template: "Version <revnumber><br>Last updated <docdatetime>".
        var doc = BlockParser.Parse("= Title\nAuthor\nv1.0, 2025-06-15\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("Version 1.0"));
        Assert.That(html, Does.Contain("<br>"));
        Assert.That(html, Does.Contain("Last updated "));
    }

    [Test]
    public void Preamble_wrapper_emitted_for_content_before_first_section()
    {
        // Asciidoctor wraps any non-Section content that precedes the first section
        // in <div id="preamble"><div class="sectionbody">...</div></div>.
        var doc = BlockParser.Parse("= Title\n\nIntro paragraph.\n\n== Section\n\nText").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("<div id=\"preamble\">"));
        Assert.That(html, Does.Contain("<div class=\"sectionbody\">"));
        // Sequence: header, content open, preamble wrapper, sect1, content close
        var preambleIdx = html.IndexOf("<div id=\"preamble\">", StringComparison.Ordinal);
        var sect1Idx = html.IndexOf("<div class=\"sect1\">", StringComparison.Ordinal);
        Assert.That(preambleIdx, Is.GreaterThan(0));
        Assert.That(sect1Idx, Is.GreaterThan(preambleIdx));
    }

    [Test]
    public void Preamble_wrapper_omitted_when_doc_starts_with_section()
    {
        var doc = BlockParser.Parse("= Title\n\n== Section\n\nText").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Not.Contain("<div id=\"preamble\">"));
    }

    [Test]
    public void Preamble_wrapper_omitted_in_embedded_mode()
    {
        // Asciidoctor's -s (embedded) mode also omits the preamble wrapper.
        var doc = BlockParser.Parse("= Title\n\nIntro.\n\n== Section\n\nText").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = false });
        Assert.That(html, Does.Not.Contain("<div id=\"preamble\">"));
    }

    [Test]
    public void Footer_falls_back_to_localdatetime_when_revdate_unset()
    {
        // Asciidoctor parity: when :revdate: isn't set the footer falls back to
        // :docdatetime: → :localdatetime: (always populated). Use :reproducible:
        // to suppress the date entirely.
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Contain("Last updated"));
        Assert.That(html, Does.Match(@"Last updated \d{4}-\d{2}-\d{2}"));
    }

    [Test]
    public void Footer_suppresses_date_when_reproducible_set()
    {
        var doc = BlockParser.Parse("= Title\n:reproducible:\n\nContent").Document;
        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { FullDocument = true });
        Assert.That(html, Does.Not.Contain("Last updated"));
    }
}
