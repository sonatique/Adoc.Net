using System.IO.Compression;
using System.Text;
using System.Xml;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>STEM content in every output, with and without an <see cref="IStemRenderer"/>.</summary>
[TestFixture]
public class StemOutputTests
{
    private const string Box = "M0 0L2 0L2 1L0 1Z";

    /// <summary>Draws every formula as a 2 em × 1 em box (0.25 em below the baseline); "bad" fails.</summary>
    private sealed class BoxRenderer : IStemRenderer
    {
        public List<(string Content, string Type, bool Display)> Calls { get; } = [];

        public StemGraphic? Render(string content, string stemType, bool display)
        {
            lock (Calls) Calls.Add((content, stemType, display));
            return content == "bad" ? null : new StemGraphic(Box, 2, 0.75, 0.25);
        }
    }

    private const string Doc = ":stem: latexmath\n\nEnergy stem:[E = mc^2] here.\n\n[stem]\n++++\n\\sum_{i=0}^n i\n++++\n";

    [TestCase("M0 0\"/><script>alert(1)</script>")]
    [TestCase("M0 0 Q1 1 2 2")]
    public void A_graphic_refuses_path_data_outputs_cannot_write_as_is(string path)
    {
        Assert.Throws<ArgumentException>(() => new StemGraphic(path, 1, 1, 0));
    }

    [Test]
    public void A_graphic_refuses_lengths_that_are_not_finite()
    {
        Assert.Throws<ArgumentException>(() => new StemGraphic(Box, double.NaN, 1, 0));
        Assert.Throws<ArgumentException>(() => new StemGraphic(Box, 1, double.PositiveInfinity, 0));
    }

    // ── HTML ────────────────────────────────────────────────────────────────

    [Test]
    public void Html_draws_svg_and_drops_the_mathjax_script()
    {
        var renderer = new BoxRenderer();
        var html = new HtmlRenderer().RenderToString(AdocParser.Parse(Doc).Document,
            new HtmlRenderOptions { FullDocument = true, StemRenderer = renderer });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("mathjax"));
            Assert.That(html, Does.Contain("aria-label=\"E = mc^2\" width=\"2em\" height=\"1em\" viewBox=\"0 0 2 1\" style=\"vertical-align:-0.25em\"><path fill=\"currentColor\" d=\"" + Box + "\"/></svg>"));
            Assert.That(html, Does.Contain("style=\"display:block;margin:0 auto\""));
            Assert.That(html, Does.Not.Contain("\\("));
            Assert.That(renderer.Calls, Is.EquivalentTo(new[] { ("E = mc^2", "latexmath", false), ("\\sum_{i=0}^n i", "latexmath", true) }));
        });
    }

    [Test]
    public void Html_without_a_renderer_keeps_mathjax()
    {
        var html = new HtmlRenderer().RenderToString(AdocParser.Parse(Doc).Document, new HtmlRenderOptions { FullDocument = true });

        Assert.That(html, Does.Contain("mathjax"));
        Assert.That(html, Does.Contain("\\(E = mc^2\\)"));
        Assert.That(html, Does.Not.Contain("<svg"));
    }

    [Test]
    public void Html_shows_the_source_of_a_formula_that_cannot_be_typeset()
    {
        var html = new HtmlRenderer().RenderToString(AdocParser.Parse("stem:[bad] and stem:[x]").Document,
            new HtmlRenderOptions { StemRenderer = new BoxRenderer() });

        Assert.That(html, Does.Contain("\\$bad\\$"));
        Assert.That(html, Does.Contain("aria-label=\"x\""));
    }

    // ── EPUB ────────────────────────────────────────────────────────────────

    private static (string Opf, string Chapter) Epub(string adoc, IStemRenderer? renderer)
    {
        var bytes = new EpubRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, new EpubRenderOptions { StemRenderer = renderer });
        using var zip = new ZipArchive(new MemoryStream(bytes));
        string Read(ZipArchiveEntry e) { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
        var opf = Read(zip.Entries.Single(e => e.FullName.EndsWith("package.opf", StringComparison.Ordinal)));
        var chapter = Read(zip.Entries.Single(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal) && !e.FullName.EndsWith("nav.xhtml", StringComparison.Ordinal)));
        return (opf, chapter);
    }

    [Test]
    public void Epub_draws_svg_and_declares_it_in_the_manifest()
    {
        var (opf, chapter) = Epub("= Book\n\nArea stem:[a < b].", new BoxRenderer());

        Assert.That(chapter, Does.Contain("aria-label=\"a &lt; b\""));
        Assert.That(opf, Does.Contain("properties=\"scripted svg\""));
        Assert.DoesNotThrow(() => new XmlDocument().LoadXml(chapter));
    }

    [Test]
    public void Epub_fallback_source_is_escaped_xhtml()
    {
        var (opf, chapter) = Epub("= Book\n\nArea stem:[a < b & c].\n\n[latexmath]\n++++\nx < y\n++++\n", renderer: null);

        Assert.That(chapter, Does.Contain("\\$a &lt; b &amp; c\\$"), "no :stem: value: AsciiMath");
        Assert.That(chapter, Does.Contain("\\[x &lt; y\\]"));
        Assert.That(opf, Does.Not.Contain("scripted svg"));
        Assert.DoesNotThrow(() => new XmlDocument().LoadXml(chapter));
    }

    // ── DocBook ─────────────────────────────────────────────────────────────

    private static XmlDocument DocBook(string adoc)
    {
        var xml = new DocBookRenderer().RenderToString(AdocParser.Parse(adoc).Document);
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlNamespaceManager Ns(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("d", "http://docbook.org/ns/docbook");
        return ns;
    }

    [Test]
    public void DocBook_writes_equations_like_asciidoctor()
    {
        var doc = DocBook(":stem: latexmath\n\nSee stem:[x^2].\n\n[stem]\n++++\na ]]> b\n++++\n\n.Sum\n[asciimath]\n++++\nsum_(i=1)^n i\n++++\n");
        var ns = Ns(doc);

        Assert.Multiple(() =>
        {
            Assert.That(doc.SelectSingleNode("//d:inlineequation/d:alt", ns)?.InnerText, Is.EqualTo("x^2"));
            Assert.That(doc.SelectSingleNode("//d:inlineequation/d:mathphrase", ns)?.InnerText, Is.EqualTo("x^2"));
            Assert.That(doc.SelectSingleNode("//d:informalequation/d:mathphrase", ns)?.InnerText, Is.EqualTo("a ]]> b"), "a ]]> in the source survives the CDATA");
            Assert.That(doc.SelectSingleNode("//d:equation/d:title", ns)?.InnerText, Is.EqualTo("Sum"));
            Assert.That(doc.SelectSingleNode("//d:equation/d:alt", ns), Is.Null, "AsciiMath has no LaTeX alt");
            Assert.That(doc.SelectSingleNode("//d:equation/d:mathphrase", ns)?.InnerText, Is.EqualTo("sum_(i=1)^n i"));
        });
    }

    // ── PDF ─────────────────────────────────────────────────────────────────

    /// <summary>The boxes BoxRenderer drew, as (left, bottom, right, top) in PDF points.</summary>
    private static List<(float Left, float Bottom, float Right, float Top)> Boxes(string pdf) =>
        System.Text.RegularExpressions.Regex.Matches(pdf,
                @"([\d.]+) ([\d.]+) m\n([\d.]+) [\d.]+ l\n[\d.]+ ([\d.]+) l\n[\d.]+ [\d.]+ l\nh\nf")
            .Select(m => (F(m.Groups[1].Value), F(m.Groups[4].Value), F(m.Groups[3].Value), F(m.Groups[2].Value)))
            .ToList();

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Pdf_draws_formulas_as_vector_paths_on_the_baseline()
    {
        var options = new PdfRenderOptions { StemRenderer = new BoxRenderer() };
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(AdocParser.Parse(Doc).Document, options));

        var boxes = Boxes(pdf);
        Assert.That(boxes, Has.Count.EqualTo(2));
        var inline = boxes[0];
        // The text after the formula restarts at its right edge, on the baseline.
        var after = System.Text.RegularExpressions.Regex.Match(pdf, @"Q\nBT\n([\d.]+) ([\d.]+) Td\n/F\d+ ([\d.]+) Tf\n\( here\.\) Tj");
        Assert.That(after.Success, Is.True, "the line's text object closes around the path and reopens after it");
        float size = F(after.Groups[3].Value), baseline = F(after.Groups[2].Value);
        Assert.Multiple(() =>
        {
            Assert.That(inline.Right - inline.Left, Is.EqualTo(2 * size).Within(0.01), "2 em wide at the text size");
            Assert.That(inline.Bottom, Is.EqualTo(baseline - 0.25f * size).Within(0.01), "0.25 em below the baseline");
            Assert.That(inline.Top - inline.Bottom, Is.EqualTo(size).Within(0.01));
            Assert.That(F(after.Groups[1].Value), Is.EqualTo(inline.Right).Within(0.01));
            var block = boxes[1];
            Assert.That((block.Left + block.Right) / 2, Is.EqualTo(options.MarginLeft + (options.PageWidth - options.MarginLeft - options.MarginRight) / 2).Within(0.5), "a block formula is centred");
        });
    }

    [Test]
    public void Embedded_font_pdf_wraps_and_justifies_around_formulas()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (!File.Exists(Path.Combine(dir, "AdocNet.slnx")))
            dir = Path.GetDirectoryName(dir)!;
        var font = Path.Combine(dir, "src", "AdocNet.Converters.Epub", "Resources", "notoserif-regular-latin.ttf");
        var words = string.Concat(Enumerable.Repeat("word stem:[x] and more text ", 40));
        var options = new PdfRenderOptions { StemRenderer = new BoxRenderer(), FontPath = font, JustifyText = true };
        var pdf = Encoding.Latin1.GetString(new PdfRenderer().RenderToBytes(AdocParser.Parse(words).Document, options));

        var boxes = Boxes(pdf);
        Assert.That(boxes, Has.Count.EqualTo(40));
        Assert.That(boxes.Select(b => b.Bottom).Distinct().Count(), Is.GreaterThan(3), "formulas spread over the wrapped lines");
        Assert.That(boxes, Has.All.Matches<(float Left, float Bottom, float Right, float Top)>(
            b => b.Left >= options.MarginLeft - 0.01f && b.Right <= options.PageWidth - options.MarginRight + 0.01f), "no formula sticks out of the text column");
    }

    [Test]
    public void Pdf_text_around_a_formula_stays_extractable()
    {
        var bytes = new PdfRenderer().RenderToBytes(AdocParser.Parse("Energy stem:[E = mc^2] here.").Document,
            new PdfRenderOptions { StemRenderer = new BoxRenderer() });
        var text = Converters.Pdf.PdfTextExtractor.NormalizeText(Converters.Pdf.PdfTextExtractor.ExtractText(bytes));

        Assert.That(text, Does.Contain("Energy"));
        Assert.That(text, Does.Contain("here."));
        Assert.That(text, Does.Not.Contain("mc^2"), "the formula is drawn, not written");
    }

    [Test]
    public void Tagged_pdf_marks_a_formula_with_its_source()
    {
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(AdocParser.Parse("Energy stem:[E = mc^2] here.").Document,
            new PdfRenderOptions { StemRenderer = new BoxRenderer(), Accessibility = true }));

        Assert.That(pdf, Does.Contain("/S /Formula"));
    }

    [Test]
    public void Pdf_without_a_renderer_shows_the_source()
    {
        var doc = AdocParser.Parse("Energy stem:[E = mc^2] here.\n\n[stem]\n++++\nx + y\n++++\n").Document;
        var text = Converters.Pdf.PdfTextExtractor.NormalizeText(Converters.Pdf.PdfTextExtractor.ExtractText(new PdfRenderer().RenderToBytes(doc)));

        Assert.That(text, Does.Contain("E = mc^2"));
        Assert.That(text, Does.Contain("x + y"));
    }
}
