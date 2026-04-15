using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Cross-renderer tests verifying determinism, structural correctness, and independence
/// across all four renderers (HTML, PDF, DocBook, EPUB).
/// </summary>
[TestFixture]
public class CrossRendererTests
{
    private static DocumentNode ParseDoc(string input) => BlockParser.Parse(input).Document;

    private static readonly string FullDocument = """
        = Test Document
        Author Name
        :sectnums:

        == Introduction

        This is a *bold* and _italic_ paragraph with `monospace` text.

        == Lists

        * Item one
        * Item two
        ** Nested item

        . First
        . Second

        == Code

        [source,csharp]
        ----
        Console.WriteLine("Hello");
        ----

        == Table

        |===
        | Header 1 | Header 2

        | Cell 1 | Cell 2
        | Cell 3 | Cell 4
        |===

        NOTE: This is an admonition.

        > A quote
        """;

    // ── Determinism ─────────────────────────────────────────────────────

    [Test]
    public void Html_rendering_is_deterministic()
    {
        var doc = ParseDoc(FullDocument);
        var renderer = new HtmlRenderer();
        var result1 = renderer.RenderToString(doc);
        var result2 = renderer.RenderToString(doc);
        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void Pdf_rendering_is_deterministic()
    {
        var doc = ParseDoc(FullDocument);
        var renderer = new PdfRenderer();
        var result1 = renderer.RenderToBytes(doc);
        var result2 = renderer.RenderToBytes(doc);
        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void DocBook_rendering_is_deterministic()
    {
        var doc = ParseDoc(FullDocument);
        var renderer = new DocBookRenderer();
        var result1 = renderer.RenderToString(doc);
        var result2 = renderer.RenderToString(doc);
        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void Epub_rendering_is_deterministic()
    {
        var doc = ParseDoc(FullDocument);
        var renderer = new EpubRenderer();
        var result1 = renderer.RenderToBytes(doc);
        var result2 = renderer.RenderToBytes(doc);
        Assert.That(result1, Is.EqualTo(result2));
    }

    // ── Structural correctness ──────────────────────────────────────────

    [Test]
    public void Html_contains_expected_structural_elements()
    {
        var doc = ParseDoc(FullDocument);
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.Multiple(() =>
        {
            // Title suppressed in embedded mode — assert <h2> for first section instead.
            Assert.That(html, Does.Contain("<h2"), "Section headings");
            Assert.That(html, Does.Contain("<strong>"), "Bold text");
            Assert.That(html, Does.Contain("<em>"), "Italic text");
            Assert.That(html, Does.Contain("<code>"), "Monospace text");
            Assert.That(html, Does.Contain("<ul>"), "Unordered list");
            Assert.That(html, Does.Contain("<ol class=\"arabic\">"), "Ordered list");
            Assert.That(html, Does.Contain("<table"), "Table");
            Assert.That(html, Does.Contain("admonitionblock"), "Admonition");
            Assert.That(html, Does.Contain("<pre class=\"highlight\">"), "Code block");
        });
    }

    [Test]
    public void Pdf_starts_with_pdf_header()
    {
        var doc = ParseDoc(FullDocument);
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(Encoding.ASCII.GetString(bytes[..5]), Is.EqualTo("%PDF-"));
    }

    [Test]
    public void DocBook_produces_valid_xml_structure()
    {
        var doc = ParseDoc(FullDocument);
        var xml = new DocBookRenderer().RenderToString(doc);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.Contain("<?xml version="));
            Assert.That(xml, Does.Contain("<article"));
            Assert.That(xml, Does.Contain("xmlns=\"http://docbook.org/ns/docbook\""));
            Assert.That(xml, Does.Contain("<section"));
            Assert.That(xml, Does.Contain("<para>"));
            Assert.That(xml, Does.Contain("<emphasis"));
            Assert.That(xml, Does.Contain("<programlisting"));
            Assert.That(xml, Does.Contain("table"));
            Assert.That(xml, Does.Contain("</article>"));
        });
    }

    [Test]
    public void Epub_produces_valid_zip_with_expected_entries()
    {
        var doc = ParseDoc(FullDocument);
        var bytes = new EpubRenderer().RenderToBytes(doc);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(entryNames, Does.Contain("mimetype"));
            Assert.That(entryNames, Does.Contain("META-INF/container.xml"));
            Assert.That(entryNames, Does.Contain("OEBPS/content.opf"));
            Assert.That(entryNames, Does.Contain("OEBPS/toc.xhtml"));
            Assert.That(entryNames, Does.Contain("OEBPS/content.xhtml"));
            Assert.That(entryNames, Does.Contain("OEBPS/style.css"));
        });
    }

    // ── Independence: renderers don't share state ───────────────────────

    [Test]
    public void Renderers_produce_independent_output_from_same_document()
    {
        var doc = ParseDoc("= Title\n\nHello *world*.");

        var html = new HtmlRenderer().RenderToString(doc);
        var pdf = new PdfRenderer().RenderToBytes(doc);
        var docbook = new DocBookRenderer().RenderToString(doc);
        var epub = new EpubRenderer().RenderToBytes(doc);

        // Each produces non-empty output
        Assert.Multiple(() =>
        {
            Assert.That(html, Is.Not.Empty);
            Assert.That(pdf, Is.Not.Empty);
            Assert.That(docbook, Is.Not.Empty);
            Assert.That(epub, Is.Not.Empty);
        });

        // HTML and DocBook are distinct text formats
        Assert.That(html, Is.Not.EqualTo(docbook));
    }

    [Test]
    public void Sequential_rendering_with_different_renderers_is_stable()
    {
        var doc = ParseDoc(FullDocument);

        // Render with each renderer, then render again — second pass should match first
        var html1 = new HtmlRenderer().RenderToString(doc);
        var pdf1 = new PdfRenderer().RenderToBytes(doc);
        var docbook1 = new DocBookRenderer().RenderToString(doc);
        var epub1 = new EpubRenderer().RenderToBytes(doc);

        var html2 = new HtmlRenderer().RenderToString(doc);
        var pdf2 = new PdfRenderer().RenderToBytes(doc);
        var docbook2 = new DocBookRenderer().RenderToString(doc);
        var epub2 = new EpubRenderer().RenderToBytes(doc);

        Assert.Multiple(() =>
        {
            Assert.That(html2, Is.EqualTo(html1), "HTML stable across renders");
            Assert.That(pdf2, Is.EqualTo(pdf1), "PDF stable across renders");
            Assert.That(docbook2, Is.EqualTo(docbook1), "DocBook stable across renders");
            Assert.That(epub2, Is.EqualTo(epub1), "EPUB stable across renders");
        });
    }

    // ── Concurrent rendering across renderers ───────────────────────────

    [Test]
    public void Concurrent_rendering_across_all_renderers()
    {
        var doc = ParseDoc(FullDocument);
        var htmlRenderer = new HtmlRenderer();
        var pdfRenderer = new PdfRenderer();
        var docbookRenderer = new DocBookRenderer();
        var epubRenderer = new EpubRenderer();

        // Capture reference output
        var refHtml = htmlRenderer.RenderToString(doc);
        var refPdf = pdfRenderer.RenderToBytes(doc);
        var refDocBook = docbookRenderer.RenderToString(doc);
        var refEpub = epubRenderer.RenderToBytes(doc);

        // Run all four concurrently 5 times each
        var tasks = new List<Task>();
        var htmlResults = new string[5];
        var pdfResults = new byte[5][];
        var docbookResults = new string[5];
        var epubResults = new byte[5][];

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(() => htmlResults[idx] = htmlRenderer.RenderToString(doc)));
            tasks.Add(Task.Run(() => pdfResults[idx] = pdfRenderer.RenderToBytes(doc)));
            tasks.Add(Task.Run(() => docbookResults[idx] = docbookRenderer.RenderToString(doc)));
            tasks.Add(Task.Run(() => epubResults[idx] = epubRenderer.RenderToBytes(doc)));
        }

        Task.WaitAll(tasks.ToArray());

        Assert.Multiple(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                Assert.That(htmlResults[i], Is.EqualTo(refHtml), $"HTML concurrent run {i}");
                Assert.That(pdfResults[i], Is.EqualTo(refPdf), $"PDF concurrent run {i}");
                Assert.That(docbookResults[i], Is.EqualTo(refDocBook), $"DocBook concurrent run {i}");
                Assert.That(epubResults[i], Is.EqualTo(refEpub), $"EPUB concurrent run {i}");
            }
        });
    }

    // ── Format property correctness ─────────────────────────────────────

    [TestCase(typeof(HtmlRenderer), "html")]
    [TestCase(typeof(PdfRenderer), "pdf")]
    [TestCase(typeof(DocBookRenderer), "docbook")]
    [TestCase(typeof(EpubRenderer), "epub")]
    public void Renderer_format_property_is_correct(Type rendererType, string expectedFormat)
    {
        var renderer = (IDocumentRenderer)Activator.CreateInstance(rendererType)!;
        Assert.That(renderer.Format, Is.EqualTo(expectedFormat));
    }

    // ── Empty document handling ─────────────────────────────────────────

    [Test]
    public void All_renderers_handle_empty_document()
    {
        var doc = ParseDoc("");

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => new HtmlRenderer().RenderToString(doc), "HTML");
            Assert.DoesNotThrow(() => new PdfRenderer().RenderToBytes(doc), "PDF");
            Assert.DoesNotThrow(() => new DocBookRenderer().RenderToString(doc), "DocBook");
            Assert.DoesNotThrow(() => new EpubRenderer().RenderToBytes(doc), "EPUB");
        });
    }

    // ── LF-only output (HTML, DocBook) ──────────────────────────────────

    [Test]
    public void Html_output_uses_lf_only()
    {
        var doc = ParseDoc("= Title\r\n\r\nParagraph\r\n");
        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Not.Contain("\r"), "HTML output should use LF-only line endings");
    }

    [Test]
    public void DocBook_output_uses_lf_only()
    {
        var doc = ParseDoc("= Title\r\n\r\nParagraph\r\n");
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Not.Contain("\r"), "DocBook output should use LF-only line endings");
    }

    // ── HTML themed vs fragment ─────────────────────────────────────────

    [Test]
    public void Html_themed_output_contains_fragment_content()
    {
        var doc = ParseDoc("= Title\n\nHello *world*.");
        var fragment = new HtmlRenderer().RenderToString(doc);
        var themed = new HtmlRenderer().RenderToString(doc, HtmlRenderOptions.Styled);

        // The themed output should contain the same content as the fragment
        Assert.That(themed, Does.Contain("<h1>Title</h1>"));
        Assert.That(themed, Does.Contain("<strong>world</strong>"));
        // Plus document wrapper
        Assert.That(themed, Does.Contain("<!DOCTYPE html>"));
        Assert.That(themed, Does.Contain("<style>"));
    }
}
