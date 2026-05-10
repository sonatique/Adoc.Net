using System.Text;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Revealjs;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class RevealjsCrossCuttingTests
{
    private static string Render(string adoc)
    {
        var doc = BlockParser.Parse(adoc).Document;
        using var ms = new MemoryStream();
        new RevealjsRenderer().Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── Heading-level mapping for nested sections ────────────────────────────

    [Test]
    public void Level3_section_renders_as_h3_not_h4()
    {
        // ==== in AdocNet's AST is SectionNode Level=3 (matching Asciidoctor's
        // level numbering for nested sections). Asciidoctor's reveal.js converter
        // emits <h{N}> for level N — so a level-3 section becomes <h3>.
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "=== Sub\n\n" +
            "==== Subsection\n\n" +
            "content");
        Assert.That(output, Does.Contain("<h3>Subsection</h3>"));
        Assert.That(output, Does.Not.Contain("<h4>Subsection</h4>"));
    }

    [Test]
    public void Level4_section_renders_as_h4()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "=== Sub\n\n" +
            "==== DeepSub\n\n" +
            "===== Deeper\n\n" +
            "content");
        Assert.That(output, Does.Contain("<h4>Deeper</h4>"));
    }

    // ── Preamble: top-level blocks before first section grouped in title slide ─

    [Test]
    public void Preamble_paragraph_grouped_inside_title_slide()
    {
        var output = Render(
            "= Doc\n\n" +
            "Preamble paragraph one.\n\n" +
            "Preamble paragraph two.\n\n" +
            "== First Slide\n\n" +
            "body");
        // Both preamble paragraphs should appear inside <div class="preamble">,
        // which itself sits inside the title <section>.
        Assert.That(output, Does.Contain("<div class=\"preamble\">"));
        // Title section should not have closed before the preamble — verify the
        // preamble div appears before any `</section>` close tag.
        var titleEnd = output.IndexOf("</section>");
        var preambleStart = output.IndexOf("<div class=\"preamble\">");
        Assert.That(preambleStart, Is.LessThan(titleEnd),
            "preamble div must be inside the title section");
    }

    [Test]
    public void No_preamble_div_when_no_blocks_before_first_section()
    {
        var output = Render(
            "= Doc\n\n" +
            "== First Slide\n\n" +
            "body");
        Assert.That(output, Does.Not.Contain("<div class=\"preamble\">"));
    }

    // ── Inline xref / interdocument xref must render their content ────────────

    [Test]
    public void Cross_reference_emits_link_with_label()
    {
        var output = Render(
            "= Doc\n\n" +
            "[[my-target]]\n" +
            "== Slide\n\n" +
            "See <<my-target,the slide>> for details.\n");
        Assert.That(output, Does.Contain("<a href=\"#my-target\">"));
        Assert.That(output, Does.Contain("the slide"));
    }

    [Test]
    public void Interdocument_xref_emits_link_with_html_target()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "See xref:other.adoc[the other doc].\n");
        Assert.That(output, Does.Contain("<a href=\"other.html\">"));
        Assert.That(output, Does.Contain("the other doc"));
    }

    [Test]
    public void Interdocument_xref_label_with_backticks_renders_as_code()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "See xref:other.adoc[learn how `Foo` works].\n");
        Assert.That(output, Does.Contain("<code>Foo</code>"));
    }

    // ── Block wrappers ────────────────────────────────────────────────────────

    [Test]
    public void Sidebar_block_wrapped_in_sidebarblock_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "****\nSidebar text.\n****");
        Assert.That(output, Does.Contain("<div class=\"sidebarblock\">"));
        Assert.That(output, Does.Contain("<div class=\"content\">"));
    }

    [Test]
    public void Example_block_wrapped_in_exampleblock_div_with_numbered_title()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            ".My Example\n====\nExample body.\n====");
        Assert.That(output, Does.Contain("<div class=\"exampleblock\">"));
        Assert.That(output, Does.Contain("Example 1. My Example"));
    }

    [Test]
    public void Source_block_wrapped_in_listingblock_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[source,java]\n----\nint x = 1;\n----");
        Assert.That(output, Does.Contain("<div class=\"listingblock\">"));
        Assert.That(output, Does.Contain("<pre"));
        Assert.That(output, Does.Contain("language-java"));
    }

    [Test]
    public void Listing_block_without_language_wrapped_in_listingblock_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "----\nplain text\n----");
        Assert.That(output, Does.Contain("<div class=\"listingblock\">"));
    }

    [Test]
    public void Literal_block_wrapped_in_literalblock_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "....\nliteral text\n....");
        Assert.That(output, Does.Contain("<div class=\"literalblock\">"));
    }

    // ── Admonition table structure ─────────────────────────────────────────────

    [Test]
    public void Admonition_renders_with_table_structure()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "NOTE: Be aware.");
        // Asciidoctor wraps admonitions in a 2-column table:
        // class="admonitionblock note" > table > tr > td.icon | td.content
        Assert.That(output, Does.Contain("admonitionblock note"));
        Assert.That(output, Does.Contain("<table>"));
        Assert.That(output, Does.Contain("td class=\"icon\""));
        Assert.That(output, Does.Contain("td class=\"content\""));
    }

    [Test]
    public void Admonition_label_uses_titlecase_in_icon_cell()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "WARNING: Danger.");
        // The icon cell has <div class="title">Warning</div> (title-case label, not WARNING).
        Assert.That(output, Does.Contain("<div class=\"title\">Warning</div>"));
    }

    // ── Example block id propagation ───────────────────────────────────────────

    [Test]
    public void Example_block_id_emitted_on_outer_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[#my-example]\n.Title\n====\nbody.\n====");
        Assert.That(output, Does.Contain("<div class=\"exampleblock\" id=\"my-example\">"));
    }

    // ── InterDocumentXref empty-label fallback ─────────────────────────────────

    [Test]
    public void Interdocument_xref_with_empty_label_uses_bracketed_basename()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "See xref:separating.adoc[] for more.");
        // Asciidoctor displays "[separating]" when no label is given.
        Assert.That(output, Does.Contain(">[separating]<"));
    }

    // ── Section numbering :sectnums: ──────────────────────────────────────────

    [Test]
    public void Sectnums_attribute_prefixes_section_titles()
    {
        var output = Render(
            "= Doc\n" +
            ":sectnums:\n\n" +
            "== First\n\nbody\n\n" +
            "== Second\n\nbody");
        Assert.That(output, Does.Contain(">1. First<"));
        Assert.That(output, Does.Contain(">2. Second<"));
    }

    [Test]
    public void Sectnums_disabled_emits_no_numeric_prefix()
    {
        var output = Render(
            "= Doc\n\n" +
            "== First\n\nbody");
        Assert.That(output, Does.Not.Contain(">1. First<"));
    }

    // ── highlight.js source highlighter ──────────────────────────────────────

    [Test]
    public void Source_highlighter_highlightjs_adds_hljs_classes()
    {
        var output = Render(
            "= Doc\n" +
            ":source-highlighter: highlight.js\n\n" +
            "== Code\n\n" +
            "[source,java]\n----\nint x;\n----");
        Assert.That(output, Does.Contain("class=\"highlight highlightjs\""));
        Assert.That(output, Does.Contain("class=\"hljs language-java\""));
        Assert.That(output, Does.Contain("data-noescape=\"true\""));
    }

    // ── Bare link: <a class="bare"> when no explicit label ───────────────────

    [Test]
    public void Bare_url_in_text_emits_class_bare()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Visit https://example.com for details.");
        Assert.That(output, Does.Contain("class=\"bare\""));
    }

    [Test]
    public void Link_with_explicit_label_no_bare_class()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Visit link:https://example.com[the site] for details.");
        Assert.That(output, Does.Not.Contain("class=\"bare\""));
    }

    // ── Nested list rendering ────────────────────────────────────────────────

    [Test]
    public void Nested_unordered_list_renders_inside_parent_item()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "* Earth\n" +
            "** Moon\n" +
            "* Mars\n");
        // The "Moon" sub-item must appear as a nested <ul> inside the "Earth" <li>.
        Assert.That(output, Does.Contain("Moon"));
        // Two list opens (outer + inner)
        int ulistCount = 0;
        int idx = 0;
        while ((idx = output.IndexOf("<div class=\"ulist\">", idx)) >= 0) { ulistCount++; idx++; }
        Assert.That(ulistCount, Is.GreaterThanOrEqualTo(2),
            "expected a nested ulist for the Moon sub-item");
    }

    [Test]
    public void Document_title_with_colon_splits_into_h1_and_h2()
    {
        var output = Render(
            "= Main Title: Subtitle Here\n\n" +
            "== Slide\n\nbody");
        Assert.That(output, Does.Contain("<h1>Main Title</h1>"));
        Assert.That(output, Does.Contain("<h2>Subtitle Here</h2>"));
    }

    [Test]
    public void Document_title_without_colon_renders_as_h1_only()
    {
        var output = Render(
            "= Plain Title\n\n" +
            "== Slide\n\nbody");
        Assert.That(output, Does.Contain("<h1>Plain Title</h1>"));
        // No <h2> for subtitle should appear in the title slide; only slide title h2s.
        // Verify by checking the title slide region (between "title\" data-state="title">" and the next </section>).
        var titleStart = output.IndexOf("data-state=\"title\"");
        var titleEnd = output.IndexOf("</section>", titleStart);
        var titleSlide = output.Substring(titleStart, titleEnd - titleStart);
        Assert.That(titleSlide, Does.Not.Contain("<h2>"));
    }

    // ── Callouts (conums + colist) ────────────────────────────────────────────

    [Test]
    public void Callout_markers_emit_b_tags_in_listing()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[source,java]\n" +
            "----\n" +
            "int x = 1; // <1>\n" +
            "int y = 2; // <2>\n" +
            "----\n" +
            "<1> First.\n" +
            "<2> Second.\n");
        // Asciidoctor's reveal.js converter renders callout numbers as <b>(N)</b>.
        Assert.That(output, Does.Contain("<b>(1)</b>"));
        Assert.That(output, Does.Contain("<b>(2)</b>"));
    }

    [Test]
    public void Callout_list_emitted_after_listing()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[source,java]\n" +
            "----\n" +
            "int x = 1; // <1>\n" +
            "----\n" +
            "<1> First explanation.\n");
        Assert.That(output, Does.Contain("<div class=\"arabic colist\">"));
        Assert.That(output, Does.Contain("First explanation."));
    }

    // ── Description list structure ───────────────────────────────────────────

    [Test]
    public void Description_list_wrapped_in_dlist_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Term One:: First definition.\n" +
            "Term Two:: Second definition.\n");
        Assert.That(output, Does.Contain("<div class=\"dlist\">"));
        Assert.That(output, Does.Contain("<dt class=\"hdlist1\">"));
    }

    [Test]
    public void Description_list_dd_wraps_text_in_p()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Term:: Some description.\n");
        Assert.That(output, Does.Contain("<dd>\n<p>Some description.</p>"));
    }

    [Test]
    public void Description_list_term_parses_inline_formatting()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "`code term`:: Description here.\n");
        Assert.That(output, Does.Contain("<code>code term</code>"));
    }

    [Test]
    public void Description_list_description_parses_inline_formatting()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Term:: The `class` attribute.\n");
        Assert.That(output, Does.Contain("<code>class</code>"));
        Assert.That(output, Does.Not.Contain("`class`"));
    }

    // ── Quote block content ──────────────────────────────────────────────────

    [Test]
    public void Quote_block_renders_inline_content_property()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[quote, Werner Vogels]\n" +
            "____\n" +
            "Everything fails all the time.\n" +
            "____\n");
        Assert.That(output, Does.Contain("Everything fails all the time."));
        Assert.That(output, Does.Contain("Werner Vogels"));
    }

    // ── Ordered list style classes ───────────────────────────────────────────

    [Test]
    public void Ordered_list_emits_arabic_class_by_default()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            ". First\n" +
            ". Second\n");
        Assert.That(output, Does.Contain("class=\"arabic olist\""));
        Assert.That(output, Does.Contain("<ol class=\"arabic\">"));
    }

    // ── Table structure ───────────────────────────────────────────────────────

    [Test]
    public void Table_emits_class_frame_grid_tableblock()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "|===\n" +
            "|A |B\n" +
            "|===\n");
        Assert.That(output, Does.Contain("class=\"frame-all grid-all tableblock\""));
    }

    [Test]
    public void Table_cell_wrapped_in_p_tableblock_with_halign_valign()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "|===\n" +
            "|cell content\n" +
            "|===\n");
        Assert.That(output, Does.Contain("class=\"halign-left tableblock valign-top\""));
        Assert.That(output, Does.Contain("<p class=\"tableblock\">cell content</p>"));
    }

    [Test]
    public void Table_with_header_emits_thead_with_th_cells()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[%header]\n" +
            "|===\n" +
            "|H1 |H2\n\n" +
            "|cell1 |cell2\n" +
            "|===\n");
        Assert.That(output, Does.Contain("<thead>"));
        Assert.That(output, Does.Contain("<th class=\"halign-left tableblock valign-top\">H1</th>"));
        Assert.That(output, Does.Contain("<tbody>"));
    }

    [Test]
    public void Table_with_title_emits_numbered_caption()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            ".My Table\n" +
            "|===\n" +
            "|cell\n" +
            "|===\n");
        Assert.That(output, Does.Contain("<caption class=\"title\">Table 1. My Table</caption>"));
    }

    // ── Preamble + no-section edge case ───────────────────────────────────────

    [Test]
    public void No_section_doc_emits_blocks_as_bare_siblings_no_preamble_div()
    {
        // When there are NO level-1 sections at all, Asciidoctor emits the
        // title slide and then bare <div> blocks as siblings of <section> —
        // not wrapped in a preamble div inside the title slide.
        var output = Render(
            "= Doc\n\n" +
            "First paragraph.\n\n" +
            "Second paragraph.");
        Assert.That(output, Does.Not.Contain("<div class=\"preamble\">"));
        // Both paragraphs render outside the title slide.
        var titleEnd = output.IndexOf("</section>");
        var firstPara = output.IndexOf("First paragraph");
        Assert.That(firstPara, Is.GreaterThan(titleEnd),
            "preamble blocks must follow the title </section> when no section exists");
    }

    // ── Admonition icon-font support :icons: font ─────────────────────────────

    [Test]
    public void Admonition_with_icons_font_uses_fa_icon_in_icon_cell()
    {
        var output = Render(
            "= Doc\n" +
            ":icons: font\n\n" +
            "== Slide\n\n" +
            "NOTE: hello.");
        // With :icons: font, the icon cell holds <i class="fa fa-info-circle" title="Note">
        // instead of <div class="title">Note</div>.
        Assert.That(output, Does.Contain("<i class=\"fa fa-info-circle\" title=\"Note\">"));
        Assert.That(output, Does.Not.Contain("<div class=\"title\">Note</div>"));
    }

    [TestCase("NOTE", "fa-info-circle", "Note")]
    [TestCase("TIP", "fa-lightbulb-o", "Tip")]
    [TestCase("WARNING", "fa-warning", "Warning")]
    [TestCase("CAUTION", "fa-fire", "Caution")]
    [TestCase("IMPORTANT", "fa-exclamation-circle", "Important")]
    public void Admonition_icon_font_glyph_per_type(string adType, string faClass, string title)
    {
        var output = Render(
            "= Doc\n" +
            ":icons: font\n\n" +
            "== Slide\n\n" +
            $"[{adType}]\n====\nhello\n====");
        Assert.That(output, Does.Contain($"<i class=\"fa {faClass}\" title=\"{title}\">"));
    }

    [Test]
    public void Admonition_without_icons_attribute_still_uses_text_label()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "NOTE: hi.");
        Assert.That(output, Does.Contain("<div class=\"title\">Note</div>"));
    }

    // ── Author email in byline ────────────────────────────────────────────────

    [Test]
    public void Author_email_renders_as_mailto_link_after_name()
    {
        var output = Render(
            "= Doc\n" +
            ":author: Alice\n" +
            ":email: alice@example.com\n\n" +
            "== Slide\n\nbody");
        Assert.That(output, Does.Contain("Alice"));
        Assert.That(output, Does.Contain("<a href=\"mailto:alice@example.com\">alice@example.com</a>"));
    }

    [Test]
    public void No_email_no_mailto_link()
    {
        var output = Render(
            "= Doc\n" +
            ":author: Alice\n\n" +
            "== Slide\n\nbody");
        Assert.That(output, Does.Not.Contain("mailto:"));
    }

    // ── Ordered list type attribute ───────────────────────────────────────────

    [Test]
    public void Ordered_list_loweralpha_emits_type_a()
    {
        // [loweralpha] explicit style → type="a" on <ol>.
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[loweralpha]\n" +
            ". First\n" +
            ". Second\n");
        Assert.That(output, Does.Contain("<ol class=\"loweralpha\" type=\"a\">"));
    }

    [Test]
    public void Ordered_list_lowerroman_emits_type_i()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[lowerroman]\n" +
            ". First\n");
        Assert.That(output, Does.Contain("type=\"i\""));
    }

    [Test]
    public void Ordered_list_arabic_does_not_emit_type()
    {
        // arabic is the default — Asciidoctor omits the type attribute.
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            ". First\n");
        Assert.That(output, Does.Not.Contain("type=\"1\""));
        Assert.That(output, Does.Contain("<ol class=\"arabic\">"));
    }

    // ── Footnote rendering ────────────────────────────────────────────────────

    [Test]
    public void Inline_footnote_emits_sup_marker()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "A line.footnote:[The note text.]\n");
        // Marker: <sup class="footnote">[<span class="footnote" title="View footnote.">1</span>]</sup>
        Assert.That(output, Does.Contain("<sup class=\"footnote\">"));
        Assert.That(output, Does.Contain("<span class=\"footnote\" title=\"View footnote.\">1</span>"));
    }

    [Test]
    public void Slide_with_footnote_emits_footnotes_div_at_end()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Text.footnote:[The note.]\n");
        // Per-slide footnotes div with numbered entries.
        Assert.That(output, Does.Contain("<div class=\"footnotes\">"));
        Assert.That(output, Does.Contain("<div class=\"footnote\">1. The note.</div>"));
    }

    [Test]
    public void Slide_without_footnotes_emits_no_footnotes_div()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Plain text.\n");
        Assert.That(output, Does.Not.Contain("<div class=\"footnotes\">"));
    }

    // ── Block roles propagated to wrapper class ──────────────────────────────

    [Test]
    public void Source_block_role_appended_to_listingblock_class()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[source,java,role=\"primary\"]\n" +
            "----\nint x;\n----");
        Assert.That(output, Does.Contain("<div class=\"listingblock primary\""));
    }

    [Test]
    public void Listing_block_with_dot_role_propagates()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[.secondary]\n" +
            "----\nplain\n----");
        Assert.That(output, Does.Contain("<div class=\"listingblock secondary\""));
    }

    [Test]
    public void Listing_block_without_role_keeps_plain_class()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "----\nplain\n----");
        Assert.That(output, Does.Contain("<div class=\"listingblock\""));
        Assert.That(output, Does.Not.Contain("<div class=\"listingblock \""));
    }

    // ── Horizontal description list ──────────────────────────────────────────

    [Test]
    public void Horizontal_dlist_uses_table_structure()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[horizontal]\n" +
            "CPU:: Intel i7\n" +
            "RAM:: 16 GB\n");
        // Asciidoctor: <div class="hdlist"><table><tr>
        //   <td class="hdlist1">CPU</td><td class="hdlist2"><p>Intel i7</p></td>
        Assert.That(output, Does.Contain("<div class=\"hdlist\">"));
        Assert.That(output, Does.Contain("<td class=\"hdlist1\">"));
        Assert.That(output, Does.Contain("<td class=\"hdlist2\">"));
        Assert.That(output, Does.Not.Contain("<dl>"),
            "horizontal dlist must not use <dl> structure");
    }

    [Test]
    public void Plain_dlist_still_uses_dl_structure()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "Term:: definition.\n");
        // Sanity: non-horizontal dlist unchanged
        Assert.That(output, Does.Contain("<dl>"));
        Assert.That(output, Does.Not.Contain("<div class=\"hdlist\">"));
    }

    // ── Q&A description list ─────────────────────────────────────────────────

    [Test]
    public void Qanda_dlist_uses_qlist_ol_structure()
    {
        var output = Render(
            "= Doc\n\n" +
            "== Slide\n\n" +
            "[qanda]\n" +
            "Question one?:: Answer one.\n" +
            "Question two?:: Answer two.\n");
        Assert.That(output, Does.Contain("<div class=\"qanda qlist\">"));
        Assert.That(output, Does.Contain("<ol>"));
        Assert.That(output, Does.Contain("<em>Question one?</em>"));
        Assert.That(output, Does.Contain("Answer one."));
        Assert.That(output, Does.Not.Contain("<dl>"),
            "qanda dlist must not use <dl>");
    }

    [Test]
    public void Preamble_does_not_create_multiple_slides()
    {
        // Three preamble blocks must not create three extra <section> slides
        // — they all live inside the title slide's preamble div.
        var output = Render(
            "= Doc\n\n" +
            "Block one.\n\n" +
            "Block two.\n\n" +
            "Block three.\n\n" +
            "== Slide\n\nbody");
        // Count <section opening tags. Should be exactly 2: title + slide.
        int sectionCount = 0;
        int idx = 0;
        while ((idx = output.IndexOf("<section", idx)) >= 0)
        {
            sectionCount++;
            idx++;
        }
        Assert.That(sectionCount, Is.EqualTo(2),
            $"Expected 2 sections (title + 1 slide), got {sectionCount}");
    }
}
