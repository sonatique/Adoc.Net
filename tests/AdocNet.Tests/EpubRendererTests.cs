using System.IO.Compression;
using AdocNet.Ast;
using AdocNet.Converters.Epub;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class EpubRendererTests
{
    [Test]
    public void Produces_valid_zip()
    {
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.Entries.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Contains_mimetype_entry()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var mimetype = zip.GetEntry("mimetype");
        Assert.That(mimetype, Is.Not.Null);
        using var reader = new StreamReader(mimetype!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("application/epub+zip"));
    }

    [Test]
    public void Contains_container_xml()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("META-INF/container.xml"), Is.Not.Null);
    }

    [Test]
    public void Contains_content_opf()
    {
        var doc = BlockParser.Parse("= My Book\n\nText").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var opf = zip.GetEntry("OEBPS/content.opf");
        Assert.That(opf, Is.Not.Null);
        using var reader = new StreamReader(opf!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("My Book"));
        Assert.That(content, Does.Contain("version=\"3.0\""));
    }

    [Test]
    public void Contains_navigation_document()
    {
        var doc = BlockParser.Parse("= Doc\n\n== Chapter 1\n\nText\n\n== Chapter 2\n\nMore").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var toc = zip.GetEntry("OEBPS/toc.xhtml");
        Assert.That(toc, Is.Not.Null);
        using var reader = new StreamReader(toc!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("Chapter 1"));
        Assert.That(content, Does.Contain("Chapter 2"));
    }

    [Test]
    public void Contains_content_xhtml()
    {
        var doc = BlockParser.Parse("Hello *bold* world").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var content = zip.GetEntry("OEBPS/content.xhtml");
        Assert.That(content, Is.Not.Null);
        using var reader = new StreamReader(content!.Open());
        var html = reader.ReadToEnd();
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Contains_stylesheet()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("OEBPS/style.css"), Is.Not.Null);
    }

    [Test]
    public void Output_is_deterministic()
    {
        var doc = BlockParser.Parse("= Title\n\n== Section\n\nContent").Document;
        var bytes1 = new EpubRenderer().RenderToBytes(doc);
        var bytes2 = new EpubRenderer().RenderToBytes(doc);
        Assert.That(bytes1, Is.EqualTo(bytes2));
    }

    [Test]
    public void Format_is_epub()
    {
        Assert.That(new EpubRenderer().Format, Is.EqualTo("epub"));
    }

    [Test]
    public void Metadata_includes_author()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe <john@example.com>\n\nContent").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var opf = zip.GetEntry("OEBPS/content.opf");
        using var reader = new StreamReader(opf!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("John Doe"));
    }
}
