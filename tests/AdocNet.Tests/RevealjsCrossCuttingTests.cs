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
