using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Editor;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class IncrementalHtmlRenderTests
{
    private static readonly HtmlRenderOptions MarkerOptions = new() { EnableIncrementalMarkers = true };

    // ── Core correctness ─────────────────────────────────────────────────────

    [Test]
    public void Incremental_output_matches_full_render_for_modified_section()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("Section 1", "Original content", "Section 2", "Unchanged");
        var newDoc = MakeDoc("Section 1", "Modified content", "Section 2", "Unchanged");

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    [Test]
    public void Unchanged_document_returns_previous_html()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "Content A", "S2", "Content B");
        var newDoc = MakeDoc("S1", "Content A", "S2", "Content B");

        var previousHtml = RenderFull(renderer, oldDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(previousHtml));
    }

    [Test]
    public void Multiple_modified_sections_all_updated()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "A", "S2", "B", "S3", "C");
        var newDoc = MakeDoc("S1", "X", "S2", "B", "S3", "Z");

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    // ── Fallback scenarios ───────────────────────────────────────────────────

    [Test]
    public void Section_added_falls_back_to_full_render()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "A");
        var newDoc = MakeDoc("S1", "A", "S2", "B");

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    [Test]
    public void Section_removed_falls_back_to_full_render()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "A", "S2", "B");
        var newDoc = MakeDoc("S1", "A");

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    [Test]
    public void No_markers_in_previous_html_falls_back_to_full_render()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "A");
        var newDoc = MakeDoc("S1", "B");

        // Render WITHOUT markers
        var previousHtml = RenderFull(renderer, oldDoc, new HtmlRenderOptions());
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    [Test]
    public void Metadata_change_falls_back_to_full_render()
    {
        var renderer = new HtmlRenderer();

        var oldDoc = MakeDoc("S1", "A");
        oldDoc.Title = "Old Title";
        var newDoc = MakeDoc("S1", "A");
        newDoc.Title = "New Title";

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var incremental = new IncrementalHtmlRenderer(renderer, s => AdocParser.Parse(s).Document);
        var result = incremental.Render(oldDoc, newDoc, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    // ── Markers ──────────────────────────────────────────────────────────────

    [Test]
    public void Markers_present_when_option_enabled()
    {
        var renderer = new HtmlRenderer();
        var doc = MakeDoc("S1", "Content");
        var html = RenderFull(renderer, doc);

        Assert.That(html, Does.Contain("<!-- sect:0 -->"));
        Assert.That(html, Does.Contain("<!-- /sect:0 -->"));
    }

    [Test]
    public void No_markers_when_option_disabled()
    {
        var renderer = new HtmlRenderer();
        var doc = MakeDoc("S1", "Content");
        var html = RenderFull(renderer, doc, new HtmlRenderOptions());

        Assert.That(html, Does.Not.Contain("<!-- sect:"));
    }

    // ── AdocEngine integration ───────────────────────────────────────────────

    [Test]
    public void ConvertIncrementalHtml_on_engine()
    {
        var renderer = new HtmlRenderer();
        var engine = new AdocEngine(renderer, s => AdocParser.Parse(s).Document);

        var oldDoc = MakeDoc("S1", "A", "S2", "B");
        var newDoc = MakeDoc("S1", "A", "S2", "Changed");

        var previousHtml = RenderFull(renderer, oldDoc);
        var expectedHtml = RenderFull(renderer, newDoc);

        var oldSnapshot = new DocumentSnapshot(0, "old", oldDoc);
        var newSnapshot = new DocumentSnapshot(1, "new", newDoc);

        var result = engine.ConvertIncrementalHtml(oldSnapshot, newSnapshot, previousHtml, MarkerOptions);

        Assert.That(result, Is.EqualTo(expectedHtml));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DocumentNode MakeDoc(params string[] titleContentPairs)
    {
        var doc = new DocumentNode();
        for (int i = 0; i < titleContentPairs.Length; i += 2)
        {
            var section = new SectionNode { Level = 1, Title = titleContentPairs[i] };
            section.AddChild(new ParagraphNode { Text = titleContentPairs[i + 1] });
            doc.AddChild(section);
        }
        return doc;
    }

    private static string RenderFull(HtmlRenderer renderer, DocumentNode doc, HtmlRenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? MarkerOptions);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
