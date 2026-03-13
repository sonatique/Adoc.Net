using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class RendererFrameworkTests
{
    [Test]
    public void HtmlRenderer_implements_IDocumentRenderer()
    {
        IDocumentRenderer renderer = new HtmlRenderer();
        Assert.That(renderer.Format, Is.EqualTo("html"));
    }

    [Test]
    public void PdfRenderer_implements_IDocumentRenderer()
    {
        IDocumentRenderer renderer = new PdfRenderer();
        Assert.That(renderer.Format, Is.EqualTo("pdf"));
    }

    [Test]
    public void RenderContext_GetOrCreate_returns_same_instance()
    {
        var doc = BlockParser.Parse("test").Document;
        var context = new RenderContext(doc, RenderOptions.Default);
        var state1 = context.GetOrCreate(() => new List<string>());
        var state2 = context.GetOrCreate(() => new List<string>());
        Assert.That(state1, Is.SameAs(state2));
    }

    [Test]
    public void RenderToString_produces_html()
    {
        var doc = BlockParser.Parse("Hello world").Document;
        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("Hello world"));
    }

    [Test]
    public void RenderToBytes_produces_pdf()
    {
        var doc = BlockParser.Parse("Hello world").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes, Is.Not.Empty);
        Assert.That(Encoding.ASCII.GetString(bytes[..5]), Is.EqualTo("%PDF-"));
    }

    [Test]
    public void AdocEngine_converts_to_html()
    {
        var engine = new AdocEngine(new HtmlRenderer(), text => BlockParser.Parse(text).Document);
        using var ms = new MemoryStream();
        engine.Convert("Hello", ms);
        var html = Encoding.UTF8.GetString(ms.ToArray());
        Assert.That(html, Does.Contain("Hello"));
    }

    [Test]
    public void Concurrent_rendering_produces_correct_output()
    {
        var doc = BlockParser.Parse("= Title\n\nParagraph").Document;
        var renderer = new HtmlRenderer();
        var results = new string[10];
        Parallel.For(0, 10, i =>
        {
            results[i] = renderer.RenderToString(doc);
        });
        // All results should be identical
        Assert.That(results.Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void AdocEngine_ConvertFile_reads_and_converts()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "Hello from file");
            var engine = new AdocEngine(new HtmlRenderer(), text => BlockParser.Parse(text).Document);
            using var ms = new MemoryStream();
            engine.ConvertFile(tmpFile, ms);
            var html = Encoding.UTF8.GetString(ms.ToArray());
            Assert.That(html, Does.Contain("Hello from file"));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Test]
    public void BlockParser_Parse_with_null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() => BlockParser.Parse("text", (ParseOptions)null!));
    }
}
