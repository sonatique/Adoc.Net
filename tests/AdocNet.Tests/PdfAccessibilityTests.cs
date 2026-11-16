using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Accessible (PDF/UA-1) output: the structure tree's shape, marked content and artifacts, and the
/// document-level entries. Full conformance is checked by veraPDF in CI (spec/pdf-ua).
/// </summary>
[TestFixture]
public class PdfAccessibilityTests
{
    private const string Sample = """
        = Accessible Sample
        Ada Writer
        :toc:
        :lang: de

        Intro with a link to https://example.org[the example site] and a note.footnote:[The note text.]

        == First

        * one
        * two

        === Deeper

        [cols="1,1",options="header"]
        |===
        |Key |Value
        2+|Spanning both columns
        |a |b
        |===

        image::images/diagram.png[A blue diagram]

        image::images/photo.png[]

        ----
        code	with a tab
        ----

        NOTE: Mind this.

        == Second

        See <<_first>>.
        """;

    private static string FindRepoRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AdocNet.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static (string Pdf, PdfRenderer Renderer) Render(string source, bool accessible = true)
    {
        var doc = AdocParser.Parse(source).Document;
        var renderer = new PdfRenderer();
        var bytes = renderer.RenderToBytes(doc, new PdfRenderOptions
        {
            Accessibility = accessible,
            BaseDirectory = Path.Combine(FindRepoRoot(), "spec", "pdf-ua"),
            ShowPageNumbers = true,
        });
        return (Encoding.Latin1.GetString(bytes), renderer);
    }

    /// <summary>Structure types in reading order (elements are written depth-first).</summary>
    private static List<string> StructTypes(string pdf) =>
        Regex.Matches(pdf, @"/Type /StructElem /S /(\w+)").Select(m => m.Groups[1].Value).ToList();

    [Test]
    public void Catalog_declares_a_tagged_pdf_ua_document()
    {
        var (pdf, _) = Render(Sample);
        Assert.That(pdf, Does.StartWith("%PDF-1.7"));
        Assert.That(pdf, Does.Contain("/StructTreeRoot ").And.Contain("/MarkInfo << /Marked true >>"));
        Assert.That(pdf, Does.Contain("/Lang (de)"), "the :lang: attribute");
        Assert.That(pdf, Does.Contain("/ViewerPreferences << /DisplayDocTitle true >>"));
        Assert.That(pdf, Does.Contain("<pdfuaid:part>1</pdfuaid:part>").And.Contain("Accessible Sample</rdf:li>"));
    }

    [Test]
    public void Only_embedded_fonts_are_used()
    {
        var (pdf, _) = Render(Sample);
        foreach (Match page in Regex.Matches(pdf, @"/Type /Page .*?/Resources << /Font << (.*?) >>"))
            Assert.That(page.Groups[1].Value, Does.Not.Match(@"/F[1-7] "), "no standard-14 font in the page resources");
        Assert.That(pdf, Does.Contain("/Subtype /Type0").And.Contain("/ToUnicode"));
    }

    [Test]
    public void A_theme_with_only_a_body_font_still_embeds_every_face()
    {
        var doc = AdocParser.Parse("= Doc\n\nText with `code` and *bold*.\n\n----\ncode block\n----\n").Document;
        var font = Path.Combine(FindRepoRoot(), "src", "AdocNet.Converters.Epub", "Resources", "notoserif-regular-latin.ttf");
        var pdf = Encoding.Latin1.GetString(new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions { Accessibility = true, FontPath = font }));
        foreach (Match page in Regex.Matches(pdf, @"/Type /Page .*?/Resources << /Font << (.*?) >>"))
            Assert.That(page.Groups[1].Value, Does.Not.Match(@"/F[1-7] "));
        Assert.That(Regex.Matches(pdf, @"stream\n(.*?)endstream", RegexOptions.Singleline).Select(m => m.Groups[1].Value),
            Has.None.Match(@"/F[1-7] [\d.]+ Tf"), "no text set in a standard font");
    }

    [Test]
    public void Headings_follow_the_document_title_without_skipping_levels()
    {
        var (pdf, _) = Render(Sample);
        var headings = StructTypes(pdf).Where(t => Regex.IsMatch(t, "^H[1-6]$")).ToList();
        Assert.That(headings, Is.EqualTo(new[] { "H1", "H2", "H3", "H2" }), "title H1, sections below it");
    }

    [Test]
    public void Structure_covers_lists_tables_figures_links_notes_code_and_the_toc()
    {
        var (pdf, _) = Render(Sample);
        var types = StructTypes(pdf);
        Assert.That(types, Does.Contain("TOC").And.Contain("TOCI"));
        Assert.That(types, Does.Contain("L").And.Contain("LI").And.Contain("Lbl").And.Contain("LBody"));
        Assert.That(types, Does.Contain("Table").And.Contain("THead").And.Contain("TBody").And.Contain("TR").And.Contain("TH").And.Contain("TD"));
        Assert.That(types, Does.Contain("Code").And.Contain("Note").And.Contain("Link").And.Contain("Div"));
        Assert.That(pdf, Does.Contain("/S /TH").And.Contain("/A << /O /Table /Scope /Column >>"));
        Assert.That(pdf, Does.Contain("/A << /O /Table /ColSpan 2 >>"));
        Assert.That(pdf, Does.Contain("/S /Figure").And.Contain("/Alt (A blue diagram)"));
        Assert.That(pdf, Does.Contain("/S /Note").And.Contain("/ID (footnote-1)").And.Contain("/IDTree"));
        Assert.That(pdf, Does.Contain("/ActualText (Note)"), "the admonition label is spoken");
    }

    [Test]
    public void Image_without_alt_text_uses_its_file_name_and_warns()
    {
        var (pdf, renderer) = Render(Sample);
        Assert.That(pdf, Does.Contain("/Alt (photo)"));
        Assert.That(renderer.Warnings, Has.Some.Contains("images/photo.png"));
    }

    [Test]
    public void Links_own_their_annotations()
    {
        var (pdf, _) = Render(Sample);
        var annots = Regex.Matches(pdf, @"/Type /Annot /Subtype /Link [^\n]*").Select(m => m.Value).ToList();
        Assert.That(annots, Is.Not.Empty);
        Assert.That(annots, Has.All.Contain("/StructParent ").And.All.Contain("/Contents "));
        Assert.That(pdf, Does.Contain("/Type /OBJR /Obj "));
        Assert.That(pdf, Does.Contain("/Tabs /S"), "pages with annotations use structure tab order");
    }

    [Test]
    public void Marked_content_is_balanced_and_matches_the_parent_tree()
    {
        var (pdf, _) = Render(Sample);
        var streams = Regex.Matches(pdf, @"stream\n(.*?)endstream", RegexOptions.Singleline).Select(m => m.Groups[1].Value)
            .Where(s => s.Contains(" Tf\n") || s.Contains(" re ")).ToList();
        Assert.That(streams, Is.Not.Empty);
        foreach (var s in streams)
        {
            int open = Regex.Matches(s, @" BDC\n|^/Artifact BMC\n|\n/Artifact BMC\n").Count;
            Assert.That(Regex.Matches(s, @"(^|\n)EMC\n").Count, Is.EqualTo(open), "every marked-content sequence is closed");
        }
        Assert.That(pdf, Does.Contain("/Artifact BMC"), "running content and decoration are artifacts");

        // Each page's MCIDs run 0..n-1 and the parent tree lists n elements for that page.
        foreach (Match page in Regex.Matches(pdf, @"/Contents \[(\d+) 0 R \d+ 0 R\] [^\n]*?/StructParents (\d+)"))
        {
            var content = Regex.Match(pdf, $@"\n{page.Groups[1].Value} 0 obj\n.*?stream\n(.*?)endstream", RegexOptions.Singleline).Groups[1].Value;
            var mcids = Regex.Matches(content, @"<</MCID (\d+)>> BDC").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            Assert.That(mcids, Is.EqualTo(Enumerable.Range(0, mcids.Count)));
            var entry = Regex.Match(pdf, $@"/Nums \[.*? {page.Groups[2].Value} \[([^\]]*)\]", RegexOptions.Singleline);
            Assert.That(entry.Success, Is.True);
            Assert.That(Regex.Matches(entry.Groups[1].Value, @"\d+ 0 R").Count, Is.EqualTo(mcids.Count));
        }
    }

    [Test]
    public void Untagged_output_is_unchanged_by_default()
    {
        var (pdf, _) = Render(Sample, accessible: false);
        Assert.That(pdf, Does.StartWith("%PDF-1.4"));
        Assert.That(pdf, Does.Not.Contain("/StructTreeRoot").And.Not.Contain("BDC").And.Not.Contain("/Artifact"));
        Assert.That(pdf, Does.Contain("/BaseFont /Helvetica"));
    }

    [Test]
    public void Pdf_ua_attribute_turns_it_on()
    {
        var (pdf, _) = Render(":pdf-ua:\n\nJust text.\n", accessible: false);
        Assert.That(pdf, Does.Contain("/StructTreeRoot ").And.Contain("/Lang (en)"));
        Assert.That(pdf, Does.Contain("Untitled document</rdf:li>").Or.Contain("dc:title"));
    }
}
