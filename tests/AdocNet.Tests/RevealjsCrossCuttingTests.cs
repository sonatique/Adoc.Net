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
