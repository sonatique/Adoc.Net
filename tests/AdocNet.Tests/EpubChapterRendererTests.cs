using System.IO.Compression;
using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Epub;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Locks in the semantic-HTML5 output contract of EpubChapterRenderer —
/// the structure asciidoctor-epub3 uses inside chapter XHTML files and that
/// the bundled epub3.css targets directly.
/// </summary>
[TestFixture]
public class EpubChapterRendererTests
{
    private static string RenderChapter(string adoc)
    {
        var doc = BlockParser.Parse(adoc).Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        // Article doctype: chapter is named after the doc title slug, or
        // _content.xhtml when there's no title.
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("EPUB/", StringComparison.Ordinal) &&
            e.FullName.EndsWith(".xhtml", StringComparison.Ordinal) &&
            !e.FullName.Contains("nav.xhtml"))
            ?? throw new InvalidOperationException("no chapter xhtml found");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // ── Sections ─────────────────────────────────────────────────────────

    [Test]
    public void Sect1_renders_as_section_class_sect1_with_h1()
    {
        var html = RenderChapter("= Doc\n\n== First\n\nbody");
        // Asciidoctor parity: top-level sections become <section class="sect1">
        // with an <h1> heading (the chapter title is a separate <h1 class="chapter-title">).
        Assert.That(html, Does.Contain("<section class=\"sect1\" title=\"First\">"));
        Assert.That(html, Does.Contain("<h1 id=\"_first\">First</h1>"));
    }

    [Test]
    public void Sect2_renders_as_section_class_sect2_with_h2()
    {
        var html = RenderChapter("= Doc\n\n== Outer\n\n=== Inner\n\nbody");
        Assert.That(html, Does.Contain("<section class=\"sect2\" title=\"Inner\">"));
        Assert.That(html, Does.Contain("<h2 id=\"_inner\">Inner</h2>"));
    }

    [Test]
    public void Section_with_sectnums_emits_numeric_prefix_in_title_attr_and_heading()
    {
        var html = RenderChapter("= Doc\n:sectnums:\n\n== First\n\nbody");
        Assert.That(html, Does.Contain("<section class=\"sect1\" title=\"1. First\">"));
        Assert.That(html, Does.Contain("<h1 id=\"_first\">1. First</h1>"));
    }

    [Test]
    public void Discrete_heading_renders_as_inline_hN_class_discrete()
    {
        var html = RenderChapter(
            "= Doc\n\n== Section\n\nbody\n\n" +
            "[discrete]\n== Discrete\n\nmore body");
        // Discrete heading at adoc level 1 becomes <h2 class="discrete"> inside
        // the parent section — does not open its own <section>.
        Assert.That(html, Does.Contain("<h2 class=\"discrete\" id=\"_discrete\">Discrete</h2>"));
    }

    // ── Paragraphs ───────────────────────────────────────────────────────

    [Test]
    public void Paragraph_renders_bare_p_no_wrapper_div()
    {
        var html = RenderChapter("Hello world.");
        Assert.That(html, Does.Contain("<p"), "expected bare <p> element");
        Assert.That(html, Does.Not.Contain("<div class=\"paragraph\">"),
            "must not use HtmlRenderer's div.paragraph wrapper");
    }

    [Test]
    public void Final_paragraph_of_chapter_gets_class_last()
    {
        var html = RenderChapter("Only paragraph.");
        Assert.That(html, Does.Contain("class=\"last\""));
    }

    [Test]
    public void Paragraph_role_emitted_as_class()
    {
        var html = RenderChapter("[.lead]\nLead text.");
        Assert.That(html, Does.Contain("class=\"lead"));
    }

    // ── Inline formatting ────────────────────────────────────────────────

    [Test]
    public void Strong_uses_semantic_strong_element()
    {
        var html = RenderChapter("Hello *bold* text.");
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Emphasis_uses_semantic_em_element()
    {
        var html = RenderChapter("Hello _italic_ text.");
        Assert.That(html, Does.Contain("<em>italic</em>"));
    }

    [Test]
    public void Monospace_uses_code_class_literal()
    {
        var html = RenderChapter("Use the `var` keyword.");
        Assert.That(html, Does.Contain("<code class=\"literal\">var</code>"));
    }

    [Test]
    public void Link_macro_adds_class_link()
    {
        var html = RenderChapter("See https://example.com[the site] for info.");
        Assert.That(html, Does.Contain("class=\"link\""));
        Assert.That(html, Does.Contain("the site"));
    }

    [Test]
    public void Link_with_window_emits_target_attr()
    {
        var html = RenderChapter("See https://example.com[site^] today.");
        Assert.That(html, Does.Contain("target=\"_blank\""));
    }

    // ── Lists ────────────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_uses_itemized_list_wrapper_and_span_principal()
    {
        var html = RenderChapter("* one\n* two");
        Assert.That(html, Does.Contain("<div class=\"itemized-list\">"));
        Assert.That(html, Does.Contain("<span class=\"principal\">one</span>"));
    }

    [Test]
    public void Ordered_list_uses_ordered_list_with_arabic_default()
    {
        var html = RenderChapter(". first\n. second");
        Assert.That(html, Does.Contain("<div class=\"ordered-list arabic"));
        Assert.That(html, Does.Contain("<ol class=\"arabic\">"));
    }

    [Test]
    public void Checklist_items_emit_input_type_checkbox()
    {
        var html = RenderChapter("* [x] done\n* [ ] todo");
        Assert.That(html, Does.Contain("<div class=\"itemized-list checklist\">"));
        Assert.That(html, Does.Contain("checked=\"\" data-item-complete=\"1\" disabled=\"\" type=\"checkbox\""));
    }

    [Test]
    public void Description_list_uses_description_list_wrapper_with_term_principal_spans()
    {
        var html = RenderChapter("Term:: definition text.");
        Assert.That(html, Does.Contain("<div class=\"description-list\">"));
        Assert.That(html, Does.Contain("<span class=\"term\">Term</span>"));
        Assert.That(html, Does.Contain("<span class=\"principal\">definition text.</span>"));
    }

    [Test]
    public void Horizontal_dlist_uses_hdlist_table_structure()
    {
        var html = RenderChapter("[horizontal]\nTerm:: definition.");
        Assert.That(html, Does.Contain("<div class=\"hdlist\">"));
        Assert.That(html, Does.Contain("<td class=\"hdlist1\">"));
        Assert.That(html, Does.Contain("<td class=\"hdlist2\">"));
    }

    [Test]
    public void Qanda_dlist_uses_ordered_list_with_em_question()
    {
        var html = RenderChapter("[qanda]\nQ?:: A.");
        Assert.That(html, Does.Contain("<div class=\"qanda qlist\">"));
        Assert.That(html, Does.Contain("<ol>"));
        Assert.That(html, Does.Contain("<em>Q?</em>"));
    }

    // ── Admonitions ──────────────────────────────────────────────────────

    [Test]
    public void Admonition_renders_as_aside_with_title_and_epub_type()
    {
        var html = RenderChapter("NOTE: Be aware.");
        Assert.That(html, Does.Contain("<aside class=\"admonition note\" title=\"Note\" epub:type=\"notice\">"));
        Assert.That(html, Does.Contain("<div class=\"content\">"));
        Assert.That(html, Does.Contain("Be aware."));
    }

    [Test]
    public void Tip_admonition_uses_epub_type_tip()
    {
        var html = RenderChapter("TIP: Save credentials in env vars.");
        Assert.That(html, Does.Contain("title=\"Tip\" epub:type=\"tip\""));
    }

    // ── Source blocks ────────────────────────────────────────────────────

    [Test]
    public void Source_block_renders_as_figure_listing()
    {
        var html = RenderChapter("[source,java]\n----\nint x = 1;\n----");
        Assert.That(html, Does.Contain("<figure class=\"listing\">"));
        Assert.That(html, Does.Contain("<pre class=\"highlight\">"));
        Assert.That(html, Does.Contain("class=\"language-java\" data-lang=\"java\""));
    }

    [Test]
    public void Listing_with_title_emits_figcaption_with_listing_n()
    {
        var html = RenderChapter(".Hello listing\n[source,java]\n----\nint x;\n----");
        Assert.That(html, Does.Contain("<figcaption>Listing 1. Hello listing</figcaption>"));
    }

    // ── Quote / Sidebar / Example ────────────────────────────────────────

    [Test]
    public void Quote_block_renders_as_div_blockquote_with_footer()
    {
        var html = RenderChapter("[quote, Werner Vogels]\n____\nEverything fails.\n____");
        Assert.That(html, Does.Contain("<div class=\"blockquote\">"));
        Assert.That(html, Does.Contain("<blockquote>"));
        Assert.That(html, Does.Contain("<footer>~ Werner Vogels</footer>"));
    }

    [Test]
    public void Sidebar_uses_aside_class_sidebar_with_epub_type()
    {
        var html = RenderChapter(".My Sidebar\n****\nbody text.\n****");
        Assert.That(html, Does.Contain("<aside class=\"sidebar titled\""));
        Assert.That(html, Does.Contain("epub:type=\"sidebar\""));
        Assert.That(html, Does.Contain("<h2>My Sidebar</h2>"));
    }

    [Test]
    public void Example_block_uses_div_example_with_numbered_title()
    {
        var html = RenderChapter(".My example\n====\nbody.\n====");
        Assert.That(html, Does.Contain("<div class=\"example\""));
        Assert.That(html, Does.Contain("<div class=\"example-title\">Example 1. My example</div>"));
        Assert.That(html, Does.Contain("<div class=\"example-content\">"));
    }

    // ── Tables ───────────────────────────────────────────────────────────

    [Test]
    public void Table_uses_div_table_class_with_table_framed_grid_classes()
    {
        var html = RenderChapter("|===\n|A |B\n|===");
        Assert.That(html, Does.Contain("<div class=\"table\">"));
        Assert.That(html, Does.Contain("<table class=\"table table-framed-all table-grid-all\">"));
    }

    [Test]
    public void Table_cell_wraps_in_p_class_tableblock()
    {
        var html = RenderChapter("|===\n|Cell content\n|===");
        Assert.That(html, Does.Contain("<td class=\"halign-left valign-top\"><p class=\"tableblock\">Cell content</p>"));
    }

    // ── Image ────────────────────────────────────────────────────────────

    [Test]
    public void Block_image_uses_figure_class_image()
    {
        var html = RenderChapter("image::pic.png[Alt text]");
        Assert.That(html, Does.Contain("<figure class=\"image\">"));
        Assert.That(html, Does.Contain("<img src=\"pic.png\" alt=\"Alt text\""));
    }

    [Test]
    public void Image_with_title_emits_figcaption_with_figure_n()
    {
        var html = RenderChapter(".My image\nimage::pic.png[Alt]");
        Assert.That(html, Does.Contain("<figcaption>Figure 1. My image</figcaption>"));
    }

    // ── XRef ─────────────────────────────────────────────────────────────

    [Test]
    public void Cross_reference_emits_a_class_xref()
    {
        var html = RenderChapter(
            "[[my-target]]\n== Target\n\nbody\n\n" +
            "See <<my-target,here>>.");
        Assert.That(html, Does.Contain("class=\"xref\""));
        Assert.That(html, Does.Contain("href=\"#my-target\""));
        Assert.That(html, Does.Contain(">here</a>"));
    }

    [Test]
    public void Interdoc_xref_converts_adoc_to_xhtml_in_href()
    {
        var html = RenderChapter("See xref:other.adoc[the other doc].");
        Assert.That(html, Does.Contain("href=\"other.xhtml\""));
        Assert.That(html, Does.Contain("class=\"xref\""));
    }

    // ── Inline macros ────────────────────────────────────────────────────

    [Test]
    public void Kbd_macro_single_key_emits_kbd_element()
    {
        // :experimental: required for kbd/btn/menu macros (Asciidoctor parity).
        var doc = BlockParser.Parse("= Doc\n:experimental:\n\nPress kbd:[F1].").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/_doc.xhtml")!.Open());
        var html = reader.ReadToEnd();
        Assert.That(html, Does.Contain("<kbd>F1</kbd>"));
    }

    [Test]
    public void Kbd_macro_keyseq_wraps_in_span_class_keyseq()
    {
        var doc = BlockParser.Parse("= Doc\n:experimental:\n\nPress kbd:[Ctrl+C].").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/_doc.xhtml")!.Open());
        var html = reader.ReadToEnd();
        Assert.That(html, Does.Contain("<span class=\"keyseq\">"));
        Assert.That(html, Does.Contain("<kbd>Ctrl</kbd>+<kbd>C</kbd>"));
    }
}
